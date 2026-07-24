using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NPCLLMChat.Actions;
using NPCLLMChat.TTS;

namespace NPCLLMChat
{
    /// <summary>
    /// Attach this component to NPCCore NPCs to enable LLM-powered conversations.
    /// Manages conversation state, history, and personality for each NPC.
    /// </summary>
    public class NPCChatComponent : MonoBehaviour
    {
        // Reference to the NPC entity
        private EntityAlive _npcEntity;
        private int _entityId;

        // Conversation state
        private List<ChatMessage> _conversationHistory = new List<ChatMessage>();
        private int _maxHistoryLength = 5;
        private bool _isWaitingForResponse = false;
        private string _currentResponse = "";
        private bool _isTyping = false;

        // NPC Personality
        private string _npcName = "Survivor";
        private string _systemPrompt;
        private string _personalityTraits = "";

        // Configuration
        private LLMConfig _config;

        // TTS Audio Player
        private NPCAudioPlayer _audioPlayer;
        private bool _ttsEnabled = true;

        // Events for UI integration
        public event Action<string> OnResponseStarted;
        public event Action<string> OnResponseComplete;
        public event Action<string> OnTypingUpdate;
        public event Action<string> OnError;
        public event Action<NPCAction> OnActionExecuted;
        public event Action<string> OnSpeechStarted;
        public event Action OnSpeechComplete;

        // Action system integration
        private bool _actionsEnabled = true;
        private EntityPlayer _lastInteractingPlayer;

        // Persistent memory (conversation + travel journal), saved per NPC per save-game.
        // Hired companions all share one memory key, so THE companion keeps a single
        // continuous history across renames, deaths, respawns and re-hires.
        private NPCMemory _memory;
        private string _memoryKey;
        private const string CompanionMemoryKey = "companion";
        private string _currentPlace;
        private float _nextPlaceCheck;
        private const float PlaceCheckIntervalSeconds = 5f;
        private const int MaxJournalEntries = 40;

        public void Initialize(EntityAlive npcEntity, LLMConfig config)
        {
            _npcEntity = npcEntity;
            _entityId = npcEntity.entityId;
            _config = config;
            _maxHistoryLength = config.ContextMemory;

            // Extract NPC name from entity if available
            _npcName = GetNPCName();

            // Restore persisted memory from the save folder, if this NPC has any
            _memoryKey = IsHiredCompanion() ? CompanionMemoryKey : _npcName;
            _memory = LoadMemoryForKey();
            foreach (var msg in _memory.messages)
            {
                _conversationHistory.Add(new ChatMessage(msg.role, msg.content));
            }
            TrimHistory();

            // Build personality-specific system prompt
            _systemPrompt = BuildSystemPrompt();

            // Initialize TTS audio player if TTS is enabled
            var ttsConfig = NPCLLMChatMod.TTSConfig;
            if (ttsConfig != null && ttsConfig.Enabled && TTSService.Instance.IsInitialized)
            {
                _audioPlayer = gameObject.AddComponent<NPCAudioPlayer>();
                _audioPlayer.Initialize(npcEntity, ttsConfig);
                _ttsEnabled = true;
                Log.Out($"[NPCLLMChat] TTS enabled for NPC: {_npcName}");
            }
            else
            {
                _ttsEnabled = false;
            }

            Log.Out($"[NPCLLMChat] Initialized chat component for NPC: {_npcName} (ID: {_entityId})");
        }

        private string GetNPCName()
        {
            // Try to get name from NPCCore or entity
            if (_npcEntity != null)
            {
                // Check for custom name in entity
                string entityName = _npcEntity.EntityName;
                if (!string.IsNullOrEmpty(entityName) && entityName != "playerMale" && entityName != "playerFemale")
                {
                    return entityName;
                }

                // Generate a name based on entity ID for consistency
                string[] names = { "Alex", "Jordan", "Sam", "Riley", "Casey", "Morgan", "Quinn", "Avery", "Blake", "Drew" };
                return names[_entityId % names.Length];
            }
            return "Survivor";
        }

