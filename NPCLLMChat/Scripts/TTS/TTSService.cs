using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace NPCLLMChat.TTS
{
    /// <summary>
    /// Handles communication with the Piper TTS HTTP server.
    /// Converts text to speech and returns AudioClips for playback.
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
        private bool _serverAvailable = false;

        // Request tracking
        private Queue<TTSRequest> _requestQueue = new Queue<TTSRequest>();
        private bool _isProcessing = false;

        // Performance tracking
        private float _lastSynthesisTimeMs = 0;
        private float _avgSynthesisTimeMs = 0;
        private int _requestCount = 0;

        public bool IsInitialized => _isInitialized;
        public bool ServerAvailable => _serverAvailable;
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

            if (_config.Enabled)
            {
                Log.Out($"[NPCLLMChat] TTSService initialized - Endpoint: {_config.Endpoint}");
                Log.Out($"[NPCLLMChat] Default voice: {_config.DefaultVoice}");

                // Check if server is available
                StartCoroutine(CheckServerHealth());
            }
            else
            {
                Log.Out("[NPCLLMChat] TTSService disabled in config");
            }
        }

        /// <summary>
        /// Check if the TTS server is responding
        /// </summary>
        private IEnumerator CheckServerHealth()
        {
            string healthUrl = _config.Endpoint.Replace("/synthesize", "/health");

            using (UnityWebRequest request = UnityWebRequest.Get(healthUrl))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    _serverAvailable = true;
                    Log.Out("[NPCLLMChat] TTS server is available");
                }
                else
                {
                    _serverAvailable = false;
                    Log.Warning($"[NPCLLMChat] TTS server not available: {request.error}");
                    Log.Warning("[NPCLLMChat] Start piper-server with: python piper_server.py --port 5050");
                }
            }
        }

        /// <summary>
        /// Synthesize text to speech and return an AudioClip
        /// </summary>
        /// <param name="text">Text to convert to speech</param>
        /// <param name="voice">Voice ID to use (optional, uses default if not specified)</param>
        /// <param name="onSuccess">Callback with the generated AudioClip</param>
        /// <param name="onError">Callback if synthesis fails</param>
        public void Synthesize(string text, string voice, Action<AudioClip> onSuccess, Action<string> onError)
        {
            if (!_isInitialized || !_config.Enabled)
            {
                onError?.Invoke("TTS not initialized or disabled");
                return;
            }

            if (!_serverAvailable)
            {
                // Try to check again in case server started
                StartCoroutine(CheckServerHealth());
                onError?.Invoke("TTS server not available");
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                onError?.Invoke("Empty text");
                return;
            }

            // Queue the request
            var request = new TTSRequest
            {
                Text = text,
                Voice = string.IsNullOrEmpty(voice) ? _config.DefaultVoice : voice,
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

        /// <summary>
        /// Process queued TTS requests one at a time
        /// </summary>
        private IEnumerator ProcessQueue()
        {
            _isProcessing = true;

            while (_requestQueue.Count > 0)
            {
                var request = _requestQueue.Dequeue();
                yield return StartCoroutine(SynthesizeCoroutine(request));
            }

            _isProcessing = false;
        }

        /// <summary>
        /// Perform actual HTTP request to TTS server
        /// </summary>
        private IEnumerator SynthesizeCoroutine(TTSRequest request)
        {
            float startTime = Time.realtimeSinceStartup;

            // Build request JSON
            string jsonBody = BuildRequestJson(request.Text, request.Voice);

            using (UnityWebRequest webRequest = new UnityWebRequest(_config.Endpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = _config.TimeoutSeconds;

                yield return webRequest.SendWebRequest();

                // Track timing
                _lastSynthesisTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                _requestCount++;
                _avgSynthesisTimeMs = ((_avgSynthesisTimeMs * (_requestCount - 1)) + _lastSynthesisTimeMs) / _requestCount;

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    // Check content type
                    string contentType = webRequest.GetResponseHeader("Content-Type");
                    if (contentType != null && contentType.Contains("audio"))
                    {
                        // Parse WAV data into AudioClip
                        byte[] wavData = webRequest.downloadHandler.data;
                        AudioClip clip = WavToAudioClip(wavData, request.Text);

                        if (clip != null)
                        {
                            Log.Out($"[NPCLLMChat] TTS synthesis completed in {_lastSynthesisTimeMs:F0}ms ({clip.length:F1}s audio)");
                            request.OnSuccess?.Invoke(clip);
                        }
                        else
                        {
                            request.OnError?.Invoke("Failed to parse WAV data");
                        }
                    }
                    else
                    {
                        // Probably an error response in JSON
                        string response = webRequest.downloadHandler.text;
                        Log.Warning($"[NPCLLMChat] TTS error response: {response}");
                        request.OnError?.Invoke($"TTS server error: {response}");
                    }
                }
                else
                {
                    _serverAvailable = false; // Mark as unavailable
                    Log.Warning($"[NPCLLMChat] TTS request failed: {webRequest.error}");
                    request.OnError?.Invoke($"TTS request failed: {webRequest.error}");
                }
            }
        }

        /// <summary>
        /// Build JSON request body for Piper TTS server
        /// </summary>
        private string BuildRequestJson(string text, string voice)
        {
            // Escape text for JSON
            string escapedText = text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

            // Build JSON with optional speech rate
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"text\": \"{escapedText}\"");
            sb.Append($", \"voice\": \"{voice}\"");

            if (Math.Abs(_config.SpeechRate - 1.0f) > 0.01f)
            {
                // length_scale is inverse of speed (0.8 = faster, 1.2 = slower)
                float lengthScale = 1.0f / _config.SpeechRate;
                sb.Append($", \"length_scale\": {lengthScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Convert WAV byte data to Unity AudioClip
        /// </summary>
        private AudioClip WavToAudioClip(byte[] wavData, string clipName)
        {
            try
            {
                // WAV header parsing
                // RIFF header: 4 bytes "RIFF", 4 bytes size, 4 bytes "WAVE"
                // fmt chunk: 4 bytes "fmt ", 4 bytes size, then format data
                // data chunk: 4 bytes "data", 4 bytes size, then audio samples

                if (wavData.Length < 44)
                {
                    Log.Error("[NPCLLMChat] WAV data too short");
                    return null;
                }

                // Verify RIFF header
                if (wavData[0] != 'R' || wavData[1] != 'I' || wavData[2] != 'F' || wavData[3] != 'F')
                {
                    Log.Error("[NPCLLMChat] Invalid WAV: missing RIFF header");
                    return null;
                }

                // Verify WAVE format
                if (wavData[8] != 'W' || wavData[9] != 'A' || wavData[10] != 'V' || wavData[11] != 'E')
                {
                    Log.Error("[NPCLLMChat] Invalid WAV: missing WAVE format");
                    return null;
                }

                // Parse fmt chunk
                int fmtOffset = 12;
                while (fmtOffset < wavData.Length - 8)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(wavData, fmtOffset, 4);
                    int chunkSize = BitConverter.ToInt32(wavData, fmtOffset + 4);

                    if (chunkId == "fmt ")
                    {
                        break;
                    }
                    fmtOffset += 8 + chunkSize;
                }

                if (fmtOffset >= wavData.Length - 8)
                {
                    Log.Error("[NPCLLMChat] Invalid WAV: fmt chunk not found");
                    return null;
                }

                // Read format data
                int audioFormat = BitConverter.ToInt16(wavData, fmtOffset + 8);
                int channels = BitConverter.ToInt16(wavData, fmtOffset + 10);
                int sampleRate = BitConverter.ToInt32(wavData, fmtOffset + 12);
                int bitsPerSample = BitConverter.ToInt16(wavData, fmtOffset + 22);

                // Find data chunk
                int dataOffset = fmtOffset + 8 + BitConverter.ToInt32(wavData, fmtOffset + 4);
                while (dataOffset < wavData.Length - 8)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(wavData, dataOffset, 4);
                    if (chunkId == "data")
                    {
                        break;
                    }
                    int chunkSize = BitConverter.ToInt32(wavData, dataOffset + 4);
                    dataOffset += 8 + chunkSize;
                }

                if (dataOffset >= wavData.Length - 8)
                {
                    Log.Error("[NPCLLMChat] Invalid WAV: data chunk not found");
                    return null;
                }

                int dataSize = BitConverter.ToInt32(wavData, dataOffset + 4);
                int dataStart = dataOffset + 8;

                // Calculate samples
                int bytesPerSample = bitsPerSample / 8;
                int sampleCount = dataSize / bytesPerSample / channels;

                // Create AudioClip
                AudioClip clip = AudioClip.Create(
                    "TTS_" + clipName.Substring(0, Math.Min(20, clipName.Length)),
                    sampleCount,
                    channels,
                    sampleRate,
                    false
                );

                // Convert samples to float
                float[] samples = new float[sampleCount * channels];

                if (bitsPerSample == 16)
                {
                    for (int i = 0; i < sampleCount * channels; i++)
                    {
                        int sampleIndex = dataStart + i * 2;
                        if (sampleIndex + 1 < wavData.Length)
                        {
                            short sample = BitConverter.ToInt16(wavData, sampleIndex);
                            samples[i] = sample / 32768f;
                        }
                    }
                }
                else if (bitsPerSample == 8)
                {
                    for (int i = 0; i < sampleCount * channels; i++)
                    {
                        int sampleIndex = dataStart + i;
                        if (sampleIndex < wavData.Length)
                        {
                            samples[i] = (wavData[sampleIndex] - 128) / 128f;
                        }
                    }
                }
                else
                {
                    Log.Warning($"[NPCLLMChat] Unsupported bits per sample: {bitsPerSample}");
                    return null;
                }

                clip.SetData(samples, 0);
                return clip;
            }
            catch (Exception ex)
            {
                Log.Error($"[NPCLLMChat] Error parsing WAV: {ex.Message}");
                return null;
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
    /// Internal class to track pending TTS requests
    /// </summary>
    internal class TTSRequest
    {
        public string Text { get; set; }
        public string Voice { get; set; }
        public Action<AudioClip> OnSuccess { get; set; }
        public Action<string> OnError { get; set; }
    }
}
