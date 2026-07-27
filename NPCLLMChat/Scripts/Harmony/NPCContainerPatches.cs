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
        }

        private static readonly Dictionary<int, Entry> _byEntityId = new Dictionary<int, Entry>();

        public static void Remember(int entityId, string containerName, ItemStack[] items)
        {
            _byEntityId[entityId] = new Entry { Items = items, ContainerName = containerName };
        }

        /// <summary>Live item array for an NPC's storage, or null if never opened.</summary>
        public static ItemStack[] Get(int entityId)
        {
            return _byEntityId.TryGetValue(entityId, out Entry entry) ? entry.Items : null;
        }

        public static string ContainerNameFor(int entityId)
        {
            return _byEntityId.TryGetValue(entityId, out Entry entry) ? entry.ContainerName : null;
        }

        public static void Forget(int entityId)
        {
            _byEntityId.Remove(entityId);
        }
    }

    [HarmonyPatch(typeof(XUiC_LootWindow), nameof(XUiC_LootWindow.SetTileEntityChest))]
    public class LootWindowSetTileEntityPatch
    {
        static void Postfix(string _lootContainerName, ITileEntityLootable _te)
        {
            if (_te == null || _te.items == null) return;

            // Containers belonging to a world block have no entity id; NPC-carried stores are
            // bound while standing next to their owner, so attribute it to the nearest NPC.
            int entityId = (_te as TileEntity)?.entityId ?? -1;
            if (entityId <= 0)
            {
                entityId = NearestNPCEntityId();
                if (entityId <= 0) return;
            }

            NPCContainerCache.Remember(entityId, _lootContainerName, _te.items);
            Log.Out($"[NPCLLMChat] Cached container '{_lootContainerName}' for entity {entityId}: " +
                    $"{WorldContextHelper.SummarizeStacks(_te.items) ?? "(empty)"}");
        }

        private static int NearestNPCEntityId()
        {
            var world = GameManager.Instance?.World;
            var player = world?.GetPrimaryPlayer();
            if (world == null || player == null) return -1;

            int closestId = -1;
            float closestDist = 6f; // you have to be standing at them to open their storage
            foreach (var entity in world.Entities.list)
            {
                if (!(entity is EntityAlive alive) || alive.entityId == player.entityId) continue;
                if (!NPCLLMChatMod.IsNPC(alive)) continue;

                float dist = Vector3.Distance(player.position, alive.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestId = alive.entityId;
                }
            }
            return closestId;
        }
    }
}
