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
                Log.Warning("[NPCLLMChat] GetOrCreateChatComponent: npc is null");
                return null;
            }

            if (npc.gameObject == null)
            {
                Log.Warning($"[NPCLLMChat] GetOrCreateChatComponent: GameObject is null for {npc.EntityName}");
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
                        Log.Out($"[NPCLLMChat] Adding NPCChatComponent to {npc.EntityName} (type: {npc.GetType().Name})");
                        chatComponent = npc.gameObject.AddComponent<NPCChatComponent>();
                        chatComponent.Initialize(npc, _config);
                        Log.Out($"[NPCLLMChat] Successfully initialized chat component for {npc.EntityName}");
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"[NPCLLMChat] Failed to add NPCChatComponent: {ex.Message}");
                        return null;
                    }
                }

                _npcChatComponents[entityId] = chatComponent;
            }

            return chatComponent;
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
                Log.Out($"[NPCLLMChat] {chatComponent.NPCName}: {response}");

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
                Log.Warning("[NPCLLMChat] EntityAliveSDX.LeaderUpdate not found - companions may " +
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
                Log.Warning($"[NPCLLMChat] {__instance.EntityName} [id {__instance.entityId}] had " +
                            "bWillRespawn=false - restored, she would have been unloaded for good");
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
                Log.Warning($"[NPCLLMChat] NPC {entity.EntityName} [id {_entityId}] removed from world at " +
                            $"({(int)entity.position.x}, {(int)entity.position.z}), dead={entity.IsDead()}");
            }
            NPCContainerCache.Forget(_entityId, entity?.EntityName);
            NPCCorePatches.RemoveChatComponent(_entityId);
        }
    }
}
