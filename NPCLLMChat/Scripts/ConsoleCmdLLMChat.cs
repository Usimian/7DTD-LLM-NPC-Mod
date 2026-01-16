using System.Collections.Generic;
using UnityEngine;
using NPCLLMChat.Actions;

namespace NPCLLMChat
{
    /// <summary>
    /// Console commands for testing and managing NPC LLM chat.
    /// </summary>
    public class ConsoleCmdLLMChat : ConsoleCmdAbstract
    {
        public override string[] getCommands()
        {
            return new string[] { "llmchat", "npcai" };
        }

        public override string getDescription()
        {
            return "NPC LLM Chat commands - llmchat <test|status|talk|action>";
        }

        public override string getHelp()
        {
            return @"NPC LLM Chat Console Commands:

llmchat test            - Test connection to LLM server
llmchat status          - Show status and performance
llmchat talk <message>  - Talk to the nearest NPC
llmchat action <action> - Execute action (follow, stop, guard, wait)
llmchat clear           - Clear conversation history
llmchat list            - List active NPC sessions

Examples:
  llmchat test
  llmchat talk Hello, how are you?
  llmchat action follow";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            if (_params.Count == 0)
            {
                SingletonMonoBehaviour<SdtdConsole>.Instance.Output(getHelp());
                return;
            }

            string subCommand = _params[0].ToLower();

            switch (subCommand)
            {
                case "test":
                    TestLLMConnection();
                    break;
                case "status":
                    ShowStatus();
                    break;
                case "talk":
                    if (_params.Count < 2)
                    {
                        SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Usage: llmchat talk <message>");
                        return;
                    }
                    string message = string.Join(" ", _params.GetRange(1, _params.Count - 1));
                    TalkToNearestNPC(message);
                    break;
                case "action":
                    if (_params.Count < 2)
                    {
                        SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Usage: llmchat action <follow|stop|guard|wait>");
                        return;
                    }
                    TestAction(_params[1]);
                    break;
                case "clear":
                    ClearAllHistory();
                    break;
                case "list":
                    ListActiveSessions();
                    break;
                default:
                    SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Unknown: {subCommand}");
                    SingletonMonoBehaviour<SdtdConsole>.Instance.Output(getHelp());
                    break;
            }
        }

        private void TestLLMConnection()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            output.Output("Testing LLM connection...");

            LLMService.Instance.SendChatRequest(
                -1,
                "You are a test. Respond with 'Connection successful!' only.",
                new List<ChatMessage>(),
                "Test",
                response => output.Output($"[SUCCESS] Response: {response}"),
                error => output.Output($"[ERROR] {error}\nMake sure Ollama is running: ollama serve")
            );
        }

        private void ShowStatus()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            output.Output("=== NPC LLM Chat Status ===");
            output.Output($"LLM Service: {(LLMService.Instance != null ? "Active" : "Inactive")}");

            var llm = LLMService.Instance;
            if (llm != null && llm.RequestCount > 0)
            {
                output.Output($"Requests: {llm.RequestCount}");
                output.Output($"Last Response: {llm.LastResponseTimeMs:F0}ms");
                output.Output($"Avg Response: {llm.AvgResponseTimeMs:F0}ms");
            }

