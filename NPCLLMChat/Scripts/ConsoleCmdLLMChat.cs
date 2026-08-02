using System.Collections.Generic;
using UnityEngine;
using NPCLLMChat.Actions;
using NPCLLMChat.TTS;
using NPCLLMChat.STT;

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
            return "NPC LLM Chat commands - llmchat <test|status|talk|tts|stt|action>";
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
llmchat persona         - Show active NPC personas
llmchat persona reload  - Re-read personas from the memory files (after hand edits)
llmchat find            - Locate NPCs: every loaded one with bearing + hire state,
                          plus the companion's last known position from her journal
llmchat persist         - Mark the nearest NPC savable so it survives logout
                          (SCore only saves NPCs carrying a Leader or Persist cvar)
llmchat hire            - Show hire state (player hire count, nearest NPC's cvars)
llmchat hire reset      - Clear a stale hire count (lost companion blocking new hires)
llmchat hire set <n>    - Set the hire count to match reality (e.g. 1 with one companion)
llmchat context         - Dump exactly what the nearest NPC is told about the world.
                          When she says she does not know something she plainly does,
                          this is the only way to see whether she was ever told it.

TTS Commands:
llmchat tts             - Show TTS status
llmchat tts test        - Test TTS with sample speech
llmchat tts on          - Enable TTS globally
llmchat tts off         - Disable TTS globally
llmchat tts voices      - List available voices

STT Commands (Voice Input):
llmchat stt             - Show STT status
llmchat stt test        - Test recording and transcription
llmchat stt on          - Enable voice input
llmchat stt off         - Disable voice input
llmchat stt devices     - List available microphones

Examples:
  llmchat test
  llmchat talk Hello, how are you?
  llmchat tts test
  llmchat stt test
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
                case "persona":
                    HandlePersonaCommand(_params);
                    break;
                case "find":
                    FindNPCs();
                    break;
                case "persist":
                    MakeNearestNPCPersist();
                    break;
                case "context":
                    DumpNearestNPCContext();
                    break;
                case "hire":
                    HandleHireCommand(_params);
                    break;
                case "tts":
                    HandleTTSCommand(_params);
                    break;
                case "stt":
                    HandleSTTCommand(_params);
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
                if (entity is EntityAlive alive && NPCLLMChatMod.IsNPC(alive) && alive.entityId != player.entityId)
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

        /// <summary>
        /// Where did the companion go? Lists every loaded NPC with bearing and hire state,
        /// then falls back to her travel journal for a last-known position when she is not
        /// loaded (unhired NPCs wander off and despawn, hired ones reload where saved).
        /// </summary>
        private void FindNPCs()
        {
            var console = SingletonMonoBehaviour<SdtdConsole>.Instance;
            // Console text never reaches the log file, so mirror every line into it - that is
            // the only way this output can be read after the fact (or by someone helping).
            System.Action<string> output = line =>
            {
                console.Output(line);
                Log.Out($"[NPCLLMChat] find| {line}");
            };

            var world = GameManager.Instance?.World;
            var player = world?.GetPrimaryPlayer();
            if (world == null || player == null)
            {
                output("No world/player loaded");
                return;
            }

            output($"Player at ({(int)player.position.x}, {(int)player.position.z})");
            output("=== Loaded NPCs ===");
            int found = 0;
            foreach (var entity in world.Entities.list)
            {
                if (!(entity is EntityAlive alive) || !NPCLLMChatMod.IsNPC(alive) || alive.entityId == player.entityId) continue;
                found++;

                float dist = Vector3.Distance(player.position, alive.position);
                var hireBits = new List<string>();
                foreach (string cvar in new[] { "Leader", "Owner" })
                {
                    if (alive.Buffs != null && alive.Buffs.HasCustomVar(cvar) && alive.Buffs.GetCustomVar(cvar) > 0f)
                        hireBits.Add($"{cvar}={alive.Buffs.GetCustomVar(cvar)}");
                }
                string hired = hireBits.Count > 0 ? "HIRED (" + string.Join(", ", hireBits) + ")" : "not hired";
                string state = alive.IsDead() ? "DEAD" : $"{alive.Health}hp";

                // SCore writes an NPC into the save only when it carries Leader or Persist;
                // an Owner-only companion is silently discarded at logout.
                bool survives = alive.Buffs != null &&
                                (alive.Buffs.HasCustomVar("Leader") || alive.Buffs.HasCustomVar("Persist"));
                string saveState = survives ? "survives logout" : "WILL VANISH AT LOGOUT (no Leader/Persist)";

                output($"  {alive.EntityName} [id {alive.entityId}] {alive.EntityClass?.entityClassName}");
                output($"    {Mathf.RoundToInt(dist)}m {Bearing(player.position, alive.position)}, at " +
                              $"({(int)alive.position.x}, {(int)alive.position.z}), {state}, {hired}");
                output($"    {saveState}");
            }
            if (found == 0) output("  (none loaded within the streamed area)");

            // Last-known position from the companion's own travel journal
            output("=== Companion's last known position ===");
            var memory = NPCMemoryStore.Load(NPCChatComponent.CompanionMemoryKey);
            if (memory == null || memory.placesVisited.Count == 0)
            {
                output("  No journal entries saved yet");
            }
            else
            {
                var last = memory.placesVisited[memory.placesVisited.Count - 1];
                var there = new Vector3(last.x, player.position.y, last.z);
                output($"  Journal: {last.place} ({last.x}, {last.z}) on Day {last.day} {last.time}");
                output($"    {Mathf.RoundToInt(Vector3.Distance(player.position, there))}m {Bearing(player.position, there)} of you");

                // The breadcrumb is refreshed as she moves, so it beats the journal when set
                if (memory.lastSeenX != 0 || memory.lastSeenZ != 0)
                {
                    var seen = new Vector3(memory.lastSeenX, player.position.y, memory.lastSeenZ);
                    output($"  Last actually seen: ({memory.lastSeenX}, {memory.lastSeenZ}) on Day {memory.lastSeenDay} {memory.lastSeenTime}");
                    output($"    {Mathf.RoundToInt(Vector3.Distance(player.position, seen))}m {Bearing(player.position, seen)} of you");
                }
            }

            // Vehicles and the drone are just as easy to lose as the companion
            if (memory != null && memory.cargoSnapshots.Count > 0)
            {
                output("=== Vehicles and stores last seen ===");
                foreach (var snap in memory.cargoSnapshots)
                {
                    if (snap.x == 0 && snap.z == 0) continue;
                    var at = new Vector3(snap.x, player.position.y, snap.z);
                    output($"  {snap.name}: ({snap.x}, {snap.z}) on Day {snap.day} {snap.time}");
                    output($"    {Mathf.RoundToInt(Vector3.Distance(player.position, at))}m {Bearing(player.position, at)} of you");
                }
            }

            float hireCount = player.Buffs.HasCustomVar("CurrentHireCount") ? player.Buffs.GetCustomVar("CurrentHireCount") : 0f;
            output($"Your CurrentHireCount = {hireCount}");
            if (found == 0 && hireCount > 0f)
            {
                output("A companion is on the books but none is loaded - walk to the position above;");
                output("if she never turns up she despawned (unhired NPCs wander off), so run 'llmchat hire set 0'.");
            }
        }

        /// <summary>
        /// SCore's EntityAliveSDX.IsSavedToFile() only writes an NPC into the save file when
        /// it has a Leader or Persist cvar; a companion hired through the Owner-only path is
        /// discarded at logout. Setting Persist makes it savable without touching its AI.
        /// </summary>
        private void MakeNearestNPCPersist()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null)
            {
                output.Output("No player found");
                return;
            }

            EntityAlive npc = FindNearestNPC(player, 15f);
            if (npc == null)
            {
                output.Output("No NPC within 15m - stand next to the companion and try again");
                return;
            }

            bool already = npc.Buffs.HasCustomVar("Leader") || npc.Buffs.HasCustomVar("Persist");
            npc.Buffs.SetCustomVar("Persist", 1f);
            Log.Out($"[NPCLLMChat] Set Persist on {npc.EntityName} [id {npc.entityId}] (was savable: {already})");
            output.Output($"{npc.EntityName} marked to persist{(already ? " (it already would have saved)" : " - it would NOT have survived logout before this")}");
            output.Output("Verify with 'llmchat find' - it should now read 'survives logout'");
        }

        /// <summary>
        /// Print the world state the nearest NPC is actually working from.
        ///
        /// Every "she says she does not know X" turns on one question - was she ever told X? -
        /// and without this the answer has to be inferred from her memory file and a reading of
        /// the code, which has been wrong as often as right. Both her journal and the wording
        /// that carries it are here, since a fact present but badly phrased reads to her exactly
        /// like a fact that is missing.
        /// </summary>
        private void DumpNearestNPCContext()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null)
            {
                output.Output("No player found");
                return;
            }

            EntityAlive npc = FindNearestNPC(player, 15f);
            var chat = npc?.GetComponent<NPCChatComponent>();
            if (chat == null)
            {
                output.Output("No NPC with a chat component within 15m - stand next to her and try again");
                return;
            }

            // Log only, bar a receipt. The console is where the conversation is read, and a few
            // thousand characters of world state dumped into it buries the thing being debugged.
            string context = chat.DumpWorldContext();
            string[] lines = context.Split('\n');
            Log.Out($"[NPCLLMChat] context| === world context given to {npc.EntityName} ===");
            foreach (string line in lines) Log.Out($"[NPCLLMChat] context| {line.TrimEnd()}");
            Log.Out("[NPCLLMChat] context| === end ===");

            output.Output($"Wrote {npc.EntityName}'s world context to the log " +
                          $"({lines.Length} lines, {context.Length} chars) - console left clear for the conversation.");
        }

        private static string Bearing(Vector3 from, Vector3 to)
        {
            string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            float angle = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;
            return dirs[Mathf.RoundToInt(angle / 45f) % 8];
        }

        private void HandleHireCommand(List<string> _params)
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null)
            {
                output.Output("No player found");
                return;
            }

            float hireCount = player.Buffs.HasCustomVar("CurrentHireCount") ? player.Buffs.GetCustomVar("CurrentHireCount") : 0f;
            output.Output($"Player CurrentHireCount = {hireCount}");

            if (_params.Count > 1 && _params[1].ToLower() == "reset")
            {
                player.Buffs.SetCustomVar("CurrentHireCount", 0f);
                output.Output("CurrentHireCount reset to 0 - talk to the NPC again to hire");
            }
            else if (_params.Count > 2 && _params[1].ToLower() == "set" && int.TryParse(_params[2], out int newCount))
            {
                player.Buffs.SetCustomVar("CurrentHireCount", newCount);
                output.Output($"CurrentHireCount set to {newCount}");
            }

            // Hire-related state of the nearest NPC, for diagnosis
            EntityAlive npc = FindNearestNPC(player, 15f);
            if (npc != null)
            {
                output.Output($"Nearest NPC: {npc.EntityName} ({npc.EntityClass?.entityClassName})");
                foreach (string cvar in new[] { "Leader", "Owner", "Hired" })
                {
                    string value = npc.Buffs.HasCustomVar(cvar) ? npc.Buffs.GetCustomVar(cvar).ToString() : "(unset)";
                    output.Output($"  {cvar} = {value}");
                }
            }
            else
            {
                output.Output("No NPC within 15m");
            }
        }

        private void HandlePersonaCommand(List<string> _params)
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            bool reload = _params.Count > 1 && _params[1].ToLower() == "reload";

            int found = 0;
            var world = GameManager.Instance?.World;
            if (world != null)
            {
                foreach (var entity in world.Entities.list)
                {
                    if (entity is EntityAlive alive)
                    {
                        var chat = alive.GetComponent<NPCChatComponent>();
                        if (chat == null) continue;
                        found++;

                        if (reload)
                        {
                            bool ok = chat.ReloadPersona();
                            output.Output($"[{chat.NPCName}] persona {(ok ? "reloaded" : "reload FAILED")}");
                        }
                        else
                        {
                            string persona = chat.PersonaText;
                            output.Output($"=== {chat.NPCName} ===");
                            output.Output(string.IsNullOrEmpty(persona)
                                ? "(no persona set - edit the memory JSON in the save's NPCLLMChat folder)"
                                : persona);
                        }
                    }
                }
            }

            if (found == 0)
            {
                output.Output("No active NPC chat sessions (talk to an NPC first, then retry)");
            }
        }

        private void HandleTTSCommand(List<string> _params)
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var tts = TTSService.Instance;

            // Default: show status
            if (_params.Count < 2)
            {
                ShowTTSStatus();
                return;
            }

            string subCommand = _params[1].ToLower();

            switch (subCommand)
            {
                case "test":
                    TestTTS();
                    break;
                case "on":
                    EnableTTS(true);
                    break;
                case "off":
                    EnableTTS(false);
                    break;
                case "voices":
                    ListVoices();
                    break;
                case "status":
                    ShowTTSStatus();
                    break;
                default:
                    output.Output($"Unknown TTS command: {subCommand}");
                    output.Output("Use: tts, tts test, tts on, tts off, tts voices");
                    break;
            }
        }

        private void ShowTTSStatus()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var tts = TTSService.Instance;
            var config = NPCLLMChatMod.TTSConfig;

            output.Output("=== TTS Status ===");
            output.Output($"TTS Enabled: {(config?.Enabled ?? false)}");
            output.Output($"TTS Initialized: {(tts?.IsInitialized ?? false)}");
            output.Output($"Server Available: {(tts?.ServerAvailable ?? false)}");

            if (tts != null && tts.RequestCount > 0)
            {
                output.Output($"Requests: {tts.RequestCount}");
                output.Output($"Last Synthesis: {tts.LastSynthesisTimeMs:F0}ms");
                output.Output($"Avg Synthesis: {tts.AvgSynthesisTimeMs:F0}ms");
            }

            if (config != null)
            {
                output.Output($"Default Voice: {config.DefaultVoice}");
                output.Output($"Companion Voice: {config.CompanionVoice}");
                output.Output($"Volume: {config.Volume:P0}");
            }

            if (!(tts?.ServerAvailable ?? false))
            {
                output.Output("");
                output.Output("To start TTS server:");
                output.Output("  python piper_server.py --port 5050");
            }
        }

        private void TestTTS()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var tts = TTSService.Instance;

            if (tts == null || !tts.IsInitialized)
            {
                output.Output("TTS not initialized");
                return;
            }

            if (!tts.ServerAvailable)
            {
                output.Output("TTS server not available. Checking...");
                tts.RefreshServerStatus();
                return;
            }

            output.Output("Testing TTS synthesis...");
            Log.Out("[NPCLLMChat] TestTTS() called");

            // Create a test audio source at player position
            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null)
            {
                output.Output("No player found for audio test");
                Log.Warning("[NPCLLMChat] TestTTS: No player found");
                return;
            }

            string testText = "Hey survivor, the wasteland is rough but we will make it through together.";
            string testVoice = NPCLLMChatMod.TTSConfig?.CompanionVoice;
            output.Output($"Testing with companion voice: {testVoice ?? "(default)"}");
            Log.Out($"[NPCLLMChat] TestTTS: Calling Synthesize with voice {testVoice}, text: {testText}");

            tts.Synthesize(
                testText,
                testVoice,
                clip =>
                {
                    Log.Out($"[NPCLLMChat] TestTTS: SUCCESS! Generated clip: {clip?.length ?? 0}s");
                    output.Output($"[SUCCESS] Generated {clip.length:F1}s audio clip");

                    // Play at player position with 2D audio for guaranteed audibility
                    var go = new GameObject("TTSTest");
                    go.transform.position = player.position;
                    var audioSource = go.AddComponent<AudioSource>();
                    audioSource.clip = clip;
                    audioSource.volume = NPCLLMChatMod.TTSConfig?.Volume ?? 0.8f;
                    audioSource.spatialBlend = 0f;  // 2D audio - always audible
                    audioSource.bypassEffects = true;
                    audioSource.bypassListenerEffects = true;
                    audioSource.bypassReverbZones = true;
                    audioSource.priority = 0;
                    audioSource.Play();

                    // Clean up after playing
                    Object.Destroy(go, clip.length + 0.5f);

                    output.Output("Playing audio...");
                },
                error =>
                {
                    Log.Warning($"[NPCLLMChat] TestTTS: ERROR - {error}");
                    output.Output($"[ERROR] TTS failed: {error}");
                    output.Output("Make sure piper_server.py is running on port 5050");
                }
            );
        }

        private void EnableTTS(bool enabled)
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var config = NPCLLMChatMod.TTSConfig;

            if (config == null)
            {
                output.Output("TTS config not loaded");
                return;
            }

            // Note: This only affects runtime state, not the config file
            // We need to update all active NPC chat components

            var world = GameManager.Instance?.World;
            if (world != null)
            {
                int updated = 0;
                foreach (var entity in world.Entities.list)
                {
                    if (entity is EntityAlive alive)
                    {
                        var chat = alive.GetComponent<NPCChatComponent>();
                        if (chat != null)
                        {
                            chat.TTSEnabled = enabled;
                            updated++;
                        }
                    }
                }
                output.Output($"TTS {(enabled ? "enabled" : "disabled")} for {updated} NPCs");
            }

            if (enabled && !TTSService.Instance.ServerAvailable)
            {
                output.Output("Warning: TTS server not available");
                TTSService.Instance.RefreshServerStatus();
            }
        }

        private void ListVoices()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var config = NPCLLMChatMod.TTSConfig;

            output.Output("=== Available Voices ===");
            output.Output($"Default: {config?.DefaultVoice ?? "en_US-lessac-medium"}");
            output.Output($"Trader: {config?.TraderVoice ?? "en_US-ryan-medium"}");
            output.Output($"Companion: {config?.CompanionVoice ?? "en_US-amy-medium"}");
            output.Output($"Bandit: {config?.BanditVoice ?? "en_US-ryan-medium"}");
            output.Output("");
            output.Output("Download more voices from:");
            output.Output("https://huggingface.co/rhasspy/piper-voices");
        }

        // ========== STT Commands ==========

        private void HandleSTTCommand(List<string> _params)
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;

            // Default: show status
            if (_params.Count < 2)
            {
                ShowSTTStatus();
                return;
            }

            string subCommand = _params[1].ToLower();

            switch (subCommand)
            {
                case "test":
                    TestSTT();
                    break;
                case "on":
                    EnableSTT(true);
                    break;
                case "off":
                    EnableSTT(false);
                    break;
                case "devices":
                    ListMicrophones();
                    break;
                case "status":
                    ShowSTTStatus();
                    break;
                case "refresh":
                    RefreshSTT();
                    break;
                default:
                    output.Output($"Unknown STT command: {subCommand}");
                    output.Output("Use: stt, stt test, stt on, stt off, stt devices, stt refresh");
                    break;
            }
        }

        private void ShowSTTStatus()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var stt = STTService.Instance;
            var mic = MicrophoneCapture.Instance;
            var config = NPCLLMChatMod.STTConfig;

            output.Output("=== STT Status ===");
            output.Output($"STT Enabled: {(config?.Enabled ?? false)}");
            output.Output($"STT Initialized: {(stt?.IsInitialized ?? false)}");
            output.Output($"Server Available: {(stt?.ServerAvailable ?? false)}");
            output.Output($"Microphone Ready: {(mic?.IsInitialized ?? false)}");
            output.Output($"Voice Input Active: {(mic?.IsEnabled ?? false)}");

            if (stt != null && stt.RequestCount > 0)
            {
                output.Output($"Requests: {stt.RequestCount}");
                output.Output($"Last Transcription: {stt.LastTranscriptionTimeMs:F0}ms");
                output.Output($"Avg Transcription: {stt.AvgTranscriptionTimeMs:F0}ms");
            }

            if (config != null)
            {
                output.Output($"Push-to-talk Key: {config.PushToTalkKey}");
                output.Output($"Max Recording: {config.MaxRecordingSeconds}s");
            }

            if (mic != null && mic.IsInitialized)
            {
                output.Output($"Selected Microphone: {mic.SelectedDevice ?? "None"}");
            }

            if (!(stt?.ServerAvailable ?? false))
            {
                output.Output("");
                output.Output("To start STT server:");
                output.Output("  python whisper_server.py --port 5051");
            }
        }

        private void TestSTT()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var stt = STTService.Instance;
            var mic = MicrophoneCapture.Instance;

            if (stt == null || !stt.IsInitialized)
            {
                output.Output("STT not initialized");
                return;
            }

            if (!stt.ServerAvailable)
            {
                output.Output("STT server not available. Checking...");
                stt.RefreshServerStatus();
                return;
            }

            if (mic == null || !mic.IsInitialized)
            {
                output.Output("Microphone not initialized");
                output.Output("Check that a microphone is connected");
                return;
            }

            output.Output("Recording for 3 seconds... Speak now!");

            // Use test recording
            mic.TestRecording(3f, wavData =>
            {
                if (wavData == null || wavData.Length < 100)
                {
                    output.Output("[ERROR] Failed to record audio");
                    return;
                }

                output.Output($"Recorded {wavData.Length} bytes, sending to server...");

                stt.Transcribe(
                    wavData,
                    text =>
                    {
                        output.Output($"[SUCCESS] Transcription: \"{text}\"");
                    },
                    error =>
                    {
                        output.Output($"[ERROR] Transcription failed: {error}");
                        output.Output("Make sure whisper_server.py is running on port 5051");
                    }
                );
            });
        }

        private void EnableSTT(bool enabled)
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var mic = MicrophoneCapture.Instance;
            var config = NPCLLMChatMod.STTConfig;

            if (config == null)
            {
                output.Output("STT config not loaded");
                return;
            }

            if (mic == null || !mic.IsInitialized)
            {
                output.Output("Microphone not initialized");
                return;
            }

            mic.IsEnabled = enabled;
            output.Output($"Voice input {(enabled ? "enabled" : "disabled")}");

            if (enabled)
            {
                output.Output($"Hold '{config.PushToTalkKey}' to talk to NPCs");

                if (!STTService.Instance.ServerAvailable)
                {
                    output.Output("Warning: STT server not available");
                    STTService.Instance.RefreshServerStatus();
                }
            }
        }

        private void ListMicrophones()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var mic = MicrophoneCapture.Instance;

            output.Output("=== Available Microphones ===");

            string[] devices = mic?.GetDevices() ?? Microphone.devices;

            if (devices == null || devices.Length == 0)
            {
                output.Output("  No microphones found!");
                output.Output("  Check your audio settings and permissions");
                return;
            }

            string selected = mic?.SelectedDevice ?? "";

            for (int i = 0; i < devices.Length; i++)
            {
                string marker = devices[i] == selected ? " [SELECTED]" : "";
                output.Output($"  [{i}] {devices[i]}{marker}");
            }

            output.Output("");
            output.Output($"Total: {devices.Length} device(s)");
        }

        private void RefreshSTT()
        {
            var output = SingletonMonoBehaviour<SdtdConsole>.Instance;
            var stt = STTService.Instance;

            if (stt == null || !stt.IsInitialized)
            {
                output.Output("STT not initialized");
                return;
            }

            output.Output("Refreshing STT server connection...");
            stt.RefreshServerStatus();
            output.Output($"Server available: {stt.ServerAvailable}");
        }
    }
}