        private string BuildSystemPrompt()
        {
            // Combine base system prompt with NPC-specific details
            string basePrompt = _config.SystemPrompt;

            // Add NPC identity
            string identityPrompt = $"Your name is {_npcName}. ";

            // Add location context if available
            string locationContext = "";
            if (_npcEntity != null)
            {
                Vector3 pos = _npcEntity.position;
                // Could be expanded to detect biome, nearby POIs, etc.
                locationContext = "You are currently surviving in the wasteland. ";
            }

            // Add any personality traits
            if (!string.IsNullOrEmpty(_personalityTraits))
            {
                return $"{identityPrompt}{locationContext}{_personalityTraits} {basePrompt}";
            }

            return $"{identityPrompt}{locationContext}{basePrompt}";
        }

        /// <summary>
        /// Set custom personality traits for this NPC
        /// </summary>
        public void SetPersonality(string traits)
        {
            _personalityTraits = traits;
            _systemPrompt = BuildSystemPrompt();
        }

        /// <summary>
        /// Process a message from the player and get an LLM response
        /// </summary>
        public void ProcessPlayerMessage(string playerMessage, Action<string> onComplete = null)
        {
            ProcessPlayerMessage(playerMessage, null, onComplete);
        }

        /// <summary>
        /// Process a message from the player with player reference for actions
        /// </summary>
        public void ProcessPlayerMessage(string playerMessage, EntityPlayer player, Action<string> onComplete = null)
        {
            if (_isWaitingForResponse)
            {
                Log.Out($"[NPCLLMChat] NPC {_npcName} is still thinking...");
                return;
            }

            if (string.IsNullOrWhiteSpace(playerMessage))
            {
                return;
            }

            _lastInteractingPlayer = player;
            _isWaitingForResponse = true;
            OnResponseStarted?.Invoke("...");

            // Add player message to history
            _conversationHistory.Add(new ChatMessage("Player", playerMessage));
            TrimHistory();

            // Build action-aware system prompt, with current world state appended
            string actionPrompt = _actionsEnabled ? BuildActionSystemPrompt() : _systemPrompt;
            actionPrompt += BuildWorldContext();

            // Send to LLM
            LLMService.Instance.SendChatRequest(
                _entityId,
                actionPrompt,
                _conversationHistory,
                playerMessage,
                response => HandleLLMResponse(response, onComplete),
                error => HandleLLMError(error)
            );
        }