            int activeNPCs = 0;
            var world = GameManager.Instance?.World;
            if (world != null)
            {
                foreach (var entity in world.Entities.list)
                {
                    if (entity is EntityAlive alive && alive.GetComponent<NPCChatComponent>() != null)
                        activeNPCs++;
                }
            }
            output.Output($"Active NPC Sessions: {activeNPCs}");
            output.Output("");
            output.Output("To talk: @Hello NPC!");
        }

        private void TalkToNearestNPC(string message)
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var player = GameManager.Instance?.World?.GetPrimaryPlayer();

            if (player == null)
            {
                output.Output("No player found");
                return;
            }

            EntityAlive nearestNPC = FindNearestNPC(player, 15f);
            if (nearestNPC == null)
            {
                output.Output("No NPC found nearby (15m range)");
                return;
            }

            var chatComponent = Harmony.NPCCorePatches.GetOrCreateChatComponent(nearestNPC);
            if (chatComponent == null)
            {
                output.Output("Failed to init chat with NPC");
                return;
            }

            output.Output($"Talking to {chatComponent.NPCName}...");
            chatComponent.ProcessPlayerMessage(message, player, response =>
            {
                output.Output($"[{chatComponent.NPCName}]: {response}");
            });
        }

        private void TestAction(string actionName)
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var player = GameManager.Instance?.World?.GetPrimaryPlayer();

            if (player == null)
            {
                output.Output("No player found");
                return;
            }

            EntityAlive nearestNPC = FindNearestNPC(player, 15f);
            if (nearestNPC == null)
            {
                output.Output("No NPC found nearby");
                return;
            }

            NPCActionType actionType;
            switch (actionName.ToLower())
            {
                case "follow": actionType = NPCActionType.Follow; break;
                case "stop": actionType = NPCActionType.StopFollow; break;
                case "wait": actionType = NPCActionType.Wait; break;
                case "guard": actionType = NPCActionType.Guard; break;
                default:
                    output.Output($"Unknown action: {actionName}");
                    output.Output("Available: follow, stop, wait, guard");
                    return;
            }

            var action = new NPCAction(actionType);
            ActionExecutor.Instance.ExecuteAction(nearestNPC, player, action);
            output.Output($"Executed {actionType} on NPC");
        }

        private EntityAlive FindNearestNPC(EntityPlayer player, float maxDistance)
        {
            EntityAlive closest = null;
            float closestDist = maxDistance;

            var world = GameManager.Instance?.World;
            if (world == null) return null;

            foreach (var entity in world.Entities.list)
            {
                if (entity is EntityAlive alive && IsNPC(alive) && alive.entityId != player.entityId)
                {
                    float dist = Vector3.Distance(player.position, alive.position);
                    if (dist < closestDist)
                    {
                        closest = alive;
                        closestDist = dist;
                    }
                }
            }
            return closest;
        }

        private bool IsNPC(EntityAlive entity)
        {
            if (entity == null) return false;
            string name = entity.GetType().Name;
            if (name.Contains("NPC") || name.Contains("Trader") || name.Contains("Hired")) return true;
            if (entity is EntityPlayer && !(entity is EntityPlayerLocal))
            {
                return ConnectionManager.Instance?.Clients?.ForEntityId(entity.entityId) == null;
            }
            return false;
        }

        private void ClearAllHistory()
        {
            int cleared = 0;
            var world = GameManager.Instance?.World;
            if (world != null)
            {
                foreach (var entity in world.Entities.list)
                {
                    if (entity is EntityAlive alive)
                    {
                        var chat = alive.GetComponent<NPCChatComponent>();
                        if (chat != null)
                        {
                            chat.ClearHistory();
                            cleared++;
                        }
                    }
                }
            }
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Cleared {cleared} NPC conversations");
        }

        private void ListActiveSessions()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            output.Output("=== Active NPC Sessions ===");

            int count = 0;
            var world = GameManager.Instance?.World;
            if (world != null)
            {
                foreach (var entity in world.Entities.list)
                {
                    if (entity is EntityAlive alive)
                    {
                        var chat = alive.GetComponent<NPCChatComponent>();
                        if (chat != null)
                        {
                            var history = chat.GetHistory();
                            var state = chat.GetCurrentState();
                            string status = state?.IsFollowing == true ? " [Following]" :
                                           state?.IsGuarding == true ? " [Guarding]" : "";
                            output.Output($"  [{alive.entityId}] {chat.NPCName} - {history.Count} msgs{status}");
                            count++;
                        }
                    }
                }
            }

            if (count == 0) output.Output("  No active sessions");
            output.Output($"Total: {count}");
        }
    }
}
