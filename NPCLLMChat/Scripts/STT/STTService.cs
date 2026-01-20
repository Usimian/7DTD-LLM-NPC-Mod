using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace NPCLLMChat.STT
{
    /// <summary>
    /// Speech-to-text service with cross-platform support.
    /// - Windows: Uses Windows Speech Recognition (System.Speech.Recognition) - no server needed
    /// - Linux: Uses Whisper HTTP server
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
        private STTProvider _activeProvider = STTProvider.Auto;
        private bool _whisperServerAvailable = false;

        // Request tracking (for Whisper)
        private Queue<STTRequest> _requestQueue = new Queue<STTRequest>();
        private bool _isProcessing = false;

        // Performance tracking
        private float _lastTranscriptionTimeMs = 0;
        private float _avgTranscriptionTimeMs = 0;
        private int _requestCount = 0;

        public bool IsInitialized => _isInitialized;
        public bool ServerAvailable => _activeProvider == STTProvider.Windows ?
            WindowsSTTProvider.Instance.IsAvailable : _whisperServerAvailable;
        public STTProvider ActiveProvider => _activeProvider;
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

            if (!_config.Enabled)
            {
                Log.Out("[NPCLLMChat] STTService disabled in config");
                return;
            }

            Log.Out($"[NPCLLMChat] STTService initializing on {PlatformHelper.PlatformName}");
            Log.Out($"[NPCLLMChat] Push-to-talk key: {_config.PushToTalkKey}");

            // Determine provider based on config and platform
            DetermineProvider();
        }

        private void DetermineProvider()
        {
            switch (_config.Provider)
            {
                case STTProvider.Windows:
                    _activeProvider = STTProvider.Windows;
                    InitializeWindows();
                    break;

                case STTProvider.Whisper:
                    _activeProvider = STTProvider.Whisper;
                    StartCoroutine(InitializeWhisper());
                    break;

                case STTProvider.Auto:
                default:
                    if (PlatformHelper.IsWindows)
                    {
                        _activeProvider = STTProvider.Windows;
                        InitializeWindows();
                    }
                    else
                    {
                        _activeProvider = STTProvider.Whisper;
                        StartCoroutine(InitializeWhisper());
                    }
                    break;
            }
        }

        private void InitializeWindows()
        {
            if (WindowsSTTProvider.Instance.IsAvailable)
            {
                Log.Out("[NPCLLMChat] STT using Windows Speech Recognition");
                Log.Out($"[NPCLLMChat] {WindowsSTTProvider.Instance.GetProviderInfo()}");
            }
            else
            {
                Log.Warning("[NPCLLMChat] Windows Speech Recognition not available!");
                Log.Warning("[NPCLLMChat] Enable in Settings > Time & Language > Speech");
            }
        }

        private IEnumerator InitializeWhisper()
        {
            Log.Out($"[NPCLLMChat] STT checking Whisper server at {_config.Endpoint}");

            string healthUrl = _config.Endpoint.Replace("/transcribe", "/health");

            using (UnityWebRequest request = UnityWebRequest.Get(healthUrl))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    _whisperServerAvailable = true;
                    Log.Out("[NPCLLMChat] STT using Whisper server");

                    try
                    {
                        string json = request.downloadHandler.text;
                        if (json.Contains("model"))
                            Log.Out($"[NPCLLMChat] Whisper health: {json}");
                    }
                    catch { }
                }
                else
                {
                    _whisperServerAvailable = false;
                    Log.Warning($"[NPCLLMChat] Whisper STT server not available: {request.error}");
                    Log.Warning("[NPCLLMChat] Start with: python whisper_server.py --port 5051");
                }
            }
        }

        /// <summary>
        /// Legacy health check for compatibility
        /// </summary>
        public IEnumerator CheckServerHealth()
        {
            yield return InitializeWhisper();
        }

        /// <summary>
        /// Transcribe audio data to text
        /// </summary>
        public void Transcribe(byte[] wavData, Action<string> onSuccess, Action<string> onError)
        {
            if (!_isInitialized || !_config.Enabled)
            {
                onError?.Invoke("STT not initialized or disabled");
                return;
            }

            if (wavData == null || wavData.Length < 44)
            {
                onError?.Invoke("Invalid or empty audio data");
                return;
            }

            if (_activeProvider == STTProvider.Windows)
            {
                TranscribeWithWindows(wavData, onSuccess, onError);
            }
            else
            {
                TranscribeWithWhisper(wavData, onSuccess, onError);
            }
        }

        #region Windows Speech Recognition

        private void TranscribeWithWindows(byte[] wavData, Action<string> onSuccess, Action<string> onError)
        {
            if (!WindowsSTTProvider.Instance.IsAvailable)
            {
                onError?.Invoke("Windows Speech Recognition not available");
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            WindowsSTTProvider.Instance.Transcribe(
                wavData,
                text =>
                {
                    _lastTranscriptionTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                    _requestCount++;
                    _avgTranscriptionTimeMs = ((_avgTranscriptionTimeMs * (_requestCount - 1)) + _lastTranscriptionTimeMs) / _requestCount;

                    Log.Out($"[NPCLLMChat] Windows STT completed in {_lastTranscriptionTimeMs:F0}ms: \"{text}\"");
                    onSuccess?.Invoke(text);
                },
                error => onError?.Invoke(error)
            );
        }

        #endregion

        #region Whisper Server

        private void TranscribeWithWhisper(byte[] wavData, Action<string> onSuccess, Action<string> onError)
        {
            if (!_whisperServerAvailable)
            {
                onError?.Invoke("Whisper STT server not available");
                return;
            }

            var request = new STTRequest
            {
                WavData = wavData,
                OnSuccess = onSuccess,
                OnError = onError
            };

            _requestQueue.Enqueue(request);

            if (!_isProcessing)
            {
                StartCoroutine(ProcessWhisperQueue());
            }
        }

        private IEnumerator ProcessWhisperQueue()
        {
            _isProcessing = true;

            while (_requestQueue.Count > 0)
            {
                var request = _requestQueue.Dequeue();
                yield return StartCoroutine(WhisperTranscribeCoroutine(request));
            }

            _isProcessing = false;
        }

        private IEnumerator WhisperTranscribeCoroutine(STTRequest request)
        {
            float startTime = Time.realtimeSinceStartup;

            using (UnityWebRequest webRequest = new UnityWebRequest(_config.Endpoint, "POST"))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(request.WavData);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "audio/wav");
                webRequest.timeout = _config.TimeoutSeconds;

                yield return webRequest.SendWebRequest();

                _lastTranscriptionTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                _requestCount++;
                _avgTranscriptionTimeMs = ((_avgTranscriptionTimeMs * (_requestCount - 1)) + _lastTranscriptionTimeMs) / _requestCount;

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string responseText = webRequest.downloadHandler.text;
                    string transcribedText = ParseWhisperResponse(responseText);

                    if (!string.IsNullOrEmpty(transcribedText))
                    {
                        Log.Out($"[NPCLLMChat] Whisper STT completed in {_lastTranscriptionTimeMs:F0}ms: \"{transcribedText}\"");
                        request.OnSuccess?.Invoke(transcribedText);
                    }
                    else
                    {
                        request.OnError?.Invoke("No speech detected");
                    }
                }
                else
                {
                    if (webRequest.result == UnityWebRequest.Result.ConnectionError)
                        _whisperServerAvailable = false;

                    request.OnError?.Invoke($"Whisper request failed: {webRequest.error}");
                }
            }
        }

        private string ParseWhisperResponse(string json)
        {
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
                return text.Replace("\\\"", "\"")
                           .Replace("\\\\", "\\")
                           .Replace("\\n", "\n")
                           .Replace("\\r", "\r")
                           .Replace("\\t", "\t")
                           .Trim();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        public void RefreshServerStatus()
        {
            if (_activeProvider == STTProvider.Whisper)
                StartCoroutine(InitializeWhisper());
        }

        public string GetStatusString()
        {
            if (!_isInitialized || !_config.Enabled) return "Disabled";

            switch (_activeProvider)
            {
                case STTProvider.Windows:
                    return WindowsSTTProvider.Instance.IsAvailable ?
                        "Windows Speech Recognition" : "Windows (not available)";
                case STTProvider.Whisper:
                    return _whisperServerAvailable ?
                        $"Whisper Server ({_config.Model})" : "Whisper (not connected)";
                default:
                    return "Unknown";
            }
        }
    }

    internal class STTRequest
    {
        public byte[] WavData { get; set; }
        public Action<string> OnSuccess { get; set; }
        public Action<string> OnError { get; set; }
    }
}
