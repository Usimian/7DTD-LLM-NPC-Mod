using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace NPCLLMChat
{
    /// <summary>
    /// Handles communication with local LLM servers (Ollama, LM Studio, etc.)
    /// This is a singleton service that manages all LLM requests for NPCs.
    /// </summary>
    public class LLMService : MonoBehaviour
    {
        private static LLMService _instance;
        public static LLMService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LLMService");
                    _instance = go.AddComponent<LLMService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        // Configuration loaded from XML
        private string _endpoint = "http://localhost:11434/api/generate";
        private string _apiKey = "";
        private string _model = "llama3";
        private int _timeoutSeconds = 30;
        private int _maxTokens = 150;
        private float _temperature = 0.7f;
        private int _numGpuLayers = -1;  // -1 = auto
        private int _numCtx = 4096;      // Context window size

        // Track ongoing requests to prevent spam
        private HashSet<int> _pendingRequests = new HashSet<int>();

        // Performance tracking
        private float _lastResponseTimeMs = 0;
        private float _avgResponseTimeMs = 0;
        private int _requestCount = 0;

        public void Initialize(LLMConfig config)
        {
            _endpoint = config.Endpoint;
            _apiKey = ResolveApiKey(config);
            _model = config.Model;
            _timeoutSeconds = config.TimeoutSeconds;
            _maxTokens = config.MaxTokens;
            _temperature = config.Temperature;
            _numGpuLayers = config.NumGPULayers;
            _numCtx = config.NumCtx;

            Log.Out($"[NPCLLMChat] LLMService initialized - Endpoint: {_endpoint}, Model: {_model}, " +
                    $"auth: {(string.IsNullOrEmpty(_apiKey) ? "none (local)" : "api key set")}");
            Log.Out($"[NPCLLMChat] GPU Layers: {(_numGpuLayers > 0 ? _numGpuLayers.ToString() : "auto")}, Context: {_numCtx}");
        }

        public float LastResponseTimeMs => _lastResponseTimeMs;
        public float AvgResponseTimeMs => _avgResponseTimeMs;
        public int RequestCount => _requestCount;

        /// <summary>
        /// Update the model name at runtime (e.g., from in-game settings UI)
        /// </summary>
        public string Endpoint => _endpoint;

        /// <summary>
        /// Switch provider at runtime. The request shape follows the URL, so pointing at an
        /// OpenAI-compatible host is all that is needed to move between local and hosted.
        /// </summary>
        public void SetEndpoint(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint)) return;
            _endpoint = endpoint;
            _apiKey = ResolveApiKey(new LLMConfig { ApiKey = _apiKey });
            Log.Out($"[NPCLLMChat] LLM endpoint changed to: {_endpoint}");
        }

        public void SetModel(string modelName)
        {
            if (!string.IsNullOrEmpty(modelName))
            {
                _model = modelName;
                Log.Out($"[NPCLLMChat] LLM model changed to: {_model}");
            }
        }

        /// <summary>
        /// Send a chat message to the LLM and get a response asynchronously.
        /// </summary>
        /// <param name="npcId">Unique NPC entity ID for tracking</param>
        /// <param name="systemPrompt">The NPC's personality/context</param>
        /// <param name="conversationHistory">Previous exchanges for context</param>
        /// <param name="playerMessage">The player's input message</param>
        /// <param name="onResponse">Callback with the LLM's response</param>
        /// <param name="onError">Callback if request fails</param>
        public void SendChatRequest(
            int npcId,
            string systemPrompt,
            List<ChatMessage> conversationHistory,
            string playerMessage,
            Action<string> onResponse,
            Action<string> onError)
        {
            // Prevent duplicate requests for same NPC
            if (_pendingRequests.Contains(npcId))
            {
                onError?.Invoke("Request already in progress for this NPC");
                return;
            }

            int chars = (systemPrompt?.Length ?? 0) + (playerMessage?.Length ?? 0);
            if (conversationHistory != null)
            {
                foreach (var message in conversationHistory) chars += message?.Content?.Length ?? 0;
            }
            WarnIfContextTight(chars, "chat");

            _pendingRequests.Add(npcId);
            StartCoroutine(SendRequestCoroutine(npcId, systemPrompt, conversationHistory, playerMessage, onResponse, onError));
        }

        /// <summary>
        /// Estimated tokens in a prompt of this many characters. Four characters to the token is
        /// the usual rule of thumb for English and is close enough to warn on - the point is to
        /// notice the ceiling coming, not to bill for it.
        /// </summary>
        public static int EstimateTokens(int chars)
        {
            return chars / 4;
        }

        // The companion's chat component used to appear only when she was first spoken to, taking
        // her travel journal, her cargo snapshots and her survives-logout flag with it. This is
        // the mod's one always-running MonoBehaviour, so it is the cheapest place to notice her.
        private float _nextCompanionScan;

        private void Update()
        {
            if (Time.unscaledTime < _nextCompanionScan) return;
            _nextCompanionScan = Time.unscaledTime + 5f;

            try
            {
                Harmony.NPCCorePatches.AttachToNearbyCompanion();
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Companion scan failed: {ex.Message}");
            }
        }

        public int ContextWindow => _numCtx;

        // Only complain when the band changes, or every reply would carry the same warning.
        private int _lastContextBand = -1;

        /// <summary>
        /// Say something before the window fills rather than after.
        ///
        /// Overrunning it does not fail loudly: the model silently loses whatever falls off, which
        /// showed up once already as her flatly denying an item that was listed near the end of her
        /// own prompt. Her notebook and the stored container contents both grow with play, so this
        /// gets tighter over a long save without anything appearing to change.
        /// </summary>
        private void WarnIfContextTight(int chars, string what)
        {
            if (_numCtx <= 0) return;

            int tokens = EstimateTokens(chars);
            int percent = tokens * 100 / _numCtx;
            int band = percent >= 95 ? 3 : percent >= 85 ? 2 : percent >= 70 ? 1 : 0;
            if (band == _lastContextBand) return;
            _lastContextBand = band;

            if (band == 0)
            {
                Log.Out($"[NPCLLMChat] Context back under 70% ({tokens} of {_numCtx} tokens)");
                return;
            }

            string advice = "Trim what she is told - the stored trader stock is the least useful of it - " +
                            "or raise NumCtx in llmconfig.xml.";
            string message = $"[NPCLLMChat] Context {percent}% full on the {what} prompt: about {tokens} tokens " +
                             $"of {_numCtx} ({chars} chars). ";

            if (band >= 2) Log.Warning(message + (band == 3
                ? "AT THE LIMIT - the oldest part of the prompt is being dropped, and she will " +
                  "start missing things she has been told. " + advice
                : advice));
            else Log.Out(message + advice);
        }

        private IEnumerator SendRequestCoroutine(
            int npcId,
            string systemPrompt,
            List<ChatMessage> conversationHistory,
            string playerMessage,
            Action<string> onResponse,
            Action<string> onError)
        {
            string requestBody;

            // Detect endpoint type and format request accordingly
            if (_endpoint.Contains("/api/generate"))
            {
                // Ollama format
                requestBody = BuildOllamaRequest(systemPrompt, conversationHistory, playerMessage);
            }
            else if (_endpoint.Contains("/v1/chat/completions"))
            {
                // OpenAI-compatible format (LM Studio, etc.)
                requestBody = BuildOpenAIRequest(systemPrompt, conversationHistory, playerMessage);
            }
            else
            {
                // Default to Ollama format
                requestBody = BuildOllamaRequest(systemPrompt, conversationHistory, playerMessage);
            }

            float startTime = Time.realtimeSinceStartup;

            using (UnityWebRequest request = new UnityWebRequest(_endpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                ApplyHeaders(request);
                request.timeout = _timeoutSeconds;

                Log.Out($"[NPCLLMChat] Sending request to LLM for NPC {npcId}");
                Log.Out($"[NPCLLMChat] Endpoint: {_endpoint}");
                Log.Out($"[NPCLLMChat] Model: {_model}");
                Log.Out($"[NPCLLMChat] Request body (first 200 chars): {requestBody.Substring(0, Math.Min(200, requestBody.Length))}");

                yield return request.SendWebRequest();

                _pendingRequests.Remove(npcId);

                // Track performance
                _lastResponseTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                _requestCount++;
                _avgResponseTimeMs = ((_avgResponseTimeMs * (_requestCount - 1)) + _lastResponseTimeMs) / _requestCount;

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string response = ParseResponse(request.downloadHandler.text);
                    if (!string.IsNullOrEmpty(response))
                    {
                        Log.Out($"[NPCLLMChat] Got response for NPC {npcId} in {_lastResponseTimeMs:F0}ms: {response.Substring(0, Math.Min(50, response.Length))}...");
                        onResponse?.Invoke(response);
                    }
                    else
                    {
                        onError?.Invoke("Empty response from LLM");
                    }
                }
                else
                {
                    string error = $"LLM request failed: {request.error}";
                    Log.Warning($"[NPCLLMChat] {error}");
                    Log.Warning($"[NPCLLMChat] Response code: {request.responseCode}");
                    Log.Warning($"[NPCLLMChat] Response text: {request.downloadHandler?.text}");
                    onError?.Invoke(error);
                }
            }
        }

        /// <summary>
        /// Send a bare completion request (no NPC conversation framing) - used for
        /// background tasks like memory summarization. Ollama endpoints only.
        /// </summary>
        /// <summary>
        /// A one-off completion. Pass a budget when the caller's output has a life of its own -
        /// see NPCChatComponent.SummaryTokenBudget - otherwise it tracks MaxTokens like a turn.
        /// </summary>
        public void SendCompletionRequest(string prompt, float temperature, Action<string> onResponse, Action<string> onError, int budget = 0)
        {
            // Carries the whole world context too, so it hits the ceiling at the same time
            WarnIfContextTight(prompt?.Length ?? 0, "completion");
            StartCoroutine(SendCompletionCoroutine(prompt, temperature, onResponse, onError, budget));
        }

        private IEnumerator SendCompletionCoroutine(string prompt, float temperature, Action<string> onResponse, Action<string> onError, int budget)
        {
            string temp = temperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // Floor of 512 because a hosted reasoning model spends the budget on hidden thinking
            // before it writes anything - the Ollama "think": false switch does not exist there.
            // (The Ollama chat path needs no such floor for exactly that reason.)
            int completionBudget = Math.Max(budget > 0 ? budget : _maxTokens, 512);
            string requestBody = _endpoint.Contains("/api/generate")
                ? $@"{{
                ""model"": ""{_model}"",
                ""prompt"": ""{EscapeJson(prompt)}"",
                ""stream"": false,
                ""think"": false,
                ""options"": {{ ""temperature"": {temp}, ""num_predict"": {completionBudget}, ""num_ctx"": {_numCtx} }}
            }}"
                // hosted providers speak chat/completions, so a one-off completion is just a
                // single user turn - summaries and combat shouts work there too
                : $@"{{
                ""model"": ""{_model}"",
                ""messages"": [{{ ""role"": ""user"", ""content"": ""{EscapeJson(prompt)}"" }}],
                ""temperature"": {temp},
                ""max_tokens"": {completionBudget},
                ""stream"": false
            }}";

            using (UnityWebRequest request = new UnityWebRequest(_endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestBody));
                request.downloadHandler = new DownloadHandlerBuffer();
                ApplyHeaders(request);
                request.timeout = 60; // background task, latency doesn't matter

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string response = ParseResponse(request.downloadHandler.text);
                    if (!string.IsNullOrEmpty(response))
                        onResponse?.Invoke(response);
                    else
                        onError?.Invoke("Empty completion response");
                }
                else
                {
                    onError?.Invoke($"Completion request failed: {request.error}");
                }
            }
        }

        private string BuildOllamaRequest(string systemPrompt, List<ChatMessage> history, string playerMessage)
        {
            // Build conversation context
            StringBuilder prompt = new StringBuilder();
            prompt.AppendLine($"System: {systemPrompt}");
            prompt.AppendLine();

            foreach (var msg in history)
            {
                prompt.AppendLine($"{msg.Role}: {msg.Content}");
            }
            prompt.AppendLine($"Player: {playerMessage}");
            prompt.AppendLine("NPC:");

            // Build options object with GPU optimization settings
            StringBuilder options = new StringBuilder();
            options.Append($"\"temperature\": {_temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            options.Append($", \"num_predict\": {_maxTokens}");
            options.Append($", \"num_ctx\": {_numCtx}");

            if (_numGpuLayers > 0)
            {
                options.Append($", \"num_gpu\": {_numGpuLayers}");
            }

            // "think": false — thinking models (qwen3 etc.) otherwise burn the whole
            // num_predict budget on reasoning and return an empty response.
            // Non-thinking models accept and ignore the flag.
            return $@"{{
                ""model"": ""{_model}"",
                ""prompt"": ""{EscapeJson(prompt.ToString())}"",
                ""stream"": false,
                ""think"": false,
                ""options"": {{ {options} }}
            }}";
        }

        private string BuildOpenAIRequest(string systemPrompt, List<ChatMessage> history, string playerMessage)
        {
            // Build messages array
            StringBuilder messages = new StringBuilder();
            messages.Append($@"{{""role"": ""system"", ""content"": ""{EscapeJson(systemPrompt)}""}}");

            foreach (var msg in history)
            {
                string role = msg.Role.ToLower() == "player" ? "user" : "assistant";
                messages.Append($@", {{""role"": ""{role}"", ""content"": ""{EscapeJson(msg.Content)}""}}");
            }
            messages.Append($@", {{""role"": ""user"", ""content"": ""{EscapeJson(playerMessage)}""}}");

            // OpenAI-compatible format. Reasoning models spend max_tokens on hidden thinking
            // before any reply, and the Ollama-only "think": false switch does not exist here,
            // so give the budget enough headroom that the visible answer is never squeezed out.
            int tokenBudget = Math.Max(_maxTokens, 512);
            return $@"{{
                ""model"": ""{_model}"",
                ""messages"": [{messages}],
                ""temperature"": {_temperature},
                ""max_tokens"": {tokenBudget}
            }}";
        }

        /// <summary>
        /// Where the API key comes from, in order: the NPCLLM_API_KEY environment variable,
        /// then a key file, then the config element. The key file exists because a desktop
        /// launched Steam does not inherit shell exports, and llmconfig.xml is version
        /// controlled - neither is a good home for a secret.
        /// </summary>
        private static string ResolveApiKey(LLMConfig config)
        {
            string key = System.Environment.GetEnvironmentVariable("NPCLLM_API_KEY");
            if (!string.IsNullOrEmpty(key)) return key.Trim();

            try
            {
                string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                string keyFile = System.IO.Path.Combine(home, ".config", "npcllm", "api_key");
                if (System.IO.File.Exists(keyFile))
                {
                    key = System.IO.File.ReadAllText(keyFile).Trim();
                    if (!string.IsNullOrEmpty(key))
                    {
                        Log.Out("[NPCLLMChat] API key loaded from ~/.config/npcllm/api_key");
                        return key;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Could not read API key file: {ex.Message}");
            }

            return config.ApiKey ?? "";
        }

        /// <summary>
        /// Content type plus, for hosted providers, the bearer token. Local Ollama and
        /// LM Studio ignore the header, so it is safe to send whenever a key is configured.
        /// </summary>
        private void ApplyHeaders(UnityWebRequest request)
        {
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("User-Agent", "NPCLLMChat/1.1 (7DaysToDie mod)");
            if (!string.IsNullOrEmpty(_apiKey))
            {
                request.SetRequestHeader("Authorization", "Bearer " + _apiKey);
            }
        }

        /// <summary>
        /// Some reasoning models emit their scratchpad inside the reply itself (Groq's qwen
        /// returns a full "&lt;think&gt;..." block as content). None of that is speech.
        /// </summary>
        private static string StripThinking(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string cleaned = System.Text.RegularExpressions.Regex.Replace(
                text, @"<think>[\s\S]*?</think>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // an unterminated block means the whole reply was thinking
            int open = cleaned.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            if (open >= 0) cleaned = cleaned.Substring(0, open);
            return cleaned.Trim();
        }

        private string ParseResponse(string jsonResponse)
        {
            try
            {
                // For Ollama: look for "response" field
                // Format: "response":"text" or "response": "text"
                if (jsonResponse.Contains("\"response\""))
                {
                    // Find the start of the response value
                    int keyIndex = jsonResponse.IndexOf("\"response\"");
                    int colonIndex = jsonResponse.IndexOf(':', keyIndex);
                    if (colonIndex < 0) return null;

                    // Skip whitespace after colon
                    int valueStart = colonIndex + 1;
                    while (valueStart < jsonResponse.Length && char.IsWhiteSpace(jsonResponse[valueStart]))
                        valueStart++;

                    // Check if it's a string value (starts with quote)
                    if (valueStart < jsonResponse.Length && jsonResponse[valueStart] == '"')
                    {
                        valueStart++; // Skip opening quote
                        int valueEnd = FindClosingQuote(jsonResponse, valueStart);
                        if (valueEnd > valueStart)
                        {
                            string response = jsonResponse.Substring(valueStart, valueEnd - valueStart);
                            response = UnescapeJson(response);
                            
                            // Clean up common LLM artifacts
                            response = CleanResponse(StripThinking(response));

                            return response;
                        }
                    }
                }

                // For OpenAI format: look for "content" in choices
                if (jsonResponse.Contains("\"content\""))
                {
                    int keyIndex = jsonResponse.IndexOf("\"content\"");
                    int colonIndex = jsonResponse.IndexOf(':', keyIndex);
                    if (colonIndex > 0)
                    {
                        int valueStart = colonIndex + 1;
                        while (valueStart < jsonResponse.Length && char.IsWhiteSpace(jsonResponse[valueStart]))
                            valueStart++;

                        if (valueStart < jsonResponse.Length && jsonResponse[valueStart] == '"')
                        {
                            valueStart++;
                            int valueEnd = FindClosingQuote(jsonResponse, valueStart);
                            if (valueEnd > valueStart)
                            {
                                return StripThinking(UnescapeJson(jsonResponse.Substring(valueStart, valueEnd - valueStart)));
                            }
                        }
                    }
                }

                Log.Warning($"Could not parse response: {jsonResponse.Substring(0, Math.Min(300, jsonResponse.Length))}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"Error parsing LLM response: {ex.Message}");
                return null;
            }
        }

        private int FindClosingQuote(string str, int start)
        {
            for (int i = start; i < str.Length; i++)
            {
                if (str[i] == '"' && (i == 0 || str[i - 1] != '\\'))
                    return i;
            }
            return -1;
        }

        private string EscapeJson(string str)
        {
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }

        private string UnescapeJson(string str)
        {
            // \uXXXX has to be handled: providers escape characters like < as \u003c, which
            // was reaching the player verbatim.
            string unicodeDecoded = System.Text.RegularExpressions.Regex.Replace(
                str, @"\\u([0-9a-fA-F]{4})",
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

            return unicodeDecoded.Replace("\\n", "\n")
                                 .Replace("\\r", "\r")
                                 .Replace("\\t", "\t")
                                 .Replace("\\\"", "\"")
                                 .Replace("\\\\", "\\");
        }

        private string CleanResponse(string response)
        {
            if (string.IsNullOrEmpty(response)) return response;

            // Remove common LLM formatting artifacts
            string cleaned = response.Trim();
            
            // If the response looks like JSON, try to extract dialogue from it
            if (cleaned.StartsWith("{") && cleaned.Contains("\""))
            {
                // Try to extract "dialogue" field
                int dialogueIndex = cleaned.IndexOf("\"dialogue\"", StringComparison.OrdinalIgnoreCase);
                if (dialogueIndex >= 0)
                {
                    int colonIndex = cleaned.IndexOf(':', dialogueIndex);
                    if (colonIndex > 0)
                    {
                        int quoteStart = cleaned.IndexOf('"', colonIndex);
                        if (quoteStart > 0)
                        {
                            int quoteEnd = quoteStart + 1;
                            while (quoteEnd < cleaned.Length)
                            {
                                if (cleaned[quoteEnd] == '"' && cleaned[quoteEnd - 1] != '\\')
                                    break;
                                quoteEnd++;
                            }
                            if (quoteEnd > quoteStart + 1)
                            {
                                cleaned = cleaned.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                                cleaned = UnescapeJson(cleaned);
                            }
                        }
                    }
                }
                // Try to extract "text" or "content" field as fallback
                else
                {
                    foreach (string field in new[] { "\"text\"", "\"content\"", "\"message\"" })
                    {
                        int fieldIndex = cleaned.IndexOf(field, StringComparison.OrdinalIgnoreCase);
                        if (fieldIndex >= 0)
                        {
                            int colonIndex = cleaned.IndexOf(':', fieldIndex);
                            if (colonIndex > 0)
                            {
                                int quoteStart = cleaned.IndexOf('"', colonIndex);
                                if (quoteStart > 0)
                                {
                                    int quoteEnd = quoteStart + 1;
                                    while (quoteEnd < cleaned.Length)
                                    {
                                        if (cleaned[quoteEnd] == '"' && cleaned[quoteEnd - 1] != '\\')
                                            break;
                                        quoteEnd++;
                                    }
                                    if (quoteEnd > quoteStart + 1)
                                    {
                                        cleaned = cleaned.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                                        cleaned = UnescapeJson(cleaned);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            // Remove "Response:" prefix (case insensitive)
            if (cleaned.StartsWith("Response:", System.StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(9).TrimStart();
            }
            
            // Remove "NPC:" prefix if present
            if (cleaned.StartsWith("NPC:", System.StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(4).TrimStart();
            }
            
            return cleaned;
        }

        public bool IsRequestPending(int npcId)
        {
            return _pendingRequests.Contains(npcId);
        }
    }

    /// <summary>
    /// Represents a single message in the conversation history
    /// </summary>
    public class ChatMessage
    {
        public string Role { get; set; }  // "Player" or "NPC"
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Configuration loaded from llmconfig.xml
    /// </summary>
    public class LLMConfig
    {
        // Server settings
        public string Endpoint { get; set; }
        public string Model { get; set; }
        // Bearer token for hosted providers. Prefer the NPCLLM_API_KEY environment
        // variable so a key never has to live in a file that gets shared or committed.
        public string ApiKey { get; set; } = "";
        public int TimeoutSeconds { get; set; }
        public int MaxTokens { get; set; }
        public float Temperature { get; set; }
        public int NumGPULayers { get; set; } = -1;  // -1 = auto
        public int NumCtx { get; set; } = 4096;      // Context window

        // Personality settings
        public string SystemPrompt { get; set; }
        public int ContextMemory { get; set; }

        // Response settings
        public bool ShowTypingIndicator { get; set; }
        public int TypingDelayMs { get; set; }

        // Action settings
        public bool ActionsEnabled { get; set; } = true;
        public float FollowDistance { get; set; } = 3.0f;
        public float GuardRadius { get; set; } = 10.0f;
    }
}
