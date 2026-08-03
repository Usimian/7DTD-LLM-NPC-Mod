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
    /// This is a different class from EntityAliveSDX.LeaderUpdate. SCore moved the leader logic
    /// into a component and left the old method behind, so the earlier patch guarded a path her
    /// entity no longer runs - which is why it never once reported a rescue while she kept
    /// disappearing.
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
            if (_reason != EnumRemoveEntityReason.Despawned) return true;

            var npc = _e as EntityAlive;
            if (npc == null || npc.IsDead() || npc.Buffs == null) return true;
            if (npc.Buffs.HasBuff("buffOrderDismiss")) return true;
            if (!NPCCorePatches.IsHiredByPlayer(npc)) return true;

            npc.bWillRespawn = true;
            Log.Warning($"Refused to despawn {npc.EntityName} [id {npc.entityId}] - " +
                        "she is hired, alive and not dismissed. Something cleared her respawn flag " +
                        "from a site the patches do not cover; please report this line.");
            return false;
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
        private static MethodInfo _getOrCreate;

        static MethodBase TargetMethod()
        {
            var sdx = AccessTools.TypeByName("EntityAliveSDX");
            var harvest = AccessTools.TypeByName("HarvestManager");
            _getOrCreate = harvest == null ? null : AccessTools.Method(harvest, "GetOrCreate");
            return sdx == null ? null : AccessTools.Method(sdx, "dropItemOnDeath");
        }

        static bool Prepare()
        {
            bool ready = TargetMethod() != null && _getOrCreate != null;
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
                var container = _getOrCreate.Invoke(null, new object[] { __instance.entityId })
                                as ITileEntityLootable;
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
            NPCCorePatches.RemoveChatComponent(_entityId);
        }
    }
}
