using System;
using System.IO;
using System.Threading;
using UnityEngine;

#if UNITY_STANDALONE_WIN
using System.Speech.Recognition;
#endif

namespace NPCLLMChat.STT
{
    /// <summary>
    /// Windows native STT provider using System.Speech.Recognition.
    /// Works out of the box on Windows without any external dependencies.
    /// Note: Windows Speech Recognition must be enabled in Windows settings.
    /// </summary>
    public class WindowsSTTProvider
    {
        private static WindowsSTTProvider _instance;
        public static WindowsSTTProvider Instance => _instance ?? (_instance = new WindowsSTTProvider());

        private bool _isInitialized = false;
        private bool _isProcessing = false;

        public bool IsAvailable => _isInitialized;
        public bool IsProcessing => _isProcessing;

        private WindowsSTTProvider()
        {
            Initialize();
        }

        private void Initialize()
        {
#if UNITY_STANDALONE_WIN
            try
            {
                // Test if speech recognition is available
                using (var recognizer = new SpeechRecognitionEngine())
                {
                    // Load a dictation grammar
                    recognizer.LoadGrammar(new DictationGrammar());
                    _isInitialized = true;
                    Log.Out("[NPCLLMChat] Windows Speech Recognition initialized");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Windows Speech Recognition not available: {ex.Message}");
                Log.Out("[NPCLLMChat] Enable Windows Speech Recognition in Settings > Time & Language > Speech");
                _isInitialized = false;
            }
#else
            Log.Out("[NPCLLMChat] Windows Speech Recognition not available on this platform");
            _isInitialized = false;
#endif
        }

        /// <summary>
        /// Transcribe WAV audio data to text using Windows Speech Recognition
        /// </summary>
        /// <param name="wavData">WAV format audio data</param>
        /// <param name="onSuccess">Callback with transcribed text</param>
        /// <param name="onError">Callback on error</param>
        public void Transcribe(byte[] wavData, Action<string> onSuccess, Action<string> onError)
        {
#if UNITY_STANDALONE_WIN
            if (!_isInitialized)
            {
                onError?.Invoke("Windows Speech Recognition not initialized");
                return;
            }

            if (_isProcessing)
            {
                onError?.Invoke("Already processing a transcription");
                return;
            }

            if (wavData == null || wavData.Length < 44)
            {
                onError?.Invoke("Invalid audio data");
                return;
            }

            _isProcessing = true;

            // Run recognition on a background thread
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string tempFile = null;
                try
                {
                    // Write WAV to temp file (Windows Speech Recognition needs a file)
                    tempFile = Path.Combine(Path.GetTempPath(), $"npcllm_stt_{Guid.NewGuid()}.wav");
                    File.WriteAllBytes(tempFile, wavData);

                    using (var recognizer = new SpeechRecognitionEngine())
                    {
                        // Load dictation grammar for free-form speech
                        recognizer.LoadGrammar(new DictationGrammar());
                        
                        // Set input to the audio file
                        recognizer.SetInputToWaveFile(tempFile);

                        // Recognize
                        RecognitionResult result = recognizer.Recognize(TimeSpan.FromSeconds(30));

                        _isProcessing = false;

                        if (result != null && !string.IsNullOrEmpty(result.Text))
                        {
                            Log.Out($"[NPCLLMChat] Windows STT result: \"{result.Text}\" (confidence: {result.Confidence:P0})");
                            onSuccess?.Invoke(result.Text);
                        }
                        else
                        {
                            onError?.Invoke("No speech detected");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _isProcessing = false;
                    Log.Warning($"[NPCLLMChat] Windows STT error: {ex.Message}");
                    onError?.Invoke($"Recognition failed: {ex.Message}");
                }
                finally
                {
                    // Clean up temp file
                    if (tempFile != null && File.Exists(tempFile))
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                }
            });
#else
            onError?.Invoke("Windows Speech Recognition not available on this platform");
#endif
        }

        /// <summary>
        /// Get provider info for display
        /// </summary>
        public string GetProviderInfo()
        {
#if UNITY_STANDALONE_WIN
            if (!_isInitialized)
                return "Not initialized - enable Windows Speech Recognition in Settings";
            return "Windows Speech Recognition (built-in)";
#else
            return "Not available (Windows only)";
#endif
        }
    }
}
