using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NPCLLMChat.STT
{
    /// <summary>
    /// Handles microphone recording with push-to-talk functionality.
    /// Converts recorded audio to WAV format for STT processing.
    /// </summary>
    public class MicrophoneCapture : MonoBehaviour
    {
        private static MicrophoneCapture _instance;
        public static MicrophoneCapture Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("MicrophoneCapture");
                    _instance = go.AddComponent<MicrophoneCapture>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        // Configuration
        private STTConfig _config;
        private bool _isInitialized = false;
        private bool _isEnabled = true;

        // Recording state
        private bool _isRecording = false;
        private AudioClip _recordingClip;
        private string _selectedDevice;
        private float _recordingStartTime;

        // Push-to-talk key
        private KeyCode _pushToTalkKeyCode = KeyCode.V;

        // Events
        public event Action OnRecordingStarted;
        public event Action OnRecordingStopped;
        public event Action<string> OnTranscriptionComplete;
        public event Action<string> OnTranscriptionError;

        // State properties
        public bool IsInitialized => _isInitialized;
        public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }
        public bool IsRecording => _isRecording;
        public float RecordingDuration => _isRecording ? Time.time - _recordingStartTime : 0f;
        public string SelectedDevice => _selectedDevice;
        public STTConfig Config => _config;

        /// <summary>
        /// Initialize microphone capture with configuration
        /// </summary>
        public void Initialize(STTConfig config)
        {
            _config = config;

            // Parse push-to-talk key
            if (!string.IsNullOrEmpty(_config.PushToTalkKey))
            {
                if (Enum.TryParse<KeyCode>(_config.PushToTalkKey, true, out KeyCode key))
                {
                    _pushToTalkKeyCode = key;
                }
                else
                {
                    Log.Warning($"[NPCLLMChat] Invalid push-to-talk key '{_config.PushToTalkKey}', using V");
                    _pushToTalkKeyCode = KeyCode.V;
                }
            }

            // Select default microphone
            string[] devices = Microphone.devices;
            if (devices.Length > 0)
            {
                _selectedDevice = devices[0];
                Log.Out($"[NPCLLMChat] MicrophoneCapture initialized - Device: {_selectedDevice}");
                Log.Out($"[NPCLLMChat] Push-to-talk key: {_pushToTalkKeyCode}");
                _isInitialized = true;
            }
            else
            {
                Log.Warning("[NPCLLMChat] No microphone devices found!");
                _isInitialized = false;
            }
        }

        /// <summary>
        /// Get list of available microphone devices
        /// </summary>
        public string[] GetDevices()
        {
            return Microphone.devices;
        }

        /// <summary>
        /// Select a specific microphone device by name
        /// </summary>
        public bool SelectDevice(string deviceName)
        {
            string[] devices = Microphone.devices;
            foreach (string device in devices)
            {
                if (device.Equals(deviceName, StringComparison.OrdinalIgnoreCase) ||
                    device.Contains(deviceName))
                {
                    _selectedDevice = device;
                    Log.Out($"[NPCLLMChat] Selected microphone: {_selectedDevice}");
                    return true;
                }
            }
            Log.Warning($"[NPCLLMChat] Microphone device not found: {deviceName}");
            return false;
        }

        void Update()
        {
            if (!_isInitialized || !_isEnabled || !_config.Enabled)
                return;

            // Always check for key release if we're recording
            // This ensures we can stop recording even if UI state changes
            if (_isRecording)
            {
                // Check if user released the key - stop immediately
                if (UnityEngine.Input.GetKeyUp(_pushToTalkKeyCode))
                {
                    Log.Out($"[NPCLLMChat] V key released detected at {RecordingDuration:F2}s");
                    StopRecordingAndTranscribe();
                    return;
                }
            }
            else
            {
                // Only check for starting recording if input is valid
                if (!ShouldProcessInput())
                    return;

                // Push-to-talk start
                if (UnityEngine.Input.GetKeyDown(_pushToTalkKeyCode))
                {
                    Log.Out("[NPCLLMChat] V key pressed detected");
                    StartRecording();
                }
            }

            // Auto-stop if recording too long
            if (_isRecording && RecordingDuration >= _config.MaxRecordingSeconds)
            {
                Log.Out("[NPCLLMChat] Max recording duration reached, stopping...");
                StopRecordingAndTranscribe();
            }

            // Recording tooltip removed per user request
        }

        /// <summary>
        /// Check if we should process voice input (not in menus, etc.)
        /// </summary>
        private bool ShouldProcessInput()
        {
            // Don't capture when in main menu or paused
            var localPlayer = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (localPlayer == null)
                return false;

            // Check for any open UI windows that would take input focus
            // This is a basic check - may need refinement
            if (GameManager.Instance.IsPaused())
                return false;

            // Don't capture when player is typing in chat or other UI
            // Check if any window manager has focus
            var windowManager = LocalPlayerUI.GetUIForPlayer(localPlayer)?.windowManager;
            if (windowManager != null && windowManager.IsModalWindowOpen())
                return false;

            return true;
        }

        /// <summary>
        /// Start recording from the microphone
        /// </summary>
        public void StartRecording()
        {
            if (_isRecording)
            {
                Log.Warning("[NPCLLMChat] Already recording");
                return;
            }

            if (string.IsNullOrEmpty(_selectedDevice))
            {
                Log.Error("[NPCLLMChat] No microphone device selected");
                OnTranscriptionError?.Invoke("No microphone available");
                return;
            }

            // Start recording with the configured sample rate
            // Use max recording duration + 1 for buffer
            _recordingClip = Microphone.Start(
                _selectedDevice,
                false,  // Don't loop
                _config.MaxRecordingSeconds + 1,
                _config.SampleRate
            );

            if (_recordingClip == null)
            {
                Log.Error("[NPCLLMChat] Failed to start microphone recording");
                OnTranscriptionError?.Invoke("Failed to start microphone");
                return;
            }

            _isRecording = true;
            _recordingStartTime = Time.time;

            Log.Out("[NPCLLMChat] Recording started...");
            OnRecordingStarted?.Invoke();
        }

        /// <summary>
        /// Stop recording and transcribe the audio
        /// </summary>
        public void StopRecordingAndTranscribe()
        {
            if (!_isRecording)
            {
                return;
            }

            _isRecording = false;

            // Get the final sample position before stopping microphone
            int samplePosition = Microphone.GetPosition(_selectedDevice);

            // Stop the microphone
            Microphone.End(_selectedDevice);

            float recordingDuration = Time.time - _recordingStartTime;
            Log.Out($"[NPCLLMChat] Recording stopped ({recordingDuration:F1}s)");

            OnRecordingStopped?.Invoke();

            // Check if we got any audio
            if (samplePosition <= 0 || _recordingClip == null)
            {
                Log.Warning("[NPCLLMChat] No audio recorded");
                OnTranscriptionError?.Invoke("No audio recorded");
                CleanupRecording();
                return;
            }

            // Convert to WAV and send to STT service
            try
            {
                byte[] wavData = ConvertToWav(_recordingClip, samplePosition);

                if (wavData == null || wavData.Length < 100)
                {
                    Log.Warning("[NPCLLMChat] Failed to convert audio to WAV");
                    OnTranscriptionError?.Invoke("Failed to process audio");
                    CleanupRecording();
                    return;
                }

                Log.Out($"[NPCLLMChat] Sending {wavData.Length} bytes to STT server...");

                // Send to STT service
                STTService.Instance.Transcribe(
                    wavData,
                    text =>
                    {
                        Log.Out($"[NPCLLMChat] Transcription: \"{text}\"");
                        OnTranscriptionComplete?.Invoke(text);
                    },
                    error =>
                    {
                        Log.Warning($"[NPCLLMChat] Transcription failed: {error}");

                        // Show error to player
                        var p = GameManager.Instance?.World?.GetPrimaryPlayer();
                        if (p != null)
                        {
                            GameManager.ShowTooltip(p, $"Voice error: {error}", false);
                        }

                        OnTranscriptionError?.Invoke(error);
                    }
                );
            }
            catch (Exception ex)
            {
                Log.Error($"[NPCLLMChat] Error processing recording: {ex.Message}");
                OnTranscriptionError?.Invoke($"Error: {ex.Message}");
            }

            CleanupRecording();
        }

        /// <summary>
        /// Cancel recording without transcribing
        /// </summary>
        public void CancelRecording()
        {
            if (!_isRecording)
                return;

            Microphone.End(_selectedDevice);
            _isRecording = false;
            CleanupRecording();
            Log.Out("[NPCLLMChat] Recording cancelled");
            OnRecordingStopped?.Invoke();
        }

        /// <summary>
        /// Clean up recording resources
        /// </summary>
        private void CleanupRecording()
        {
            if (_recordingClip != null)
            {
                Destroy(_recordingClip);
                _recordingClip = null;
            }
        }

        /// <summary>
        /// Convert AudioClip to WAV byte array
        /// </summary>
        private byte[] ConvertToWav(AudioClip clip, int sampleCount)
        {
            if (clip == null || sampleCount <= 0)
                return null;

            // Get audio samples
            float[] samples = new float[sampleCount * clip.channels];
            clip.GetData(samples, 0);

            // Convert to mono if stereo (Whisper expects mono)
            float[] monoSamples;
            if (clip.channels > 1)
            {
                monoSamples = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < clip.channels; ch++)
                    {
                        sum += samples[i * clip.channels + ch];
                    }
                    monoSamples[i] = sum / clip.channels;
                }
            }
            else
            {
                monoSamples = samples;
            }

            // Create WAV file
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int sampleRate = clip.frequency;
                int channels = 1;  // Mono
                int bitsPerSample = 16;
                int byteRate = sampleRate * channels * bitsPerSample / 8;
                int blockAlign = channels * bitsPerSample / 8;
                int dataSize = monoSamples.Length * bitsPerSample / 8;

                // RIFF header
                writer.Write(new char[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);  // File size - 8
                writer.Write(new char[] { 'W', 'A', 'V', 'E' });

                // fmt chunk
                writer.Write(new char[] { 'f', 'm', 't', ' ' });
                writer.Write(16);  // Chunk size
                writer.Write((short)1);  // Audio format (PCM)
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write((short)blockAlign);
                writer.Write((short)bitsPerSample);

                // data chunk
                writer.Write(new char[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);

                // Write samples as 16-bit integers
                foreach (float sample in monoSamples)
                {
                    // Clamp and convert to 16-bit
                    float clamped = Mathf.Clamp(sample, -1f, 1f);
                    short intSample = (short)(clamped * 32767f);
                    writer.Write(intSample);
                }

                return stream.ToArray();
            }
        }

        /// <summary>
        /// Test recording without transcription (for debugging)
        /// </summary>
        public void TestRecording(float durationSeconds, Action<byte[]> onComplete)
        {
            StartCoroutine(TestRecordingCoroutine(durationSeconds, onComplete));
        }

        private IEnumerator TestRecordingCoroutine(float durationSeconds, Action<byte[]> onComplete)
        {
            if (string.IsNullOrEmpty(_selectedDevice))
            {
                Log.Error("[NPCLLMChat] No microphone device for test");
                onComplete?.Invoke(null);
                yield break;
            }

            Log.Out($"[NPCLLMChat] Test recording for {durationSeconds}s...");

            AudioClip testClip = Microphone.Start(
                _selectedDevice,
                false,
                (int)durationSeconds + 1,
                _config?.SampleRate ?? 16000
            );

            if (testClip == null)
            {
                Log.Error("[NPCLLMChat] Failed to start test recording");
                onComplete?.Invoke(null);
                yield break;
            }

            yield return new WaitForSeconds(durationSeconds);

            int samplePosition = Microphone.GetPosition(_selectedDevice);
            Microphone.End(_selectedDevice);

            if (samplePosition <= 0)
            {
                Log.Warning("[NPCLLMChat] Test recording got no samples");
                Destroy(testClip);
                onComplete?.Invoke(null);
                yield break;
            }

            byte[] wavData = ConvertToWav(testClip, samplePosition);
            Destroy(testClip);

            Log.Out($"[NPCLLMChat] Test recording complete: {wavData?.Length ?? 0} bytes");
            onComplete?.Invoke(wavData);
        }

        void OnDestroy()
        {
            if (_isRecording)
            {
                Microphone.End(_selectedDevice);
            }
            CleanupRecording();
            _instance = null;
        }
    }
}
