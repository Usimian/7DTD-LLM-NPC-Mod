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
        public const string CompanionMemoryKey = "companion";

        public bool IsCompanion => _memoryKey == CompanionMemoryKey;
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

            // Responses are read aloud verbatim by TTS, so narration must never appear in them
            const string speechOnlyRule = " Your response is spoken out loud, word for word. Respond ONLY with " +
                "the words you actually say, in first person. Never narrate or describe your actions, movements, " +
                "tone, or expressions. Never write about yourself in third person. No stage directions like " +
                "*smiles* or (sighs). Never voice a sound effect or gesture as a word either " +
                "(no \"Click.\", no \"Sigh.\", no \"Shrug.\"). When you feel something, say it in " +
                "plain words - \"I'm nervous about this\" - instead of acting it out.";

            // Add any personality traits
            if (!string.IsNullOrEmpty(_personalityTraits))
            {
                return $"{identityPrompt}{locationContext}{_personalityTraits} {basePrompt}{speechOnlyRule}";
            }

            return $"{identityPrompt}{locationContext}{basePrompt}{speechOnlyRule}";
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
            NoteHowPlayerSpoke(playerMessage);
            TrimHistory();

            // Build action-aware system prompt, with current world state appended
            string actionPrompt = _actionsEnabled ? BuildActionSystemPrompt() : _systemPrompt;
            actionPrompt += BuildWorldContext();
            // Last instruction before the question carries the most weight, and the length rule
            // in the base prompt was being buried under a thousand words of persona and state.
            actionPrompt += BuildAnswerLengthGuidance(playerMessage);

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
- heal: Player asks for healing or medical help - or you can see they are hurt and you decide to patch them up yourself (if you're capable)
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

Stay in character. Only perform actions that make sense for your personality.
The ""dialogue"" field and any plain response are spoken aloud word for word: only words you actually say, no narration, no describing your actions.";
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

            // She is allowed to let something pass without answering
            if (IsSilence(dialogueResponse))
            {
                Log.Out($"[NPCLLMChat] {_npcName} chose not to answer");
                _currentResponse = "";
                onComplete?.Invoke("");
                OnResponseComplete?.Invoke("");
                return;
            }

            // Keep whole sentences within the word budget rather than cutting mid-word
            dialogueResponse = CapLength(dialogueResponse);

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

            // Trigger TTS if enabled. Stage directions (*clicks tongue*, (sighs), [looks
            // away]) stay in the displayed text but must not be read aloud.
            string speech = StripStageDirections(dialogueResponse);
            if (_ttsEnabled && _audioPlayer != null && TTSService.Instance.ServerAvailable && !string.IsNullOrWhiteSpace(speech))
            {
                OnSpeechStarted?.Invoke(speech);
                _audioPlayer.Speak(speech, () => OnSpeechComplete?.Invoke());
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

        // Only a runaway-reply backstop. The prompt governs ordinary length; this used to sit
        // at 60 and was clipping legitimate stories mid-telling.
        private const int MaxReplyWords = 120;

        /// <summary>
        /// People are not randomly terse or chatty - they run on a mood that comes from their
        /// situation. Hers is derived from what is actually happening: a fight a minute ago, a
        /// wound, the small hours, a horde due tomorrow, or a fresh cup of coffee. The mood sets
        /// both her manner and how much she says, and repetition still overrides it.
        /// </summary>
        private string BuildAnswerLengthGuidance(string playerMessage)
        {
            int repeats = CountRecentRepeats(playerMessage);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine();

            string mood, manner;
            int wordLimit;
            DescribeMood(out mood, out manner, out wordLimit);
            _quietMood = _moodKey == "hurt" || _moodKey == "combat" || _moodKey == "night" ||
                         _moodKey == "dawn" || _moodKey == "fogbound" || _moodKey == "storm";
            sb.AppendLine($"[Your state right now]\n{mood} {manner}");
            sb.AppendLine("If the player asks why you are quiet, short or chirpy, tell him plainly how you " +
                          "feel - do not pretend to be fine.");
            sb.AppendLine(DescribeRapport());
            sb.AppendLine();
            sb.AppendLine("[How to answer - this overrides every style note above]");

            if (repeats >= 2)
            {
                sb.AppendLine($"The player has now asked you this {repeats + 1} times in a row and you have " +
                              "answered every single time. React the way your mood above would: call it out, " +
                              "let the irritation show. Under 20 words.");
            }
            else if (repeats == 1)
            {
                sb.AppendLine("You answered this exact question a moment ago. Repeat yourself in ONE WORD - " +
                              "nothing else, no explanation, no joke.");
            }
            else
            {
                sb.AppendLine($"Answer in one sentence, {wordLimit} words at most, in the manner described above. " +
                              "Even a yes or no carries a few words of your own voice the FIRST time it is asked - " +
                              "save the bare one-word answer for when the player repeats himself.");
                sb.AppendLine("Only when the player asks for a story, an explanation or directions do you get " +
                              "up to 80 words - then tell it properly, all the way to the end.");
                sb.AppendLine("Never pad the answer with a plan, a follow-up question or a joke tacked on the end.");
                sb.AppendLine("Not everything deserves a reply. If the player is making small talk, thinking out " +
                              "loud, or saying something that asks nothing of you, a grunt is plenty - \"Mm.\", " +
                              "\"Yeah.\", \"Hm.\" - or say nothing at all by answering with exactly <silence> " +
                              "and no other characters. " + (_quietMood
                                  ? "In the state you are in, silence or a grunt is the likely answer."
                                  : "Use it when it fits; a real answer is still the norm when you are asked something."));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Where the two of them stand, in her own terms. Rapport moves slowly and persists in
        /// her memory file, so kindness and neglect both accumulate across sessions.
        /// </summary>
        private string DescribeRapport()
        {
            float r = _memory?.rapport ?? 0f;
            if (r > 0.45f)
                return "You are fond of this player and it shows - you give them the benefit of the doubt.";
            if (r > 0.15f)
                return "You get on well with this player.";
            if (r < -0.45f)
                return "This player has worn out your patience lately. You are short with them and slow to volunteer anything.";
            if (r < -0.15f)
                return "You are a little cool towards this player at the moment.";
            return "You and this player are on ordinary working terms.";
        }

        private void AdjustRapport(float delta, string why)
        {
            if (_memory == null) return;
            float before = _memory.rapport;
            _memory.rapport = Mathf.Clamp(_memory.rapport + delta, -1f, 1f);
            if (Mathf.Abs(_memory.rapport - before) > 0.001f)
            {
                Log.Out($"[NPCLLMChat] {_npcName} rapport {before:F2} -> {_memory.rapport:F2} ({why})");
            }
        }

        private static readonly string[] KindWords =
            { "thank", "thanks", "good job", "nice work", "well done", "appreciate", "you ok", "you alright", "sorry" };
        private static readonly string[] UnkindWords =
            { "shut up", "idiot", "useless", "stupid", "shut it", "worthless", "quiet" };

        /// <summary>
        /// What the player says is the loudest signal of how they treat her.
        /// </summary>
        private void NoteHowPlayerSpoke(string playerMessage)
        {
            string text = (playerMessage ?? "").ToLowerInvariant();
            foreach (string word in KindWords)
            {
                if (text.Contains(word)) { AdjustRapport(0.06f, $"kind words: {word}"); return; }
            }
            foreach (string word in UnkindWords)
            {
                if (text.Contains(word)) { AdjustRapport(-0.1f, $"harsh words: {word}"); return; }
            }
        }

        /// <summary>
        /// Highest-priority thing happening to her wins: pain, then danger, then duty, then
        /// stimulants, then the hour of the night.
        /// </summary>
        private void DescribeMood(out string mood, out string manner, out int wordLimit)
        {
            ResolveMood(out string key, out mood, out manner, out wordLimit);

            // Moods have inertia: pain and gunfire land instantly, but calm has to be earned -
            // she does not snap from keyed-up to cheerful the moment the shooting stops.
            bool urgent = key == "hurt" || key == "combat" || key == "player-hurt";
            if (key != _moodKey)
            {
                bool leavingUrgent = _moodKey == "hurt" || _moodKey == "combat" || _moodKey == "player-hurt";
                if (!urgent && leavingUrgent && Time.unscaledTime - _moodSetAt < 120f)
                {
                    ResolveMoodByKey(_moodKey, out mood, out manner, out wordLimit);
                    return;
                }
                _moodKey = key;
                _moodSetAt = Time.unscaledTime;
            }
        }

        private void ResolveMood(out string key, out string mood, out string manner, out int wordLimit)
        {
            float health = _npcEntity.GetMaxHealth() > 0
                ? (float)_npcEntity.Health / _npcEntity.GetMaxHealth() : 1f;
            float sinceCombat = Time.unscaledTime - _lastCombatTime;
            float sinceCoffee = Time.unscaledTime - _lastCaffeineTime;

            int day; string time;
            WorldContextHelper.GetGameDayTime(out day, out time);
            int hour = 12;
            if (!string.IsNullOrEmpty(time) && time.Length >= 2) int.TryParse(time.Substring(0, 2), out hour);
            int bloodMoonDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
            bool hordeSoon = bloodMoonDay > 0 && bloodMoonDay - day <= 1;

            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            bool playerBadlyHurt = player?.Stats?.Health != null && player.Stats.Health.ValuePercentUI < 0.4f;

            float rain = WeatherManager.Instance?.GetCurrentRainfallPercent() ?? 0f;
            float snow = WeatherManager.Instance?.GetCurrentSnowfallPercent() ?? 0f;

            // A biome storm outranks ordinary weather: it is a scheduled, dangerous event
            var biomeHere = GameManager.Instance?.World?.GetBiome((int)_npcEntity.position.x, (int)_npcEntity.position.z);
            var biomeWeather = biomeHere != null ? WeatherManager.Instance?.FindBiomeWeather(biomeHere.m_BiomeType) : null;
            int stormState = biomeWeather?.stormState ?? 0;
            float fog = biomeWeather?.FogPercent() ?? 0f;

            bool soaked = rain > 0.45f || snow > 0.45f;
            bool blind = fog > 0.55f;

            key = health < 0.45f ? "hurt"
                : sinceCombat < 90f ? "combat"
                : playerBadlyHurt ? "player-hurt"
                : stormState >= 2 ? "storm"
                : hordeSoon ? "horde"
                : stormState == 1 ? "storm-coming"
                : soaked ? "soaked"
                : blind ? "fogbound"
                : sinceCoffee < 600f ? "coffee"
                : (hour >= 22 || hour < 5) ? "night"
                : (hour >= 5 && hour < 8) ? "dawn"
                : "easy";
            ResolveMoodByKey(key, out mood, out manner, out wordLimit);
        }

        private static void ResolveMoodByKey(string key, out string mood, out string manner, out int wordLimit)
        {
            if (key == "hurt")
            {
                mood = "You are hurt and it is wearing on you.";
                manner = "Short, flat answers. Not much patience for chat.";
                wordLimit = 10;
            }
            else if (key == "combat")
            {
                mood = "You were shooting less than a minute ago and the adrenaline has not dropped.";
                manner = "Clipped and alert, still scanning. Words come out fast and few.";
                wordLimit = 10;
            }
            else if (key == "player-hurt")
            {
                mood = "The player is badly hurt and you have noticed.";
                manner = "Focused and practical, nurse first, jokes later.";
                wordLimit = 15;
            }
            else if (key == "horde")
            {
                mood = "The blood moon is almost on you.";
                manner = "Businesslike and a little tense. No banter you do not have time for.";
                wordLimit = 12;
            }
            else if (key == "storm")
            {
                mood = "A biome storm has caught you out in the open.";
                manner = "Urgent. Get under cover and say so - this is not the moment for conversation.";
                wordLimit = 12;
            }
            else if (key == "storm-coming")
            {
                mood = "You can feel a biome storm building - it lands soon.";
                manner = "Pushing to find shelter before it arrives, brisk about it.";
                wordLimit = 14;
            }
            else if (key == "soaked")
            {
                mood = "You are out in the wet and thoroughly sick of it.";
                manner = "Grousing about the weather, keen to get under cover - say so.";
                wordLimit = 15;
            }
            else if (key == "fogbound")
            {
                mood = "The fog is so thick you cannot see what is coming.";
                manner = "Uneasy and watchful, voice down, no interest in small talk.";
                wordLimit = 12;
            }
            else if (key == "coffee")
            {
                mood = "You have just had coffee and you are enjoying it.";
                manner = "Awake, chatty, quick with a joke - the most talkative you get.";
                wordLimit = 25;
            }
            else if (key == "night")
            {
                mood = "It is the middle of the night and you are tired.";
                manner = "Low and slow, minimal words, half asleep.";
                wordLimit = 10;
            }
            else if (key == "dawn")
            {
                mood = "It is barely dawn and you are not properly awake yet.";
                manner = "Groggy and grumbling, short answers until the day starts.";
                wordLimit = 12;
            }
            else
            {
                mood = "Nothing pressing. An ordinary day out here.";
                manner = "Warm and easy, in no particular hurry.";
                wordLimit = 15;
            }
        }

        /// <summary>
        /// How many times the player has just asked this same thing, ignoring case, spacing and
        /// punctuation, within the last few exchanges.
        /// </summary>
        private int CountRecentRepeats(string playerMessage)
        {
            string current = Normalize(playerMessage);
            if (string.IsNullOrEmpty(current)) return 0;

            int seen = 0, playerTurnsChecked = 0;
            // the current message is already in history, so start from the end and skip it
            for (int i = _conversationHistory.Count - 2; i >= 0 && playerTurnsChecked < 6; i--)
            {
                if (_conversationHistory[i].Role != "Player") continue;
                playerTurnsChecked++;
                if (Normalize(_conversationHistory[i].Content) == current) seen++;
                else break; // only an unbroken run counts as nagging
            }
            return seen;
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Last line of defence against a rambling reply: keep whole sentences up to a word
        /// budget. Prompting does most of the work; this stops the outliers reaching the player.
        /// </summary>
        /// <summary>
        /// The model's way of saying nothing. Anything that is only the marker counts, since it
        /// likes to wrap it in quotes or punctuation.
        /// </summary>
        private static bool IsSilence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string bare = text.Trim().Trim('"', '\'', '*', '.', '<', '>', ' ').ToLowerInvariant();
            // tolerate the older marker and the escaped forms that leaked through
            return bare == "noreply" || bare == "no reply" || bare == "silence" ||
                   bare.Replace("\\u003c", "").Replace("\\u003e", "") == "silence";
        }

        private static string CapLength(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length <= MaxReplyWords) return text;

            var kept = new System.Text.StringBuilder();
            int words = 0;
            foreach (string sentence in System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?])\s+"))
            {
                int length = sentence.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (words > 0 && words + length > MaxReplyWords) break;
                kept.Append(kept.Length > 0 ? " " : "").Append(sentence);
                words += length;
            }
            string capped = kept.ToString().Trim();
            return string.IsNullOrEmpty(capped) ? text : capped;
        }

        private string StripStageDirections(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string cleaned = text;
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\*[^*]*\*", " ");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\([^)]*\)", " ");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\[[^\]]*\]", " ");

            // Unmarked narration wrapping quoted speech, first person or third:
            // '"Click." I pinch my nose. "We aren't in Arizona, hon."' or
            // 'Ratchet clicks her tongue. "Two kits left, hon."'
            // Two tells: her name sits outside the quotes, or the quoted parts carry most
            // of the text (so the quotes ARE the dialogue and the rest is stage business).
            // An inner quotation - 'Rekt told me "get out" last week, hon.' - trips neither.
            var quoted = System.Text.RegularExpressions.Regex.Matches(cleaned, "[\"“]([^\"“”]+)[\"”]");
            if (quoted.Count > 0)
            {
                string outside = System.Text.RegularExpressions.Regex.Replace(cleaned, "[\"“][^\"“”]*[\"”]", " ");
                bool nameOutside = false;
                if (!string.IsNullOrEmpty(_npcName))
                {
                    foreach (string word in _npcName.Split(' '))
                    {
                        if (word.Length > 2 && outside.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            nameOutside = true;
                            break;
                        }
                    }
                }

                int quotedChars = 0;
                var parts = new List<string>();
                foreach (System.Text.RegularExpressions.Match m in quoted)
                {
                    parts.Add(m.Groups[1].Value);
                    quotedChars += m.Groups[1].Value.Length;
                }
                bool quotedIsMostOfIt = quotedChars * 2 >= cleaned.Length;

                if (nameOutside || quotedIsMostOfIt)
                {
                    cleaned = string.Join(" ", parts);
                }
            }

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
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
            foreach (var snap in newer.cargoSnapshots)
            {
                older.cargoSnapshots.RemoveAll(s => s.name == snap.name);
                older.cargoSnapshots.Add(snap);
            }
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
            sb.AppendLine("Rewrite the long-term memory, merging in anything from the excerpts worth keeping: facts about the player, promises made, shared events, plans, things learned about each other. Keep it under 150 words of plain prose. Output only the memory text, no preamble.");
            sb.AppendLine("Record what happened, not grievances. This memory is read back as her standing attitude every time she speaks, so do NOT write complaints about the player, demands, ultimatums, orders, or judgements of their competence - a passing irritation in one conversation must not become a permanent stance. Keep the tone that of a companion who likes and trusts the player.");

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

                if (IsCompanion && Time.unscaledTime >= _nextCargoCheck)
                {
                    _nextCargoCheck = Time.unscaledTime + CargoCheckIntervalSeconds;
                    RecordLastSeenPosition();
                    RefreshCargoSnapshots();
                }

                WarnIfCarryingGearUnhired();

                if (IsCompanion) CheckForSomethingWorthSaying();
            }
            catch (Exception ex)
            {
                // World/POI APIs can be touchy during load/unload; never break the NPC over the journal
                Log.Warning($"[NPCLLMChat] Travel journal update failed: {ex.Message}");
                _nextPlaceCheck = Time.unscaledTime + 60f;
            }
        }

        private const float CargoCheckIntervalSeconds = 30f;
        private float _nextCargoCheck;
        private bool _warnedUnhired;
        private float _lastCombatTime = -9999f;
        private string _moodKey = "";
        private bool _quietMood;
        private float _moodSetAt;
        private int _lastOwnHealth = -1;
        private int _lastPackCount = -1;
        private float _lastCaffeineTime = -9999f;
        private int _lastCoffeeCount = -1;

        /// <summary>
        /// Loot containers carry the entity id of whoever owns them, which is how a modded
        /// NPC's storage can be located without depending on the mod's own types.
        /// </summary>
        private static ItemStack[] FindContainerForEntity(int entityId)
        {
            var world = GameManager.Instance?.World;
            if (world == null) return null;
            foreach (var chunk in world.ChunkCache.GetChunkArrayCopySync())
            {
                foreach (var tileEntity in chunk.tileEntities.list)
                {
                    if (tileEntity.entityId != entityId) continue;
                    if (tileEntity is TileEntityLootContainer loot && loot.items != null) return loot.items;
                }
            }
            return null;
        }

        // ========== Unprompted remarks ==========

        private const float ShoutRange = 20f;        // how close a threat has to be to matter
        private const float WarnCooldown = 75f;      // one "behind you" per fight, not per zombie
        private const float TriumphCooldown = 240f;
        private float _nextWarnTime;
        private float _nextTriumphTime;
        private int _hostilesSeenThisFight;
        private bool _remarkPending;

        // Unprompted lines are the most immersive thing she does and the easiest to overdo:
        // every trigger has its own cooldown, and nothing at all inside the global gap.
        private const float RemarkGlobalGap = 50f;
        private float _nextRemarkTime;
        private readonly Dictionary<string, float> _nextByTrigger = new Dictionary<string, float>();
        private string _lastRemarkedPlace;
        private bool _wasBleeding;
        private int _lastStormState;
        private bool _saidGoodnight;

        /// <summary>
        /// She speaks up on her own for two things worth interrupting for: something closing on
        /// the player from behind, and the quiet after a real fight. Both are rate limited, and
        /// the line itself comes from the model so it stays in character instead of canned.
        /// </summary>
        private void CheckForSomethingWorthSaying()
        {
            if (_remarkPending || _isWaitingForResponse || _audioPlayer == null) return;

            var world = GameManager.Instance?.World;
            var player = world?.GetPrimaryPlayer();
            if (player == null) return;
            if (Vector3.Distance(_npcEntity.position, player.position) > ShoutRange) return;

            EntityAlive sneakingUp = null;
            int hostilesNear = 0;
            Vector3 playerFacing = player.GetLookVector();

            foreach (var entity in world.Entities.list)
            {
                if (!(entity is EntityEnemy hostile) || hostile.IsDead()) continue;
                float dist = Vector3.Distance(player.position, hostile.position);
                if (dist > ShoutRange) continue;
                hostilesNear++;

                // behind = the player is looking away from it, and it is close enough to bite
                if (dist < 12f && sneakingUp == null)
                {
                    Vector3 toHostile = (hostile.position - player.position).normalized;
                    if (Vector3.Dot(new Vector3(playerFacing.x, 0f, playerFacing.z).normalized,
                                    new Vector3(toHostile.x, 0f, toHostile.z)) < -0.25f)
                    {
                        sneakingUp = hostile;
                    }
                }
            }

            if (hostilesNear > 0) _lastCombatTime = Time.unscaledTime;
            if (hostilesNear > _hostilesSeenThisFight) _hostilesSeenThisFight = hostilesNear;

            if (sneakingUp != null && Time.unscaledTime >= _nextWarnTime)
            {
                _nextWarnTime = Time.unscaledTime + WarnCooldown;
                _nextRemarkTime = Time.unscaledTime + RemarkGlobalGap;
                SpeakUnprompted("Something is closing on the player from BEHIND, close. Shout a warning of " +
                                "THREE WORDS OR FEWER. No name, no advice, no sentence - just the shout.");
                return;
            }

            // the fight is effectively won: it was a real scrap and most of them are down
            bool mostlyCleared = _hostilesSeenThisFight >= 3 && hostilesNear <= _hostilesSeenThisFight / 3;
            if (mostlyCleared && Time.unscaledTime >= _nextTriumphTime)
            {
                _nextTriumphTime = Time.unscaledTime + TriumphCooldown;
                _nextRemarkTime = Time.unscaledTime + RemarkGlobalGap;
                int killed = _hostilesSeenThisFight - hostilesNear;
                _hostilesSeenThisFight = 0;
                SpeakUnprompted($"The shooting just stopped - about {killed} of them down, both of you standing. " +
                                "One pleased remark, SIX WORDS OR FEWER. No plan, no advice, no follow-up.");
                return;
            }

            if (hostilesNear == 0) _hostilesSeenThisFight = 0;

            // Nothing tactical to shout about, so look for something else worth saying
            if (hostilesNear == 0) CheckForSomethingWorthMentioning(player);
        }

        /// <summary>
        /// The quieter half of speaking up: things a companion would remark on unasked - a wound
        /// opening, a storm gathering, arriving somewhere you both know, nightfall before a horde.
        /// One at a time, highest priority first, and never inside the global gap.
        /// </summary>
        private void CheckForSomethingWorthMentioning(EntityPlayer player)
        {
            if (_remarkPending || Time.unscaledTime < _nextRemarkTime) return;

            int day; string time;
            WorldContextHelper.GetGameDayTime(out day, out time);
            int hour = 12;
            if (!string.IsNullOrEmpty(time) && time.Length >= 2) int.TryParse(time.Substring(0, 2), out hour);

            // 1. she is badly hurt herself
            float ownHealth = _npcEntity.GetMaxHealth() > 0
                ? (float)_npcEntity.Health / _npcEntity.GetMaxHealth() : 1f;
            if (ownHealth < 0.35f && Ready("own-wound", 240f))
            {
                Remark("own-wound", "You are badly hurt and the player has not noticed. Say so, plainly, in a few words.");
                return;
            }

            // 2. the player has started bleeding since you last looked
            bool bleeding = false;
            if (player?.Buffs?.ActiveBuffs != null)
            {
                foreach (var buff in player.Buffs.ActiveBuffs)
                {
                    string name = buff.BuffClass?.Name ?? "";
                    if (name.IndexOf("bleed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("infection", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bleeding = true;
                        break;
                    }
                }
            }
            if (bleeding && !_wasBleeding && Ready("player-wound", 120f))
            {
                _wasBleeding = true;
                Remark("player-wound", "You have just noticed the player is bleeding or infected. Tell them, " +
                                       "briefly, like a nurse who has seen it a thousand times.");
                return;
            }
            _wasBleeding = bleeding;

            // 3. a biome storm changing state is worth a word
            var biome = GameManager.Instance?.World?.GetBiome((int)_npcEntity.position.x, (int)_npcEntity.position.z);
            int stormState = (biome != null ? WeatherManager.Instance?.FindBiomeWeather(biome.m_BiomeType)?.stormState : 0) ?? 0;
            if (stormState > _lastStormState && stormState >= 1 && Ready("storm", 300f))
            {
                _lastStormState = stormState;
                Remark("storm", stormState >= 2
                    ? "The biome storm has just hit. Say something short about getting under cover."
                    : "You can feel a biome storm building. Mention it and suggest shelter, briefly.");
                return;
            }
            _lastStormState = stormState;

            // 4. arriving somewhere the two of you already know
            if (!string.IsNullOrEmpty(_currentPlace) && _currentPlace != _lastRemarkedPlace && Ready("arrival", 180f))
            {
                _lastRemarkedPlace = _currentPlace;
                var mark = _memory?.markedPlaces?.Find(m =>
                    !string.IsNullOrEmpty(m.poi) && m.poi.Equals(_currentPlace, StringComparison.OrdinalIgnoreCase));
                bool visitedBefore = _memory?.placesVisited?.Exists(v =>
                    v.place.Equals(_currentPlace, StringComparison.OrdinalIgnoreCase)) ?? false;

                if (mark != null)
                {
                    Remark("arrival", $"You have just walked into {_currentPlace} - the place the player asked you " +
                                      $"to remember as \"{mark.label}\". Say so in a few words.");
                    return;
                }
                if (visitedBefore)
                {
                    Remark("arrival", $"You have just arrived back at {_currentPlace}, somewhere you two have been " +
                                      "before. A short line of recognition, nothing more.");
                    return;
                }
            }

            // 5. dusk on the eve of a horde
            int bloodMoonDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
            if (bloodMoonDay == day && hour >= 19 && hour < 22 && Ready("horde-dusk", 3600f))
            {
                Remark("horde-dusk", "The sun is going down and the blood moon horde comes TONIGHT. One short line " +
                                     "about it - you are not making jokes now.");
                return;
            }

            // 6. the player is running out of food or water
            var stats = player?.Stats;
            if (stats != null && Ready("player-empty", 900f))
            {
                bool starving = stats.Food != null && stats.Food.ValuePercentUI < 0.2f;
                bool parched = stats.Water != null && stats.Water.ValuePercentUI < 0.2f;
                if (starving || parched)
                {
                    Remark("player-empty", starving
                        ? "The player is running on empty and needs to eat. Tell them, shortly."
                        : "The player is badly dehydrated and needs water. Tell them, shortly.");
                    return;
                }
            }
        }

        private bool Ready(string trigger, float cooldown)
        {
            float next;
            if (_nextByTrigger.TryGetValue(trigger, out next) && Time.unscaledTime < next) return false;
            _nextByTrigger[trigger] = Time.unscaledTime + cooldown;
            return true;
        }

        private void Remark(string trigger, string situation)
        {
            _nextRemarkTime = Time.unscaledTime + RemarkGlobalGap;
            SpeakUnprompted(situation);
        }

        /// <summary>
        /// Mid-fight is no time for a paragraph: keep the first sentence, and no more than
        /// eight words of it.
        /// </summary>
        private static string Shorten(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return line;

            int cut = line.IndexOfAny(new[] { '.', '!', '?' });
            string first = cut >= 0 ? line.Substring(0, cut + 1) : line;

            string[] words = first.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 8) return first.Trim();

            // cut on the last clause break inside the limit, so it never ends mid-thought
            for (int i = Math.Min(7, words.Length - 1); i >= 2; i--)
            {
                if (words[i].EndsWith(",") || words[i].EndsWith(";"))
                {
                    return string.Join(" ", words, 0, i + 1).TrimEnd(',', ';') + ".";
                }
            }
            // no clean break: a whole short sentence beats a mangled fragment
            if (words.Length <= 12) return first.Trim();
            return string.Join(" ", words, 0, 8).TrimEnd(',', ';', ':') + ".";
        }

        private void SpeakUnprompted(string situation)
        {
            _remarkPending = true;
            string prompt = _systemPrompt + BuildWorldContext() +
                            "\n\n[Right now]\n" + situation +
                            "\nOutput only the words you shout or say out loud. No narration. Keep it very short.";

            LLMService.Instance.SendCompletionRequest(prompt, 0.9f,
                line =>
                {
                    _remarkPending = false;
                    string speech = Shorten(StripStageDirections(line));
                    if (string.IsNullOrWhiteSpace(speech)) return;

                    Log.Out($"[NPCLLMChat] {_npcName} speaks up: {speech}");
                    if (_ttsEnabled) _audioPlayer.Speak(speech);

                    var player = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
                    if (player != null) GameManager.ShowTooltip(player, $"{_npcName}: {speech}", false);
                },
                error =>
                {
                    _remarkPending = false;
                    Log.Warning($"[NPCLLMChat] Unprompted remark failed: {error}");
                });
        }

        /// <summary>
        /// Keep a breadcrumb of where she actually is, so a lost companion can be found even
        /// when she strays between POIs (the travel journal only fires on POI arrival).
        /// </summary>
        private void RecordLastSeenPosition()
        {
            if (_memory == null) return;
            int x = (int)_npcEntity.position.x;
            int z = (int)_npcEntity.position.z;
            if (x == _memory.lastSeenX && z == _memory.lastSeenZ) return;

            int day; string time;
            WorldContextHelper.GetGameDayTime(out day, out time);
            _memory.lastSeenX = x;
            _memory.lastSeenZ = z;
            _memory.lastSeenDay = day;
            _memory.lastSeenTime = time;
            PersistMemory();
        }

        /// <summary>
        /// An unhired NPC wanders off and despawns, taking anything stored on it - including
        /// the weapon it needs in its inventory to wield. Say so once, while the gear is still
        /// recoverable.
        /// </summary>
        private void WarnIfCarryingGearUnhired()
        {
            if (_warnedUnhired || IsHiredCompanion()) return;

            var items = _npcEntity.lootContainer?.items;
            if (items == null) return;
            bool hasGear = false;
            foreach (var stack in items)
            {
                if (stack != null && !stack.IsEmpty()) { hasGear = true; break; }
            }
            if (!hasGear) return;

            _warnedUnhired = true;
            Log.Warning($"[NPCLLMChat] {_npcName} is carrying gear but is NOT hired - will wander off and despawn with it");
            var player = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
            if (player != null)
            {
                GameManager.ShowTooltip(player, $"{_npcName} is holding your gear but is NOT hired - hire her or she wanders off with it", false);
            }
        }

        /// <summary>
        /// The companion keeps mental notes of what's stored where: the player's vehicles,
        /// the supply drone, and player-owned storage containers grouped by the place they
        /// sit in. Only what's currently loaded can be seen; everything else keeps its
        /// last-seen snapshot with the day/time she saw it.
        /// </summary>
        private void RefreshCargoSnapshots()
        {
            var world = GameManager.Instance?.World;
            var player = world?.GetPrimaryPlayer();
            if (world == null || player == null || _memory == null) return;

            int day; string time;
            WorldContextHelper.GetGameDayTime(out day, out time);
            bool changed = false;

            foreach (var entity in world.Entities.list)
            {
                if (entity is EntityDrone drone)
                {
                    if (drone.Owner is EntityPlayerLocal ||
                        (drone.OwnerID != null && drone.OwnerID.Equals(Platform.PlatformManager.InternalLocalUserIdentifier)))
                    {
                        changed |= UpdateCargoSnapshot("the supply drone", day, time,
                            WorldContextHelper.SummarizeStacks(drone.bag?.GetSlots()), drone.position);
                    }
                }
                else if (entity is EntityVehicle vehicle && vehicle.LocalPlayerIsOwner())
                {
                    changed |= UpdateCargoSnapshot($"the {VehicleName(vehicle)}", day, time,
                        WorldContextHelper.SummarizeStacks(vehicle.bag?.GetSlots()), vehicle.position);
                }
            }

            // Traders she has actually stood next to: remember what they had in stock. Nothing
            // is known until she has visited one, so this list starts empty.
            foreach (var entity in world.Entities.list)
            {
                if (!(entity is EntityTrader trader)) continue;
                if (entity.GetType().Name.Contains("SDX")) continue;      // SCore NPCs derive from EntityTrader
                if (Vector3.Distance(_npcEntity.position, trader.position) > 20f) continue;

                var stock = trader.TileEntityTrader?.TraderData?.PrimaryInventory;
                string stockSummary = WorldContextHelper.SummarizeStacks(stock, 25);
                if (string.IsNullOrEmpty(stockSummary)) continue;
                changed |= UpdateCargoSnapshot($"{TraderName(trader)}'s stock", day, time, stockSummary, trader.position);
            }

            // Player-owned storage containers, one snapshot per place ("storage at Trader Rekt")
            var byPlace = new Dictionary<string, List<ItemStack>>();
            var placePositions = new Dictionary<string, Vector3>();
            foreach (var chunk in world.ChunkCache.GetChunkArrayCopySync())
            {
                foreach (var tileEntity in chunk.tileEntities.list)
                {
                    if (!(tileEntity is TileEntitySecureLootContainer container) || !container.LocalPlayerIsOwner())
                        continue;
                    Vector3i wp = container.ToWorldPos();
                    string place = WorldContextHelper.GetPOINameAt(new Vector3(wp.x, wp.y, wp.z));
                    string key = string.IsNullOrEmpty(place) ? "storage out in the wild" : $"storage at {place}";
                    if (!byPlace.TryGetValue(key, out var stacks))
                    {
                        stacks = new List<ItemStack>();
                        byPlace[key] = stacks;
                    }
                    if (!placePositions.ContainsKey(key)) placePositions[key] = new Vector3(wp.x, wp.y, wp.z);
                    if (container.items != null) stacks.AddRange(container.items);
                }
            }
            foreach (var group in byPlace)
            {
                changed |= UpdateCargoSnapshot(group.Key, day, time, WorldContextHelper.SummarizeStacks(group.Value),
                    placePositions.TryGetValue(group.Key, out var where) ? where : Vector3.zero);
            }

            if (changed) PersistMemory();
        }

        private bool UpdateCargoSnapshot(string name, int day, string time, string summary, Vector3 position)
        {
            if (string.IsNullOrEmpty(summary)) summary = "empty";
            int x = (int)position.x, z = (int)position.z;
            var snap = _memory.cargoSnapshots.Find(s => s.name == name);
            if (snap == null)
            {
                _memory.cargoSnapshots.Add(new CargoSnapshot
                {
                    name = name, day = day, time = time, summary = summary, x = x, z = z
                });
                return true;
            }
            // a vehicle that has been driven somewhere is worth persisting even when its
            // cargo is unchanged - that position is how it gets found again
            bool moved = Mathf.Abs(snap.x - x) + Mathf.Abs(snap.z - z) > 20;
            bool changed = snap.summary != summary || moved;
            snap.day = day;
            snap.time = time;
            snap.summary = summary;
            snap.x = x;
            snap.z = z;
            return changed;
        }

        private static string TraderName(EntityTrader trader)
        {
            string name = trader.EntityName ?? "trader";
            if (name.StartsWith("npcTrader")) name = name.Substring("npcTrader".Length);
            return string.IsNullOrEmpty(name) ? "the trader" : $"trader {name}";
        }

        private static string VehicleName(EntityVehicle vehicle)
        {
            string name = EntityClass.list[vehicle.entityClass]?.entityClassName ?? "vehicle";
            if (name.StartsWith("vehicle")) name = name.Substring("vehicle".Length);
            return string.IsNullOrEmpty(name) ? "vehicle" : name.ToLowerInvariant();
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

                Vector3 npcPos = _npcEntity.position;
                sb.AppendLine($"Your map position: {(int)npcPos.x} E/W, {(int)npcPos.z} N/S.");

                string surroundings = WorldContextHelper.DescribeSurroundings(npcPos);
                if (!string.IsNullOrEmpty(surroundings))
                {
                    sb.AppendLine($"The ground and sky around you: {surroundings}.");
                    sb.AppendLine("You are standing out in that, not reading it off a screen - it is fair game " +
                                  "to grumble about, and worth suggesting shelter or waiting it out when the " +
                                  "weather turns genuinely bad.");
                }

                sb.AppendLine("Dukes (casino coins) are the money everyone uses out here. Traders buy loot, crafted goods, and materials for dukes and sell supplies for them, so selling things to traders for a profit is ordinary business, not fantasy - the player knows the going rates better than you do.");

                int bloodMoonDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
                if (bloodMoonDay > 0)
                {
                    if (bloodMoonDay <= day)
                        sb.AppendLine("TONIGHT is a blood moon - the horde comes at nightfall.");
                    else
                        sb.AppendLine($"The next blood moon horde comes on the night of Day {bloodMoonDay} ({bloodMoonDay - day} day{(bloodMoonDay - day == 1 ? "" : "s")} from now).");
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
                        string rel = WorldContextHelper.DescribeRelative(_npcEntity.position, mark.x, mark.z);
                        sb.AppendLine($"- \"{mark.label}\" (marked Day {mark.day} {mark.time},{at} map position {mark.x} E/W, {mark.z} N/S - {rel})");
                    }
                }

                // Her own inventory is on her person, so it's read live rather than snapshotted.
                // SCore NPCs hold what the player hands them in lootContainer (the container
                // the hire UI opens); the vanilla bag is normally player-only, so read both.
                // Three separate stores: lootContainer (what the hire UI shows), the vanilla
                // bag, and the toolbelt slots that hold what she actually equips and uses.
                var carried = new List<ItemStack>();
                if (_npcEntity.lootContainer?.items != null) carried.AddRange(_npcEntity.lootContainer.items);
                var ownBag = _npcEntity.bag?.GetSlots();
                if (ownBag != null) carried.AddRange(ownBag);
                var belt = _npcEntity.inventory?.CloneItemStack();
                if (belt != null) carried.AddRange(belt);
                // SCore NPCs derive from EntityTrader, and the companion UI can route items
                // into the trader-side inventory rather than the loot container
                var traderStock = (_npcEntity as EntityTrader)?.TileEntityTrader?.TraderData?.PrimaryInventory;
                if (traderStock != null) carried.AddRange(traderStock);
                // The companion UI's container is mod-managed and none of the entity's own
                // fields point at it, but SCore stamps its tile entities with the owning
                // entity id - so find the container that belongs to her.
                var ownContainer = FindContainerForEntity(_npcEntity.entityId);
                if (ownContainer != null) carried.AddRange(ownContainer);
                // Her real store is only ever exposed when the player opens it, so use what
                // was captured then (the array stays live, so edits since are reflected)
                var opened = Harmony.NPCContainerCache.Get(_npcEntity.entityId, _npcName);
                if (opened != null) carried.AddRange(opened);

                // A cup going missing from her pack is the only evidence that she drank it
                int coffeeNow = 0;
                foreach (var stack in carried)
                {
                    var itemClass = stack?.itemValue?.ItemClass;
                    string name = itemClass?.GetItemName() ?? "";
                    if (name.IndexOf("coffee", StringComparison.OrdinalIgnoreCase) >= 0) coffeeNow += stack.count;
                }
                if (_lastCoffeeCount >= 0 && coffeeNow < _lastCoffeeCount) _lastCaffeineTime = Time.unscaledTime;
                _lastCoffeeCount = coffeeNow;

                // Being handed things counts as being looked after; being left wounded does not
                int packCount = 0;
                foreach (var stack in carried) if (stack != null && !stack.IsEmpty()) packCount += stack.count;
                if (_lastPackCount >= 0 && packCount > _lastPackCount) AdjustRapport(0.04f, "given supplies");
                _lastPackCount = packCount;

                int ownHealth = _npcEntity.Health;
                if (_lastOwnHealth >= 0)
                {
                    if (ownHealth < _lastOwnHealth - 15) AdjustRapport(-0.03f, "took a beating");
                    else if (ownHealth > _lastOwnHealth + 5) AdjustRapport(0.05f, "patched up");
                }
                _lastOwnHealth = ownHealth;

                string carrying = WorldContextHelper.SummarizeStacks(carried);
                Log.Out($"[NPCLLMChat] {_npcName} inventory by source -> " +
                        $"lootContainer: {WorldContextHelper.SummarizeStacks(_npcEntity.lootContainer?.items) ?? "(empty)"} | " +
                        $"bag: {WorldContextHelper.SummarizeStacks(ownBag) ?? "(empty)"} | " +
                        $"belt: {WorldContextHelper.SummarizeStacks(belt) ?? "(empty)"} | " +
                        $"trader: {WorldContextHelper.SummarizeStacks(traderStock) ?? "(empty)"} | " +
                        $"own container: {WorldContextHelper.SummarizeStacks(ownContainer) ?? "(none found)"} | " +
                        $"opened store: {WorldContextHelper.SummarizeStacks(opened) ?? "(never opened)"}");

                string wielded = _npcEntity.inventory?.holdingItem?.GetLocalizedItemName();
                if (!string.IsNullOrEmpty(wielded) && wielded != "Air")
                {
                    sb.AppendLine($"The weapon you have in your hands right now: {wielded}.");
                }
                sb.AppendLine(string.IsNullOrEmpty(carrying)
                    ? "You are carrying nothing in your own bag right now."
                    : $"What you are carrying in your own bag right now: {carrying}.");
                sb.AppendLine("This list is live and correct as of this moment. The player hands you things " +
                              "mid-conversation, so if you listed your belongings earlier and this list differs, " +
                              "THIS list is right and what you said before is out of date - just accept the new item.");

                if (_memory != null && _memory.cargoSnapshots.Count > 0)
                {
                    sb.AppendLine("Stored supplies you keep mental track of (contents as of when you last saw them - they may have changed since):");
                    foreach (var snap in _memory.cargoSnapshots)
                    {
                        sb.AppendLine($"- {snap.name} (last checked Day {snap.day} {snap.time}): {snap.summary}");
                    }
                }
                sb.AppendLine("You do NOT know what the player is carrying in their pack; if it ever matters, just ask.");

                // She knows her own state precisely - it is her body and her ammo
                float ownHealthPct = _npcEntity.GetMaxHealth() > 0
                    ? (float)_npcEntity.Health / _npcEntity.GetMaxHealth() : 1f;
                string ownState = ownHealthPct >= 0.95f ? "unhurt"
                    : ownHealthPct >= 0.7f ? "scratched up but fine"
                    : ownHealthPct >= 0.4f ? "hurt and feeling it"
                    : "badly hurt and in trouble";
                sb.AppendLine($"Your own condition: {ownState} ({Mathf.RoundToInt(ownHealthPct * 100)}% health). " +
                              "Mention it yourself if it is bad - the player cannot see your health.");

                // NPC weapons are Infinite_ammo=true in XNPCCore (the token ammo stack only
                // exists to make them fire), so she must never talk about running low.
                sb.AppendLine("Your own weapons never run out of ammunition - never ask the player for ammo " +
                              "and never say you are running low.");

                string quests = WorldContextHelper.DescribeQuests(
                    GameManager.Instance?.World?.GetPrimaryPlayer(), _npcEntity.position);
                if (!string.IsNullOrEmpty(quests))
                {
                    sb.AppendLine("Jobs the two of you are carrying right now:");
                    sb.AppendLine(quests);
                    sb.AppendLine("If asked what to do next, an unstarted job is the obvious suggestion - name who it came from.");
                }

                string condition = WorldContextHelper.DescribePlayerCondition(GameManager.Instance?.World?.GetPrimaryPlayer());
                if (!string.IsNullOrEmpty(condition))
                {
                    sb.AppendLine($"How the player looks to you right now, at a glance: {condition}.");
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
                        // entries from before coordinates were recorded deserialize as (0,0)
                        string rel = (visit.x == 0 && visit.z == 0)
                            ? ""
                            : $" - {WorldContextHelper.DescribeRelative(_npcEntity.position, visit.x, visit.z)}";
                        sb.AppendLine($"- {visit.place} (Day {visit.day} {visit.time}, at map position {visit.x} E/W, {visit.z} N/S{rel})");
                    }
                }

                sb.AppendLine("When asked where a place is, point the player to it using the compass direction and rough distance given above (e.g. \"about 400 meters northeast of here\") and say how close it is. Answer only from these facts; if you don't know a place, say so honestly.");
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
