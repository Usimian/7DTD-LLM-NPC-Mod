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

        /// <summary>
        /// Drop what was remembered when the NPC leaves the world.
        ///
        /// The array is only live while the container behind it is. Picking her up serialises
        /// the store into the pickup item and setting her down builds a fresh one, so the array
        /// held here is detached the moment she is removed - and the by-name fallback, which
        /// exists to survive the new entity id, would go on serving those contents under it.
        /// She would state the pack she had before the pickup with complete confidence, which
        /// is the failure that lost a beer once already. Better a companion who says she needs
        /// to check than one who is certain and wrong.
        /// </summary>
        public static void Forget(int entityId, string npcName)
        {
            bool had = _byEntityId.Remove(entityId);
            if (!string.IsNullOrEmpty(npcName)) had |= _byName.Remove(npcName);
            if (had) Log.Out($"[NPCLLMChat] Forgot cached store for {npcName ?? "?"} [id {entityId}] - " +
                             "it left the world, so open her pack once when she is back");
        }
    }

    [HarmonyPatch(typeof(XUiC_LootWindow), nameof(XUiC_LootWindow.SetTileEntityChest))]
    public class LootWindowSetTileEntityPatch
    {
        static void Postfix(string _lootContainerName, ITileEntityLootable _te)
        {
            if (_te == null || _te.items == null) return;

            EntityAlive npc = NearestNPC();
            if (npc == null) return;

            // Only a store the NPC is carrying may be attributed to her, and the window title is
            // the one reliable way to tell: an NPC's own store is titled "traderNPC" or her name.
            // Testing the tile entity instead does not work - V2.6 storage crates are a
            // CompositeTileEntity whose storage feature is NOT a TileEntity, so the old
            // "is this a placed block?" cast came back null, the guard never ran, and a crate
            // full of corn became her pack. Corpse bags and the drone slipped through the same way.
            // The title alone decides. bPlayerStorage used to be rejected alongside it, to keep
            // player-placed crates out - but a hired companion's pack is player storage too, so
            // the one container this exists to capture was the one being thrown away. Crates are
            // already excluded by the title: "Wood Storage Crate Insecure" is not her name.
            if (!IsStoreOf(npc, _lootContainerName))
            {
                Log.Out($"[NPCLLMChat] Not attributing container '{_lootContainerName}' to " +
                        $"{npc.EntityName ?? "?"} - not her store");
                return;
            }

            int entityId = npc.entityId;
            string npcName = npc.EntityName;
            NPCContainerCache.Remember(entityId, npcName, _lootContainerName, _te.items);
            Log.Out($"[NPCLLMChat] Cached container '{_lootContainerName}' for {npcName ?? "?"} [id {entityId}] " +
                    $"(playerStorage={_te.bPlayerStorage}): {WorldContextHelper.SummarizeStacks(_te.items) ?? "(empty)"}");
        }

        /// <summary>
        /// The hire UI titles a companion's store "traderNPC"; opening another NPC's store titles
        /// it with that NPC's name. Anything else - "Steel Storage Crate", "Junk Eyebot",
        /// "Drowned Zombie" - belongs to someone or something else.
        /// </summary>
        private static bool IsStoreOf(EntityAlive npc, string containerName)
        {
            if (string.IsNullOrEmpty(containerName)) return false;
            if (containerName.Equals("traderNPC", System.StringComparison.OrdinalIgnoreCase)) return true;

            string npcName = npc.EntityName;
            return !string.IsNullOrEmpty(npcName) &&
                   containerName.IndexOf(npcName, System.StringComparison.OrdinalIgnoreCase) >= 0;
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
