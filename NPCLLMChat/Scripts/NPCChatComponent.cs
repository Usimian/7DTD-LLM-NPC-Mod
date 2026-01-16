using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NPCLLMChat.Actions;

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

        // Events for UI integration
        public event Action<string> OnResponseStarted;
        public event Action<string> OnResponseComplete;
        public event Action<string> OnTypingUpdate;
        public event Action<string> OnError;
        public event Action<NPCAction> OnActionExecuted;

        // Action system integration
        private bool _actionsEnabled = true;
        private EntityPlayer _lastInteractingPlayer;

        public void Initialize(EntityAlive npcEntity, LLMConfig config)
        {
            _npcEntity = npcEntity;
            _entityId = npcEntity.entityId;
            _config = config;
            _maxHistoryLength = config.ContextMemory;

            // Extract NPC name from entity if available
            _npcName = GetNPCName();

            // Build personality-specific system prompt
            _systemPrompt = BuildSystemPrompt();

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

            // Build action-aware system prompt
            string actionPrompt = _actionsEnabled ? BuildActionSystemPrompt() : _systemPrompt;

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

            // Execute action if parsed
            if (action != null && action.Type != NPCActionType.None && _npcEntity != null)
            {
                try
                {
                    ActionExecutor.Instance.ExecuteAction(_npcEntity, _lastInteractingPlayer, action);
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
            // Keep conversation history within limits
            while (_conversationHistory.Count > _maxHistoryLength * 2) // *2 for player + NPC pairs
            {
                _conversationHistory.RemoveAt(0);
            }
        }

        /// <summary>
        /// Clear conversation history (e.g., when player leaves and returns)
        /// </summary>
        public void ClearHistory()
        {
            _conversationHistory.Clear();
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

        /// <summary>
        /// Get the current state of this NPC from the action system
        /// </summary>
        public NPCState GetCurrentState()
        {
            return ActionExecutor.Instance?.GetNPCState(_entityId);
        }
    }
}
