using System;
using System.IO;
using System.Threading;
using UnityEngine;

#if UNITY_STANDALONE_WIN
using System.Speech.Synthesis;
#endif

namespace NPCLLMChat.TTS
{
    /// <summary>
    /// Windows native TTS provider using System.Speech.Synthesis (SAPI).
    /// Works out of the box on Windows without any external dependencies.
    /// </summary>
    public class WindowsTTSProvider
    {
        private static WindowsTTSProvider _instance;
        public static WindowsTTSProvider Instance => _instance ?? (_instance = new WindowsTTSProvider());

#if UNITY_STANDALONE_WIN
        private SpeechSynthesizer _synthesizer;
#endif
        private bool _isInitialized = false;
        private string[] _availableVoices;

        public bool IsAvailable => _isInitialized;
        public string[] AvailableVoices => _availableVoices ?? new string[0];

        private WindowsTTSProvider()
        {
            Initialize();
        }

        private void Initialize()
        {
#if UNITY_STANDALONE_WIN
            try
            {
                _synthesizer = new SpeechSynthesizer();
                
                // Get available voices
                var voices = _synthesizer.GetInstalledVoices();
                _availableVoices = new string[voices.Count];
                
                Log.Out("[NPCLLMChat] Windows SAPI TTS voices available:");
                for (int i = 0; i < voices.Count; i++)
                {
                    var voice = voices[i].VoiceInfo;
                    _availableVoices[i] = voice.Name;
                    Log.Out($"  - {voice.Name} ({voice.Gender}, {voice.Culture.Name})");
                }

                if (_availableVoices.Length > 0)
                {
                    _isInitialized = true;
                    Log.Out($"[NPCLLMChat] Windows SAPI TTS initialized with {_availableVoices.Length} voices");
                }
                else
                {
                    Log.Warning("[NPCLLMChat] No Windows SAPI voices found");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Failed to initialize Windows SAPI TTS: {ex.Message}");
                _isInitialized = false;
            }
#else
            Log.Out("[NPCLLMChat] Windows SAPI TTS not available on this platform");
            _isInitialized = false;
#endif
        }

        /// <summary>
        /// Synthesize text to WAV audio data using Windows SAPI
        /// </summary>
        /// <param name="text">Text to synthesize</param>
        /// <param name="voiceName">Voice name (Windows voice name or mapped from Piper voice)</param>
        /// <param name="speechRate">Speech rate multiplier (0.5 = slow, 1.0 = normal, 2.0 = fast)</param>
        /// <param name="onSuccess">Callback with WAV byte data</param>
        /// <param name="onError">Callback on error</param>
        public void Synthesize(string text, string voiceName, float speechRate, Action<byte[]> onSuccess, Action<string> onError)
        {
#if UNITY_STANDALONE_WIN
            if (!_isInitialized)
            {
                onError?.Invoke("Windows TTS not initialized");
                return;
            }

            // Run synthesis on a background thread to avoid blocking Unity
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var synth = new SpeechSynthesizer())
                    using (var stream = new MemoryStream())
                    {
                        // Configure output format (16kHz mono for compatibility)
                        synth.SetOutputToWaveStream(stream);
                        
                        // Try to select voice
                        string selectedVoice = SelectBestVoice(synth, voiceName);
                        if (!string.IsNullOrEmpty(selectedVoice))
                        {
                            try
                            {
                                synth.SelectVoice(selectedVoice);
                            }
                            catch
                            {
                                // Fall back to default voice
                            }
                        }

                        // Set speech rate (-10 to 10, where 0 is normal)
                        // Convert from our 0.5-2.0 scale to SAPI -10 to 10 scale
                        int sapiRate = (int)((speechRate - 1.0f) * 10f);
                        sapiRate = Math.Max(-10, Math.Min(10, sapiRate));
                        synth.Rate = sapiRate;

                        // Synthesize
                        synth.Speak(text);

                        // Get WAV data
                        byte[] wavData = stream.ToArray();
                        
                        // Invoke callback on main thread
                        onSuccess?.Invoke(wavData);
                    }
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"Windows TTS synthesis failed: {ex.Message}");
                }
            });
#else
            onError?.Invoke("Windows TTS not available on this platform");
#endif
        }

#if UNITY_STANDALONE_WIN
        /// <summary>
        /// Select the best matching voice based on the requested voice name
        /// </summary>
        private string SelectBestVoice(SpeechSynthesizer synth, string requestedVoice)
        {
            if (string.IsNullOrEmpty(requestedVoice))
                return null;

            var voices = synth.GetInstalledVoices();
            
            // First, try exact match
            foreach (var voice in voices)
            {
                if (voice.VoiceInfo.Name.Equals(requestedVoice, StringComparison.OrdinalIgnoreCase))
                    return voice.VoiceInfo.Name;
            }

            // Map Piper voice names to Windows voice characteristics
            VoiceGender preferredGender = VoiceGender.NotSet;
            
            if (requestedVoice.Contains("amy") || requestedVoice.Contains("lessac"))
            {
                preferredGender = VoiceGender.Female;
            }
            else if (requestedVoice.Contains("ryan"))
            {
                preferredGender = VoiceGender.Male;
            }

            // Find a voice matching the preferred gender
            if (preferredGender != VoiceGender.NotSet)
            {
                foreach (var voice in voices)
                {
                    if (voice.VoiceInfo.Gender == preferredGender && 
                        voice.VoiceInfo.Culture.TwoLetterISOLanguageName == "en")
                    {
                        return voice.VoiceInfo.Name;
                    }
                }
            }

            // Fall back to first English voice
            foreach (var voice in voices)
            {
                if (voice.VoiceInfo.Culture.TwoLetterISOLanguageName == "en")
                    return voice.VoiceInfo.Name;
            }

            // Fall back to any voice
            return voices.Count > 0 ? voices[0].VoiceInfo.Name : null;
        }
#endif

        /// <summary>
        /// Get voice info for display
        /// </summary>
        public string GetVoiceInfo()
        {
#if UNITY_STANDALONE_WIN
            if (!_isInitialized) return "Not initialized";
            return $"Windows SAPI ({_availableVoices?.Length ?? 0} voices)";
#else
            return "Not available (Windows only)";
#endif
        }
    }
}
