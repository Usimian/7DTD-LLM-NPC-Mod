using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NPCLLMChat.Actions
{
    /// <summary>
    /// Executes NPC actions by interfacing with NPCCore/SCore AI tasks
    /// and game systems.
    /// </summary>
    public class ActionExecutor : MonoBehaviour
    {
        private static ActionExecutor _instance;
        public static ActionExecutor Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("NPCActionExecutor");
                    _instance = go.AddComponent<ActionExecutor>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private Dictionary<int, NPCState> _npcStates = new Dictionary<int, NPCState>();

        public event Action<int, NPCActionType, string> OnActionStarted;
        public event Action<int, NPCActionType, bool, string> OnActionCompleted;

        public void ExecuteAction(EntityAlive npc, EntityPlayer player, NPCAction action)
        {
            if (npc == null || action == null) return;

            int npcId = npc.entityId;
            Log.Out($"Executing action {action.Type} for NPC {npcId}");

            if (!_npcStates.TryGetValue(npcId, out NPCState state))
            {
                state = new NPCState(npcId);
                _npcStates[npcId] = state;
            }

            OnActionStarted?.Invoke(npcId, action.Type, action.DialogueBefore);

            try
            {
                switch (action.Type)
                {
                    case NPCActionType.Follow:
                        ExecuteFollow(npc, player, state);
                        break;
                    case NPCActionType.StopFollow:
                        ExecuteStopFollow(npc, state);
                        break;
                    case NPCActionType.Wait:
                        ExecuteWait(npc, state);
                        break;
                    case NPCActionType.Guard:
                        ExecuteGuard(npc, state, action);
                        break;
                    case NPCActionType.Trade:
                        ExecuteTrade(npc, player);
                        break;
                    case NPCActionType.Heal:
                        ExecuteHeal(npc, player, action);
                        break;
                    case NPCActionType.Emote:
                        ExecuteEmote(npc, action);
                        break;
                    case NPCActionType.Refuse:
                        // Just dialogue, maybe shake head
                        break;
                    default:
                        break;
                }

                OnActionCompleted?.Invoke(npcId, action.Type, true, null);
            }
            catch (Exception ex)
            {
                Log.Error($"Action execution failed: {ex.Message}");
                OnActionCompleted?.Invoke(npcId, action.Type, false, ex.Message);
            }
        }

        private void ExecuteFollow(EntityAlive npc, EntityPlayer player, NPCState state)
        {
            state.IsFollowing = true;
            state.FollowTarget = player;

            // Try NPCCore/SCore task system via reflection
            TrySetNPCCoreTask(npc, "follow", player.entityId);

            // Fallback: Start follow coroutine
            StartCoroutine(FollowPlayerCoroutine(npc, player, state));
            Log.Out($"NPC {npc.entityId} following player");
        }

        private IEnumerator FollowPlayerCoroutine(EntityAlive npc, EntityPlayer player, NPCState state)
        {
            float followDistance = 3f;

            while (state.IsFollowing && npc != null && player != null && npc.IsAlive())
            {
                float distance = Vector3.Distance(npc.position, player.position);

                if (distance > followDistance)
                {
                    Vector3 direction = (player.position - npc.position).normalized;
                    Vector3 targetPos = player.position - direction * (followDistance * 0.8f);

                    // Try to use pathfinding
                    try
                    {
                        var moveHelper = npc.moveHelper;
                        if (moveHelper != null)
                        {
                            moveHelper.SetMoveTo(targetPos, false);
                        }
                    }
                    catch { }
                }

                yield return new WaitForSeconds(0.5f);
            }
        }

        private void ExecuteStopFollow(EntityAlive npc, NPCState state)
        {
            state.IsFollowing = false;
            state.FollowTarget = null;
            TrySetNPCCoreTask(npc, "stay", 0);
            Log.Out($"NPC {npc.entityId} stopped following");
        }

        private void ExecuteWait(EntityAlive npc, NPCState state)
        {
            state.IsFollowing = false;
            state.IsWaiting = true;
            state.WaitPosition = npc.position;
            TrySetNPCCoreTask(npc, "stay", 0);
            Log.Out($"NPC {npc.entityId} waiting at position");
        }

        private void ExecuteGuard(EntityAlive npc, NPCState state, NPCAction action)
        {
            state.IsGuarding = true;
            state.GuardPosition = npc.position;
            state.GuardRadius = action.GetParamFloat("radius", 10f);
            TrySetNPCCoreTask(npc, "guard", 0);
            Log.Out($"NPC {npc.entityId} guarding area");
        }

        private void ExecuteTrade(EntityAlive npc, EntityPlayer player)
        {
            if (player is EntityPlayerLocal localPlayer)
            {
                ShowMessage(localPlayer, $"[{GetNPCName(npc)}]: I don't have a trade inventory set up, but we can chat!");
            }
        }

        private void ExecuteHeal(EntityAlive npc, EntityPlayer player, NPCAction action)
        {
            if (player == null) return;

            int healAmount = action.GetParamInt("amount", 25);
            float currentHealth = player.Health;
            float maxHealth = player.GetMaxHealth();

            if (currentHealth < maxHealth)
            {
                float newHealth = Math.Min(currentHealth + healAmount, maxHealth);
                player.Health = (int)newHealth;

                if (player is EntityPlayerLocal localPlayer)
                {
                    ShowMessage(localPlayer, $"[{GetNPCName(npc)}] healed you for {(int)(newHealth - currentHealth)} HP");
                }
                Log.Out($"NPC healed player for {healAmount}");
            }
        }

        private void ExecuteEmote(EntityAlive npc, NPCAction action)
        {
            string emoteName = action.GetParam("emote", "nod").ToLower();
            Log.Out($"NPC {npc.entityId} emote: {emoteName}");
            // Animation would be triggered here if NPC has animator
        }

        private bool TrySetNPCCoreTask(EntityAlive npc, string taskName, int targetId)
        {
            try
            {
                // Try SCore's EntityUtilities if available
                var utilsType = Type.GetType("SCore.Scripts.Utils.EntityUtilities, SCore");
                if (utilsType != null)
                {
                    var setTaskMethod = utilsType.GetMethod("SetCurrentOrder");
                    if (setTaskMethod != null)
                    {
                        setTaskMethod.Invoke(null, new object[] { npc.entityId, taskName });
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Out($"Could not set NPCCore task: {ex.Message}");
            }
            return false;
        }

        private string GetNPCName(EntityAlive npc)
        {
            var chatComponent = npc.GetComponent<NPCChatComponent>();
            return chatComponent?.NPCName ?? npc.EntityName ?? "NPC";
        }

        private void ShowMessage(EntityPlayerLocal player, string message)
        {
            GameManager.ShowTooltip(player, message, false);
        }

        public NPCState GetNPCState(int entityId)
        {
            return _npcStates.TryGetValue(entityId, out var state) ? state : null;
        }

        public void ClearNPCState(int entityId)
        {
            _npcStates.Remove(entityId);
        }
    }

    public class NPCState
    {
        public int EntityId { get; set; }
        public bool IsFollowing { get; set; }
        public EntityPlayer FollowTarget { get; set; }
        public bool IsWaiting { get; set; }
        public Vector3 WaitPosition { get; set; }
        public bool IsGuarding { get; set; }
        public Vector3 GuardPosition { get; set; }
        public float GuardRadius { get; set; }
        public bool IsFleeing { get; set; }
        public DateTime LastInteraction { get; set; }

        public NPCState(int entityId)
        {
            EntityId = entityId;
            LastInteraction = DateTime.Now;
        }
    }
}
