using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace NPCLLMChat.STT
{
    /// <summary>
    /// Handles communication with the Whisper STT HTTP server.
    /// Converts speech audio to text via the faster-whisper backend.
    /// </summary>
    public class STTService : MonoBehaviour
    {
        private static STTService _instance;
        public static STTService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("STTService");
                    _instance = go.AddComponent<STTService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        // Configuration
        private STTConfig _config;
        private bool _isInitialized = false;
        private bool _serverAvailable = false;

        // Request tracking
        private Queue<STTRequest> _requestQueue = new Queue<STTRequest>();
        private bool _isProcessing = false;

        // Performance tracking
        private float _lastTranscriptionTimeMs = 0;
        private float _avgTranscriptionTimeMs = 0;
        private int _requestCount = 0;

        public bool IsInitialized => _isInitialized;
        public bool ServerAvailable => _serverAvailable;
        public float LastTranscriptionTimeMs => _lastTranscriptionTimeMs;
        public float AvgTranscriptionTimeMs => _avgTranscriptionTimeMs;
        public int RequestCount => _requestCount;
        public STTConfig Config => _config;

        /// <summary>
        /// Initialize the STT service with configuration
        /// </summary>
        public void Initialize(STTConfig config)
        {
            _config = config;
            _isInitialized = true;

            if (_config.Enabled)
            {
                Log.Out($"[NPCLLMChat] STTService initialized - Endpoint: {_config.Endpoint}");
                Log.Out($"[NPCLLMChat] Push-to-talk key: {_config.PushToTalkKey}");

                // Check if server is available
                StartCoroutine(CheckServerHealth());
            }
            else
            {
                Log.Out("[NPCLLMChat] STTService disabled in config");
            }
        }

        /// <summary>
        /// Check if the STT server is responding
        /// </summary>
        public IEnumerator CheckServerHealth()
        {
            string healthUrl = _config.Endpoint.Replace("/transcribe", "/health");

            using (UnityWebRequest request = UnityWebRequest.Get(healthUrl))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    _serverAvailable = true;
                    Log.Out("[NPCLLMChat] STT server is available");

                    // Parse health response to log model info
                    try
                    {
                        string json = request.downloadHandler.text;
                        if (json.Contains("model"))
                        {
                            Log.Out($"[NPCLLMChat] STT server health: {json}");
                        }
                    }
                    catch { }
                }
                else
                {
                    _serverAvailable = false;
                    Log.Warning($"[NPCLLMChat] STT server not available: {request.error}");
                    Log.Warning("[NPCLLMChat] Start whisper-server with: python whisper_server.py --port 5051");
                }
            }
        }

        /// <summary>
        /// Transcribe audio data to text
        /// </summary>
        /// <param name="wavData">WAV format audio data (16kHz, mono, 16-bit)</param>
        /// <param name="onSuccess">Callback with transcribed text</param>
        /// <param name="onError">Callback if transcription fails</param>
        public void Transcribe(byte[] wavData, Action<string> onSuccess, Action<string> onError)
        {
            if (!_isInitialized || !_config.Enabled)
            {
                Log.Warning("[NPCLLMChat] STT transcribe failed: not initialized or disabled");
                onError?.Invoke("STT not initialized or disabled");
                return;
            }

            if (!_serverAvailable)
            {
                Log.Warning("[NPCLLMChat] STT transcribe failed: server not available");
                Log.Warning("[NPCLLMChat] Start whisper server: python whisper_server.py --port 5051");
                // Try to check again in case server started
                StartCoroutine(CheckServerHealth());
                onError?.Invoke("STT server not available. Start whisper_server.py");
                return;
            }

            if (wavData == null || wavData.Length < 44)
            {
                onError?.Invoke("Invalid or empty audio data");
                return;
            }

            // Queue the request
            var request = new STTRequest
            {
                WavData = wavData,
                OnSuccess = onSuccess,
                OnError = onError
            };

            _requestQueue.Enqueue(request);

            // Process queue if not already processing
            if (!_isProcessing)
            {
                StartCoroutine(ProcessQueue());
            }
        }

        /// <summary>
        /// Process queued STT requests one at a time
        /// </summary>
        private IEnumerator ProcessQueue()
        {
            _isProcessing = true;

            while (_requestQueue.Count > 0)
            {
                var request = _requestQueue.Dequeue();
                yield return StartCoroutine(TranscribeCoroutine(request));
            }

            _isProcessing = false;
        }

        /// <summary>
        /// Perform actual HTTP request to STT server
        /// </summary>
        private IEnumerator TranscribeCoroutine(STTRequest request)
        {
            float startTime = Time.realtimeSinceStartup;

            using (UnityWebRequest webRequest = new UnityWebRequest(_config.Endpoint, "POST"))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(request.WavData);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "audio/wav");
                webRequest.timeout = _config.TimeoutSeconds;

                yield return webRequest.SendWebRequest();

                // Track timing
                _lastTranscriptionTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                _requestCount++;
                _avgTranscriptionTimeMs = ((_avgTranscriptionTimeMs * (_requestCount - 1)) + _lastTranscriptionTimeMs) / _requestCount;

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string responseText = webRequest.downloadHandler.text;
                    Log.Out($"[NPCLLMChat] STT server response: {responseText}");

                    // Parse JSON response to get text
                    string transcribedText = ParseTranscriptionResponse(responseText);

                    if (!string.IsNullOrEmpty(transcribedText))
                    {
                        Log.Out($"[NPCLLMChat] STT transcription completed in {_lastTranscriptionTimeMs:F0}ms: \"{transcribedText}\"");
                        request.OnSuccess?.Invoke(transcribedText);
                    }
                    else
                    {
                        Log.Warning("[NPCLLMChat] STT returned empty transcription");
                        request.OnError?.Invoke("No speech detected");
                    }
                }
                else
                {
                    // Don't mark server as unavailable on single failed request
                    // Only connection errors should trigger this
                    if (webRequest.result == UnityWebRequest.Result.ConnectionError)
                    {
                        _serverAvailable = false;
                    }

                    Log.Warning($"[NPCLLMChat] STT request failed: {webRequest.error}");
                    Log.Warning($"[NPCLLMChat] Request result: {webRequest.result}");
                    Log.Warning($"[NPCLLMChat] Response code: {webRequest.responseCode}");

                    // Try to parse error message from response
                    string errorMsg = webRequest.error;
                    try
                    {
                        string responseText = webRequest.downloadHandler?.text;
                        if (!string.IsNullOrEmpty(responseText))
                        {
                            Log.Warning($"[NPCLLMChat] Server response: {responseText}");
                            if (responseText.Contains("error"))
                            {
                                errorMsg = ParseErrorResponse(responseText);
                            }
                        }
                    }
                    catch { }

                    request.OnError?.Invoke($"STT request failed: {errorMsg}");
                }
            }
        }

        /// <summary>
        /// Parse JSON response to extract transcribed text
        /// </summary>
        private string ParseTranscriptionResponse(string json)
        {
            // Simple JSON parsing for {"text": "..."}
            try
            {
                int textStart = json.IndexOf("\"text\"");
                if (textStart < 0) return null;

                int colonPos = json.IndexOf(':', textStart);
                if (colonPos < 0) return null;

                int valueStart = json.IndexOf('"', colonPos + 1);
                if (valueStart < 0) return null;

                int valueEnd = valueStart + 1;
                while (valueEnd < json.Length)
                {
                    if (json[valueEnd] == '"' && json[valueEnd - 1] != '\\')
                        break;
                    valueEnd++;
                }

                if (valueEnd >= json.Length) return null;

                string text = json.Substring(valueStart + 1, valueEnd - valueStart - 1);

                // Unescape JSON string
                text = text.Replace("\\\"", "\"")
                           .Replace("\\\\", "\\")
                           .Replace("\\n", "\n")
                           .Replace("\\r", "\r")
                           .Replace("\\t", "\t");

                return text.Trim();
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Failed to parse STT response: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse error response JSON
        /// </summary>
        private string ParseErrorResponse(string json)
        {
            try
            {
                int errorStart = json.IndexOf("\"error\"");
                if (errorStart < 0) return json;

                int colonPos = json.IndexOf(':', errorStart);
                if (colonPos < 0) return json;

                int valueStart = json.IndexOf('"', colonPos + 1);
                if (valueStart < 0) return json;

                int valueEnd = valueStart + 1;
                while (valueEnd < json.Length)
                {
                    if (json[valueEnd] == '"' && json[valueEnd - 1] != '\\')
                        break;
                    valueEnd++;
                }

                return json.Substring(valueStart + 1, valueEnd - valueStart - 1);
            }
            catch
            {
                return json;
            }
        }

        /// <summary>
        /// Refresh server availability check
        /// </summary>
        public void RefreshServerStatus()
        {
            StartCoroutine(CheckServerHealth());
        }
    }

    /// <summary>
    /// Internal class to track pending STT requests
    /// </summary>
    internal class STTRequest
    {
        public byte[] WavData { get; set; }
        public Action<string> OnSuccess { get; set; }
        public Action<string> OnError { get; set; }
    }
}
