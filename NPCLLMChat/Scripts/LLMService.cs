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
            _model = config.Model;
            _timeoutSeconds = config.TimeoutSeconds;
            _maxTokens = config.MaxTokens;
            _temperature = config.Temperature;
            _numGpuLayers = config.NumGPULayers;
            _numCtx = config.NumCtx;

            Log.Out($"[NPCLLMChat] LLMService initialized - Endpoint: {_endpoint}, Model: {_model}");
            Log.Out($"[NPCLLMChat] GPU Layers: {(_numGpuLayers > 0 ? _numGpuLayers.ToString() : "auto")}, Context: {_numCtx}");
        }

        public float LastResponseTimeMs => _lastResponseTimeMs;
        public float AvgResponseTimeMs => _avgResponseTimeMs;
        public int RequestCount => _requestCount;

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

            _pendingRequests.Add(npcId);
            StartCoroutine(SendRequestCoroutine(npcId, systemPrompt, conversationHistory, playerMessage, onResponse, onError));
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
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = _timeoutSeconds;

                Log.Out($"[NPCLLMChat] Sending request to LLM for NPC {npcId}");

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
                    onError?.Invoke(error);
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

            // Ollama /api/generate format with GPU optimizations
            return $@"{{
                ""model"": ""{_model}"",
                ""prompt"": ""{EscapeJson(prompt.ToString())}"",
                ""stream"": false,
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

            // OpenAI-compatible format
            return $@"{{
                ""model"": ""{_model}"",
                ""messages"": [{messages}],
                ""temperature"": {_temperature},
                ""max_tokens"": {_maxTokens}
            }}";
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
                            return UnescapeJson(response);
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
                                return UnescapeJson(jsonResponse.Substring(valueStart, valueEnd - valueStart));
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
            return str.Replace("\\n", "\n")
                      .Replace("\\r", "\r")
                      .Replace("\\t", "\t")
                      .Replace("\\\"", "\"")
                      .Replace("\\\\", "\\");
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
        public int MaxResponseLength { get; set; }

        // Action settings
        public bool ActionsEnabled { get; set; } = true;
        public float FollowDistance { get; set; } = 3.0f;
        public float GuardRadius { get; set; } = 10.0f;
    }
}
