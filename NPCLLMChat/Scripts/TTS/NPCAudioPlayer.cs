using System;
using UnityEngine;

namespace NPCLLMChat.TTS
{
    /// <summary>
    /// Manages audio playback for a single NPC with 3D spatial audio.
    /// Attach to an NPC entity to enable voice playback.
    /// </summary>
    public class NPCAudioPlayer : MonoBehaviour
    {
        private AudioSource _audioSource;
        private EntityAlive _npcEntity;
        private TTSConfig _config;

        // State tracking
        private bool _isSpeaking = false;
        private string _currentText = "";
        private Action _onSpeechComplete;

        // Voice settings for this NPC
        private string _voiceId;
        private float _volumeMultiplier = 1.0f;

        public bool IsSpeaking => _isSpeaking;
        public string CurrentText => _currentText;
        public string VoiceId => _voiceId;

        /// <summary>
        /// Initialize the audio player for an NPC
        /// </summary>
        public void Initialize(EntityAlive npc, TTSConfig config)
        {
            _npcEntity = npc;
            _config = config;

            // Create and configure AudioSource
            _audioSource = gameObject.AddComponent<AudioSource>();

            // 3D spatial audio settings
            _audioSource.spatialBlend = 0.7f;  // Mostly 3D but with some 2D fallback for audibility
            _audioSource.rolloffMode = AudioRolloffMode.Linear;
            _audioSource.maxDistance = config.MaxDistance;
            _audioSource.minDistance = config.MinDistance;
            _audioSource.dopplerLevel = 0f;  // Disable doppler for speech clarity
            _audioSource.spread = 60f;  // Wider spread for better audibility

            // General settings
            _audioSource.volume = config.Volume;
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;

            // Bypass game audio processing that might block custom audio
            _audioSource.bypassEffects = true;
            _audioSource.bypassListenerEffects = true;
            _audioSource.bypassReverbZones = true;
            _audioSource.priority = 0;  // Highest priority

            // Determine voice based on NPC type
            _voiceId = DetermineVoice(npc);

            Log.Out($"[NPCLLMChat] NPCAudioPlayer initialized for NPC {npc.entityId} with voice {_voiceId}");
        }

        /// <summary>
        /// Determine which voice to use based on NPC characteristics
        /// </summary>
        private string DetermineVoice(EntityAlive npc)
        {
            if (_config == null) return "en_US-lessac-medium";

            // Check entity class name for type hints
            string entityClass = npc.EntityClass?.entityClassName?.ToLower() ?? "";

            if (entityClass.Contains("trader"))
            {
                return _config.TraderVoice;
            }
            else if (entityClass.Contains("bandit") || entityClass.Contains("hostile"))
            {
                return _config.BanditVoice;
            }
            else if (entityClass.Contains("companion") || entityClass.Contains("hire") || entityClass.Contains("follow"))
            {
                return _config.CompanionVoice;
            }

            // Default voice
            return _config.DefaultVoice;
        }

        /// <summary>
        /// Set a custom voice for this NPC
        /// </summary>
        public void SetVoice(string voiceId)
        {
            _voiceId = voiceId;
        }

        /// <summary>
        /// Set volume multiplier for this NPC (on top of global config)
        /// </summary>
        public void SetVolumeMultiplier(float multiplier)
        {
            _volumeMultiplier = Mathf.Clamp(multiplier, 0f, 2f);
            UpdateVolume();
        }

        private void UpdateVolume()
        {
            if (_audioSource != null && _config != null)
            {
                _audioSource.volume = _config.Volume * _volumeMultiplier;
            }
        }

        /// <summary>
        /// Speak the given text using TTS
        /// </summary>
        public void Speak(string text, Action onComplete = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onComplete?.Invoke();
                return;
            }

            // Stop any current speech
            if (_isSpeaking)
            {
                StopSpeaking();
            }

            _currentText = text;
            _onSpeechComplete = onComplete;

            // Request TTS synthesis
            TTSService.Instance.Synthesize(
                text,
                _voiceId,
                OnAudioClipReady,
                OnTTSError
            );
        }

        /// <summary>
        /// Called when TTS synthesis completes successfully
        /// </summary>
        private void OnAudioClipReady(AudioClip clip)
        {
            if (clip == null)
            {
                Log.Warning("[NPCLLMChat] Received null AudioClip");
                _onSpeechComplete?.Invoke();
                return;
            }

            if (_audioSource == null)
            {
                Log.Warning("[NPCLLMChat] AudioSource is null");
                _onSpeechComplete?.Invoke();
                return;
            }

            // Update position to match NPC
            UpdatePosition();

            // Play the audio
            _audioSource.clip = clip;
            _audioSource.Play();
            _isSpeaking = true;

            Log.Out($"[NPCLLMChat] NPC speaking: \"{_currentText.Substring(0, Math.Min(30, _currentText.Length))}...\" ({clip.length:F1}s)");
        }

        /// <summary>
        /// Called when TTS synthesis fails
        /// </summary>
        private void OnTTSError(string error)
        {
            Log.Warning($"[NPCLLMChat] TTS failed: {error}");
            _isSpeaking = false;
            _onSpeechComplete?.Invoke();
        }

        /// <summary>
        /// Stop current speech playback
        /// </summary>
        public void StopSpeaking()
        {
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }

            if (_audioSource != null && _audioSource.clip != null)
            {
                // Clean up the clip to free memory
                Destroy(_audioSource.clip);
                _audioSource.clip = null;
            }

            _isSpeaking = false;
            _currentText = "";
            _onSpeechComplete?.Invoke();
            _onSpeechComplete = null;
        }

        /// <summary>
        /// Update audio source position to match NPC
        /// </summary>
        private void UpdatePosition()
        {
            if (_npcEntity != null && _audioSource != null)
            {
                // Position audio at NPC's head (approximate)
                Vector3 headPos = _npcEntity.position;
                headPos.y += 1.6f; // Rough head height
                transform.position = headPos;
            }
        }

        private void Update()
        {
            // Keep position synced with NPC
            if (_isSpeaking)
            {
                UpdatePosition();
            }

            // Check if speech finished
            if (_isSpeaking && _audioSource != null && !_audioSource.isPlaying)
            {
                _isSpeaking = false;

                // Clean up clip
                if (_audioSource.clip != null)
                {
                    Destroy(_audioSource.clip);
                    _audioSource.clip = null;
                }

                // Notify completion
                var callback = _onSpeechComplete;
                _onSpeechComplete = null;
                callback?.Invoke();
            }
        }

        private void OnDestroy()
        {
            // Clean up audio resources
            if (_audioSource != null && _audioSource.clip != null)
            {
                Destroy(_audioSource.clip);
            }
        }

        /// <summary>
        /// Get playback progress (0 to 1)
        /// </summary>
        public float GetProgress()
        {
            if (_audioSource != null && _audioSource.clip != null && _audioSource.clip.length > 0)
            {
                return _audioSource.time / _audioSource.clip.length;
            }
            return 0f;
        }

        /// <summary>
        /// Get remaining time in seconds
        /// </summary>
        public float GetRemainingTime()
        {
            if (_audioSource != null && _audioSource.clip != null)
            {
                return _audioSource.clip.length - _audioSource.time;
            }
            return 0f;
        }
    }
}