        /// <summary>
        /// Build system prompt that includes action instructions for the LLM
        /// </summary>
        private string BuildActionSystemPrompt()
        {
            return _systemPrompt + @"

IMPORTANT: You can perform actions based on player requests. When you agree to do something, include a JSON action block in your response.

Available actions and when to use them:
- follow: Player asks you to come with them, accompany them, follow them
- stop: Player asks you to stop following or stay where you are
- wait: Player asks you to wait or hold position
- guard: Player asks you to guard, protect, or watch an area
- trade: Player wants to trade, buy, sell, or see your items
- give: You decide to give the player an item
- heal: Player asks for healing or medical help (if you're capable)
- remember: Player asks you to remember/mark/note the current location (include a ""label"" field naming it)
- refuse: You decline a request (dangerous, unreasonable, out of character)

Response format when taking action:
{""action"": ""follow"", ""dialogue"": ""Sure, I'll come with you. Lead the way.""}

For dialogue only (no action):
Just respond naturally without JSON.

Examples:
Player: ""Come with me, I need backup""
Response: {""action"": ""follow"", ""dialogue"": ""Alright, I've got your back. Let's move.""}

Player: ""What's it like out here?""
Response: It's rough. Every day is a fight for survival, but we manage.

Player: ""Can you give me some bandages?""
Response: {""action"": ""give"", ""dialogue"": ""Here, take these. Stay safe out there."", ""item"": ""bandage"", ""amount"": 2}

Player: ""Remember this spot"" or ""Mark this place: bandits""
Response: {""action"": ""remember"", ""dialogue"": ""Got it. I'll remember this place."", ""label"": ""bandits""}

Stay in character. Only perform actions that make sense for your personality.";
        }

        private void HandleLLMResponse(string response, Action<string> onComplete)
        {
            _isWaitingForResponse = false;

            // Parse response for actions
            NPCAction action = null;
            string dialogueResponse = response;

            if (_actionsEnabled)
            {
                action = ActionParser.Parse(response);
                if (action != null && !string.IsNullOrEmpty(action.DialogueBefore))
                {
                    dialogueResponse = action.DialogueBefore;
                }
                Log.Out($"[NPCLLMChat] Parsed action: {action?.Type ?? NPCActionType.None}");
            }

            // Trim response if too long
            if (dialogueResponse.Length > _config.MaxResponseLength)
            {
                dialogueResponse = dialogueResponse.Substring(0, _config.MaxResponseLength);
                // Try to end at a sentence
                int lastPeriod = dialogueResponse.LastIndexOf('.');
                if (lastPeriod > _config.MaxResponseLength / 2)
                {
                    dialogueResponse = dialogueResponse.Substring(0, lastPeriod + 1);
                }
            }

            // Add NPC response to history (store original for context)
            _conversationHistory.Add(new ChatMessage("NPC", dialogueResponse));
            TrimHistory();
            PersistMemory();
            MaybeSummarize();

            // Execute action if parsed
            if (action != null && action.Type != NPCActionType.None && _npcEntity != null)
            {
                try
                {
                    if (action.Type == NPCActionType.Remember)
                    {
                        // Memory action - handled here, not by the world-action executor
                        RememberPlace(action.GetParam("label", action.GetParam("name", "this spot")));
                    }
                    else
                    {
                        ActionExecutor.Instance.ExecuteAction(_npcEntity, _lastInteractingPlayer, action);
                    }
                    OnActionExecuted?.Invoke(action);
                }
                catch (Exception ex)
                {
                    Log.Error($"[NPCLLMChat] Action execution failed: {ex.Message}");
                }
            }

            // Trigger typing effect if enabled
            if (_config.ShowTypingIndicator && _config.TypingDelayMs > 0)
            {
                StartCoroutine(TypeResponseCoroutine(dialogueResponse, onComplete));
            }
            else
            {
                _currentResponse = dialogueResponse;
                OnResponseComplete?.Invoke(dialogueResponse);
                onComplete?.Invoke(dialogueResponse);
            }

            // Trigger TTS if enabled
            if (_ttsEnabled && _audioPlayer != null && TTSService.Instance.ServerAvailable)
            {
                OnSpeechStarted?.Invoke(dialogueResponse);
                _audioPlayer.Speak(dialogueResponse, () => OnSpeechComplete?.Invoke());
            }
        }

        private IEnumerator TypeResponseCoroutine(string fullResponse, Action<string> onComplete)
        {
            _isTyping = true;
            _currentResponse = "";

            foreach (char c in fullResponse)
            {
                _currentResponse += c;
                OnTypingUpdate?.Invoke(_currentResponse);
                yield return new WaitForSeconds(_config.TypingDelayMs / 1000f);
            }

            _isTyping = false;
            OnResponseComplete?.Invoke(fullResponse);
            onComplete?.Invoke(fullResponse);
        }

        private void HandleLLMError(string error)
        {
            _isWaitingForResponse = false;

            // Provide a fallback response
            string fallback = GetFallbackResponse();
            OnError?.Invoke(error);
            OnResponseComplete?.Invoke(fallback);

            Log.Warning($"[NPCLLMChat] Error for NPC {_npcName}: {error}. Using fallback.");
        }

        private string GetFallbackResponse()
        {
            // Immersion-preserving fallback responses
            string[] fallbacks = {
                "*looks distracted* Sorry, what was that?",
                "*pauses, scanning the horizon* Hold on... thought I heard something.",
                "Hmm? My mind wandered for a second there.",
                "*rubs temples* Long day. What were you saying?",
                "Give me a moment... *checks surroundings*"
            };
            return fallbacks[UnityEngine.Random.Range(0, fallbacks.Length)];
        }

        private void TrimHistory()
        {
            // Expired messages queue for long-term summarization instead of vanishing
            while (_conversationHistory.Count > _maxHistoryLength * 2) // *2 for player + NPC pairs
            {
                var expired = _conversationHistory[0];
                _conversationHistory.RemoveAt(0);
                _memory?.pendingSummary.Add(new SavedMessage { role = expired.Role, content = expired.Content });
            }
            // Bound the queue if summarization keeps failing
            while (_memory != null && _memory.pendingSummary.Count > 60)
            {
                _memory.pendingSummary.RemoveAt(0);
            }
        }

        /// <summary>
        /// Clear conversation history (e.g., when player leaves and returns)
        /// </summary>
        public void ClearHistory()
        {
            _conversationHistory.Clear();
            if (_memory != null)
            {
                // Full conversational amnesia; travel journal and marked places survive
                _memory.longTermMemory = null;
                _memory.pendingSummary.Clear();
                NPCMemoryStore.DeleteMessages(_memoryKey, _memory);
            }
        }

        /// <summary>
        /// Re-read the persona field from the memory file on disk, so hand edits
        /// apply without restarting the game (the mod never writes persona content,
        /// so this cannot lose anything).
        /// </summary>
        public bool ReloadPersona()
        {
            if (_memory == null) return false;
            var fromDisk = NPCMemoryStore.Load(_memoryKey);
            if (fromDisk == null) return false;
            _memory.persona = fromDisk.persona;
            Log.Out($"[NPCLLMChat] {_npcName} persona reloaded ({_memory.persona?.Length ?? 0} chars)");
            return true;
        }

        public string PersonaText => _memory?.persona;

        private void PersistMemory()
        {
            if (_memory == null) return;

            _memory.npcName = _npcName;
            _memory.messages.Clear();
            foreach (var msg in _conversationHistory)
            {
                _memory.messages.Add(new SavedMessage { role = msg.Role, content = msg.Content });
            }
            NPCMemoryStore.Save(_memoryKey, _memory);
        }

        /// <summary>
        /// True when this NPC has been hired by a player (SCore stores the hiring
        /// player's entity id in the Leader/Owner cvars).
        /// </summary>
        private bool IsHiredCompanion()
        {
            try
            {
                if (_npcEntity?.Buffs == null) return false;
                foreach (string cvar in new[] { "Leader", "Owner" })
                {
                    if (_npcEntity.Buffs.HasCustomVar(cvar) && _npcEntity.Buffs.GetCustomVar(cvar) > 0f)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private NPCMemory LoadMemoryForKey()
        {
            var memory = NPCMemoryStore.Load(_memoryKey);
            if (_memoryKey == CompanionMemoryKey)
            {
                // Fold in anything recorded under this NPC's name before it was hired
                var named = NPCMemoryStore.Load(_npcName);
                if (named != null)
                {
                    memory = MergeMemories(memory, named);
                    NPCMemoryStore.DeleteFile(_npcName);
                    NPCMemoryStore.Save(CompanionMemoryKey, memory);
                    Log.Out($"[NPCLLMChat] Folded {_npcName}'s memory into the companion memory");
                }
            }
            return memory ?? new NPCMemory { npcName = _npcName };
        }

        /// <summary>
        /// Hired mid-session: switch this NPC onto the shared companion memory,
        /// keeping both the prior companion history and the current conversation.
        /// </summary>
        private void RefreshMemoryKey()
        {
            if (_memoryKey == CompanionMemoryKey || !IsHiredCompanion()) return;

            Log.Out($"[NPCLLMChat] {_npcName} is now the player's companion - unifying memory");
            string oldKey = _memoryKey;
            _memoryKey = CompanionMemoryKey;

            var existing = NPCMemoryStore.Load(CompanionMemoryKey);
            if (existing != null)
            {
                _memory = MergeMemories(existing, _memory);
                _conversationHistory.Clear();
                foreach (var msg in _memory.messages)
                {
                    _conversationHistory.Add(new ChatMessage(msg.role, msg.content));
                }
                TrimHistory();
            }
            NPCMemoryStore.DeleteFile(oldKey);
            PersistMemory();
        }

        /// <summary>
        /// Append newer memory onto older; the newer side wins on journal revisits.
        /// </summary>
        private static NPCMemory MergeMemories(NPCMemory older, NPCMemory newer)
        {
            if (older == null) return newer;
            if (newer == null) return older;

            older.messages.AddRange(newer.messages);
            foreach (var visit in newer.placesVisited)
            {
                older.placesVisited.RemoveAll(p => p.place == visit.place);
                older.placesVisited.Add(visit);
            }
            foreach (var mark in newer.markedPlaces)
            {
                older.markedPlaces.RemoveAll(m => m.label == mark.label);
                older.markedPlaces.Add(mark);
            }
            older.pendingSummary.AddRange(newer.pendingSummary);
            older.persona = string.IsNullOrEmpty(older.persona) ? newer.persona : older.persona;
            if (!string.IsNullOrEmpty(newer.longTermMemory))
            {
                older.longTermMemory = string.IsNullOrEmpty(older.longTermMemory)
                    ? newer.longTermMemory
                    : older.longTermMemory + "\n" + newer.longTermMemory;
            }
            older.npcName = newer.npcName ?? older.npcName;
            return older;
        }

        /// <summary>
        /// Store the NPC's current location under a player-given label.
        /// </summary>
        private void RememberPlace(string label)
        {
            if (_memory == null || _npcEntity == null || string.IsNullOrWhiteSpace(label)) return;

            int day; string time;
            WorldContextHelper.GetGameDayTime(out day, out time);

            _memory.markedPlaces.RemoveAll(m => m.label == label);
            _memory.markedPlaces.Add(new MarkedPlace
            {
                label = label,
                poi = WorldContextHelper.GetPOINameAt(_npcEntity.position),
                day = day,
                time = time,
                x = (int)_npcEntity.position.x,
                z = (int)_npcEntity.position.z
            });
            while (_memory.markedPlaces.Count > 30)
            {
                _memory.markedPlaces.RemoveAt(0);
            }
            NPCMemoryStore.Save(_memoryKey, _memory);
            Log.Out($"[NPCLLMChat] {_npcName} marked place '{label}' at ({(int)_npcEntity.position.x}, {(int)_npcEntity.position.z})");
        }

        // ========== Long-term memory summarization ==========

        private bool _isSummarizing;
        private const int SummarizeBatchSize = 10; // messages (5 exchanges) per summarization pass

        private void MaybeSummarize()
        {
            if (_isSummarizing || _memory == null || _memory.pendingSummary.Count < SummarizeBatchSize) return;

            _isSummarizing = true;
            int batchCount = _memory.pendingSummary.Count;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"You maintain the private long-term memory of {_npcName}, an NPC companion in a post-apocalyptic survival game.");
            sb.AppendLine();
            sb.AppendLine("Current long-term memory:");
            sb.AppendLine(string.IsNullOrEmpty(_memory.longTermMemory) ? "(empty)" : _memory.longTermMemory);
            sb.AppendLine();
            sb.AppendLine("Conversation excerpts that are about to be forgotten:");
            foreach (var msg in _memory.pendingSummary)
            {
                sb.AppendLine($"{(msg.role == "NPC" ? _npcName : "Player")}: {msg.content}");
            }
            sb.AppendLine();
            sb.AppendLine("Rewrite the long-term memory, merging in anything from the excerpts worth keeping: facts about the player, promises made, shared events, plans, opinions formed. Keep it under 150 words of plain prose. Output only the memory text, no preamble.");

            LLMService.Instance.SendCompletionRequest(
                sb.ToString(),
                0.3f,
                summary =>
                {
                    _isSummarizing = false;
                    if (_memory == null || string.IsNullOrWhiteSpace(summary)) return;
                    _memory.longTermMemory = summary.Trim();
                    _memory.pendingSummary.RemoveRange(0, Math.Min(batchCount, _memory.pendingSummary.Count));
                    NPCMemoryStore.Save(_memoryKey, _memory);
                    Log.Out($"[NPCLLMChat] {_npcName} long-term memory updated ({_memory.longTermMemory.Length} chars)");
                },
                error =>
                {
                    _isSummarizing = false;
                    Log.Warning($"[NPCLLMChat] Summarization failed (will retry later): {error}");
                });
        }

        // ========== Travel journal + world context ==========

        private void Update()
        {
            if (_memory == null || _npcEntity == null) return;
            if (Time.unscaledTime < _nextPlaceCheck) return;
            _nextPlaceCheck = Time.unscaledTime + PlaceCheckIntervalSeconds;

            try
            {
                RefreshMemoryKey();
                CheckCurrentPlace();
            }
            catch (Exception ex)
            {
                // World/POI APIs can be touchy during load/unload; never break the NPC over the journal
                Log.Warning($"[NPCLLMChat] Travel journal update failed: {ex.Message}");
                _nextPlaceCheck = Time.unscaledTime + 60f;
            }
        }

        private void CheckCurrentPlace()
        {
            string place = WorldContextHelper.GetPOINameAt(_npcEntity.position);
            if (place == _currentPlace) return;
            _currentPlace = place;
            if (string.IsNullOrEmpty(place)) return;

            // Revisiting a known place just updates its timestamp; new places append
            int day; string time;
            WorldContextHelper.GetGameDayTime(out day, out time);

            var existing = _memory.placesVisited.Find(p => p.place == place);
            if (existing != null)
            {
                existing.day = day;
                existing.time = time;
                _memory.placesVisited.Remove(existing);
                _memory.placesVisited.Add(existing);
            }
            else
            {
                _memory.placesVisited.Add(new PlaceVisit
                {
                    place = place,
                    day = day,
                    time = time,
                    x = (int)_npcEntity.position.x,
                    z = (int)_npcEntity.position.z
                });
                while (_memory.placesVisited.Count > MaxJournalEntries)
                {
                    _memory.placesVisited.RemoveAt(0);
                }
                Log.Out($"[NPCLLMChat] {_npcName} travel journal: arrived at {place} (Day {day} {time})");
            }
            NPCMemoryStore.Save(_memoryKey, _memory);
        }

        private string BuildWorldContext()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("[Current world state - facts you know:]");

                int day; string time;
                WorldContextHelper.GetGameDayTime(out day, out time);
                sb.AppendLine($"It is Day {day}, around {time}.");

                if (!string.IsNullOrEmpty(_currentPlace))
                {
                    sb.AppendLine($"You are currently at: {_currentPlace}.");
                }

                if (_memory != null && !string.IsNullOrEmpty(_memory.persona))
                {
                    sb.AppendLine("Who you are - your character, always stay true to this:");
                    sb.AppendLine(_memory.persona);
                }

                if (_memory != null && !string.IsNullOrEmpty(_memory.longTermMemory))
                {
                    sb.AppendLine("Things you remember from your shared history with the player:");
                    sb.AppendLine(_memory.longTermMemory);
                }

                if (_memory != null && _memory.markedPlaces.Count > 0)
                {
                    sb.AppendLine("Locations the player asked you to remember:");
                    foreach (var mark in _memory.markedPlaces)
                    {
                        string at = string.IsNullOrEmpty(mark.poi) ? "" : $" at {mark.poi},";
                        sb.AppendLine($"- \"{mark.label}\" (marked Day {mark.day} {mark.time},{at} map position {mark.x} E/W, {mark.z} N/S)");
                    }
                }

                string nearby = WorldContextHelper.DescribeNearbyPOIs(_npcEntity.position, 5, 1000f);
                if (!string.IsNullOrEmpty(nearby))
                {
                    sb.AppendLine($"Locations you know of nearby: {nearby}.");
                }

                if (_memory != null && _memory.placesVisited.Count > 0)
                {
                    sb.AppendLine("Places you have visited (oldest first, with when you were last there):");
                    foreach (var visit in _memory.placesVisited)
                    {
                        sb.AppendLine($"- {visit.place} (Day {visit.day} {visit.time}, at map position {visit.x} E/W, {visit.z} N/S)");
                    }
                }

                sb.AppendLine("When asked about places or directions, answer from these facts. If you don't know a place, say so honestly.");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] World context failed: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Get the current conversation history
        /// </summary>
        public List<ChatMessage> GetHistory()
        {
            return new List<ChatMessage>(_conversationHistory);
        }

        public bool IsWaitingForResponse => _isWaitingForResponse;
        public bool IsTyping => _isTyping;
        public string CurrentResponse => _currentResponse;
        public string NPCName => _npcName;
        public EntityAlive NPCEntity => _npcEntity;
        public bool ActionsEnabled
        {
            get => _actionsEnabled;
            set => _actionsEnabled = value;
        }

        // TTS properties and methods
        public bool TTSEnabled
        {
            get => _ttsEnabled;
            set => _ttsEnabled = value;
        }

        public bool IsSpeaking => _audioPlayer != null && _audioPlayer.IsSpeaking;

        /// <summary>
        /// Stop any current speech playback
        /// </summary>
        public void StopSpeaking()
        {
            if (_audioPlayer != null)
            {
                _audioPlayer.StopSpeaking();
            }
        }

        /// <summary>
        /// Set a custom voice for this NPC
        /// </summary>
        public void SetVoice(string voiceId)
        {
            if (_audioPlayer != null)
            {
                _audioPlayer.SetVoice(voiceId);
            }
        }

        /// <summary>
        /// Get the current state of this NPC from the action system
        /// </summary>
        public NPCState GetCurrentState()
        {
            return ActionExecutor.Instance?.GetNPCState(_entityId);
        }
    }
}
