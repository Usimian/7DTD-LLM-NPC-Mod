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

        // Companion voice dropdown, populated from the TTS server's installed voices
        private XUiC_ComboBoxList<string> cbxCompanionVoice;
        private XUiC_ComboBoxList<string> cbxProvider;

        // Label shown in the dropdown -> endpoint URL, and the model that suits it.
        private static readonly string[] ProviderLabels = { "Local Ollama", "Groq (cloud)" };
        private static readonly string[] ProviderEndpoints =
        {
            "http://localhost:11434/api/generate",
            "https://api.groq.com/openai/v1/chat/completions"
        };
        private static readonly string[] ProviderModels = { "qwen3.6:35b", "llama-3.3-70b-versatile" };

        private XUiC_TextInput txtModel;

        private XUiC_SimpleButton btnReloadPersona;

        private XUiV_Label lblContextUsage;

        private EntityPlayerLocal _entityPlayerLocal;

        // CVar names for persistence
        private const string CVAR_TTS_ENABLED = "NPCLLMChat_TTSEnabled";
        private const string CVAR_STT_ENABLED = "NPCLLMChat_STTEnabled";
        private const string CVAR_VOLUME = "NPCLLMChat_Volume";
        private const string CVAR_SPEECH_RATE = "NPCLLMChat_SpeechRate";
        private const string CVAR_MAX_HISTORY = "NPCLLMChat_MaxHistory";
        private const string CVAR_CHAT_DISTANCE = "NPCLLMChat_ChatDistance";
        private const string CVAR_VOICE_DISTANCE = "NPCLLMChat_VoiceDistance";
        private const string CVAR_COMPANION_VOICE = "NPCLLMChat_CompanionVoice";
        private const string CVAR_ENDPOINT = "NPCLLMChat_Endpoint";
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


            cbxCompanionVoice = GetChildById("cbxCompanionVoice") as XUiC_ComboBoxList<string>;
            cbxProvider = GetChildById("cbxProvider") as XUiC_ComboBoxList<string>;
            UnityEngine.Debug.Log($"[NPCLLMChat] Init: cbxProvider = {(cbxProvider != null ? "found" : "NULL")}");
            if (cbxProvider != null) cbxProvider.OnValueChanged += CbxProvider_OnValueChanged;
            UnityEngine.Debug.Log($"[NPCLLMChat] Init: cbxCompanionVoice = {(cbxCompanionVoice != null ? "found" : "NULL")}");

            txtModel = GetChildById("txtModel") as XUiC_TextInput;

            btnReloadPersona = GetChildById("btnReloadPersona") as XUiC_SimpleButton;

            lblContextUsage = GetChildById("lblContextUsage")?.ViewComponent as XUiV_Label;

            ShowRealSliderValues();
            if (btnReloadPersona != null) btnReloadPersona.OnPressed += BtnReloadPersona_OnPressed;

            // Wire up button events
            if (btnClose != null) btnClose.OnPressed += BtnClose_OnPressed;
            if (btnSave != null) btnSave.OnPressed += BtnSave_OnPressed;
            if (btnCancel != null) btnCancel.OnPressed += BtnCancel_OnPressed;
            if (btnTestTTS != null) btnTestTTS.OnPressed += BtnTestTTS_OnPressed;
            if (btnTestSTT != null) btnTestSTT.OnPressed += BtnTestSTT_OnPressed;
            if (btnTestLLM != null) btnTestLLM.OnPressed += BtnTestLLM_OnPressed;
            if (btnClearConversations != null) btnClearConversations.OnPressed += BtnClearConversations_OnPressed;
        }

        /// <summary>
        /// Label each slider with what it actually means.
        ///
        /// XUiC_Slider.Value is Mathf.Clamp01, so the control is always a 0-1 fraction and the
        /// min_value/max_value attributes in the window XML do nothing at all. The settings were
        /// being stored correctly - the code maps each range on the way in and out - but the
        /// number on screen was the raw fraction, so Memory read "0.41" instead of 10 and Chat
        /// Distance read "0.17" instead of 5 metres. valueFormatter exists for this.
        /// </summary>
        private void ShowRealSliderValues()
        {
            if (sliderVolume != null)
                sliderVolume.ValueFormatter = v => $"{Mathf.RoundToInt(v * 100f)}%";

            if (sliderSpeechRate != null)
                sliderSpeechRate.ValueFormatter = v => $"{0.5f + v * 1.5f:0.00}x";

            if (sliderMaxHistory != null)
                sliderMaxHistory.ValueFormatter = v => $"{Mathf.RoundToInt(3f + v * 17f)} messages";

            if (sliderChatDistance != null)
                sliderChatDistance.ValueFormatter = v => $"{Mathf.RoundToInt(3f + v * 12f)}m";

            if (sliderVoiceDistance != null)
                sliderVoiceDistance.ValueFormatter = v => $"{Mathf.RoundToInt(5f + v * 15f)}m";
        }

        /// <summary>
        /// How full the companion's prompt is, measured from the real thing rather than estimated
        /// in the abstract.
        ///
        /// Her notebook and the container contents she keeps track of both grow the longer a save
        /// runs, and overrunning the window is silent - the model simply loses the far end of what
        /// she was told, which reads as her having forgotten something she was plainly given. This
        /// is the number that says how much room is left before that starts.
        /// </summary>
        private void ShowContextUsage()
        {
            if (lblContextUsage == null) return;

            try
            {
                int window = LLMService.Instance?.ContextWindow ?? 0;
                var companion = FindCompanion();
                if (companion == null || window <= 0)
                {
                    lblContextUsage.Text = "no companion nearby";
                    return;
                }

                int tokens = LLMService.EstimateTokens(companion.DumpWorldContext().Length);
                int percent = tokens * 100 / window;
                string verdict = percent >= 85 ? " - TOO FULL" : percent >= 70 ? " - getting full" : "";
                lblContextUsage.Text = $"{tokens} / {window} tokens ({percent}%){verdict}";
            }
            catch (Exception ex)
            {
                lblContextUsage.Text = "unavailable";
                Log.Warning($"[NPCLLMChat] Context usage readout failed: {ex.Message}");
            }
        }

        /// <summary>The player's companion, if she is loaded and close enough to read.</summary>
        private NPCChatComponent FindCompanion()
        {
            var world = GameManager.Instance?.World;
            if (world == null || _entityPlayerLocal == null) return null;

            NPCChatComponent nearest = null;
            float nearestDistance = 30f;
            foreach (var entity in world.Entities.list)
            {
                if (!(entity is EntityAlive alive) || alive.IsDead()) continue;
                var chat = alive.GetComponent<NPCChatComponent>();
                if (chat == null || !chat.IsCompanion) continue;

                float distance = Vector3.Distance(_entityPlayerLocal.position, alive.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = chat;
                }
            }
            return nearest;
        }

        public override void OnOpen()
        {
            base.OnOpen();

            _entityPlayerLocal = xui.playerUI.entityPlayer;

            UnityEngine.Debug.Log($"[NPCLLMChat] OnOpen called, about to load settings");

            // Load current settings from player buffs (or defaults from config)
            LoadSettings();
            ShowContextUsage();

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
                float volumeNormalized = GetFloatPref(CVAR_VOLUME, TTSService.Instance?.Config?.Volume ?? 0.8f);
                sliderVolume.Value = volumeNormalized;
                UnityEngine.Debug.Log($"[NPCLLMChat] LoadSettings: Setting sliderVolume.Value to {volumeNormalized}");
            }

            if (sliderSpeechRate != null)
            {
                // Sliders work in normalized 0.0-1.0 range
                // Speech rate is 0.5-2.0, need to map to 0.0-1.0
                // XML has min=50, max=200, so: (rate - 0.5) / (2.0 - 0.5) = (rate - 0.5) / 1.5
                float speechRate = GetFloatPref(CVAR_SPEECH_RATE, TTSService.Instance?.Config?.SpeechRate ?? 1.0f);
                float sliderNormalized = (speechRate - 0.5f) / 1.5f;  // Map 0.5-2.0 to 0.0-1.0
                sliderSpeechRate.Value = sliderNormalized;
                UnityEngine.Debug.Log($"[NPCLLMChat] LoadSettings: Speech rate={speechRate}, setting slider to normalized {sliderNormalized}");
            }

            // Companion voice dropdown: show the saved choice immediately, then swap in
            // the server's full installed-voice list when it answers
            if (cbxCompanionVoice != null)
            {
                string companionVoice = GetStringCVar(CVAR_COMPANION_VOICE, TTSService.Instance?.Config?.CompanionVoice ?? "en_US-amy-medium");
                PopulateVoiceList(new List<string> { companionVoice }, companionVoice);
                TTSService.Instance?.FetchAvailableVoices(voices =>
                {
                    if (voices != null && voices.Count > 0)
                    {
                        PopulateVoiceList(voices, companionVoice);
                    }
                });
            }

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

            // Provider follows whatever endpoint is actually in use
            if (cbxProvider != null)
            {
                string endpoint = GetStringCVar(CVAR_ENDPOINT, LLMService.Instance?.Endpoint ?? "");
                cbxProvider.Elements.Clear();
                foreach (string label in ProviderLabels) cbxProvider.Elements.Add(label);

                // anything that is not the cloud endpoint is treated as the local one
                bool isCloud = endpoint.IndexOf("groq.com", StringComparison.OrdinalIgnoreCase) >= 0;
                cbxProvider.SelectedIndex = isCloud ? 1 : 0;
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

                SetFloatPref(CVAR_VOLUME, volumeNormalized);
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

                SetFloatPref(CVAR_SPEECH_RATE, speechRate);
                if (TTSService.Instance != null)
                {
                    TTSService.Instance.Config.SpeechRate = speechRate;
                    UnityEngine.Debug.Log($"[NPCLLMChat] SaveSettings: Set TTSService speech rate to {speechRate}");
                }
            }

            // Save companion voice from the dropdown and apply it to active NPCs
            if (cbxProvider != null && !string.IsNullOrEmpty(cbxProvider.Value))
            {
                int index = Array.IndexOf(ProviderLabels, cbxProvider.Value);
                if (index >= 0)
                {
                    SetStringCVar(CVAR_ENDPOINT, ProviderEndpoints[index]);
                    LLMService.Instance?.SetEndpoint(ProviderEndpoints[index]);
                }
            }

            if (cbxCompanionVoice != null && !string.IsNullOrEmpty(cbxCompanionVoice.Value))
            {
                string companionVoice = cbxCompanionVoice.Value;
                SetStringCVar(CVAR_COMPANION_VOICE, companionVoice);
                if (TTSService.Instance != null)
                {
                    TTSService.Instance.Config.CompanionVoice = companionVoice;
                }
                RefreshActiveVoices();
                UnityEngine.Debug.Log($"[NPCLLMChat] Companion voice set to: {companionVoice}");
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

        private void BtnReloadPersona_OnPressed(XUiController _sender, int _mouseButton)
        {
            // Push the on-disk persona into any live companion, then refresh the preview
            int reloaded = 0;
            var world = GameManager.Instance?.World;
            if (world != null)
            {
                foreach (var entity in world.Entities.list)
                {
                    if (entity is EntityAlive alive)
                    {
                        var chat = alive.GetComponent<NPCChatComponent>();
                        if (chat != null && chat.IsCompanion && chat.ReloadPersona())
                        {
                            reloaded++;
                        }
                    }
                }
            }
            GameManager.ShowTooltip(_entityPlayerLocal,
                reloaded > 0 ? "Persona reloaded and applied to companion" : "Persona reloaded (companion not active yet)", false);
        }

        /// <summary>
        /// Picking a provider suggests the model that belongs with it - a Groq endpoint with
        /// an Ollama tag like "qwen3.6:35b" in the model box just fails at request time.
        /// </summary>
        private void CbxProvider_OnValueChanged(XUiController sender, string oldValue, string newValue)
        {
            if (txtModel == null) return;
            int index = Array.IndexOf(ProviderLabels, newValue);
            if (index < 0) return;

            bool modelSuitsNewProvider = index == 0
                ? txtModel.Text.Contains(":")     // ollama tags look like name:size
                : !txtModel.Text.Contains(":");
            if (!modelSuitsNewProvider)
            {
                txtModel.Text = ProviderModels[index];
            }
        }

        private void PopulateVoiceList(List<string> voices, string selected)
        {
            if (cbxCompanionVoice == null) return;

            if (!string.IsNullOrEmpty(selected) && !voices.Contains(selected))
            {
                voices.Insert(0, selected);
            }
            cbxCompanionVoice.Elements.Clear();
            foreach (string voice in voices)
            {
                cbxCompanionVoice.Elements.Add(voice);
            }
            int index = voices.IndexOf(selected);
            cbxCompanionVoice.SelectedIndex = index >= 0 ? index : 0;
        }

        private void RefreshActiveVoices()
        {
            var world = GameManager.Instance?.World;
            if (world == null) return;
            foreach (var entity in world.Entities.list)
            {
                if (entity is EntityAlive alive)
                {
                    alive.GetComponent<NPCAudioPlayer>()?.RefreshVoice();
                }
            }
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

        // Volume and speech rate must survive a restart, so they live in PlayerPrefs like the
        // voice and model - buff cvars were being written but never read back.
        private float GetFloatPref(string key, float defaultValue)
        {
            return PlayerPrefs.GetFloat(key, defaultValue);
        }

        private void SetFloatPref(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
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
            // Preview whatever companion voice is currently selected in the dropdown
            string voice = !string.IsNullOrEmpty(cbxCompanionVoice?.Value)
                ? cbxCompanionVoice.Value
                : TTSService.Instance.Config?.CompanionVoice ?? "en_US-lessac-medium";

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
