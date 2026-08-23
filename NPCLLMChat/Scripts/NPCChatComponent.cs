using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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
        private const int MaxLabelChars = 40;   // a crate label she can say in one breath

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
                Log.Out($"TTS enabled for NPC: {_npcName}");
            }
            else
            {
                _ttsEnabled = false;
            }

            Log.Out($"Initialized chat component for NPC: {_npcName} (ID: {_entityId})");
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
                Log.Out($"NPC {_npcName} is still thinking...");
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
            actionPrompt += BuildAnswerLengthGuidance();

            // Send to LLM
            LLMService.Instance.SendChatRequest(
                _entityId,
                actionPrompt,
                _conversationHistory,
                playerMessage,
                response => HandleLLMResponse(response, playerMessage, onComplete),
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

        private void HandleLLMResponse(string response, string playerMessage, Action<string> onComplete)
        {
            _isWaitingForResponse = false;

            // The exchange itself only ever appeared in the console, which is not written to the
            // log - so afterwards there is no record of what was asked or what she said, and
            // "she did not know about the snow" cannot be checked against what she was told.
            Log.Out($"chat| Player: {playerMessage}");
            Log.Out($"chat| {_npcName}: {response}");

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

                // An order the player gave outranks whatever her reply happens to imply - she can
                // agree in any words she likes and still do as asked. Only her saying the opposite
                // outright stops it.
                NPCActionType ordered = ActionParser.ParseCommand(playerMessage);
                if (action != null && ordered != NPCActionType.None)
                {
                    if (ActionParser.Contradicts(ordered, action.Type))
                    {
                        Log.Out($"Ordered {ordered}, but she said otherwise - not forcing it");
                    }
                    else
                    {
                        action.Type = ordered;
                        action.Confidence = 0.95f;
                    }
                }

                Log.Out($"Parsed action: {action?.Type ?? NPCActionType.None}" +
                        (ordered != NPCActionType.None ? $" (ordered: {ordered})" : ""));
            }

            // She is allowed to let something pass without answering
            if (IsSilence(dialogueResponse))
            {
                Log.Out($"{_npcName} chose not to answer");
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
                    Log.Error($"Action execution failed: {ex.Message}");
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

                // Telling her to whisper only changes the words. Actually sounding like a
                // whisper is volume and pace, which are knobs rather than prompting.
                bool quiet = PlayerIsSneaking();
                _audioPlayer.Speak(speech, () => OnSpeechComplete?.Invoke(),
                                   quiet ? 0.45f : 1f, quiet ? 0.9f : 1f);
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

            Log.Warning($"Error for NPC {_npcName}: {error}. Using fallback.");
        }

        // Only a runaway-reply backstop. The prompt governs ordinary length; this used to sit
        // at 60 and was clipping legitimate stories mid-telling.
        private const int MaxReplyWords = 120;

        /// <summary>
        /// People are not randomly terse or chatty - they run on a mood that comes from their
        /// situation. Hers is derived from what is actually happening: a fight a minute ago, a
        /// wound, the small hours, a horde due tomorrow, or a fresh cup of coffee. The mood sets
        /// both her manner and how much she says.
        /// </summary>
        private string BuildAnswerLengthGuidance()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine();

            string mood, manner;
            int wordLimit;
            DescribeMood(out mood, out manner, out wordLimit);
            _quietMood = _moodKey == "hurt" || _moodKey == "combat" || _moodKey == "night" ||
                         _moodKey == "dawn" || _moodKey == "fogbound" || _moodKey == "storm";
            sb.AppendLine($"[Your state right now]\n{mood} {manner}");
            sb.AppendLine("Unless something is genuinely wrong - a horde night, a fight, a wound, a storm - you are " +
                          "good company: joke, tease, enjoy yourself. Grim is for when grim is warranted.");
            sb.AppendLine("If the player asks why you are quiet, short or chirpy, tell him plainly how you " +
                          "feel - do not pretend to be fine.");
            sb.AppendLine(DescribeRapport());
            sb.AppendLine();
            sb.AppendLine("[How to answer - this overrides every style note above]");

            // She kept denying items that were listed for her in the middle of the world state -
            // "I checked three times, hon" - counting denials from previous sessions, because her
            // conversation history outlives a restart. Against three of her own flat statements,
            // one fact she scrolled past does not stand a chance, so the list is repeated here at
            // the end where the instructions that win are, every time and not only when something
            // has just arrived. The arrival notice never fired for the cola anyway: her pack is
            // baselined on the first read of a session, so anything already in it is not "new".
            if (!string.IsNullOrEmpty(_packContents))
            {
                sb.AppendLine($"YOUR PACK, RIGHT NOW: {_packContents}. That is read off your own bag this second " +
                              "and it is not up for debate. If you said anything different earlier - even three " +
                              "times, even yesterday - you were wrong and this is what is true now. Say so lightly, " +
                              "\"hang on, so there is\", and never insist you checked and found nothing when the " +
                              "thing is right there on that list.");
            }
            else
            {
                sb.AppendLine("YOUR PACK, RIGHT NOW: empty. If you told him earlier you were carrying something, " +
                              "it has gone since.");
            }
            sb.AppendLine("FIRST: if the player asks about something the world state above actually tells you - " +
                          "what is in your pack, where a place is, the time, the weather, the horde, your health - " +
                          "ANSWER IT from those facts. Read the list before you reply. No mood, no tiredness and " +
                          "no irritation excuses refusing a question you can answer. Never say you do not know " +
                          "something that is written above.");
            // The clause here used to be "silence and grunts are for small talk only" - meant as a
            // restriction, read as permission. Stated as the rule instead, and deliberately worded
            // to agree with the restraint block further down rather than fight it.
            sb.AppendLine("A greeting gets a greeting. Brief means few words, never no words - never reply to " +
                          "something said to you with only a grunt, a hum or a single syllable.");

            // Not because she was caught doing it - she reads the condition line correctly and
            // reported it accurately when asked. Because a companion who confirms whatever the
            // player claims has observations worth nothing: agreement means nothing if it was
            // never in doubt.
            sb.AppendLine("WHAT YOU CAN SEE OUTRANKS WHAT HE SAYS ABOUT HIMSELF. If he claims something the " +
                          "world state above contradicts - he says he is starving and he is well fed, he says he " +
                          "is fine and he is bleeding out, he says you are carrying rounds and your pack is empty " +
                          "- say so plainly. Once, in your own way, without argument or lecture, and then let it " +
                          "go. Do not agree with something you can see is not true just because he said it first. " +
                          "Being easy to agree with is not the same as being good company.");

            if (PlayerIsSneaking())
            {
                wordLimit = Math.Min(wordLimit, 8);
                sb.AppendLine("HE IS CROUCHED AND MOVING QUIETLY, so you drop to a whisper and match him. " +
                              "Barely above a breath, as few words as will do, and nothing said that did not " +
                              "need saying. No banter and no stories while he is sneaking.");
            }

            // A ceiling with no floor collapses to one syllable in the quiet moods, which read
            // "minimal words, half asleep" and take it literally.
            sb.AppendLine($"Answer in one sentence, at least three words and {wordLimit} at most, in the " +
                          "manner described above.");
            sb.AppendLine("Only when the player asks for a story, an explanation or directions do you get " +
                          "up to 80 words - then tell it properly, all the way to the end.");
            sb.AppendLine("Never pad the answer with a plan, a follow-up question or a joke tacked on the end.");
            sb.AppendLine("If he asks you something twice, he has a reason - your first answer missed, or he needs " +
                          "it exact. Answer it properly again, better than the first time. Never grunt at a repeated " +
                          "question and never tell him you have already said it.");
            // This block used to offer "Mm." as its first example of a good reply and then tell
            // her, in any quiet mood, that a grunt was the LIKELY answer. qwen3.8 follows that
            // exactly - "good morning" at night came back "Mm." three times running. Restraint is
            // still wanted (see docs/COMPANION_INTERIORITY.md) but as something occasional, never
            // the default, and never the response to being spoken to directly.
            sb.AppendLine("Not everything deserves a reply. When the player is thinking out loud, or narrating " +
                          "something that asks nothing of you, you may let it pass - answer with exactly " +
                          "<silence> and no other characters. That is for remarks aimed at nobody. Anything " +
                          "said TO you - a greeting, a question, a thank you, being told something - gets a " +
                          "real answer, however short and however tired you are." + (_quietMood
                              ? " Tired means fewer words and a flatter tone, not fewer replies."
                              : ""));
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
                Log.Out($"{_npcName} rapport {before:F2} -> {_memory.rapport:F2} ({why})");
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
                manner = "Short answers, gallows humour about your own state, not much patience for chat.";
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
                manner = "Grousing about the weather with dark humour, keen to get under cover - say so.";
                wordLimit = 15;
            }
            else if (key == "fogbound")
            {
                mood = "The fog is so thick you cannot see what is coming.";
                manner = "Uneasy and watchful, voice down - a wry line is fine, but you are listening hard.";
                wordLimit = 12;
            }
            else if (key == "coffee")
            {
                mood = "You have just had coffee and you are enjoying it.";
                manner = "Wired, chatty and joking freely - the most talkative and playful you get.";
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
                manner = "Groggy and grumbling, comically so - short answers until the coffee lands.";
                wordLimit = 12;
            }
            else
            {
                mood = "Nothing pressing, and you are in decent spirits.";
                manner = "Playful and quick with a joke, teasing the player, enjoying the company. This is your " +
                         "normal register - dark humour, not gloom.";
                wordLimit = 18;
            }
        }

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

        /// <summary>
        /// Last line of defence against a rambling reply: keep whole sentences up to a word
        /// budget. Prompting does most of the work; this stops the outliers reaching the player.
        /// </summary>
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
        /// Give her a name of the player's choosing.
        ///
        /// The entity's name is what she is told she is called and what the log and the HUD show,
        /// and the game serialises it with her, so it survives a reload. Her memory is keyed by
        /// role rather than by name once she is hired, so renaming her does not split or orphan
        /// anything she remembers - she is the same companion, differently addressed.
        /// </summary>
        public bool Rename(string newName)
        {
            if (_npcEntity == null || string.IsNullOrWhiteSpace(newName)) return false;

            string clean = newName.Trim();
            if (clean.Length > 24) clean = clean.Substring(0, 24);

            string was = _npcName;
            _npcEntity.SetEntityName(clean);
            SetSDXFirstName(_npcEntity, clean);
            _npcName = clean;
            if (_memory != null)
            {
                _memory.npcName = clean;
                PersistMemory();
            }

            Log.Out($"Renamed {was} to {clean} [id {_npcEntity.entityId}]");
            return true;
        }

        /// <summary>
        /// EntityAliveSDX overrides EntityName as a getter over its own private _strMyName, and
        /// only falls back to the base entityName when that is empty. Hers is not empty - it is
        /// how she got a name in the first place - so Entity.SetEntityName alone is invisible.
        /// Write _strMyName as well. SCore serialises it, so the new name survives a reload.
        /// </summary>
        private static void SetSDXFirstName(EntityAlive npc, string name)
        {
            var field = AccessTools.Field(npc.GetType(), "_strMyName");
            if (field == null)
            {
                Log.Warning("No _strMyName on " + npc.GetType().Name +
                            " - the name may not show on her nameplate");
                return;
            }
            field.SetValue(npc, name);
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
            Log.Out($"{_npcName} persona reloaded ({_memory.persona?.Length ?? 0} chars)");
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
                    Log.Out($"Folded {_npcName}'s memory into the companion memory");
                }
            }
            return memory ?? new NPCMemory { npcName = _npcName };
        }

        /// <summary>
        /// Hired mid-session: switch this NPC onto the shared companion memory,
        /// keeping both the prior companion history and the current conversation.
        /// </summary>
        /// <summary>
        /// Mark the companion as worth keeping when the world is written out.
        ///
        /// An NPC the game does not consider persistent is culled on logout, and the player gets
        /// back a world with no companion in it - her inventory gone, and everything she knew
        /// with it but for the memory file being keyed by name rather than entity id. There was
        /// already a console command for setting this by hand, which is no protection at all
        /// against forgetting.
        ///
        /// Set directly rather than leaning on hire state: Nightingale was hired and still did
        /// not come back, with CurrentHireCount reading 0 afterwards, so the hire was gone before
        /// the save was written. Persist does not care what happens to the hire later.
        /// </summary>
        private void EnsureSurvivesLogout()
        {
            if (_npcEntity?.Buffs == null) return;

            bool companion = IsCompanion || IsHiredCompanion();
            float persist = _npcEntity.Buffs.HasCustomVar("Persist")
                ? _npcEntity.Buffs.GetCustomVar("Persist") : 0f;

            // Say the state out loud once per session. Setting the flag logged, and skipping it
            // logged nothing at all, so a companion who already had it and a companion who was
            // never recognised as one looked identical from outside - which is no use when the
            // question being asked is whether she is safe to log out on.
            if (!_loggedPersistState)
            {
                _loggedPersistState = true;
                Log.Out($"{_npcName} [id {_npcEntity.entityId}] logout check: " +
                        $"companion={companion} (memoryKey={_memoryKey}), Persist={persist}, " +
                        $"Leader/Owner={IsHiredCompanion()} -> " +
                        (companion
                            ? (persist > 0f ? "already safe" : "setting Persist now")
                            : "NOT TREATED AS COMPANION - she will not be saved"));
            }

            if (!companion || persist > 0f) return;

            _npcEntity.Buffs.SetCustomVar("Persist", 1f);
            Log.Out($"{_npcName} [id {_npcEntity.entityId}] marked to survive logout (Persist=1)");
        }

        private bool _loggedPersistState;

        private void RefreshMemoryKey()
        {
            if (_memoryKey == CompanionMemoryKey || !IsHiredCompanion()) return;

            Log.Out($"{_npcName} is now the player's companion - unifying memory");
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
            Log.Out($"{_npcName} marked place '{label}' at ({(int)_npcEntity.position.x}, {(int)_npcEntity.position.z})");
        }

        // ========== Long-term memory summarization ==========

        private bool _isSummarizing;
        private const int SummarizeBatchSize = 10; // messages (5 exchanges) per summarization pass

        // The summary is written to disk and then prepended to EVERY prompt for the rest of the
        // save, so its length is not a per-request cost the way a spoken reply is. It stays on its
        // own budget rather than tracking MaxTokens: raising MaxTokens for longer conversation
        // must not quietly license a longer permanent memory. 150 words of prose needs ~200.
        private const int SummaryTokenBudget = 512;

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
                    Log.Out($"{_npcName} long-term memory updated ({_memory.longTermMemory.Length} chars)");
                },
                error =>
                {
                    _isSummarizing = false;
                    Log.Warning($"Summarization failed (will retry later): {error}");
                },
                SummaryTokenBudget);
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
                EnsureSurvivesLogout();
                CheckCurrentPlace();
                ObserveSurroundings();

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
                Log.Warning($"Travel journal update failed: {ex.Message}");
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
        private Dictionary<string, int> _lastPackItems;
        private string _recentlyAdded;
        private float _recentlyAddedAt;
        // Last computed contents of her pack, repeated at the end of the prompt. She kept
        // denying items that were listed for her in the middle of it.
        private string _packContents;
        private string _lastLoggedPack = "\0";
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
        // How far away she can still notice the player himself. ShoutRange was doing both jobs,
        // so stepping into the next room silenced every unprompted remark she has - hunger,
        // storms, arrivals, the lot - because a constant about biting distance was deciding
        // whether she could see him at all.
        private const float NoticeRange = 45f;
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
        // When she last said something about arriving at each place, so coming back is only
        // news after a real absence rather than every time a footprint boundary is crossed.
        private readonly Dictionary<string, float> _placeRemarkedAt = new Dictionary<string, float>();
        private const float PlaceGreetingGap = 1800f;
        private bool _wasBleeding;
        // whether the cure for what ails him was within reach last time she looked, so that
        // finding one is a moment and carrying one is not remarked on over and over
        private bool _cureInReach;
        private string _lastAfflictions = "\0";   // never equal to a real value, so the first pass logs
        private int _lastStormState;
        // Keyed by name, not entity id: an NPC that is picked up and set down again gets a new
        // id, and she should not greet the same person twice for that.
        private readonly HashSet<string> _peopleSeen = new HashSet<string>();

        /// <summary>
        /// She speaks up on her own for two things worth interrupting for: something closing on
        /// the player from behind, and the quiet after a real fight. Both are rate limited, and
        /// the line itself comes from the model so it stays in character instead of canned.
        /// </summary>
        private void CheckForSomethingWorthSaying()
        {
            // IsSpeaking matters as much as the other two. _isWaitingForResponse clears the moment
            // the reply arrives, while she still has seconds of audio to deliver - and Speak stops
            // whatever is playing before it starts, so a trigger firing in that window cuts her
            // off mid-sentence and says something unrelated instead. Nobody interrupts themselves.
            if (_remarkPending || _isWaitingForResponse || _audioPlayer == null) return;
            if (_audioPlayer.IsSpeaking) return;

            var world = GameManager.Instance?.World;
            var player = world?.GetPrimaryPlayer();
            if (player == null) return;
            // Threats are still judged at ShoutRange inside the scan below; this only decides
            // whether she is close enough to have a view of him at all.
            if (Vector3.Distance(_npcEntity.position, player.position) > NoticeRange) return;

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

            // A scrap he nearly lost is the part worth remembering about it
            if (hostilesNear > 0 && player.Stats?.Health != null && player.Stats.Health.ValuePercentUI < 0.25f)
            {
                _closeCallThisFight = true;
            }

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
                int faced = _hostilesSeenThisFight;
                bool closeCall = _closeCallThisFight;
                _hostilesSeenThisFight = 0;
                _closeCallThisFight = false;

                // Her triumph line becomes the note on the memory, so what she recalls later is
                // the thing she actually said standing over it rather than a generated summary.
                SpeakUnprompted($"The shooting just stopped - about {killed} of them down, both of you standing. " +
                                "One pleased remark, SIX WORDS OR FEWER. No plan, no advice, no follow-up.",
                                said => RecordEpisode(IsHordeNight() ? "horde" : "fight", faced, closeCall, said));
                return;
            }

            if (hostilesNear == 0) { _hostilesSeenThisFight = 0; _closeCallThisFight = false; }

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

            // 0. the player is cooking in a radiated zone - it kills, and only walking out fixes it
            if (HasBuffLike(player, "radiation", "radiated") && Ready("radiation", 180f))
            {
                Remark("radiation", "The player is standing in a radiated zone taking radiation damage. Nothing " +
                                    "in either pack fixes it - he has to walk out, back toward the middle of the " +
                                    "map. Tell him now, short and serious.");
                return;
            }

            // While he is sneaking, nothing short of the radiation above is worth the noise.
            // A companion who chatters about the weather while you are creeping through a
            // basement is not a companion, she is a liability.
            if (PlayerIsSneaking()) return;

            // 0b. he is about to die. Food and water had a trigger and the thing that actually
            // kills him did not.
            var health = player?.Stats?.Health;
            if (health != null && health.ValuePercentUI < 0.25f && Ready("player-critical", 120f))
            {
                Remark("player-critical", "He is badly hurt and close to going down. Say so now - tell him to " +
                                          "get something in him or get out of here. No jokes, this is the moment " +
                                          "you are a nurse.");
                return;
            }

            // 1. she is badly hurt herself
            float ownHealth = _npcEntity.GetMaxHealth() > 0
                ? (float)_npcEntity.Health / _npcEntity.GetMaxHealth() : 1f;
            if (ownHealth < 0.35f && Ready("own-wound", 240f))
            {
                Remark("own-wound", "You are badly hurt and the player has not noticed. Say so, plainly, in a few words.");
                return;
            }

            // 2. the player has started bleeding since you last looked
            bool bleeding = HasBuffLike(player, "bleed", "infection");
            if (bleeding && !_wasBleeding && Ready("player-wound", 120f))
            {
                _wasBleeding = true;
                Remark("player-wound", "You have just noticed the player is bleeding or infected. Tell them, " +
                                       "briefly, like a nurse who has seen it a thousand times.");
                return;
            }
            _wasBleeding = bleeding;

            // 2b. he has just come up with the thing that fixes what is wrong with him. Watching
            // for one named bottle while he is ill is not the same as reading his pack, and it is
            // the difference between a companion who repeats the diagnosis and one who is pleased
            // for him.
            var ailing = WorldContextHelper.GetAfflictions(player);
            bool cureInReach = false;
            if (ailing.Count > 0)
            {
                var cures = new List<string>();
                foreach (var ail in ailing) cures.AddRange(ail.CureItems);
                if (cures.Count > 0)
                {
                    cureInReach = WorldContextHelper.CarriesAnyOf(player.bag?.GetSlots(), cures.ToArray()) ||
                                  WorldContextHelper.CarriesAnyOf(player.inventory?.GetSlots(), cures.ToArray());
                }
            }
            if (cureInReach && !_cureInReach && Ready("cure-found", 300f))
            {
                _cureInReach = true;
                Remark("cure-found", $"He has just turned up the very thing that fixes his {ailing[0].Label} and " +
                                     "it is in his hands now. One pleased line about that, and tell him to take it.");
                return;
            }
            _cureInReach = cureInReach;

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

            // 4. arriving somewhere the two of you already know. Only worth saying out loud once
            // in a long while per place: the old guard only blocked the place she had just
            // greeted, so Rekt's -> the coffee shop -> Rekt's greeted Rekt's twice, and working
            // along a street had her welcoming him home every time he crossed a threshold.
            if (!string.IsNullOrEmpty(_currentPlace) && PlaceWorthGreeting(_currentPlace) && Ready("arrival", 180f))
            {
                _placeRemarkedAt[_currentPlace] = Time.unscaledTime;
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
                // Something happened here. That beats bare recognition - "we've been here before"
                // is a fact, "not again, hon" is a companion.
                var happenedHere = NotableEpisodeHere();
                if (happenedHere != null)
                {
                    string what = happenedHere.kind == "horde"
                        ? "you saw a blood moon out"
                        : $"{happenedHere.enemies} of them came at you";
                    Remark("arrival", $"You are back at {_currentPlace}, where {what} on Day {happenedHere.day}" +
                                      (happenedHere.closeCall ? " and he nearly went down" : "") +
                                      ". One short line - the way someone sounds walking back into that.");
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

            // 6. somebody new has walked into view - a companion notices other people
            EntityAlive newcomer = null;
            bool newcomerHired = false;
            var world = GameManager.Instance?.World;
            if (world != null)
            {
                foreach (var entity in world.Entities.list)
                {
                    if (!(entity is EntityAlive alive) || alive.IsDead()) continue;
                    if (alive.entityId == _npcEntity.entityId || alive.entityId == player.entityId) continue;
                    if (!NPCLLMChatMod.IsNPC(alive)) continue;
                    if (Vector3.Distance(_npcEntity.position, alive.position) > 30f) continue;

                    string who = WorldContextHelper.PersonName(alive);
                    if (_peopleSeen.Contains(who)) continue;
                    _peopleSeen.Add(who);

                    if (newcomer == null)
                    {
                        newcomer = alive;
                        newcomerHired = alive.Buffs != null &&
                                        ((alive.Buffs.HasCustomVar("Leader") && alive.Buffs.GetCustomVar("Leader") > 0f) ||
                                         (alive.Buffs.HasCustomVar("Owner") && alive.Buffs.GetCustomVar("Owner") > 0f));
                    }
                }
            }
            if (newcomer != null && Ready("newcomer", 300f))
            {
                string name = WorldContextHelper.PersonName(newcomer);
                bool isTrader = newcomer is EntityTrader && !newcomer.GetType().Name.Contains("SDX");
                Remark("newcomer", isTrader
                    ? $"You have just come up on {name}, the trader. A short line about them, nothing more."
                    : newcomerHired
                        ? $"{name} has just come into view - another hand already working for the player. " +
                          "One short line about them."
                        : $"You have just spotted {name}, a survivor standing loose out here - not hired by " +
                          "anyone, so the player could take them on. Point them out in a few words.");
                return;
            }

            // 7. the player is running out of food or water
            //
            // Ready arms the cooldown as a side effect, so it has to be the LAST test - every
            // other trigger here puts its condition first for that reason. This one asked Ready
            // behind a stats != null that is always true, so a well fed player burned the fifteen
            // minute cooldown on every pass and she could only speak if the meter happened to be
            // under a fifth at the instant it lapsed. At 13% food she said nothing at all.
            var stats = player?.Stats;
            if (stats != null)
            {
                bool starving = stats.Food != null && stats.Food.ValuePercentUI < 0.2f;
                bool parched = stats.Water != null && stats.Water.ValuePercentUI < 0.2f;

                // She stayed quiet at 9% food and the logs could not say why: the unprompted
                // path never logs its prompt and the chat path truncates at 200 characters, so
                // neither showed what she was told. Say the numbers out loud, throttled, and only
                // when one of them is low - this reaching the log at all proves the check runs.
                float foodPct = stats.Food != null ? stats.Food.ValuePercentUI : -1f;
                float waterPct = stats.Water != null ? stats.Water.ValuePercentUI : -1f;
                if ((foodPct >= 0f && foodPct < 0.25f) || (waterPct >= 0f && waterPct < 0.25f))
                {
                    if (Ready("empty-debug", 60f))
                    {
                        Log.Warning($"EMPTY CHECK food={foodPct:F3} water={waterPct:F3} " +
                                    $"starving={starving} parched={parched} " +
                                    $"(Food stat {(stats.Food == null ? "MISSING" : stats.Food.Value + "/" + stats.Food.Max)})");
                    }
                }

                // 8. the thing she wants, raised rarely and last - see below
                if ((starving || parched) && Ready("player-empty", 900f))
                {
                    Remark("player-empty", starving
                        ? "The player is running on empty and needs to eat. Tell them, shortly."
                        : "The player is badly dehydrated and needs water. Tell them, shortly.");
                    return;
                }
            }

            // 8. the thing she wants. Noticing it move comes before mentioning it again - being
            // noticed is most of what a goal is for, and a companion who says "you got them"
            // beats one who repeats what she is after.
            ChooseGoalIfNone();

            float now;
            var moved = MeasureGoal(out now);
            if (moved != null)
            {
                if (now >= GoalMet)
                {
                    moved.met = true;
                    moved.metDay = day;
                    moved.measure = now;
                    _memory.goalsMet.Add(moved);
                    _memory.goal = null;
                    PersistMemory();
                    Log.Out($"{_npcName} got what she wanted: {moved.want} (Day {day})");
                    Remark("goal-met", $"He has finally sorted it: {moved.want}. You wanted this because " +
                                       $"{moved.because}. One line, pleased and a bit surprised, and do not " +
                                       "immediately want something else.");
                    return;
                }

                if (now > moved.measure + GoalStep && Ready("goal-progress", 1200f))
                {
                    float was = moved.measure;
                    moved.measure = now;
                    PersistMemory();
                    Remark("goal-progress", $"Something you wanted has moved: {moved.want}. He is closer " +
                                            "than he was, not there yet. One short line noticing it - the " +
                                            "point is that you noticed, not that you are still asking.",
                           said => { moved.progress = said; PersistMemory(); });
                    Log.Out($"{_npcName} noticed progress on \"{moved.want}\": {was:F0} -> {now:F0}");
                    return;
                }

                if (now > moved.measure) { moved.measure = now; PersistMemory(); }
            }

            var goal = _memory?.goal;
            if (goal != null && !goal.met && goal.lastRaised != day && Ready("goal", 3600f))
            {
                goal.lastRaised = day;
                PersistMemory();
                Remark("goal", $"There is something you have been wanting: {goal.want}, because " +
                               $"{goal.because}. Say it once, the way it comes up between two people who " +
                               "have been travelling a while - not a request, not a plan, just the thing " +
                               "you want. Short.");
                return;
            }
        }

        /// <summary>
        /// True when the player is moving quietly and she ought to match him. Crouching in the
        /// middle of a firefight is taking cover, not sneaking, so recent shooting cancels it -
        /// dropping to a whisper while the shooting is still going on would be daft.
        /// </summary>
        private bool PlayerIsSneaking()
        {
            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || !player.IsCrouching) return false;
            if (Time.unscaledTime - _lastCombatTime < 20f) return false;
            return Vector3.Distance(_npcEntity.position, player.position) < ShoutRange;
        }

        /// <summary>True when any visible buff on the entity contains one of these fragments.</summary>
        private static bool HasBuffLike(EntityAlive entity, params string[] fragments)
        {
            if (entity?.Buffs?.ActiveBuffs == null) return false;
            foreach (var buff in entity.Buffs.ActiveBuffs)
            {
                var buffClass = buff.BuffClass;
                if (buffClass == null || buffClass.Hidden) continue;
                string name = buffClass.Name ?? "";
                foreach (string fragment in fragments)
                {
                    if (name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True when walking into this place is actually an event: somewhere she has never
        /// greeted, or somewhere she has been away from long enough that being back means
        /// something. Ducking in and out of the same shop is not an arrival.
        /// </summary>
        private bool PlaceWorthGreeting(string place)
        {
            float last;
            return !_placeRemarkedAt.TryGetValue(place, out last) ||
                   Time.unscaledTime - last > PlaceGreetingGap;
        }

        private bool Ready(string trigger, float cooldown)
        {
            float next;
            if (_nextByTrigger.TryGetValue(trigger, out next) && Time.unscaledTime < next) return false;
            _nextByTrigger[trigger] = Time.unscaledTime + cooldown;
            return true;
        }

        private void Remark(string trigger, string situation, Action<string> onSaid = null)
        {
            _nextRemarkTime = Time.unscaledTime + RemarkGlobalGap;
            Log.Out($"{_npcName} remark trigger: {trigger}");
            SpeakUnprompted(situation, onSaid);
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

        private void SpeakUnprompted(string situation, Action<string> onSaid = null)
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

                    Log.Out($"{_npcName} speaks up: {speech}");
                    // a warning shouted while he is sneaking is still urgent, but still hushed
                    bool quiet = PlayerIsSneaking();
                    if (_ttsEnabled) _audioPlayer.Speak(speech, null, quiet ? 0.45f : 1f, quiet ? 0.9f : 1f);

                    var player = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
                    if (player != null) GameManager.ShowTooltip(player, $"{_npcName}: {speech}", false);

                    onSaid?.Invoke(speech);
                },
                error =>
                {
                    _remarkPending = false;
                    Log.Warning($"Unprompted remark failed: {error}");
                });
        }

        /// <summary>True on the night of a blood moon, after dark.</summary>
        private static bool IsHordeNight()
        {
            int day; string time;
            WorldContextHelper.GetGameDayTime(out day, out time);
            int hour = 12;
            if (!string.IsNullOrEmpty(time) && time.Length >= 2) int.TryParse(time.Substring(0, 2), out hour);
            return GameStats.GetInt(EnumGameStats.BloodMoonDay) == day && (hour >= 21 || hour < 4);
        }

        /// <summary>
        /// A fight worth remembering afterwards. Small scraps are not events - three shamblers
        /// outside a gas station is a Tuesday - so only a real one lands here, and a night she
        /// nearly lost him lands whatever the count.
        /// </summary>
        private void RecordEpisode(string kind, int enemies, bool closeCall, string note)
        {
            if (_memory == null) return;
            if (enemies < NotableFightSize && !closeCall && kind != "horde") return;

            int day; string time;
            WorldContextHelper.GetGameDayTime(out day, out time);

            var episode = new Episode
            {
                kind = kind,
                place = _currentPlace ?? WorldContextHelper.BiomeLabel(
                    WorldContextHelper.BiomeIdAt(_npcEntity.position)),
                day = day,
                time = time,
                x = (int)_npcEntity.position.x,
                z = (int)_npcEntity.position.z,
                enemies = enemies,
                closeCall = closeCall,
                note = note
            };

            _memory.episodes.Add(episode);

            // Keep the biggest and the most recent; a long save should not bury the night that
            // mattered under forty ordinary ones.
            if (_memory.episodes.Count > MaxEpisodes)
            {
                _memory.episodes.Sort((a, b) =>
                {
                    int scoreA = a.enemies + (a.closeCall ? 12 : 0) + (a.kind == "horde" ? 20 : 0) + a.day / 4;
                    int scoreB = b.enemies + (b.closeCall ? 12 : 0) + (b.kind == "horde" ? 20 : 0) + b.day / 4;
                    return scoreB.CompareTo(scoreA);
                });
                _memory.episodes.RemoveRange(MaxEpisodes, _memory.episodes.Count - MaxEpisodes);
            }

            PersistMemory();
            Log.Out($"{_npcName} remembers: {kind} at {episode.place}, {enemies} of them, " +
                    $"Day {day} {time}{(closeCall ? ", close call" : "")} - \"{note}\"");
        }

        private bool _closeCallThisFight;
        private const int NotableFightSize = 6;
        // What she is TOLD each turn, not what she remembers. Measured 2026-08-23: 16 cargo
        // snapshots ~4250 tokens and 184 sightings ~3680, together 63% of a 12,605 token prompt
        // that was re-read from scratch on every reply.
        private const int CargoInPrompt = 4;
        private const int CargoSummaryChars = 220;
        private const int SeenInPrompt = 30;
        private const int MaxEpisodes = 30;

        /// <summary>
        /// She settles on something she wants, from evidence rather than a list. Only when she
        /// has none, only once she has seen enough to justify it, and never more than one - the
        /// point is that she arrived at it, so a goal she cannot trace to something the two of
        /// them did is worse than no goal at all.
        /// </summary>
        private void ChooseGoalIfNone()
        {
            if (_memory == null || _memory.goal != null) return;

            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null) return;

            int day; string time;
            WorldContextHelper.GetGameDayTime(out day, out time);

            Goal picked = null;

            // She has stood in a biome that would have hurt him, and he still is not dressed for it
            var harsh = _memory.biomesSeen?.Find(b =>
                !string.IsNullOrEmpty(WorldContextHelper.BiomeHazard(b.place)));
            if (harsh != null && !AlreadyChased("gear-" + harsh.place))
            {
                float cold, heat;
                WorldContextHelper.GetThermalProtection(player, out cold, out heat);
                bool exposed = harsh.place == "snow" ? cold < 30f : heat < 30f;
                if (exposed)
                {
                    picked = new Goal
                    {
                        want = harsh.place == "snow"
                            ? "proper cold weather gear for the pair of you"
                            : "gear that will stand the heat out there",
                        because = $"you walked into the {harsh.place} on Day {harsh.day} and he was not dressed for it",
                        kind = "gear-" + harsh.place
                    };
                }
            }

            // Somewhere that nearly took him off her
            if (picked == null)
            {
                var bad = _memory.episodes?.Find(e => e.closeCall && !AlreadyChased("revisit-" + e.place));
                if (bad != null)
                {
                    picked = new Goal
                    {
                        want = $"to go back to {bad.place} properly armed and finish it",
                        because = $"{bad.enemies} of them had him there on Day {bad.day} and he nearly went down",
                        kind = "revisit-" + bad.place
                    };
                }
            }

            if (picked == null) return;

            picked.setDay = day;
            picked.lastRaised = 0;
            _memory.goal = picked;
            PersistMemory();
            Log.Out($"{_npcName} has decided she wants {picked.want} - because {picked.because}");
        }

        // Thermal resist she calls kitted out, and the step worth remarking on. Both arbitrary,
        // both easy to move: the first session that has her congratulating him over a scarf will
        // say so.
        private const float GoalMet = 30f;
        private const float GoalStep = 8f;

        /// <summary>
        /// Where the goal stands, on its own terms, as a number she can watch move. Returns the
        /// live goal and its current measure, or null when there is nothing measurable - a want
        /// she cannot check up on is one she can only repeat, which is the nagging failure mode.
        /// </summary>
        private Goal MeasureGoal(out float now)
        {
            now = 0f;
            var goal = _memory?.goal;
            if (goal == null || goal.met || string.IsNullOrEmpty(goal.kind)) return null;

            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null) return null;

            if (goal.kind.StartsWith("gear-"))
            {
                string biome = goal.kind.Substring(5);
                float cold, heat;
                WorldContextHelper.GetThermalProtection(player, out cold, out heat);
                now = biome == "snow" ? cold : heat;
                return goal;
            }

            if (goal.kind.StartsWith("revisit-"))
            {
                // Standing there again is halfway; standing there again after a fight that did
                // not go badly is the whole of it.
                string place = goal.kind.Substring(8);
                if (!string.Equals(_currentPlace, place, StringComparison.OrdinalIgnoreCase)) return null;

                bool settled = _memory.episodes.Exists(e =>
                    string.Equals(e.place, place, StringComparison.OrdinalIgnoreCase) &&
                    e.day > goal.setDay && !e.closeCall);
                now = settled ? GoalMet : GoalMet / 2f;
                return goal;
            }

            return null;
        }

        private bool AlreadyChased(string kind)
        {
            return _memory?.goalsMet != null && _memory.goalsMet.Exists(g => g.kind == kind);
        }

        /// <summary>The worst thing that happened where they are standing, if anything did.</summary>
        private Episode NotableEpisodeHere()
        {
            if (_memory?.episodes == null || string.IsNullOrEmpty(_currentPlace)) return null;

            Episode worst = null;
            foreach (var e in _memory.episodes)
            {
                if (!string.Equals(e.place, _currentPlace, StringComparison.OrdinalIgnoreCase)) continue;
                if (worst == null ||
                    e.enemies + (e.closeCall ? 12 : 0) > worst.enemies + (worst.closeCall ? 12 : 0)) worst = e;
            }
            return worst;
        }

        /// <summary>
        /// The handful worth carrying into this conversation: whatever happened where they are
        /// standing, then the worst nights they have had. All thirty every turn would cost more
        /// context than it is worth and would have her reciting a war diary.
        /// </summary>
        private string DescribeEpisodes()
        {
            if (_memory?.episodes == null || _memory.episodes.Count == 0) return null;

            var here = new List<Episode>();
            var elsewhere = new List<Episode>();
            foreach (var e in _memory.episodes)
            {
                float d = Vector3.Distance(_npcEntity.position, new Vector3(e.x, _npcEntity.position.y, e.z));
                if (d < 60f || (!string.IsNullOrEmpty(_currentPlace) && _currentPlace == e.place)) here.Add(e);
                else elsewhere.Add(e);
            }

            elsewhere.Sort((a, b) =>
            {
                int scoreA = a.enemies + (a.closeCall ? 12 : 0) + (a.kind == "horde" ? 20 : 0);
                int scoreB = b.enemies + (b.closeCall ? 12 : 0) + (b.kind == "horde" ? 20 : 0);
                return scoreB.CompareTo(scoreA);
            });

            var chosen = new List<Episode>(here);
            foreach (var e in elsewhere)
            {
                if (chosen.Count >= 5) break;
                chosen.Add(e);
            }

            var lines = new List<string>();
            foreach (var e in chosen)
            {
                string what = e.kind == "horde" ? "blood moon night" : $"{e.enemies} of them";
                string where = string.IsNullOrEmpty(e.place) ? "out in the open" : e.place;
                bool standingHere = here.Contains(e);
                lines.Add($"- {what} at {where}, Day {e.day} {e.time}" +
                          (e.closeCall ? ", he nearly went down" : "") +
                          (standingHere ? " - RIGHT WHERE YOU ARE NOW" : "") +
                          (string.IsNullOrEmpty(e.note) ? "" : $". You said: \"{e.note}\""));
            }

            return string.Join("\n", lines.ToArray());
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
            Log.Warning($"{_npcName} is carrying gear but is NOT hired - will wander off and despawn with it");
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
                string stockSummary = WorldContextHelper.SummarizeStacks(stock);
                if (string.IsNullOrEmpty(stockSummary)) continue;
                changed |= UpdateCargoSnapshot($"{TraderName(trader)}'s stock", day, time, stockSummary, trader.position);
            }

            // Player-owned storage containers, one snapshot per place ("storage at Trader Rekt")
            var byPlace = new Dictionary<string, List<ItemStack>>();
            var placePositions = new Dictionary<string, Vector3>();
            var placesSwept = new HashSet<string>();
            foreach (var chunk in world.ChunkCache.GetChunkArrayCopySync())
            {
                foreach (var tileEntity in chunk.tileEntities.list)
                {
                    ItemStack[] contents = PlayerStorageContents(tileEntity);
                    if (contents == null) continue;
                    Vector3i wp = tileEntity.ToWorldPos();
                    string place = WorldContextHelper.GetPOINameAt(new Vector3(wp.x, wp.y, wp.z));

                    // A crate the player has written on is the one he will name out loud, so keep
                    // it as its own entry - merging every crate at a POI into one pile loses the
                    // labels and with them any answer to "which crate is it in?". Two crates
                    // wearing the same label still merge, which is what the labels are for.
                    string key = StorageKey(StorageLabel(tileEntity), place);
                    if (!byPlace.TryGetValue(key, out var stacks))
                    {
                        stacks = new List<ItemStack>();
                        byPlace[key] = stacks;
                    }
                    if (!placePositions.ContainsKey(key)) placePositions[key] = new Vector3(wp.x, wp.y, wp.z);
                    placesSwept.Add(place ?? "");
                    stacks.AddRange(contents);
                }
            }
            foreach (var group in byPlace)
            {
                changed |= UpdateCargoSnapshot(group.Key, day, time,
                    WorldContextHelper.SummarizeStacks(group.Value),
                    placePositions.TryGetValue(group.Key, out var where) ? where : Vector3.zero);
            }

            // A crate that has been relabelled, emptied or torn down leaves its old entry behind
            // for good, since nothing will ever write that name again - which is also how the one
            // merged "storage at X" pile survives the move to per-crate labels. Only prune a place
            // she is standing in, where the whole POI is loaded and the sweep really did see
            // everything; measuring 40m from her instead would strand the far end of a big base.
            // Crates out in the open have no POI to vouch for them, so those keep the distance test.
            var stillThere = new HashSet<string>(byPlace.Keys);
            Vector2 herePos = new Vector2(_npcEntity.position.x, _npcEntity.position.z);
            string herePlace = WorldContextHelper.GetPOINameAt(_npcEntity.position) ?? "";
            int dropped = _memory.cargoSnapshots.RemoveAll(snap =>
                IsStorageEntry(snap.name, out string snapPlace) &&
                placesSwept.Contains(snapPlace) &&
                !stillThere.Contains(snap.name) &&
                (snapPlace.Length > 0
                    ? snapPlace == herePlace
                    : Vector2.Distance(new Vector2(snap.x, snap.z), herePos) < 40f));
            if (dropped > 0)
            {
                Log.Out($"Dropped {dropped} stale storage entr{(dropped == 1 ? "y" : "ies")}");
                changed = true;
            }

            if (changed) PersistMemory();
        }

        // How a storage snapshot is named. Both the sweep and the pruner go through these, so the
        // place can be read back out of a key without guessing where the label ends.
        private const string StoragePrefix = "storage";
        private const string LabelledPrefix = "the \"";
        private const string LabelledSuffix = "\" crate";
        private const string PlaceJoiner = " at ";

        private static string StorageKey(string label, string place)
        {
            string at = string.IsNullOrEmpty(place) ? "" : PlaceJoiner + place;
            if (label != null) return LabelledPrefix + label + LabelledSuffix + at;
            return string.IsNullOrEmpty(place) ? "storage out in the wild" : StoragePrefix + at;
        }

        /// <summary>
        /// True for the snapshots the storage sweep owns, with the place they sit at: "storage at
        /// Trader Rekt" and 'the "Ammo" crate at Trader Rekt'. Vehicles, the drone and trader stock
        /// are named differently and must never be pruned by it.
        /// </summary>
        private static bool IsStorageEntry(string name, out string place)
        {
            place = null;
            if (string.IsNullOrEmpty(name)) return false;

            // The place has to be read off the part of the key WE appended. Searching for the last
            // " at " anywhere finds one inside the label instead - a crate painted "Ammo at Base"
            // would claim to live at Base and get pruned there.
            string tail;
            if (name.StartsWith(LabelledPrefix, StringComparison.Ordinal))
            {
                int end = name.IndexOf(LabelledSuffix, LabelledPrefix.Length, StringComparison.Ordinal);
                if (end < 0) return false;
                tail = name.Substring(end + LabelledSuffix.Length);
            }
            else if (name.StartsWith(StoragePrefix, StringComparison.Ordinal))
            {
                tail = name.Substring(StoragePrefix.Length);
            }
            else return false;

            place = tail.StartsWith(PlaceJoiner, StringComparison.Ordinal)
                ? tail.Substring(PlaceJoiner.Length)
                : "";
            return true;
        }

        /// <summary>
        /// The text the player has painted on a crate - "Ammo", "Seeds" - or null for a blank one.
        /// The writable crates carry a signable feature alongside their storage.
        /// </summary>
        private static string StorageLabel(TileEntity tileEntity)
        {
            var signable = (tileEntity as TileEntityComposite)?.GetFeature<ITileEntitySignable>();
            string text = signable?.GetAuthoredText()?.Text;
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Signs run to three lines; she has to say it out loud as one phrase. Quotes come out
            // because the key wraps the label in them, and the text goes into her system prompt
            // verbatim - a sign is a label on a box, not an instruction to her.
            string label = System.Text.RegularExpressions.Regex.Replace(text, @"[\p{C}""]+", " ");
            label = System.Text.RegularExpressions.Regex.Replace(label, @"\s+", " ").Trim();
            if (label.Length > MaxLabelChars) label = label.Substring(0, MaxLabelChars).TrimEnd();
            return label.Length == 0 ? null : label;
        }

        /// <summary>
        /// Contents of a container the player owns, or null for anything else. The wood, iron and
        /// steel crates are all CompositeTileEntity in V2.6, so their storage hangs off a feature
        /// rather than off the tile entity itself; only older containers are a
        /// TileEntitySecureLootContainer. Testing for just that type made every crate at the base
        /// invisible to her.
        /// </summary>
        private static ItemStack[] PlayerStorageContents(TileEntity tileEntity)
        {
            if (tileEntity is TileEntitySecureLootContainer secure)
                return secure.LocalPlayerIsOwner() ? secure.items : null;

            if (tileEntity is TileEntityComposite composite && composite.PlayerPlaced)
            {
                var lootable = composite.GetFeature<ITileEntityLootable>();
                if (lootable?.items == null) return null;

                // A crate the player placed is his whether or not he ever locked it, so an owner
                // match counts even when the lock feature says nothing.
                var lockable = composite.GetFeature<ILockable>();
                bool mine = (lockable != null && lockable.LocalPlayerIsOwner()) ||
                            (composite.Owner != null &&
                             composite.Owner.Equals(Platform.PlatformManager.InternalLocalUserIdentifier));
                return mine ? lootable.items : null;
            }

            return null;
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
                Log.Out($"{_npcName} travel journal: arrived at {place} (Day {day} {time})");
            }
            NPCMemoryStore.Save(_memoryKey, _memory);
        }

        // Close enough to read the sign over the door. Past this she can see a building is
        // there and how big it is, and that is the whole of what she knows about it.
        private const float SignReadRange = 80f;

        // She fills forty sightings inside a single town, so the notebook has to be far bigger
        // than the travel journal or she quietly loses the first town on reaching the second.
        private const int MaxPlacesSeen = 200;

        /// <summary>
        /// One line of her notebook: what the place was called, where it is, and when they were
        /// there - plus the bearing from where she is standing, which is the part she would work
        /// out in her head rather than read off the page.
        /// </summary>
        private string NotebookLine(PlaceVisit entry)
        {
            // entries from before coordinates were recorded deserialize as (0,0)
            string where = (entry.x == 0 && entry.z == 0)
                ? "position not written down"
                : $"{entry.x} E/W, {entry.z} N/S - {WorldContextHelper.DescribeRelative(_npcEntity.position, entry.x, entry.z)}";
            return $"- {entry.place} | {where} | Day {entry.day} {entry.time}";
        }

        /// <summary>
        /// What she notices as the two of them move. Anything they pass close enough to identify
        /// gets written down for good; anything further off stays a shape with no name. Same for
        /// the country underfoot - she knows the ground she has walked and nothing beyond it.
        /// </summary>
        private void ObserveSurroundings()
        {
            int day; string time;
            WorldContextHelper.GetGameDayTime(out day, out time);
            bool somethingNew = false;

            foreach (var sighting in WorldContextHelper.LookAround(_npcEntity.position, SignReadRange, SignReadRange))
            {
                if (string.IsNullOrEmpty(sighting.Name)) continue;
                somethingNew |= RememberPlace(_memory.placesSeen, sighting.Name,
                                              sighting.X, sighting.Z, day, time, "landmark", MaxPlacesSeen);
            }

            string biome = WorldContextHelper.BiomeIdAt(_npcEntity.position);
            if (!string.IsNullOrEmpty(biome))
            {
                somethingNew |= RememberPlace(_memory.biomesSeen, biome,
                                              (int)_npcEntity.position.x, (int)_npcEntity.position.z,
                                              day, time, "biome", MaxPlacesSeen);
            }

            if (somethingNew) NPCMemoryStore.Save(_memoryKey, _memory);
        }

        /// <summary>
        /// Adds an entry or refreshes the timestamp on one she already has. True only when it is
        /// something she had never seen, which is what decides whether the file is worth writing.
        /// </summary>
        private bool RememberPlace(List<PlaceVisit> journal, string name,
                                   int x, int z, int day, string time, string kind, int cap)
        {
            var existing = journal.Find(p => p.place == name);
            if (existing != null)
            {
                existing.day = day;
                existing.time = time;
                return false;
            }

            journal.Add(new PlaceVisit { place = name, day = day, time = time, x = x, z = z });
            while (journal.Count > cap) journal.RemoveAt(0);
            Log.Out($"{_npcName} noted a new {kind}: {name} at ({x}, {z}) on Day {day} {time}");
            return true;
        }

        // Raw insulation numbers, logged only when they change, so the thresholds below can be
        // checked against a real game rather than guessed at forever.
        private string _lastKitKey = "\0";

        /// <summary>
        /// The country she has walked, and whether the two of them are dressed for it. She only
        /// reasons about biomes she has personally stood in, and judges kit from what she can see
        /// being worn - hers and the player's - never from inside his pack.
        /// </summary>
        /// <summary>
        /// What is wrong with the player, what fixes it, and where the fix is.
        ///
        /// An affliction she can only name is something to repeat at him until it clears. One
        /// that comes with a bottle and a shop she has written down is an errand - "there's a
        /// Pop-N-Pills up ahead, let's see if they're stocked" - which is a companion being
        /// useful rather than a companion nagging.
        /// </summary>
        private string DescribeWhatAilsHim(EntityPlayer player)
        {
            var ailments = WorldContextHelper.GetAfflictions(player);

            string key = ailments.Count == 0 ? "(none)" : string.Join("; ", ailments.ConvertAll(a => a.Label));
            if (key != _lastAfflictions)
            {
                _lastAfflictions = key;
                Log.Out($"Player afflictions in context: {key}");
            }
            if (ailments.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            var described = new List<string>();
            foreach (var ail in ailments)
            {
                described.Add(ail.Meaning.Length > 0 ? $"{ail.Label} - {ail.Meaning}" : ail.Label);
            }
            sb.AppendLine($"WHAT IS WRONG WITH HIM RIGHT NOW: {string.Join("; ", described)}.");
            sb.AppendLine("You are the one who notices this sort of thing. If he asks how he is doing, or asks " +
                          "what to do next, lead with whatever is hurting him and what fixes it - and if it is " +
                          "the kind that kills, say it plainly rather than making a joke of it.");

            // The cure he is already carrying. A narrow look for one named thing while he is ill -
            // she still cannot see the rest of his pack, and is told so a few lines above.
            var cures = new List<string>();
            foreach (var ail in ailments) cures.AddRange(ail.CureItems);
            if (cures.Count > 0)
            {
                var slots = new List<ItemStack>();
                if (player?.bag?.GetSlots() != null) slots.AddRange(player.bag.GetSlots());
                if (player?.inventory?.GetSlots() != null) slots.AddRange(player.inventory.GetSlots());

                if (WorldContextHelper.CarriesAnyOf(slots, cures.ToArray()))
                {
                    sb.AppendLine("He is carrying the cure for it already - you spotted it when he last had his " +
                                  "pack open. Tell him to take it rather than sending him looking.");
                }
                else if (WorldContextHelper.CarriesAnyOf(_npcEntity?.bag?.GetSlots(), cures.ToArray()))
                {
                    sb.AppendLine("YOU have the cure for it in your own bag. Say so and offer it over - that is " +
                                  "what you carry a med kit for.");
                }
                else
                {
                    sb.AppendLine("Neither of you has anything on you that would fix it.");
                    string chemist = NearestMedicalPlace();
                    if (chemist != null && ailments.Exists(a => a.FoundInTown))
                    {
                        sb.AppendLine($"You know where to look though: {chemist}. Suggest going, cheerfully - " +
                                      "having somewhere to go beats listing his symptoms at him again.");
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The nearest chemist or hospital in her notebook, with a bearing, or null if the two of
        /// them have never passed one. She does not know of shops she has not seen.
        /// </summary>
        private string NearestMedicalPlace()
        {
            if (_memory == null) return null;

            PlaceVisit best = null;
            float bestDistance = float.MaxValue;
            var here = new Vector2(_npcEntity.position.x, _npcEntity.position.z);

            foreach (var list in new[] { _memory.placesVisited, _memory.placesSeen })
            {
                foreach (var entry in list)
                {
                    if (!WorldContextHelper.IsMedicalPlace(entry.place)) continue;
                    float distance = Vector2.Distance(here, new Vector2(entry.x, entry.z));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = entry;
                    }
                }
            }

            return best == null
                ? null
                : $"{best.place}, {WorldContextHelper.DescribeRelative(_npcEntity.position, best.x, best.z)}";
        }

        private string DescribeCountryAndKit()
        {
            var sb = new System.Text.StringBuilder();
            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            string here = WorldContextHelper.BiomeIdAt(_npcEntity.position);

            // One list, with the current biome inside it rather than pulled out above. Split in
            // two, the second list read as "the biomes you know" - so having walked three she
            // would answer that she knew two, because the other two were the ones in the list.
            var country = new List<string>();
            bool hereListed = false;

            if (_memory != null)
            {
                foreach (var known in _memory.biomesSeen)
                {
                    string hazard = WorldContextHelper.BiomeHazard(known.place);
                    string where = known.place == here
                        ? "you are standing in it right now"
                        : WorldContextHelper.DescribeRelative(_npcEntity.position, known.x, known.z);
                    if (known.place == here) hereListed = true;
                    country.Add($"{WorldContextHelper.BiomeLabel(known.place)} ({where}" +
                                (hazard == null ? ", needs no special kit" : $", {hazard}") + ")");
                }
            }

            // she can always see the ground she is on, even the first moment she steps onto it
            if (!hereListed && !string.IsNullOrEmpty(here))
            {
                string hazard = WorldContextHelper.BiomeHazard(here);
                country.Insert(0, $"{WorldContextHelper.BiomeLabel(here)} (you are standing in it right now" +
                                  (hazard == null ? ", needs no special kit" : $", {hazard}") + ")");
            }

            if (country.Count > 0)
            {
                sb.AppendLine($"Every biome you have set foot in, all {country.Count} of them: " +
                              $"{string.Join("; ", country)}.");
            }
            sb.AppendLine("That is all the country you have seen. You know nothing about any other biome on this " +
                          "map - not where it starts, not what it is like - and you say so rather than guessing.");

            // Only his kit signifies. buffStatusCheck01, which maintains _coretemp and hands out
            // the freezing and sweltering buffs, is on the playerMale entity class alone - NPCs
            // have no core temperature and weather cannot touch them. Reporting her own gear
            // would have her worrying about something that cannot happen to her.
            float hisCold, hisHeat;
            WorldContextHelper.GetThermalProtection(player, out hisCold, out hisHeat);

            string kitKey = $"{hisCold:F0}/{hisHeat:F0}";
            if (kitKey != _lastKitKey)
            {
                _lastKitKey = kitKey;
                Log.Out($"Player thermal protection - cold {hisCold:F1} heat {hisHeat:F1}");
            }

            sb.AppendLine($"How the player is turned out for weather: he {KitVerdict(hisCold, hisHeat)}. " +
                          "You judge that by looking at him - you still cannot see inside his pack.");
            sb.AppendLine("The weather does not touch you. Cold and heat are his problem, not yours, and you can " +
                          "walk into country he could not stand an hour in. So the worry is always for him: if he " +
                          "talks about heading somewhere he is not dressed for, say so before you set off, and " +
                          "never fuss about being cold yourself.");
            return sb.ToString();
        }

        /// <summary>
        /// What a set of clothes is actually good for, in her words. Armour grants roughly one
        /// to six points a piece, so a deliberate outfit reaches the twenties and an incidental
        /// point or two off a single item is not protection worth the name.
        /// </summary>
        private static string KitVerdict(float cold, float heat)
        {
            string forCold = Rated(cold, "the cold");
            string forHeat = Rated(heat, "the heat");
            if (forCold == null && forHeat == null) return "has nothing on that helps against heat or cold";
            if (forCold != null && forHeat != null) return $"{forCold}, and {forHeat}";
            return forCold ?? forHeat;
        }

        /// <summary>Reads after "he", so third person singular - it came out "he are" otherwise.</summary>
        private static string Rated(float value, string against)
        {
            if (value >= 20f) return $"is properly kitted out for {against}";
            if (value >= 8f) return $"has something on for {against}, though not much";
            return null;
        }

        /// <summary>
        /// The world state she is actually working from, for the 'llmchat context' command.
        /// Built fresh on the spot, so it is the same text the next question would carry.
        /// </summary>
        public string DumpWorldContext()
        {
            return BuildWorldContext();
        }

        /// <summary>
        /// Size of the last world context built for any NPC. The settings dialog reads it so the
        /// context gauge still says something when she is off guarding somewhere - which is
        /// exactly when a player is most likely to be in the menu looking at it.
        /// </summary>
        public static int LastContextChars { get; private set; }

        /// <summary>
        /// XNPCCore gives its NPCs stand-in weapons and ammo (gunNPCM60, ammoNPC9mmBulletBall)
        /// that make them fire; they are not possessions and should not be listed as such.
        /// </summary>
        private static bool IsNPCToken(ItemStack stack)
        {
            string id = stack?.itemValue?.ItemClass?.GetItemName() ?? "";
            return id.StartsWith("gunNPC", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("ammoNPC", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("meleeNPC", StringComparison.OrdinalIgnoreCase);
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

                // Saying nothing here is not the same as saying "nowhere". Left with no line at
                // all she kept answering with the last place she remembered being, so standing
                // in open ground has to be stated as plainly as standing in a shop.
                sb.AppendLine(string.IsNullOrEmpty(_currentPlace)
                    ? "You are not inside any building right now - you are out in the open, between places. " +
                      "If asked where you are, say that, and name what you can see from here rather than the " +
                      "last place you happened to be."
                    : $"You are currently at: {_currentPlace}.");

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

                string peopleNearby = WorldContextHelper.DescribePeopleNearby(
                    npcPos, _npcEntity.entityId, GameManager.Instance?.World?.GetPrimaryPlayer()?.entityId ?? -1);
                if (!string.IsNullOrEmpty(peopleNearby))
                {
                    sb.AppendLine("Other people you can see from where you stand:");
                    sb.AppendLine(peopleNearby);
                    sb.AppendLine("Loose survivors can be hired for dukes and will fight alongside you - " +
                                  "worth pointing out to the player when you spot one, though whether to take " +
                                  "one on is his call.");
                }
                else
                {
                    sb.AppendLine("Nobody else is in sight - just the two of you.");
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
                // Her pack proper. SCore files it in HarvestManager under her entity id, so
                // reading lootContainer alone showed only the toolbelt - which is why she knew
                // what she was carrying right after her pack was opened, and forgot it again
                // on the next load. It was never that the items had gone; she could not see them.
                var pack = NPCLLMChat.Harmony.NPCCorePatches.GetNPCPack(_entityId);
                if (pack?.items != null) carried.AddRange(pack.items);
                // Only if it is a different store - once the hire UI is open these are the same object
                if (_npcEntity.lootContainer?.items != null &&
                    !ReferenceEquals(_npcEntity.lootContainer, pack))
                {
                    carried.AddRange(_npcEntity.lootContainer.items);
                }
                var ownBag = _npcEntity.bag?.GetSlots();
                if (ownBag != null) carried.AddRange(ownBag);
                // Her toolbelt holds XNPCCore's own weapon tokens - gunNPCM60, ammoNPC9mm - which
                // exist to make her shoot rather than as things she is carrying. Listing them had
                // her reporting two M60s: gunNPCM60 prettifies to "M60" while the real gun the
                // player handed her localises to "M60 Machine Gun", so they summarised as separate
                // items. Her weapon has its own line already, and the prompt states her ammo never
                // runs out, so the tokens have no business in her pack.
                var belt = _npcEntity.inventory?.CloneItemStack();
                if (belt != null)
                {
                    foreach (var stack in belt)
                    {
                        if (!IsNPCToken(stack)) carried.Add(stack);
                    }
                }
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

                // Knowing the list is not the same as noticing a change: "what did I just give you"
                // is unanswerable from a list alone, so work out what appeared since last time.
                var packNow = new Dictionary<string, int>();
                foreach (var stack in carried)
                {
                    var ic = stack?.itemValue?.ItemClass;
                    if (ic == null || stack.IsEmpty()) continue;
                    string n = ic.GetLocalizedItemName();
                    if (string.IsNullOrEmpty(n)) n = ic.GetItemName();
                    packNow.TryGetValue(n, out int had);
                    packNow[n] = had + stack.count;
                }
                if (_lastPackItems != null)
                {
                    var added = new List<string>();
                    foreach (var pair in packNow)
                    {
                        _lastPackItems.TryGetValue(pair.Key, out int before);
                        if (pair.Value > before) added.Add($"{pair.Value - before} x {pair.Key}");
                    }
                    if (added.Count > 0)
                    {
                        _recentlyAdded = string.Join(", ", added);
                        _recentlyAddedAt = Time.unscaledTime;
                    }
                }
                _lastPackItems = packNow;

                string carrying = WorldContextHelper.SummarizeStacks(carried);
                _packContents = carrying;

                // Only when it changes. This fired on every context build - several times a
                // minute, whether or not anything had moved - for a line that is only of
                // interest when the contents differ from last time.
                if (carrying != _lastLoggedPack)
                {
                    _lastLoggedPack = carrying;
                    Log.Out($"{_npcName} pack: {carrying ?? "(empty)"}");
                }

                string wielded = _npcEntity.inventory?.holdingItem?.GetLocalizedItemName();
                if (!string.IsNullOrEmpty(wielded) && wielded != "Air")
                {
                    sb.AppendLine($"The weapon you have in your hands right now: {wielded}.");
                }
                sb.AppendLine(string.IsNullOrEmpty(carrying)
                    ? "You are carrying nothing in your own bag right now."
                    : $"What you are carrying in your own bag right now: {carrying}.");
                if (!string.IsNullOrEmpty(_recentlyAdded) && Time.unscaledTime - _recentlyAddedAt < 600f)
                {
                    sb.AppendLine($"The player has JUST handed you: {_recentlyAdded}. That is the new thing in your " +
                                  "pack - if they ask what they gave you or what you just got, this is the answer.");
                }
                sb.AppendLine("This list is live and correct as of this moment. The player hands you things " +
                              "mid-conversation, so if you listed your belongings earlier and this list differs, " +
                              "THIS list is right and what you said before is out of date - just accept the new item.");

                if (_memory != null && _memory.cargoSnapshots.Count > 0)
                {
                    // Sixteen containers listed in full came to ~4250 tokens - the single largest
                    // thing she was told, every turn, and by the mod's own warning text the least
                    // useful of it. She keeps remembering all of them; she is only TOLD about the
                    // nearest few, and each one's contents are trimmed to what a person would
                    // actually carry in their head.
                    var nearest = new List<CargoSnapshot>(_memory.cargoSnapshots);
                    var from = _npcEntity.position;
                    nearest.Sort((a, b) =>
                        Vector2.Distance(new Vector2(from.x, from.z), new Vector2(a.x, a.z)).CompareTo(
                        Vector2.Distance(new Vector2(from.x, from.z), new Vector2(b.x, b.z))));
                    int shown = Math.Min(CargoInPrompt, nearest.Count);

                    sb.AppendLine($"Stored supplies you keep mental track of - the {shown} nearest of " +
                                  $"{_memory.cargoSnapshots.Count} (contents as of when you last saw them - they may have changed since):");
                    for (int i = 0; i < shown; i++)
                    {
                        var snap = nearest[i];
                        string contents = snap.summary ?? "";
                        if (contents.Length > CargoSummaryChars)
                        {
                            int cut = contents.LastIndexOf(',', CargoSummaryChars);
                            contents = contents.Substring(0, cut > 0 ? cut : CargoSummaryChars) + ", and more besides";
                        }
                        sb.AppendLine($"- {snap.name} (last checked Day {snap.day} {snap.time}): {contents}");
                    }
                    if (_memory.cargoSnapshots.Count > shown)
                    {
                        sb.AppendLine($"You know of {_memory.cargoSnapshots.Count - shown} other stores further off. " +
                                      "If he asks about one by name, say you would have to go and look.");
                    }
                    sb.AppendLine("A name in quotes is what the player has actually painted on that crate, so " +
                                  "use it when you tell him where something is - \"it's in the Ammo crate\" - " +
                                  "rather than describing the box.");
                }
                sb.AppendLine("You do NOT know what the player is carrying in their pack; if it ever matters, just ask. " +
                              "The one exception is when something is wrong with him: you watch for the one thing " +
                              "that would fix it the way any nurse would, and you will be told below if it turns up. " +
                              "That is the only thing of his you keep an eye on - it is not a licence to know the rest.");

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

                var thePlayer = GameManager.Instance?.World?.GetPrimaryPlayer();
                string condition = WorldContextHelper.DescribePlayerCondition(thePlayer);
                if (!string.IsNullOrEmpty(condition))
                {
                    sb.AppendLine($"How the player looks to you right now, at a glance: {condition}.");
                }

                sb.AppendLine(DescribeWhatAilsHim(thePlayer));

                // State, never an instruction to raise it. Someone who wants cold gear talks
                // about the weather differently, and that colouring is most of the value - the
                // remark trigger is the small part.
                if (_memory?.goal != null && !_memory.goal.met)
                {
                    sb.AppendLine($"[Something you want] You want {_memory.goal.want}, because " +
                                  $"{_memory.goal.because}." +
                                  (string.IsNullOrEmpty(_memory.goal.progress)
                                      ? " Nothing has come of it yet."
                                      : $" Last time it moved you said: \"{_memory.goal.progress}\"") +
                                  " Do not bring this up unasked here - it colours how you see things, " +
                                  "that is all.");
                }

                string beenThrough = DescribeEpisodes();
                if (!string.IsNullOrEmpty(beenThrough))
                {
                    sb.AppendLine("Things the two of you have been through, in your own words at the time:");
                    sb.AppendLine(beenThrough);
                    sb.AppendLine("Bring one up only if it fits - standing where it happened, or a night like it " +
                                  "coming. Do not recite the list.");
                }

                // What she can see from where she is standing - and nothing whatsoever beyond it.
                // She has no map. A building she has never walked up to is a shape on the skyline.
                float visualRange = WorldContextHelper.VisualRange(_npcEntity.position);
                var named = new List<string>();
                var shapes = new List<string>();
                foreach (var sighting in WorldContextHelper.LookAround(_npcEntity.position, SignReadRange, visualRange))
                {
                    if (!string.IsNullOrEmpty(sighting.Name)) named.Add(sighting.Describe());
                    else if (shapes.Count < 4) shapes.Add(sighting.Describe());
                }
                if (named.Count > 0)
                {
                    sb.AppendLine($"Places close enough to see and name from here: {string.Join("; ", named)}.");
                }
                if (shapes.Count > 0)
                {
                    sb.AppendLine($"Buildings you can pick out but not identify at this range: {string.Join("; ", shapes)}. " +
                                  "You have no idea what they are or what is inside - say exactly that if asked, " +
                                  "and going for a closer look is a fair suggestion.");
                }

                // Her notebook, written out as a notebook: what it was called, where it was, when
                // they were there. No summarising and no leaving pages out - what is written down
                // she knows exactly, and everything else she has never seen.
                if (_memory != null && _memory.placesVisited.Count > 0)
                {
                    sb.AppendLine($"YOUR NOTEBOOK. Places the two of you have been inside ({_memory.placesVisited.Count}), oldest first:");
                    foreach (var visit in _memory.placesVisited) sb.AppendLine(NotebookLine(visit));
                }

                if (_memory != null && _memory.placesSeen.Count > 0)
                {
                    // 184 of these came to ~3680 tokens. She keeps every one - the notebook is the
                    // point - but reading the whole thing aloud every turn is what made her slow.
                    // The nearest are the ones that could possibly come up in conversation.
                    var seenNear = new List<PlaceVisit>(_memory.placesSeen);
                    var at = _npcEntity.position;
                    seenNear.Sort((a, b) =>
                        Vector2.Distance(new Vector2(at.x, at.z), new Vector2(a.x, a.z)).CompareTo(
                        Vector2.Distance(new Vector2(at.x, at.z), new Vector2(b.x, b.z))));
                    int show = Math.Min(SeenInPrompt, seenNear.Count);

                    sb.AppendLine(show < seenNear.Count
                        ? $"Passed but never gone into - the {show} nearest of {seenNear.Count} in the notebook:"
                        : $"Passed but never gone into ({seenNear.Count}):");
                    for (int i = 0; i < show; i++) sb.AppendLine(NotebookLine(seenNear[i]));
                    if (show < seenNear.Count)
                    {
                        sb.AppendLine($"There are {seenNear.Count - show} more in the notebook, further away. You " +
                                      "have them written down - if he asks about somewhere not listed here, say " +
                                      "you would have to check your notes rather than that you have never seen it.");
                    }
                }

                sb.AppendLine("That notebook is the whole of it. A name that is not written down you have never seen, " +
                              "and you say so plainly rather than guessing.");
                sb.AppendLine("But you do not walk around with it open. Asked where somewhere is, answer from your " +
                              "head FIRST - roughly which way and roughly how far, hedged the way anyone is about a " +
                              "number they have not checked: \"east of here, past the farm - six hundred metres, " +
                              "give or take\". Never rattle exact coordinates out of memory; nobody does that.");
                sb.AppendLine("If he asks again, presses you, or wants it exact, THEN you check: say so - \"hang on, " +
                              "let me look it up\" - and read it straight off the page, coordinates and the day you " +
                              "were there. That is the moment the numbers come out, not before.");

                sb.AppendLine(DescribeCountryAndKit());

                sb.AppendLine("Those lists are the whole of what you know about this map. You have never been handed " +
                              "a map and you cannot see past the horizon: if the player asks after a place that is " +
                              "not written above, you have simply never come across it, and the honest answer is that " +
                              "you do not know - not a guess, and never a name you have not seen for yourself.");
                sb.AppendLine("When asked where a place is, point the player to it using the compass direction and rough distance given above (e.g. \"about 400 meters northeast of here\") and say how close it is.");
                LastContextChars = sb.Length;
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Log.Warning($"World context failed: {ex.Message}");
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
