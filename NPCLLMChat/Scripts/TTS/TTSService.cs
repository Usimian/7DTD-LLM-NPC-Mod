using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace NPCLLMChat.TTS
{
    /// <summary>
    /// Text-to-speech service with cross-platform support.
    /// - Windows: Uses Windows SAPI (System.Speech.Synthesis) - no server needed
    /// - Linux: Uses Piper HTTP server
    /// </summary>
    public class TTSService : MonoBehaviour
    {
        private static TTSService _instance;
        public static TTSService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("TTSService");
                    _instance = go.AddComponent<TTSService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        // Configuration
        private TTSConfig _config;
        private bool _isInitialized = false;
        private TTSProvider _activeProvider = TTSProvider.Auto;
        private bool _piperServerAvailable = false;

        // Request tracking (for Piper)
        private Queue<TTSRequest> _requestQueue = new Queue<TTSRequest>();
        private bool _isProcessing = false;

        // Performance tracking
        private float _lastSynthesisTimeMs = 0;
        private float _avgSynthesisTimeMs = 0;
        private int _requestCount = 0;

        public bool IsInitialized => _isInitialized;
        public bool ServerAvailable => _activeProvider == TTSProvider.Windows ? 
            WindowsTTSProvider.Instance.IsAvailable : _piperServerAvailable;
        public TTSProvider ActiveProvider => _activeProvider;
        public float LastSynthesisTimeMs => _lastSynthesisTimeMs;
        public float AvgSynthesisTimeMs => _avgSynthesisTimeMs;
        public int RequestCount => _requestCount;
        public TTSConfig Config => _config;

        /// <summary>
        /// Initialize the TTS service with configuration
        /// </summary>
        public void Initialize(TTSConfig config)
        {
            _config = config;
            _isInitialized = true;

            if (!_config.Enabled)
            {
                Log.Out("[NPCLLMChat] TTSService disabled in config");
                return;
            }

            Log.Out($"[NPCLLMChat] TTSService initializing on {PlatformHelper.PlatformName}");

            // Determine provider based on config and platform
            DetermineProvider();
        }

        private void DetermineProvider()
        {
            switch (_config.Provider)
            {
                case TTSProvider.Windows:
                    _activeProvider = TTSProvider.Windows;
                    InitializeWindows();
                    break;

                case TTSProvider.Piper:
                    _activeProvider = TTSProvider.Piper;
                    StartCoroutine(InitializePiper());
                    break;

                case TTSProvider.Auto:
                default:
                    if (PlatformHelper.IsWindows)
                    {
                        _activeProvider = TTSProvider.Windows;
                        InitializeWindows();
                    }
                    else
                    {
                        _activeProvider = TTSProvider.Piper;
                        StartCoroutine(InitializePiper());
                    }
                    break;
            }
        }

        private void InitializeWindows()
        {
            if (WindowsTTSProvider.Instance.IsAvailable)
            {
                Log.Out($"[NPCLLMChat] TTS using Windows SAPI");
                Log.Out($"[NPCLLMChat] {WindowsTTSProvider.Instance.GetVoiceInfo()}");
            }
            else
            {
                Log.Warning("[NPCLLMChat] Windows SAPI not available!");
            }
        }

        private IEnumerator InitializePiper()
        {
            Log.Out($"[NPCLLMChat] TTS checking Piper server at {_config.Endpoint}");
            
            string healthUrl = _config.Endpoint.Replace("/synthesize", "/health");

            using (UnityWebRequest request = UnityWebRequest.Get(healthUrl))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    _piperServerAvailable = true;
                    Log.Out("[NPCLLMChat] TTS using Piper server");
                }
                else
                {
                    _piperServerAvailable = false;
                    Log.Warning($"[NPCLLMChat] Piper TTS server not available: {request.error}");
                    Log.Warning("[NPCLLMChat] Start with: python piper_server.py --port 5050");
                }
            }
        }

        /// <summary>
        /// Synthesize text to speech and return an AudioClip
        /// </summary>
        public void Synthesize(string text, string voice, Action<AudioClip> onSuccess, Action<string> onError)
        {
            if (!_isInitialized || !_config.Enabled)
            {
                onError?.Invoke("TTS not initialized or disabled");
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                onError?.Invoke("Empty text");
                return;
            }

            string selectedVoice = string.IsNullOrEmpty(voice) ? _config.DefaultVoice : voice;

            if (_activeProvider == TTSProvider.Windows)
            {
                SynthesizeWithWindows(text, selectedVoice, onSuccess, onError);
            }
            else
            {
                SynthesizeWithPiper(text, selectedVoice, onSuccess, onError);
            }
        }

        /// <summary>
        /// Synthesize with default voice
        /// </summary>
        public void Synthesize(string text, Action<AudioClip> onSuccess, Action<string> onError)
        {
            Synthesize(text, null, onSuccess, onError);
        }

        /// <summary>
        /// Get voice ID for NPC type
        /// </summary>
        public string GetVoiceForNPCType(string npcType)
        {
            if (_config == null) return "en_US-lessac-medium";

            switch (npcType?.ToLower())
            {
                case "trader":
                    return _config.TraderVoice;
                case "companion":
                    return _config.CompanionVoice;
                case "bandit":
                    return _config.BanditVoice;
                default:
                    return _config.DefaultVoice;
            }
        }

        #region Windows SAPI

        private void SynthesizeWithWindows(string text, string voice, Action<AudioClip> onSuccess, Action<string> onError)
        {
            if (!WindowsTTSProvider.Instance.IsAvailable)
            {
                onError?.Invoke("Windows TTS not available");
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            WindowsTTSProvider.Instance.Synthesize(
                text,
                voice,
                _config.SpeechRate,
                wavData =>
                {
                    StartCoroutine(ProcessWavResult(wavData, text, startTime, onSuccess, onError));
                },
                error => onError?.Invoke(error)
            );
        }

        #endregion

        #region Piper Server

        private void SynthesizeWithPiper(string text, string voice, Action<AudioClip> onSuccess, Action<string> onError)
        {
            if (!_piperServerAvailable)
            {
                onError?.Invoke("Piper TTS server not available");
                return;
            }

            var request = new TTSRequest
            {
                Text = text,
                Voice = voice,
                OnSuccess = onSuccess,
                OnError = onError
            };

            _requestQueue.Enqueue(request);

            if (!_isProcessing)
            {
                StartCoroutine(ProcessPiperQueue());
            }
        }

        private IEnumerator ProcessPiperQueue()
        {
            _isProcessing = true;

            while (_requestQueue.Count > 0)
            {
                var request = _requestQueue.Dequeue();
                yield return StartCoroutine(PiperSynthesizeCoroutine(request));
            }

            _isProcessing = false;
        }

        private IEnumerator PiperSynthesizeCoroutine(TTSRequest request)
        {
            float startTime = Time.realtimeSinceStartup;

            string jsonBody = BuildPiperRequestJson(request.Text, request.Voice);

            using (UnityWebRequest webRequest = new UnityWebRequest(_config.Endpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = _config.TimeoutSeconds;

                yield return webRequest.SendWebRequest();

                _lastSynthesisTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                _requestCount++;
                _avgSynthesisTimeMs = ((_avgSynthesisTimeMs * (_requestCount - 1)) + _lastSynthesisTimeMs) / _requestCount;

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string contentType = webRequest.GetResponseHeader("Content-Type");
                    if (contentType != null && contentType.Contains("audio"))
                    {
                        byte[] wavData = webRequest.downloadHandler.data;
                        AudioClip clip = WavToAudioClip(wavData, request.Text);

                        if (clip != null)
                        {
                            Log.Out($"[NPCLLMChat] Piper TTS completed in {_lastSynthesisTimeMs:F0}ms ({clip.length:F1}s)");
                            request.OnSuccess?.Invoke(clip);
                        }
                        else
                        {
                            request.OnError?.Invoke("Failed to parse WAV data");
                        }
                    }
                    else
                    {
                        request.OnError?.Invoke($"Piper error: {webRequest.downloadHandler.text}");
                    }
                }
                else
                {
                    _piperServerAvailable = false;
                    request.OnError?.Invoke($"Piper request failed: {webRequest.error}");
                }
            }
        }

        private string BuildPiperRequestJson(string text, string voice)
        {
            string escapedText = text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"text\": \"{escapedText}\"");
            sb.Append($", \"voice\": \"{voice}\"");

            if (Math.Abs(_config.SpeechRate - 1.0f) > 0.01f)
            {
                float lengthScale = 1.0f / _config.SpeechRate;
                sb.Append($", \"length_scale\": {lengthScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            sb.Append("}");
            return sb.ToString();
        }

        #endregion

        #region WAV Processing

        private IEnumerator ProcessWavResult(byte[] wavData, string text, float startTime, Action<AudioClip> onSuccess, Action<string> onError)
        {
            yield return null;

            if (wavData == null || wavData.Length < 44)
            {
                onError?.Invoke("TTS returned invalid data");
                yield break;
            }

            AudioClip clip = WavToAudioClip(wavData, text);

            _lastSynthesisTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            _requestCount++;
            _avgSynthesisTimeMs = ((_avgSynthesisTimeMs * (_requestCount - 1)) + _lastSynthesisTimeMs) / _requestCount;

            if (clip != null)
            {
                Log.Out($"[NPCLLMChat] TTS completed in {_lastSynthesisTimeMs:F0}ms ({clip.length:F1}s)");
                onSuccess?.Invoke(clip);
            }
            else
            {
                onError?.Invoke("Failed to parse TTS audio");
            }
        }

        private AudioClip WavToAudioClip(byte[] wavData, string clipName)
        {
            try
            {
                if (wavData.Length < 44) return null;
                if (wavData[0] != 'R' || wavData[1] != 'I' || wavData[2] != 'F' || wavData[3] != 'F') return null;
                if (wavData[8] != 'W' || wavData[9] != 'A' || wavData[10] != 'V' || wavData[11] != 'E') return null;

                int fmtOffset = 12;
                while (fmtOffset < wavData.Length - 8)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(wavData, fmtOffset, 4);
                    if (chunkId == "fmt ") break;
                    fmtOffset += 8 + BitConverter.ToInt32(wavData, fmtOffset + 4);
                }
                if (fmtOffset >= wavData.Length - 8) return null;

                int channels = BitConverter.ToInt16(wavData, fmtOffset + 10);
                int sampleRate = BitConverter.ToInt32(wavData, fmtOffset + 12);
                int bitsPerSample = BitConverter.ToInt16(wavData, fmtOffset + 22);

                int dataOffset = fmtOffset + 8 + BitConverter.ToInt32(wavData, fmtOffset + 4);
                while (dataOffset < wavData.Length - 8)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(wavData, dataOffset, 4);
                    if (chunkId == "data") break;
                    dataOffset += 8 + BitConverter.ToInt32(wavData, dataOffset + 4);
                }
                if (dataOffset >= wavData.Length - 8) return null;

                int dataSize = BitConverter.ToInt32(wavData, dataOffset + 4);
                int dataStart = dataOffset + 8;
                int sampleCount = dataSize / (bitsPerSample / 8) / channels;

                AudioClip clip = AudioClip.Create(
                    "TTS_" + clipName.Substring(0, Math.Min(20, clipName.Length)),
                    sampleCount, channels, sampleRate, false);

                float[] samples = new float[sampleCount * channels];
                if (bitsPerSample == 16)
                {
                    for (int i = 0; i < sampleCount * channels; i++)
                    {
                        int idx = dataStart + i * 2;
                        if (idx + 1 < wavData.Length)
                            samples[i] = BitConverter.ToInt16(wavData, idx) / 32768f;
                    }
                }
                else if (bitsPerSample == 8)
                {
                    for (int i = 0; i < sampleCount * channels; i++)
                    {
                        int idx = dataStart + i;
                        if (idx < wavData.Length)
                            samples[i] = (wavData[idx] - 128) / 128f;
                    }
                }

                clip.SetData(samples, 0);
                return clip;
            }
            catch (Exception ex)
            {
                Log.Error($"[NPCLLMChat] WAV parse error: {ex.Message}");
                return null;
            }
        }

        #endregion

        public void RefreshServerStatus()
        {
            if (_activeProvider == TTSProvider.Piper)
                StartCoroutine(InitializePiper());
        }

        public string GetStatusString()
        {
            if (!_isInitialized || !_config.Enabled) return "Disabled";

            switch (_activeProvider)
            {
                case TTSProvider.Windows:
                    return $"Windows SAPI ({WindowsTTSProvider.Instance.AvailableVoices?.Length ?? 0} voices)";
                case TTSProvider.Piper:
                    return _piperServerAvailable ? $"Piper Server ({_config.Endpoint})" : "Piper (not connected)";
                default:
                    return "Unknown";
            }
        }
    }

    internal class TTSRequest
    {
        public string Text { get; set; }
        public string Voice { get; set; }
        public Action<AudioClip> OnSuccess { get; set; }
        public Action<string> OnError { get; set; }
    }
}
