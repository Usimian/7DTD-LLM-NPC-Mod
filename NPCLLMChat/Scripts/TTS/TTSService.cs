using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace NPCLLMChat.TTS
{
    /// <summary>
    /// Text-to-speech service. Windows and Linux alike talk to the Piper HTTP server; System.
    /// Speech is not in the game's Mono runtime, so there is no in-process voice on any platform.
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
        public bool ServerAvailable => _piperServerAvailable;
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
                Log.Out("TTSService disabled in config");
                return;
            }

            Log.Out($"TTSService initializing on {PlatformHelper.PlatformName}");

            // Determine provider based on config and platform
            DetermineProvider();
        }

        private void DetermineProvider()
        {
            // Note: Windows SAPI (System.Speech) is not available in Unity's Mono runtime
            // All platforms use Piper TTS server
            _activeProvider = TTSProvider.Piper;
            StartCoroutine(InitializePiper());
        }

        private IEnumerator InitializePiper()
        {
            Log.Out($"TTS checking Piper server at {_config.Endpoint}");
            
            string healthUrl = _config.Endpoint.Replace("/synthesize", "/health");

            using (UnityWebRequest request = UnityWebRequest.Get(healthUrl))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    _piperServerAvailable = true;
                    Log.Out("TTS using Piper server");
                }
                else
                {
                    _piperServerAvailable = false;
                    Log.Warning($"Piper TTS server not available: {request.error}");
                    Log.Warning("Start with: python piper_server.py --port 5050");
                }
            }
        }

        /// <summary>
        /// Synthesize text to speech and return an AudioClip
        /// </summary>
        /// <param name="rateScale">
        /// Per-line speed, on top of the configured SpeechRate. Below 1 for the lines she says
        /// quietly - a whisper is slower as well as softer - and above 1 for a shouted warning.
        /// </param>
        public void Synthesize(string text, string voice, Action<AudioClip> onSuccess, Action<string> onError,
                               float rateScale = 1f)
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

            // All platforms use Piper TTS server
            SynthesizeWithPiper(ApplyPronunciations(text), selectedVoice, onSuccess, onError, rateScale);
        }

        /// <summary>
        /// Respell the handful of words Piper gets wrong, for the synthesizer only - the line on
        /// screen keeps the real spelling. Piper derives pronunciation from spelling, so place
        /// names are where it loses: "Tucson" phonemizes to TUCK-sun, and "Tooson" to TOO-sun.
        /// Whole words only, so "Tucsonan" is left alone.
        /// </summary>
        private string ApplyPronunciations(string text)
        {
            if (_config.Pronunciations.Count == 0 || string.IsNullOrEmpty(text)) return text;

            foreach (var pair in _config.Pronunciations)
            {
                text = System.Text.RegularExpressions.Regex.Replace(
                    text,
                    $@"\b{System.Text.RegularExpressions.Regex.Escape(pair.Key)}\b",
                    pair.Value,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            return text;
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

        #region Piper Server

        private void SynthesizeWithPiper(string text, string voice, Action<AudioClip> onSuccess, Action<string> onError,
                                         float rateScale)
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
                RateScale = rateScale,
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

            string jsonBody = BuildPiperRequestJson(request.Text, request.Voice, request.RateScale);

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
                            Log.Out($"Piper TTS completed in {_lastSynthesisTimeMs:F0}ms ({clip.length:F1}s)");
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

        private string BuildPiperRequestJson(string text, string voice, float rateScale)
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

            // Piper measures the opposite of speed: length_scale stretches each phoneme, so
            // faster speech is a smaller number. The per-line scale rides on top of the config.
            float rate = _config.SpeechRate * (rateScale > 0.01f ? rateScale : 1f);
            if (Math.Abs(rate - 1.0f) > 0.01f)
            {
                float lengthScale = 1.0f / rate;
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
                Log.Out($"TTS completed in {_lastSynthesisTimeMs:F0}ms ({clip.length:F1}s)");
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
                Log.Error($"WAV parse error: {ex.Message}");
                return null;
            }
        }

        #endregion

        public void RefreshServerStatus()
        {
            if (_activeProvider == TTSProvider.Piper)
                StartCoroutine(InitializePiper());
        }

        /// <summary>
        /// Ask the Piper server which voices are installed. Calls back with null on failure.
        /// </summary>
        public void FetchAvailableVoices(Action<List<string>> onResult)
        {
            StartCoroutine(FetchVoicesCoroutine(onResult));
        }

        private IEnumerator FetchVoicesCoroutine(Action<List<string>> onResult)
        {
            string url = _config.Endpoint.Replace("/synthesize", "/voices");
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onResult?.Invoke(null);
                    yield break;
                }

                List<string> ids = null;
                try
                {
                    var parsed = Newtonsoft.Json.Linq.JObject.Parse(request.downloadHandler.text);
                    ids = new List<string>();
                    foreach (var voice in parsed["voices"])
                    {
                        string id = (string)voice["id"];
                        if (!string.IsNullOrEmpty(id)) ids.Add(id);
                    }
                    ids.Sort();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Could not parse voice list: {ex.Message}");
                    ids = null;
                }
                onResult?.Invoke(ids);
            }
        }

        public string GetStatusString()
        {
            if (!_isInitialized || !_config.Enabled) return "Disabled";
            return _piperServerAvailable ? $"Piper Server ({_config.Endpoint})" : "Piper (not connected)";
        }
    }

    internal class TTSRequest
    {
        public string Text { get; set; }
        public string Voice { get; set; }
        /// <summary>Speed for this line alone, multiplying the configured SpeechRate.</summary>
        public float RateScale { get; set; } = 1f;
        public Action<AudioClip> OnSuccess { get; set; }
        public Action<string> OnError { get; set; }
    }
}
