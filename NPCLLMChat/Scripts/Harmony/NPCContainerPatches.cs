using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NPCLLMChat.Harmony
{
    /// <summary>
    /// The companion's storage is not reachable from the entity: lootContainer, bag, the
    /// toolbelt and the trader inventory all read empty while the "traderNPC" panel plainly
    /// holds items, and no tile entity carries her id (SCore serialises the store into the
    /// pickup item). The one place the real container is exposed is the moment the game binds
    /// it to the loot window, so remember what was in it, keyed by the NPC standing closest.
    /// </summary>
    public static class NPCContainerCache
    {
        private class Entry
        {
            public ItemStack[] Items;
            public string ContainerName;
            public string NpcName;
        }

        // Keyed by entity id AND by name: picking an NPC up and setting it down again gives
        // it a NEW entity id, which silently orphaned the cached store.
        private static readonly Dictionary<int, Entry> _byEntityId = new Dictionary<int, Entry>();
        private static readonly Dictionary<string, Entry> _byName = new Dictionary<string, Entry>();

        public static void Remember(int entityId, string npcName, string containerName, ItemStack[] items)
        {
            var entry = new Entry { Items = items, ContainerName = containerName, NpcName = npcName };
            _byEntityId[entityId] = entry;
            if (!string.IsNullOrEmpty(npcName)) _byName[npcName] = entry;
        }

        /// <summary>Live item array for an NPC's storage, or null if never opened.</summary>
        public static ItemStack[] Get(int entityId, string npcName)
        {
            if (_byEntityId.TryGetValue(entityId, out Entry entry)) return entry.Items;
            if (!string.IsNullOrEmpty(npcName) && _byName.TryGetValue(npcName, out entry)) return entry.Items;
            return null;
        }
    }

    [HarmonyPatch(typeof(XUiC_LootWindow), nameof(XUiC_LootWindow.SetTileEntityChest))]
    public class LootWindowSetTileEntityPatch
    {
        static void Postfix(string _lootContainerName, ITileEntityLootable _te)
        {
            if (_te == null || _te.items == null) return;

            // Only an NPC's carried store may be attributed to an NPC. A placed block sits at
            // real world coordinates, so opening a crate next to her must not make its contents
            // hers - that briefly had her owning 278 raw meat from a storage crate.
            var asTile = _te as TileEntity;
            bool isPlacedBlock = asTile != null && asTile.ToWorldPos() != Vector3i.zero;
            if (isPlacedBlock && (asTile.entityId <= 0)) return;

            EntityAlive npc = NearestNPC();
            int entityId = (_te as TileEntity)?.entityId ?? -1;
            if (entityId <= 0)
            {
                if (npc == null) return;
                entityId = npc.entityId;
            }

            string npcName = npc != null ? npc.EntityName : null;
            NPCContainerCache.Remember(entityId, npcName, _lootContainerName, _te.items);
            Log.Out($"[NPCLLMChat] Cached container '{_lootContainerName}' for {npcName ?? "?"} [id {entityId}]: " +
                    $"{WorldContextHelper.SummarizeStacks(_te.items) ?? "(empty)"}");
        }

        private static EntityAlive NearestNPC()
        {
            var world = GameManager.Instance?.World;
            var player = world?.GetPrimaryPlayer();
            if (world == null || player == null) return null;

            EntityAlive closest = null;
            float closestDist = 6f; // you have to be standing at them to open their storage
            foreach (var entity in world.Entities.list)
            {
                if (!(entity is EntityAlive alive) || alive.entityId == player.entityId) continue;
                if (!NPCLLMChatMod.IsNPC(alive)) continue;

                float dist = Vector3.Distance(player.position, alive.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = alive;
                }
            }
            return closest;
        }
    }
}
