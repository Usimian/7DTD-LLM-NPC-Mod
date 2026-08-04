using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NPCLLMChat.Harmony
{
    /// <summary>
    /// Harmony patches to integrate LLM chat with NPCCore's dialogue system.
    /// </summary>
    public class NPCCorePatches
    {
        private static HarmonyLib.Harmony _harmony;
        private static Dictionary<int, NPCChatComponent> _npcChatComponents = new Dictionary<int, NPCChatComponent>();
        private static LLMConfig _config;

        public static void Initialize(LLMConfig config)
        {
            _config = config;
            _harmony = new HarmonyLib.Harmony("com.npcllmchat.patches");

            try
            {
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Out("Harmony patches applied successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to apply Harmony patches: {ex.Message}");
            }
        }

        public static void Shutdown()
        {
            _harmony?.UnpatchSelf();
            _npcChatComponents.Clear();
        }

        public static NPCChatComponent GetOrCreateChatComponent(EntityAlive npc)
        {
            if (npc == null)
            {
                Log.Warning("GetOrCreateChatComponent: npc is null");
                return null;
            }

            if (npc.gameObject == null)
            {
                Log.Warning($"GetOrCreateChatComponent: GameObject is null for {npc.EntityName}");
                return null;
            }

            int entityId = npc.entityId;

            // A cache hit can hold a component whose GameObject was destroyed (world
            // reload, despawn) - Unity's overloaded == flags those as null. Treat that
            // as a miss and rebuild on the live entity.
            if (!_npcChatComponents.TryGetValue(entityId, out NPCChatComponent chatComponent) || chatComponent == null)
            {
                chatComponent = npc.gameObject.GetComponent<NPCChatComponent>();

                if (chatComponent == null)
                {
                    try
                    {
                        Log.Out($"Adding NPCChatComponent to {npc.EntityName} (type: {npc.GetType().Name})");
                        chatComponent = npc.gameObject.AddComponent<NPCChatComponent>();
                        chatComponent.Initialize(npc, _config);
                        Log.Out($"Successfully initialized chat component for {npc.EntityName}");
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"Failed to add NPCChatComponent: {ex.Message}");
                        return null;
                    }
                }

                _npcChatComponents[entityId] = chatComponent;
            }

            return chatComponent;
        }

        // Every tick of a Stay order clears the flag, so this fires constantly once it starts -
        // report the first, then only when the count crosses a power of ten, so the log says it
        // is happening and roughly how much without becoming the log.
        private static readonly Dictionary<int, int> _respawnRescues = new Dictionary<int, int>();

        internal static void NoteRespawnRescue(EntityAlive npc)
        {
            _respawnRescues.TryGetValue(npc.entityId, out int count);
            count++;
            _respawnRescues[npc.entityId] = count;

            bool worthSaying = count == 1 || count == 10 || count == 100 || count == 1000 || count % 10000 == 0;
            if (worthSaying)
            {
                Log.Warning($"{npc.EntityName} [id {npc.entityId}] bWillRespawn was cleared " +
                            $"({count}x) - SCore does this on every order that is not Follow or Loot. " +
                            "Restored; without this she would be unloaded and lost.");
            }
        }

        private static MethodInfo _harvestGetOrCreate;
        private static MethodInfo _harvestHas;
        private static bool _harvestResolved;

        /// <summary>
        /// SCore keeps an NPC's real pack in HarvestManager - one TileEntityLootContainer per
        /// entity id - and not in entity.lootContainer, which stays empty until the hire UI
        /// fills it. Anything that wants to know what she is carrying has to ask here.
        /// </summary>
        internal static ITileEntityLootable GetNPCPack(int entityId)
        {
            if (!_harvestResolved)
            {
                _harvestResolved = true;
                var harvest = AccessTools.TypeByName("HarvestManager");
                _harvestGetOrCreate = harvest == null ? null : AccessTools.Method(harvest, "GetOrCreate");
                _harvestHas = harvest == null ? null : AccessTools.Method(harvest, "Has");
                if (_harvestGetOrCreate == null)
                {
                    Log.Warning("HarvestManager not found - she will not be able to see her own pack");
                }
            }
            if (_harvestGetOrCreate == null) return null;

            try
            {
                // Ask Has first: GetOrCreate would manufacture an empty container for every
                // entity id it is handed, which is not a question worth asking of the world.
                if (_harvestHas != null && !(bool)_harvestHas.Invoke(null, new object[] { entityId })) return null;
                return _harvestGetOrCreate.Invoke(null, new object[] { entityId }) as ITileEntityLootable;
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not read pack for entity {entityId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// For logging only, and deliberately looser than IsHiredByPlayer, which wants the cvar
        /// present AND above zero. SCore removes "Leader" outright on the Loot order, and
        /// EntityUtilities.GetLeader removes it whenever the leader entity is momentarily not in
        /// the world - during load, for one. A strict test falls silent in precisely the window
        /// worth watching, which is how a companion left on 2026-08-03 with an empty log.
        ///
        /// Buffs only: this is called from the chunk save thread, so no Unity API here.
        /// </summary>
        internal static bool LooksLikeCompanion(EntityAlive npc)
        {
            if (npc?.Buffs == null) return false;
            return npc.Buffs.HasCustomVar("Leader") || npc.Buffs.HasCustomVar("Owner") ||
                   npc.Buffs.HasCustomVar("Persist");
        }

        internal static bool IsHiredByPlayer(EntityAlive npc)
        {
            if (npc?.Buffs == null) return false;
            foreach (string cvar in new[] { "Leader", "Owner" })
            {
                if (npc.Buffs.HasCustomVar(cvar) && npc.Buffs.GetCustomVar(cvar) > 0f) return true;
            }
            return false;
        }

        public static void RemoveChatComponent(int entityId)
        {
            _npcChatComponents.Remove(entityId);
        }

        public static LLMConfig Config => _config;
    }

    /// <summary>
    /// Patch the game's chat system to intercept NPC-directed messages
    /// </summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ChatMessageServer))]
    public class ChatMessageServerPatch
    {
        static bool Prefix(ClientInfo _cInfo, EChatType _chatType, int _senderEntityId, string _msg,
            List<int> _recipientEntityIds, EMessageSender _msgSender)
        {
            // Check if message starts with @ (NPC chat prefix)
            if (!string.IsNullOrEmpty(_msg) && _msg.StartsWith("@"))
            {
                EntityPlayer player = GameManager.Instance.World?.GetEntity(_senderEntityId) as EntityPlayer;
                if (player != null)
                {
                    EntityAlive nearbyNPC = FindNearbyNPC(player);
                    if (nearbyNPC != null)
                    {
                        string cleanMessage = _msg.Substring(1).Trim();
                        ProcessNPCChat(player, nearbyNPC, cleanMessage);
                        return false; // Don't process as normal chat
                    }
                }
            }
            return true;
        }

        static EntityAlive FindNearbyNPC(EntityPlayer player)
        {
            float maxDistance = 5f;
            EntityAlive closestNPC = null;
            float closestDistance = maxDistance;

            var world = GameManager.Instance.World;
            if (world == null) return null;

            foreach (var entity in world.Entities.list)
            {
                if (entity is EntityAlive alive && IsNPCEntity(alive) && alive.entityId != player.entityId)
                {
                    float distance = Vector3.Distance(player.position, alive.position);
                    if (distance < closestDistance)
                    {
                        closestNPC = alive;
                        closestDistance = distance;
                    }
                }
            }

            return closestNPC;
        }

        static bool IsNPCEntity(EntityAlive entity)
        {
            if (entity == null) return false;

            string entityClassName = entity.GetType().Name;

            // NPCCore/XNPCCore NPC types
            if (entityClassName.Contains("NPC") ||
                entityClassName.Contains("Hired") ||
                entityClassName.Contains("Trader") ||
                entityClassName.Contains("Bandit"))
            {
                return true;
            }

            // Check entity class name
            if (entity.EntityClass != null)
            {
                string className = entity.EntityClass.entityClassName;
                if (className != null && (
                    className.ToLower().Contains("npc") ||
                    className.ToLower().Contains("trader") ||
                    className.ToLower().Contains("survivor")))
                {
                    return true;
                }
            }

            // Player-like NPCs (no client info = NPC)
            if (entity is EntityPlayer && !(entity is EntityPlayerLocal))
            {
                var clientInfo = ConnectionManager.Instance?.Clients?.ForEntityId(entity.entityId);
                if (clientInfo == null)
                {
                    return true;
                }
            }

            return false;
        }

        static void ProcessNPCChat(EntityPlayer player, EntityAlive npc, string message)
        {
            var chatComponent = NPCCorePatches.GetOrCreateChatComponent(npc);
            if (chatComponent == null)
            {
                Log.Warning("Could not create chat component for NPC");
                return;
            }

            // Process the message with player reference for actions
            chatComponent.ProcessPlayerMessage(message, player, response =>
            {
                Log.Out($"{chatComponent.NPCName}: {response}");

                // Show response on screen (important when TTS unavailable)
                if (!string.IsNullOrWhiteSpace(response) && player is EntityPlayerLocal localPlayer)
                {
                    GameManager.ShowTooltip(localPlayer, $"{chatComponent.NPCName}: {response}", false);
                }
            });
        }
    }

    /// <summary>
    /// Put her respawn flag back the instant SCore clears it.
    ///
    /// EntityAliveSDX.LeaderUpdate begins by looking up her leader and, finding none, sets
    /// bWillRespawn = false and returns. Vanilla World then unloads her permanently, because
    /// that flag is the whole of what it consults. The lookup fails for ordinary reasons - a
    /// thirty tick leader cache expiring, the player not registered yet during a load - so the
    /// loss is intermittent, which is what made it so hard to pin down.
    ///
    /// A postfix here rather than a timer, because LeaderUpdate is the only thing that clears
    /// the flag: guard it at the source and there is no window at all. A sweep, however fast,
    /// only narrows the window - and she has been lost enough times already.
    ///
    /// Dismissal still works: buffOrderDismiss is left alone, so the one deliberate way to be
    /// rid of her behaves exactly as before.
    /// </summary>
    [HarmonyPatch]
    public class LeaderUpdateRespawnPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("EntityAliveSDX");
            return type == null ? null : AccessTools.Method(type, "LeaderUpdate");
        }

        static bool Prepare()
        {
            bool found = TargetMethod() != null;
            if (!found)
            {
                Log.Warning("EntityAliveSDX.LeaderUpdate not found - companions may " +
                            "vanish when SCore cannot resolve their leader");
            }
            return found;
        }

        static void Postfix(EntityAlive __instance)
        {
            if (__instance == null || __instance.IsDead() || __instance.Buffs == null) return;
            if (__instance.Buffs.HasBuff("buffOrderDismiss")) return;
            if (!NPCCorePatches.IsHiredByPlayer(__instance)) return;

            if (!__instance.bWillRespawn)
            {
                __instance.bWillRespawn = true;
                NPCCorePatches.NoteRespawnRescue(__instance);
            }

            // Her chat component is hung here too. This runs as part of the game's own tick on
            // her, so it is the event that says she exists and is hired - which is the whole of
            // what a scan was ever asking, only without a clock of our own to be wrong about.
            if (__instance.gameObject != null && __instance.gameObject.GetComponent<NPCChatComponent>() == null)
            {
                NPCCorePatches.GetOrCreateChatComponent(__instance);
            }
        }
    }

    /// <summary>
    /// The actual vanishing: a companion told to stay is marked not to come back.
    ///
    /// NPCLeaderComponent.OnUpdateLive ends with
    ///     var order = EntityUtilities.GetCurrentOrder(entityId);
    ///     if (order == Orders.Follow || order == Orders.Loot) { HandleFollowOrder(...); return; }
    ///     _entity.bWillRespawn = false;
    ///
    /// so every order that is not Follow or Loot - Stay, Guard - clears the flag on every tick,
    /// and vanilla World then unloads her permanently. Leave her guarding the base and walk far
    /// enough for the chunk to unload and she is gone, with her gear. Keep her following and she
    /// is fine, which is exactly the pattern: lost when left behind, never when tagging along.
    ///
    /// Which entities this covers, corrected 2026-08-04: NPCLeaderComponent is built only by
    /// EntityAliveSDXV4, and EntityAliveSDXV4 is a sibling of EntityAliveSDX - both derive from
    /// EntityTrader - not a base of it. XNPCCore declares Class="EntityAliveSDX, SCore", so this
    /// patch has never fired for the companion in this game; every rescue in the logs came from
    /// LeaderUpdateRespawnPatch on the older path, including the 21,163 in one session. The
    /// earlier note here had that the wrong way round. Kept because SCore is plainly migrating
    /// toward V4 and the same bug lives in both.
    ///
    /// Dismissal still works: OnUpdateLive returns early on buffOrderDismiss without touching the
    /// flag, and the two remaining clears in SCore are the deliberate unload paths that also strip
    /// her from the player's Companions list.
    /// </summary>
    [HarmonyPatch]
    public class LeaderComponentRespawnPatch
    {
        private static FieldInfo _entityField;

        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("NPCLeaderComponent");
            if (type == null) return null;
            _entityField = AccessTools.Field(type, "_entity");
            return AccessTools.Method(type, "OnUpdateLive");
        }

        static bool Prepare()
        {
            bool ready = TargetMethod() != null && _entityField != null;
            if (!ready)
            {
                Log.Warning("NPCLeaderComponent.OnUpdateLive not found - a companion " +
                            "left on Stay or Guard may be unloaded and lost");
            }
            return ready;
        }

        static void Postfix(object __instance)
        {
            var npc = _entityField?.GetValue(__instance) as EntityAlive;
            if (npc == null || npc.IsDead() || npc.Buffs == null) return;
            if (npc.Buffs.HasBuff("buffOrderDismiss")) return;
            if (!NPCCorePatches.IsHiredByPlayer(npc)) return;

            // NPCLeaderComponent is constructed only by EntityAliveSDXV4, a sibling of
            // EntityAliveSDX rather than a base of it, so this never runs for an XNPCCore
            // companion - that mod declares Class="EntityAliveSDX, SCore". Everything keeping
            // her alive today comes through LeaderUpdateRespawnPatch instead. Kept for V4 NPCs,
            // and so the day SCore moves her class the component still finds her.
            if (npc.gameObject != null && npc.gameObject.GetComponent<NPCChatComponent>() == null)
            {
                NPCCorePatches.GetOrCreateChatComponent(npc);
            }

            if (npc.bWillRespawn) return;
            npc.bWillRespawn = true;
            NPCCorePatches.NoteRespawnRescue(npc);
        }
    }

    /// <summary>
    /// Last line: refuse to despawn a living companion, whatever cleared her flag.
    ///
    /// World decides with
    ///     if (e.IsMarkedForUnload() &amp;&amp; !e.isEntityRemote &amp;&amp; !e.bWillRespawn)
    ///         unloadEntity(e, e.IsDespawned ? Despawned : Killed);
    ///
    /// so holding bWillRespawn up already prevents it. This covers the case where something
    /// clears the flag from a site not patched here - the point is that there should be no
    /// combination of orders, distance or timing under which a hired companion is deleted.
    ///
    /// Narrow on purpose. Only EnumRemoveEntityReason.Despawned, which is the cull; a genuine
    /// death is Killed and she is dead by then anyway, chunk unloads come through as Unloaded
    /// and are how she is saved, and a dismissal has already stopped being hired.
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.unloadEntity))]
    public class RefuseToDespawnCompanionPatch
    {
        static bool Prefix(Entity _e, EnumRemoveEntityReason _reason)
        {
            var npc = _e as EntityAlive;
            if (!NPCCorePatches.LooksLikeCompanion(npc)) return true;

            // Every exit, not just the one this patch blocks. World.RemoveEntity is only one
            // road out of the world - the marked-for-unload sweep and UnloadEntities both call
            // unloadEntity directly, so a companion could leave with nothing in the log at all.
            // That is exactly the hole she went through on 2026-08-03: no removal recorded, and
            // no way afterwards to tell which path took her.
            Log.Warning($"UNLOAD {npc.EntityName} [id {npc.entityId}] reason={_reason} at " +
                        $"({(int)npc.position.x}, {(int)npc.position.z}) " +
                        $"dead={npc.IsDead()} bWillRespawn={npc.bWillRespawn} " +
                        $"savedToFile={SafeIsSavedToFile(npc)} dismissed={npc.Buffs.HasBuff("buffOrderDismiss")}");

            // The refusal itself stays strict: only a companion that is genuinely still hired
            // gets held in the world against the game's wishes.
            if (_reason != EnumRemoveEntityReason.Despawned) return true;
            if (npc.IsDead()) return true;
            if (npc.Buffs.HasBuff("buffOrderDismiss")) return true;
            if (!NPCCorePatches.IsHiredByPlayer(npc)) return true;

            npc.bWillRespawn = true;
            Log.Warning($"Refused to despawn {npc.EntityName} [id {npc.entityId}] - " +
                        "she is hired, alive and not dismissed. Something cleared her respawn flag " +
                        "from a site the patches do not cover; please report this line.");
            return false;
        }

        /// <summary>
        /// The save gate, asked without being able to throw inside a log line.
        /// </summary>
        internal static string SafeIsSavedToFile(Entity e)
        {
            try { return e.IsSavedToFile().ToString(); }
            catch (Exception ex) { return "threw:" + ex.GetType().Name; }
        }
    }

    /// <summary>
    /// Everything she was carrying drops where she fell.
    ///
    /// SCore's dropItemOnDeath rolls against lootDropProb, which XNPCCore sets between 0.03 and
    /// 0.2 - so a companion's whole pack usually evaporated with her. Worse, the drop it makes
    /// passes a null container and her belongings are not in the entity's lootContainer at all:
    /// SCore keeps them in HarvestManager, a TileEntityLootContainer per entity id. Forcing the
    /// probability would have spilled an empty bag.
    ///
    /// So the roll is replaced. Her real container is fetched from HarvestManager and dropped
    /// outright, which leaves death meaning something - she is gone, and a replacement inherits
    /// her memory but not her body - while a corpse run gets the gear back. Nothing is created;
    /// this only stops the game deleting what she was already holding.
    /// </summary>
    [HarmonyPatch]
    public class DropCompanionPackOnDeathPatch
    {
        static MethodBase TargetMethod()
        {
            var sdx = AccessTools.TypeByName("EntityAliveSDX");
            return sdx == null ? null : AccessTools.Method(sdx, "dropItemOnDeath");
        }

        static bool Prepare()
        {
            bool ready = TargetMethod() != null;
            if (!ready)
            {
                Log.Warning("Could not hook companion death drops - her pack may be " +
                            "lost when she dies");
            }
            return ready;
        }

        static bool Prefix(EntityAlive __instance)
        {
            if (__instance == null || !NPCCorePatches.IsHiredByPlayer(__instance)) return true;

            try
            {
                var container = NPCCorePatches.GetNPCPack(__instance.entityId);
                if (container == null || container.IsEmpty())
                {
                    Log.Out($"{__instance.EntityName} died carrying nothing");
                    return false;
                }

                string carried = WorldContextHelper.SummarizeStacks(container.items) ?? "(nothing)";
                GameManager.Instance.DropContentOfLootContainerServer(
                    BlockValue.Air, new Vector3i(__instance.position), __instance.entityId, container);
                Log.Warning($"{__instance.EntityName} died at " +
                            $"({(int)__instance.position.x}, {(int)__instance.position.z}) - dropped: {carried}");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to drop {__instance.EntityName}'s pack: {ex.Message}");
                return true;   // let SCore have its roll rather than drop nothing at all
            }
            return false;
        }
    }

    /// <summary>
    /// Chunk.Write asks IsSavedToFile of every entity it holds and writes only the ones that say
    /// yes. If a companion is in the world at shutdown and absent from the save afterwards, this
    /// is where the two stories part.
    ///
    /// Runs on the chunk save thread - RegionFileManager starts a real one - as well as the main
    /// thread, so the seen-map is locked and nothing here touches a Unity API. Chunk.Write asks
    /// twice per entity per write, and SaveRandomChunks keeps writing, so it speaks only when the
    /// answer changes. A standing "False" would otherwise flood the log in exactly the state
    /// being hunted, which is the mistake the previous commit was written to undo.
    /// </summary>
    [HarmonyPatch]
    public class SaveGateWatchPatch
    {
        private static readonly Dictionary<int, bool> _lastVerdict = new Dictionary<int, bool>();
        private static readonly object _lock = new object();

        static MethodBase TargetMethod()
        {
            var sdx = AccessTools.TypeByName("EntityAliveSDX");
            return sdx == null ? null : AccessTools.Method(sdx, "IsSavedToFile");
        }

        static bool Prepare()
        {
            bool found = TargetMethod() != null;
            if (!found) Log.Warning("EntityAliveSDX.IsSavedToFile not found - cannot watch the save gate");
            return found;
        }

        static void Postfix(EntityAlive __instance, bool __result)
        {
            if (!NPCCorePatches.LooksLikeCompanion(__instance)) return;

            int id = __instance.entityId;
            lock (_lock)
            {
                if (_lastVerdict.TryGetValue(id, out bool was) && was == __result) return;
                _lastVerdict[id] = __result;
            }

            string carried = __instance.Buffs.HasCustomVar("Leader") ? "Leader"
                           : __instance.Buffs.HasCustomVar("Persist") ? "Persist"
                           : "neither";
            Log.Warning($"SAVE GATE [id {id}] -> {__result} " +
                        $"(carried by {carried}, spawnerSource={__instance.GetSpawnerSource()})");
        }

        internal static void Forget(int entityId)
        {
            lock (_lock) { _lastVerdict.Remove(entityId); }
        }
    }

    /// <summary>
    /// The last dark stretch of the chain. Everything the other watchers can see said she was
    /// healthy at the final save - in a chunk, gate agreeing to write her - and she still did not
    /// come back, and never even reached SpawnEntityInWorld on the next load. So the question is
    /// no longer what state she was in but whether she was ever captured at all.
    ///
    /// EntityCreationData is the record an entity becomes on its way to disk: Chunk.Write makes
    /// one per entity it saves, and a chunk being unloaded makes one per entity it is holding.
    /// If this never fires for her, she was never written, and the loss is upstream of the file.
    /// If it fires and she is still gone next session, the file or the read is at fault.
    ///
    /// Y is in the line on purpose - she was guarding an upper floor when she went.
    /// </summary>
    [HarmonyPatch(typeof(EntityCreationData), MethodType.Constructor, new[] { typeof(Entity), typeof(bool) })]
    public class PersistWatchPatch
    {
        static void Postfix(Entity _e, bool _bNetworkWrite)
        {
            // The network variant is chatter to clients, not persistence, and would drown the log
            if (_bNetworkWrite) return;

            var npc = _e as EntityAlive;
            if (!NPCCorePatches.LooksLikeCompanion(npc)) return;

            Log.Warning($"CAPTURED FOR SAVE {npc.EntityName} [id {npc.entityId}] at " +
                        $"({(int)npc.position.x}, {(int)npc.position.y}, {(int)npc.position.z}) " +
                        $"chunk({World.toChunkXZ((int)npc.position.x)}, {World.toChunkXZ((int)npc.position.z)}) " +
                        $"band={Utils.Fastfloor(npc.position.y / 16f)} " +
                        $"savedToFile={RefuseToDespawnCompanionPatch.SafeIsSavedToFile(npc)}");
        }
    }

    /// <summary>
    /// The other end of the same chain: Chunk.OnLoad turns each saved stub back into an entity
    /// through here. A companion who was written but does not come back fails at this step, and
    /// the only way it can return null is an unknown entity class - worth saying out loud, since
    /// that would be silent otherwise and the chunk would simply drop her.
    /// </summary>
    [HarmonyPatch(typeof(EntityFactory), nameof(EntityFactory.CreateEntity), new[] { typeof(EntityCreationData) })]
    public class RestoreWatchPatch
    {
        static void Postfix(EntityCreationData _ecd, Entity __result)
        {
            if (__result == null)
            {
                Log.Warning($"RESTORE FAILED for saved entity '{_ecd?.entityName}' " +
                            $"[id {_ecd?.id}] class={_ecd?.entityClass} - dropped from the world");
                return;
            }

            var npc = __result as EntityAlive;
            if (!NPCCorePatches.LooksLikeCompanion(npc)) return;

            Log.Warning($"RESTORED FROM SAVE {npc.EntityName} [id {npc.entityId}] at " +
                        $"({(int)npc.position.x}, {(int)npc.position.y}, {(int)npc.position.z})");
        }
    }

    /// <summary>
    /// Placing her from the item makes a brand new entity with a brand new id - EntityFactory
    /// .CreateEntity, not a restore - so every pick-up-and-put-down changes who she is as far as
    /// anything keyed by id is concerned. Record the new id at the moment it appears.
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.SpawnEntityInWorld))]
    public class SpawnWatchPatch
    {
        static void Postfix(Entity _entity)
        {
            var npc = _entity as EntityAlive;
            if (npc == null || npc.Buffs == null) return;
            // Not every SDX entity - that is SCore's enemies, bandits and flying zombies too, a
            // warning apiece on a horde night. A placement is hired a moment later, by
            // SetLeaderAndOwner after the spawn, so accept a companion item's InitialInventory
            // mark as well as the cvars that are not set yet.
            if (!NPCCorePatches.LooksLikeCompanion(npc) &&
                !npc.Buffs.HasCustomVar("InitialInventory")) return;

            Log.Warning($"SPAWNED {npc.EntityName} [id {npc.entityId}] at " +
                        $"({(int)npc.position.x}, {(int)npc.position.z}) " +
                        $"hired={NPCCorePatches.IsHiredByPlayer(npc)} " +
                        $"bWillRespawn={npc.bWillRespawn} " +
                        $"savedToFile={RefuseToDespawnCompanionPatch.SafeIsSavedToFile(npc)}");
        }
    }

    /// <summary>
    /// A roll call on the game's own autosave. GameManager runs a 30 second countdown and calls
    /// World.SaveWorldState, so this is the game's clock rather than one of ours, and it ticks
    /// throughout play. RegionFileManager.WaitSaveDone looked like the natural hook and is not:
    /// it is reached only from the pause menu and shutdown, five times in a 79 minute session
    /// and four of those in one burst - it could never bracket a disappearance mid-session.
    ///
    /// Speaks only when the answer changes. A settled companion writes one line and then nothing
    /// until she moves chunk, loses a flag, or goes; the moment she goes is a line of its own.
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.SaveWorldState))]
    public class SaveRollCallPatch
    {
        private static string _lastCall;

        static void Postfix(World __instance)
        {
            RollCall(__instance, false);
        }

        /// <summary>
        /// The autosave is the heartbeat, but World.Save is the one that matters: it inlines its
        /// own worldState write rather than calling SaveWorldState, so the shutdown save would
        /// pass unremarked. Always speak there, unchanged or not - "she was still here at the
        /// last save" is the fact worth having when the next session opens without her.
        /// </summary>
        internal static void RollCall(World world, bool force)
        {
            if (world?.Entities?.list == null) return;

            var call = new List<string>();
            foreach (var e in world.Entities.list)
            {
                var npc = e as EntityAlive;
                if (!NPCCorePatches.LooksLikeCompanion(npc)) continue;
                call.Add($"{npc.EntityName} [id {npc.entityId}] " +
                         $"chunk({World.toChunkXZ((int)npc.position.x)}, {World.toChunkXZ((int)npc.position.z)}) " +
                         $"dead={npc.IsDead()} bWillRespawn={npc.bWillRespawn} " +
                         $"addedToChunk={npc.addedToChunk} hired={NPCCorePatches.IsHiredByPlayer(npc)} " +
                         $"savedToFile={RefuseToDespawnCompanionPatch.SafeIsSavedToFile(npc)}");
            }

            string now = call.Count == 0 ? "nobody" : string.Join(" | ", call.ToArray());
            if (now == _lastCall && !force) return;
            _lastCall = now;
            Log.Warning((force ? "ROLL CALL (final save) " : "ROLL CALL ") + now);
        }
    }

    /// <summary>
    /// The last word before the world closes.
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.Save))]
    public class FinalRollCallPatch
    {
        static void Postfix(World __instance)
        {
            SaveRollCallPatch.RollCall(__instance, true);
        }
    }

    /// <summary>
    /// Clean up when NPCs are removed
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.RemoveEntity))]
    public class RemoveEntityPatch
    {
        static void Prefix(int _entityId)
        {
            // Companions keep vanishing across restarts; record the moment one is removed
            // from the world so the log distinguishes "despawned while playing/at shutdown"
            // from "never restored at load".
            var world = GameManager.Instance?.World;
            var entity = world?.GetEntity(_entityId) as EntityAlive;
            if (entity != null && entity.GetComponent<NPCChatComponent>() != null)
            {
                Log.Warning($"NPC {entity.EntityName} [id {_entityId}] removed from world at " +
                            $"({(int)entity.position.x}, {(int)entity.position.z}), dead={entity.IsDead()}");
            }
            NPCContainerCache.Forget(_entityId, entity?.EntityName);
            SaveGateWatchPatch.Forget(_entityId);
            NPCCorePatches.RemoveChatComponent(_entityId);
        }
    }
}
