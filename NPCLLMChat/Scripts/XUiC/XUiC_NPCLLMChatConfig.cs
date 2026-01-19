using System;
using System.Collections.Generic;
using NPCLLMChat;
using NPCLLMChat.STT;
using NPCLLMChat.TTS;
using UnityEngine;

// XUI controllers must be in the global namespace for 7DTD to find them
/// <summary>
/// Controller for the NPCLLMChat configuration window.
/// Manages UI controls and persists settings to player buffs.
/// </summary>
public class XUiC_NPCLLMChatConfig : XUiController
    {
        // UI Controls
        private XUiC_SimpleButton btnClose;
        private XUiC_SimpleButton btnSave;
        private XUiC_SimpleButton btnCancel;
        private XUiC_SimpleButton btnTestTTS;
        private XUiC_SimpleButton btnTestSTT;
        private XUiC_SimpleButton btnTestLLM;
        private XUiC_SimpleButton btnClearConversations;

        private XUiC_ToggleButton toggleTTS;
        private XUiC_ToggleButton toggleSTT;

        private XUiC_Slider sliderVolume;
        private XUiC_Slider sliderSpeechRate;
        private XUiC_Slider sliderMaxHistory;
        private XUiC_Slider sliderChatDistance;
        private XUiC_Slider sliderVoiceDistance;

        // Default voice radio buttons
        private XUiC_ToggleButton radioDefaultLessac;
        private XUiC_ToggleButton radioDefaultAmy;
        private XUiC_ToggleButton radioDefaultRyan;

        // Companion voice radio buttons
        private XUiC_ToggleButton radioCompanionLessac;
        private XUiC_ToggleButton radioCompanionAmy;
        private XUiC_ToggleButton radioCompanionRyan;

        // Trader voice radio buttons
        private XUiC_ToggleButton radioTraderLessac;
        private XUiC_ToggleButton radioTraderAmy;
        private XUiC_ToggleButton radioTraderRyan;

        private XUiC_TextInput txtModel;

        private EntityPlayerLocal _entityPlayerLocal;

        // Available voices (should match TTS config)
        private readonly string[] _availableVoices = new[]
        {
            "en_US-lessac-medium",
            "en_US-amy-medium",
            "en_US-ryan-medium",
            "en_US-joe-medium",
            "en_GB-alan-medium"
        };

        // CVar names for persistence
        private const string CVAR_TTS_ENABLED = "NPCLLMChat_TTSEnabled";
        private const string CVAR_STT_ENABLED = "NPCLLMChat_STTEnabled";
        private const string CVAR_VOLUME = "NPCLLMChat_Volume";
        private const string CVAR_SPEECH_RATE = "NPCLLMChat_SpeechRate";
        private const string CVAR_MAX_HISTORY = "NPCLLMChat_MaxHistory";
        private const string CVAR_CHAT_DISTANCE = "NPCLLMChat_ChatDistance";
        private const string CVAR_VOICE_DISTANCE = "NPCLLMChat_VoiceDistance";
        private const string CVAR_DEFAULT_VOICE = "NPCLLMChat_DefaultVoice";
        private const string CVAR_COMPANION_VOICE = "NPCLLMChat_CompanionVoice";
        private const string CVAR_TRADER_VOICE = "NPCLLMChat_TraderVoice";
        private const string CVAR_MODEL = "NPCLLMChat_Model";

        public override void Init()
        {
            base.Init();

            // Get UI controls
            btnClose = GetChildById("btnClose") as XUiC_SimpleButton;
            btnSave = GetChildById("btnSave") as XUiC_SimpleButton;
            btnCancel = GetChildById("btnCancel") as XUiC_SimpleButton;
            btnTestTTS = GetChildById("btnTestTTS") as XUiC_SimpleButton;
            btnTestSTT = GetChildById("btnTestSTT") as XUiC_SimpleButton;
            btnTestLLM = GetChildById("btnTestLLM") as XUiC_SimpleButton;
            btnClearConversations = GetChildById("btnClearConversations") as XUiC_SimpleButton;

            toggleTTS = GetChildById("toggleTTS") as XUiC_ToggleButton;
            toggleSTT = GetChildById("toggleSTT") as XUiC_ToggleButton;

            sliderVolume = GetChildById("sliderVolume") as XUiC_Slider;
            sliderSpeechRate = GetChildById("sliderSpeechRate") as XUiC_Slider;
            sliderMaxHistory = GetChildById("sliderMaxHistory") as XUiC_Slider;
            sliderChatDistance = GetChildById("sliderChatDistance") as XUiC_Slider;
            sliderVoiceDistance = GetChildById("sliderVoiceDistance") as XUiC_Slider;

            // Debug: Log which sliders were found
            UnityEngine.Debug.Log($"[NPCLLMChat] Init: sliderVolume = {(sliderVolume != null ? "found" : "NULL")}");
            UnityEngine.Debug.Log($"[NPCLLMChat] Init: sliderSpeechRate = {(sliderSpeechRate != null ? "found" : "NULL")}");
            UnityEngine.Debug.Log($"[NPCLLMChat] Init: sliderMaxHistory = {(sliderMaxHistory != null ? "found" : "NULL")}");
            UnityEngine.Debug.Log($"[NPCLLMChat] Init: sliderChatDistance = {(sliderChatDistance != null ? "found" : "NULL")}");
            UnityEngine.Debug.Log($"[NPCLLMChat] Init: sliderVoiceDistance = {(sliderVoiceDistance != null ? "found" : "NULL")}");


            // Get radio buttons for voice selection
            radioDefaultLessac = GetChildById("radioDefaultLessac") as XUiC_ToggleButton;
            radioDefaultAmy = GetChildById("radioDefaultAmy") as XUiC_ToggleButton;
            radioDefaultRyan = GetChildById("radioDefaultRyan") as XUiC_ToggleButton;

            radioCompanionLessac = GetChildById("radioCompanionLessac") as XUiC_ToggleButton;
            radioCompanionAmy = GetChildById("radioCompanionAmy") as XUiC_ToggleButton;
            radioCompanionRyan = GetChildById("radioCompanionRyan") as XUiC_ToggleButton;

            radioTraderLessac = GetChildById("radioTraderLessac") as XUiC_ToggleButton;
            radioTraderAmy = GetChildById("radioTraderAmy") as XUiC_ToggleButton;
            radioTraderRyan = GetChildById("radioTraderRyan") as XUiC_ToggleButton;

            txtModel = GetChildById("txtModel") as XUiC_TextInput;

            // Wire up button events
            if (btnClose != null) btnClose.OnPressed += BtnClose_OnPressed;
            if (btnSave != null) btnSave.OnPressed += BtnSave_OnPressed;
            if (btnCancel != null) btnCancel.OnPressed += BtnCancel_OnPressed;
            if (btnTestTTS != null) btnTestTTS.OnPressed += BtnTestTTS_OnPressed;
            if (btnTestSTT != null) btnTestSTT.OnPressed += BtnTestSTT_OnPressed;
            if (btnTestLLM != null) btnTestLLM.OnPressed += BtnTestLLM_OnPressed;
            if (btnClearConversations != null) btnClearConversations.OnPressed += BtnClearConversations_OnPressed;
        }

        public override void OnOpen()
        {
            base.OnOpen();

            _entityPlayerLocal = xui.playerUI.entityPlayer;

            UnityEngine.Debug.Log($"[NPCLLMChat] OnOpen called, about to load settings");

            // Load current settings from player buffs (or defaults from config)
            LoadSettings();

            // Log the actual slider values after loading
            if (sliderVolume != null)
                UnityEngine.Debug.Log($"[NPCLLMChat] OnOpen: After LoadSettings, sliderVolume.Value = {sliderVolume.Value}");
            if (sliderSpeechRate != null)
                UnityEngine.Debug.Log($"[NPCLLMChat] OnOpen: After LoadSettings, sliderSpeechRate.Value = {sliderSpeechRate.Value}");
        }

        private void LoadSettings()
        {
            if (_entityPlayerLocal == null) return;

            var buffs = _entityPlayerLocal.Buffs;

            // Load TTS settings
            if (toggleTTS != null)
            {
                toggleTTS.Value = GetBoolCVar(CVAR_TTS_ENABLED, TTSService.Instance?.Config?.Enabled ?? true);
            }

            if (sliderVolume != null)
            {
                // Sliders work in normalized 0.0-1.0 range regardless of XML min/max
                // TTSService also stores volume as 0.0-1.0, so use directly
                float volumeNormalized = TTSService.Instance?.Config?.Volume ?? 0.8f;
                sliderVolume.Value = volumeNormalized;
                UnityEngine.Debug.Log($"[NPCLLMChat] LoadSettings: Setting sliderVolume.Value to {volumeNormalized}");
            }

            if (sliderSpeechRate != null)
            {
                // Sliders work in normalized 0.0-1.0 range
                // Speech rate is 0.5-2.0, need to map to 0.0-1.0
                // XML has min=50, max=200, so: (rate - 0.5) / (2.0 - 0.5) = (rate - 0.5) / 1.5
                float speechRate = TTSService.Instance?.Config?.SpeechRate ?? 1.0f;
                float sliderNormalized = (speechRate - 0.5f) / 1.5f;  // Map 0.5-2.0 to 0.0-1.0
                sliderSpeechRate.Value = sliderNormalized;
                UnityEngine.Debug.Log($"[NPCLLMChat] LoadSettings: Speech rate={speechRate}, setting slider to normalized {sliderNormalized}");
            }

            // Load default voice radio buttons
            var defaultVoice = GetStringCVar(CVAR_DEFAULT_VOICE, TTSService.Instance?.Config?.DefaultVoice ?? "en_US-lessac-medium");
            if (radioDefaultLessac != null) radioDefaultLessac.Value = (defaultVoice == "en_US-lessac-medium");
            if (radioDefaultAmy != null) radioDefaultAmy.Value = (defaultVoice == "en_US-amy-medium");
            if (radioDefaultRyan != null) radioDefaultRyan.Value = (defaultVoice == "en_US-ryan-medium");

            // Load companion voice radio buttons
            var companionVoice = GetStringCVar(CVAR_COMPANION_VOICE, TTSService.Instance?.Config?.CompanionVoice ?? "en_US-amy-medium");
            if (radioCompanionLessac != null) radioCompanionLessac.Value = (companionVoice == "en_US-lessac-medium");
            if (radioCompanionAmy != null) radioCompanionAmy.Value = (companionVoice == "en_US-amy-medium");
            if (radioCompanionRyan != null) radioCompanionRyan.Value = (companionVoice == "en_US-ryan-medium");

            // Load trader voice radio buttons
            var traderVoice = GetStringCVar(CVAR_TRADER_VOICE, TTSService.Instance?.Config?.TraderVoice ?? "en_US-ryan-medium");
            if (radioTraderLessac != null) radioTraderLessac.Value = (traderVoice == "en_US-lessac-medium");
            if (radioTraderAmy != null) radioTraderAmy.Value = (traderVoice == "en_US-amy-medium");
            if (radioTraderRyan != null) radioTraderRyan.Value = (traderVoice == "en_US-ryan-medium");

            // Load STT settings
            if (toggleSTT != null)
            {
                toggleSTT.Value = GetBoolCVar(CVAR_STT_ENABLED, STTService.Instance?.Config?.Enabled ?? true);
            }

            // Load conversation settings - need to normalize to 0-1 range
            if (sliderMaxHistory != null)
            {
                // Range 3-20, normalize to 0-1
                float maxHistory = GetFloatCVar(CVAR_MAX_HISTORY, 10f);
                float normalized = (maxHistory - 3f) / (20f - 3f);
                sliderMaxHistory.Value = normalized;
            }

            if (sliderChatDistance != null)
            {
                // Range 3-15, normalize to 0-1
                float chatDistance = GetFloatCVar(CVAR_CHAT_DISTANCE, 5f);
                float normalized = (chatDistance - 3f) / (15f - 3f);
                sliderChatDistance.Value = normalized;
            }

            if (sliderVoiceDistance != null)
            {
                // Range 5-20, normalize to 0-1
                float voiceDistance = GetFloatCVar(CVAR_VOICE_DISTANCE, 15f);
                float normalized = (voiceDistance - 5f) / (20f - 5f);
                sliderVoiceDistance.Value = normalized;
            }

            // Load model setting
            if (txtModel != null)
            {
                txtModel.Text = GetStringCVar(CVAR_MODEL, "llama3.3:70b");
            }
        }

        private void SaveSettings()
        {
            if (_entityPlayerLocal == null) return;

            UnityEngine.Debug.Log($"[NPCLLMChat] SaveSettings called");

            var buffs = _entityPlayerLocal.Buffs;

            // Save TTS settings
            if (toggleTTS != null)
            {
                SetBoolCVar(CVAR_TTS_ENABLED, toggleTTS.Value);
                if (TTSService.Instance != null)
                {
                    TTSService.Instance.Config.Enabled = toggleTTS.Value;
                }
            }

            if (sliderVolume != null)
            {
                // Slider value is already 0.0-1.0 (normalized), use directly
                float volumeNormalized = sliderVolume.Value;
                UnityEngine.Debug.Log($"[NPCLLMChat] SaveSettings: sliderVolume.Value={volumeNormalized}");

                SetFloatCVar(CVAR_VOLUME, volumeNormalized);
                if (TTSService.Instance != null)
                {
                    TTSService.Instance.Config.Volume = volumeNormalized;
                    UnityEngine.Debug.Log($"[NPCLLMChat] SaveSettings: Set TTSService volume to {volumeNormalized}");
                }
            }

            if (sliderSpeechRate != null)
            {
                // Slider value is 0.0-1.0 (normalized), convert back to 0.5-2.0 range
                float sliderNormalized = sliderSpeechRate.Value;
                float speechRate = 0.5f + (sliderNormalized * 1.5f);  // Map 0.0-1.0 to 0.5-2.0
                UnityEngine.Debug.Log($"[NPCLLMChat] SaveSettings: sliderSpeechRate.Value={sliderNormalized}, converting to rate={speechRate}");

                SetFloatCVar(CVAR_SPEECH_RATE, speechRate);
                if (TTSService.Instance != null)
                {
                    TTSService.Instance.Config.SpeechRate = speechRate;
                    UnityEngine.Debug.Log($"[NPCLLMChat] SaveSettings: Set TTSService speech rate to {speechRate}");
                }
            }

            // Save default voice based on radio button selection
            string defaultVoice = "en_US-lessac-medium";
            if (radioDefaultAmy != null && radioDefaultAmy.Value) defaultVoice = "en_US-amy-medium";
            else if (radioDefaultRyan != null && radioDefaultRyan.Value) defaultVoice = "en_US-ryan-medium";
            SetStringCVar(CVAR_DEFAULT_VOICE, defaultVoice);
            if (TTSService.Instance != null)
            {
                TTSService.Instance.Config.DefaultVoice = defaultVoice;
            }

            // Save companion voice based on radio button selection
            string companionVoice = "en_US-lessac-medium";
            if (radioCompanionAmy != null && radioCompanionAmy.Value) companionVoice = "en_US-amy-medium";
            else if (radioCompanionRyan != null && radioCompanionRyan.Value) companionVoice = "en_US-ryan-medium";
            SetStringCVar(CVAR_COMPANION_VOICE, companionVoice);
            if (TTSService.Instance != null)
            {
                TTSService.Instance.Config.CompanionVoice = companionVoice;
            }

            // Save trader voice based on radio button selection
            string traderVoice = "en_US-lessac-medium";
            if (radioTraderAmy != null && radioTraderAmy.Value) traderVoice = "en_US-amy-medium";
            else if (radioTraderRyan != null && radioTraderRyan.Value) traderVoice = "en_US-ryan-medium";
            SetStringCVar(CVAR_TRADER_VOICE, traderVoice);
            if (TTSService.Instance != null)
            {
                TTSService.Instance.Config.TraderVoice = traderVoice;
            }

            // Save STT settings
            if (toggleSTT != null)
            {
                SetBoolCVar(CVAR_STT_ENABLED, toggleSTT.Value);
                if (STTService.Instance != null)
                {
                    STTService.Instance.Config.Enabled = toggleSTT.Value;
                }
                if (MicrophoneCapture.Instance != null)
                {
                    MicrophoneCapture.Instance.IsEnabled = toggleSTT.Value;
                }
            }

            // Save conversation settings - denormalize from 0-1 back to actual range
            if (sliderMaxHistory != null)
            {
                // Denormalize from 0-1 to 3-20
                float normalized = sliderMaxHistory.Value;
                float maxHistory = 3f + (normalized * (20f - 3f));
                SetFloatCVar(CVAR_MAX_HISTORY, maxHistory);
            }

            if (sliderChatDistance != null)
            {
                // Denormalize from 0-1 to 3-15
                float normalized = sliderChatDistance.Value;
                float chatDistance = 3f + (normalized * (15f - 3f));
                SetFloatCVar(CVAR_CHAT_DISTANCE, chatDistance);
            }

            if (sliderVoiceDistance != null)
            {
                // Denormalize from 0-1 to 5-20
                float normalized = sliderVoiceDistance.Value;
                float voiceDistance = 5f + (normalized * (20f - 5f));
                SetFloatCVar(CVAR_VOICE_DISTANCE, voiceDistance);
            }

            // Save model setting
            if (txtModel != null)
            {
                string newModel = txtModel.Text.Trim();
                SetStringCVar(CVAR_MODEL, newModel);

                // Update LLMService model immediately
                if (LLMService.Instance != null)
                {
                    LLMService.Instance.SetModel(newModel);
                    UnityEngine.Debug.Log($"[NPCLLMChat] Updated LLMService model to: {newModel}");
                }
            }

            GameManager.ShowTooltip(_entityPlayerLocal, "Settings saved successfully", false);
        }

        // CVar helper methods
        private bool GetBoolCVar(string cvar, bool defaultValue)
        {
            if (_entityPlayerLocal.Buffs.HasCustomVar(cvar))
            {
                return _entityPlayerLocal.Buffs.GetCustomVar(cvar) > 0f;
            }
            return defaultValue;
        }

        private float GetFloatCVar(string cvar, float defaultValue)
        {
            if (_entityPlayerLocal.Buffs.HasCustomVar(cvar))
            {
                return _entityPlayerLocal.Buffs.GetCustomVar(cvar);
            }
            return defaultValue;
        }

        private string GetStringCVar(string cvar, string defaultValue)
        {
            // String storage: use a special encoding or separate storage
            // For simplicity, we'll use PlayerPrefs which persists across sessions
            return PlayerPrefs.GetString(cvar, defaultValue);
        }

        private void SetBoolCVar(string cvar, bool value)
        {
            _entityPlayerLocal.Buffs.SetCustomVar(cvar, value ? 1f : 0f);
        }

        private void SetFloatCVar(string cvar, float value)
        {
            _entityPlayerLocal.Buffs.SetCustomVar(cvar, value);
        }

        private void SetStringCVar(string cvar, string value)
        {
            PlayerPrefs.SetString(cvar, value);
            PlayerPrefs.Save();
        }

        // Button handlers
        private void BtnClose_OnPressed(XUiController _sender, int _mouseButton)
        {
            CloseWindow();
        }

        private void BtnSave_OnPressed(XUiController _sender, int _mouseButton)
        {
            SaveSettings();
            CloseWindow();
        }

        private void BtnCancel_OnPressed(XUiController _sender, int _mouseButton)
        {
            CloseWindow();
        }

        private void BtnTestTTS_OnPressed(XUiController _sender, int _mouseButton)
        {
            if (TTSService.Instance == null || !TTSService.Instance.IsInitialized)
            {
                GameManager.ShowTooltip(_entityPlayerLocal, "TTS service not available", false);
                return;
            }

            string testText = "Hello! This is a test of the text to speech system.";
            // Get selected default voice from radio buttons
            string voice = "en_US-lessac-medium";
            if (radioDefaultAmy != null && radioDefaultAmy.Value) voice = "en_US-amy-medium";
            else if (radioDefaultRyan != null && radioDefaultRyan.Value) voice = "en_US-ryan-medium";

            GameManager.ShowTooltip(_entityPlayerLocal, "Generating test audio...", false);

            // Use Synthesize to generate audio, then play it
            TTSService.Instance.Synthesize(
                testText,
                voice,
                audioClip => {
                    // Play the audio using coroutine on the player entity
                    if (audioClip != null && _entityPlayerLocal != null)
                    {
                        UnityEngine.Debug.Log($"[NPCLLMChat] Test audio clip received: {audioClip.name}, length: {audioClip.length}s, samples: {audioClip.samples}");
                        float volume = sliderVolume?.Value / 100f ?? 0.8f;

                        // Try using the game's main thread to ensure audio plays
                        ThreadManager.AddSingleTaskMainThread("PlayTestTTS", (_taskInfo) => {
                            _entityPlayerLocal.StartCoroutine(PlayTestAudio(audioClip, volume));
                        });

                        GameManager.ShowTooltip(_entityPlayerLocal, "Playing test audio...", false);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning("[NPCLLMChat] AudioClip or player is null!");
                    }
                },
                error => {
                    UnityEngine.Debug.LogError($"[NPCLLMChat] TTS test error: {error}");
                    GameManager.ShowTooltip(_entityPlayerLocal, $"TTS test failed: {error}", false);
                }
            );
        }

        private System.Collections.IEnumerator PlayTestAudio(AudioClip clip, float volume)
        {
            UnityEngine.Debug.Log($"[NPCLLMChat] PlayTestAudio coroutine started, volume: {volume}");

            // Create a temporary GameObject for playing the audio
            GameObject tempAudio = new GameObject("TempTestAudio");
            tempAudio.transform.position = _entityPlayerLocal.position;
            UnityEngine.Debug.Log($"[NPCLLMChat] Created temp audio GameObject at position: {_entityPlayerLocal.position}");

            AudioSource source = tempAudio.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.spatialBlend = 0f; // 2D audio
            source.playOnAwake = false;
            source.loop = false;

            // Critical settings for 7DTD custom audio - same as NPCAudioPlayer
            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;
            source.priority = 0;  // Highest priority

            UnityEngine.Debug.Log($"[NPCLLMChat] AudioSource configured, calling Play()");
            source.Play();

            UnityEngine.Debug.Log($"[NPCLLMChat] AudioSource.isPlaying: {source.isPlaying}, time: {source.time}");

            // Wait for audio to finish
            yield return new WaitForSeconds(clip.length);

            UnityEngine.Debug.Log("[NPCLLMChat] Audio playback complete, cleaning up");

            // Cleanup
            UnityEngine.Object.Destroy(tempAudio);
        }

        private void BtnTestSTT_OnPressed(XUiController _sender, int _mouseButton)
        {

            if (STTService.Instance == null || !STTService.Instance.IsInitialized)
            {
                GameManager.ShowTooltip(_entityPlayerLocal, "STT service not available", false);
                return;
            }

            if (MicrophoneCapture.Instance == null || !MicrophoneCapture.Instance.IsInitialized)
            {
                GameManager.ShowTooltip(_entityPlayerLocal, "Microphone not available", false);
                return;
            }

            GameManager.ShowTooltip(_entityPlayerLocal, "Recording for 3 seconds...", false);

            MicrophoneCapture.Instance.TestRecording(3f, wavData =>
            {
                if (wavData == null || wavData.Length == 0)
                {
                    GameManager.ShowTooltip(_entityPlayerLocal, "No audio captured", false);
                    return;
                }

                STTService.Instance.Transcribe(
                    wavData,
                    text => {
                        GameManager.ShowTooltip(_entityPlayerLocal, $"You said: \"{text}\"", false);
                    },
                    error => {
                        GameManager.ShowTooltip(_entityPlayerLocal, $"STT test failed: {error}", false);
                    }
                );
            });
        }

        private void BtnTestLLM_OnPressed(XUiController _sender, int _mouseButton)
        {

            if (LLMService.Instance == null)
            {
                GameManager.ShowTooltip(_entityPlayerLocal, "LLM service not available", false);
                return;
            }

            // LLMService exists and Instance is not null means it's initialized
            GameManager.ShowTooltip(_entityPlayerLocal, $"AI service is ready! Model: {txtModel?.Text ?? "unknown"}", false);
        }

        private void BtnClearConversations_OnPressed(XUiController _sender, int _mouseButton)
        {

            // Clear all NPC conversation histories
            var npcs = GameManager.Instance.World.Entities.list;
            int clearedCount = 0;

            foreach (var entity in npcs)
            {
                if (entity is EntityAlive npc)
                {
                    var chatComponent = npc.gameObject?.GetComponent<NPCChatComponent>();
                    if (chatComponent != null)
                    {
                        chatComponent.ClearHistory();
                        clearedCount++;
                    }
                }
            }

            GameManager.ShowTooltip(_entityPlayerLocal, $"Cleared {clearedCount} conversation(s)", false);
        }

        private void CloseWindow()
        {
            xui.playerUI.windowManager.Close(this.windowGroup.ID);
        }
    }
