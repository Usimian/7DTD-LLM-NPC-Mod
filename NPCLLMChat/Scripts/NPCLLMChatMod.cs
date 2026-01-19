using System;
using System.IO;
using System.Xml;
using UnityEngine;
using NPCLLMChat.TTS;
using NPCLLMChat.STT;

namespace NPCLLMChat
{
    /// <summary>
    /// Main mod entry point for NPC LLM Chat.
    /// Handles initialization, configuration loading, and lifecycle management.
    /// </summary>
    public class NPCLLMChatMod : IModApi
    {
        private static string _modPath;
        private static LLMConfig _config;
        private static TTSConfig _ttsConfig;
        private static STTConfig _sttConfig;
        private static bool _initialized = false;

        public static TTSConfig TTSConfig => _ttsConfig;
        public static STTConfig STTConfig => _sttConfig;

        public void InitMod(Mod _modInstance)
        {
            _modPath = _modInstance.Path;
            Log.Out("Initializing NPC LLM Chat mod...");

            // Load configurations
            _config = LoadConfig();
            if (_config == null)
            {
                Log.Error("Failed to load configuration. Mod disabled.");
                return;
            }

            // Initialize LLM Service
            LLMService.Instance.Initialize(_config);

            // Check if player has overridden the model in PlayerPrefs
            string savedModel = PlayerPrefs.GetString("NPCLLMChat_Model", "");
            if (!string.IsNullOrEmpty(savedModel))
            {
                LLMService.Instance.SetModel(savedModel);
                Log.Out($"[NPCLLMChat] Using player-configured model: {savedModel}");
            }

            // Load TTS configuration and initialize TTS service
            _ttsConfig = LoadTTSConfig();
            if (_ttsConfig != null && _ttsConfig.Enabled)
            {
                TTSService.Instance.Initialize(_ttsConfig);
            }

            // Load STT configuration and initialize STT service
            _sttConfig = LoadSTTConfig();
            if (_sttConfig != null && _sttConfig.Enabled)
            {
                STTService.Instance.Initialize(_sttConfig);
                MicrophoneCapture.Instance.Initialize(_sttConfig);

                // Wire up microphone capture to talk to nearest NPC
                MicrophoneCapture.Instance.OnTranscriptionComplete += OnVoiceTranscribed;
            }

            // Register for game events using A21 API with proper delegate signatures
            ModEvents.GameStartDone.RegisterHandler(GameStartDoneHandler);
            ModEvents.GameShutdown.RegisterHandler(GameShutdownHandler);

            Log.Out("Mod initialized successfully!");
        }

        private static void GameStartDoneHandler(ref ModEvents.SGameStartDoneData data)
        {
            if (_initialized) return;

            Log.Out("Game started - initializing Harmony patches...");

            try
            {
                Harmony.NPCCorePatches.Initialize(_config);
                _initialized = true;
                Log.Out("Ready! Talk to NPCs using @message in chat");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void GameShutdownHandler(ref ModEvents.SGameShutdownData data)
        {
            Log.Out("Shutting down...");
            Harmony.NPCCorePatches.Shutdown();
            _initialized = false;
        }

        /// <summary>
        /// Called when voice transcription completes - sends message to nearest NPC
        /// </summary>
        private static void OnVoiceTranscribed(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Log.Warning("[NPCLLMChat] Voice transcription returned empty text");
                return;
            }

            Log.Out($"[NPCLLMChat] Voice transcribed: \"{text}\"");

            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null)
            {
                Log.Warning("[NPCLLMChat] No player found for voice input");
                return;
            }

            // Find nearest NPC within range
            EntityAlive nearestNPC = FindNearestNPC(player, 15f);
            if (nearestNPC == null)
            {
                Log.Out("[NPCLLMChat] No NPC nearby to talk to via voice");
                Log.Out($"[NPCLLMChat] Checked {GameManager.Instance?.World?.Entities?.list?.Count ?? 0} entities");
                // Show feedback to player
                GameManager.ShowTooltip(player, "No NPC nearby to talk to", false);
                return;
            }

            // Get or create chat component
            var chatComponent = Harmony.NPCCorePatches.GetOrCreateChatComponent(nearestNPC);
            if (chatComponent == null)
            {
                Log.Warning("[NPCLLMChat] Failed to get chat component for NPC");
                return;
            }

            Log.Out($"[NPCLLMChat] Voice message to {chatComponent.NPCName}: \"{text}\"");

            // Show what we heard
            GameManager.ShowTooltip(player, $"You: {text}", false);

            // Send message to NPC
            chatComponent.ProcessPlayerMessage(text, player, response =>
            {
                Log.Out($"[NPCLLMChat] {chatComponent.NPCName} responded: {response}");
            });
        }

        /// <summary>
        /// Find nearest NPC within range
        /// </summary>
        private static EntityAlive FindNearestNPC(EntityPlayer player, float maxDistance)
        {
            EntityAlive closest = null;
            float closestDist = maxDistance;
            int npcCount = 0;

            var world = GameManager.Instance?.World;
            if (world == null) return null;

            Log.Out("[NPCLLMChat] Scanning entities for NPCs...");
            foreach (var entity in world.Entities.list)
            {
                if (entity is EntityAlive alive && alive.entityId != player.entityId)
                {
                    float dist = UnityEngine.Vector3.Distance(player.position, alive.position);
                    bool isNPC = IsNPC(alive);
                    Log.Out($"[NPCLLMChat] Entity: {alive.EntityName} (type: {alive.GetType().Name}) at {dist:F1}m - IsNPC: {isNPC}");

                    if (isNPC)
                    {
                        npcCount++;
                        if (dist < closestDist)
                        {
                            closest = alive;
                            closestDist = dist;
                        }
                    }
                }
            }

            Log.Out($"[NPCLLMChat] Found {npcCount} NPCs total, closest: {closest?.EntityName ?? "none"} at {closestDist:F1}m");
            return closest;
        }

        /// <summary>
        /// Check if entity is an NPC
        /// </summary>
        private static bool IsNPC(EntityAlive entity)
        {
            if (entity == null) return false;
            string name = entity.GetType().Name;

            // Check for common NPC type names
            if (name.Contains("NPC") || name.Contains("Trader") || name.Contains("Hired")) return true;

            // Check for SCore/SDX NPCs
            if (name.Contains("SDX")) return true;

            // Check for other player entities that aren't the local player
            if (entity is EntityPlayer && !(entity is EntityPlayerLocal))
            {
                return ConnectionManager.Instance?.Clients?.ForEntityId(entity.entityId) == null;
            }

            return false;
        }

        private LLMConfig LoadConfig()
        {
            string configPath = Path.Combine(_modPath, "Config", "llmconfig.xml");

            if (!File.Exists(configPath))
            {
                Log.Warning($"Config file not found at {configPath}, using defaults");
                return GetDefaultConfig();
            }

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(configPath);

                var config = new LLMConfig();

                // Server settings
                var serverNode = doc.SelectSingleNode("//Server");
                if (serverNode != null)
                {
                    config.Endpoint = GetNodeValue(serverNode, "Endpoint", "http://localhost:11434/api/generate");
                    config.Model = GetNodeValue(serverNode, "Model", "llama3.3:70b");
                    config.TimeoutSeconds = int.Parse(GetNodeValue(serverNode, "TimeoutSeconds", "15"));
                    config.MaxTokens = int.Parse(GetNodeValue(serverNode, "MaxTokens", "200"));
                    config.Temperature = float.Parse(GetNodeValue(serverNode, "Temperature", "0.8"), System.Globalization.CultureInfo.InvariantCulture);
                    config.NumGPULayers = int.Parse(GetNodeValue(serverNode, "NumGPULayers", "83"));
                    config.NumCtx = int.Parse(GetNodeValue(serverNode, "NumCtx", "4096"));
                }

                // Personality settings
                var personalityNode = doc.SelectSingleNode("//Personality");
                if (personalityNode != null)
                {
                    config.SystemPrompt = GetNodeValue(personalityNode, "SystemPrompt",
                        "You are a survivor in a post-apocalyptic zombie wasteland. Keep responses brief and in-character.");
                    config.ContextMemory = int.Parse(GetNodeValue(personalityNode, "ContextMemory", "10"));
                }

                // Response settings
                var responseNode = doc.SelectSingleNode("//Response");
                if (responseNode != null)
                {
                    config.ShowTypingIndicator = bool.Parse(GetNodeValue(responseNode, "ShowTypingIndicator", "false"));
                    config.TypingDelayMs = int.Parse(GetNodeValue(responseNode, "TypingDelayMs", "0"));
                    config.MaxResponseLength = int.Parse(GetNodeValue(responseNode, "MaxResponseLength", "300"));
                }

                // Action settings
                var actionsNode = doc.SelectSingleNode("//Actions");
                if (actionsNode != null)
                {
                    config.ActionsEnabled = bool.Parse(GetNodeValue(actionsNode, "Enabled", "true"));
                    config.FollowDistance = float.Parse(GetNodeValue(actionsNode, "FollowDistance", "3.0"), System.Globalization.CultureInfo.InvariantCulture);
                    config.GuardRadius = float.Parse(GetNodeValue(actionsNode, "GuardRadius", "10.0"), System.Globalization.CultureInfo.InvariantCulture);
                }

                Log.Out($"Configuration loaded - Model: {config.Model}, GPU Layers: {config.NumGPULayers}");
                return config;
            }
            catch (Exception ex)
            {
                Log.Error($"Error loading config: {ex.Message}");
                return GetDefaultConfig();
            }
        }

        private string GetNodeValue(XmlNode parent, string childName, string defaultValue)
        {
            var child = parent.SelectSingleNode(childName);
            return child?.InnerText ?? defaultValue;
        }

        private LLMConfig GetDefaultConfig()
        {
            return new LLMConfig
            {
                // Server
                Endpoint = "http://localhost:11434/api/generate",
                Model = "llama3.3:70b",
                TimeoutSeconds = 15,
                MaxTokens = 200,
                Temperature = 0.8f,
                NumGPULayers = 83,
                NumCtx = 4096,

                // Personality
                SystemPrompt = "You are a survivor in a post-apocalyptic zombie wasteland. You speak naturally, showing weariness but also hope. Keep responses brief (1-3 sentences) and in-character. Never break character or mention being an AI.",
                ContextMemory = 10,

                // Response
                ShowTypingIndicator = false,
                TypingDelayMs = 0,
                MaxResponseLength = 300,

                // Actions
                ActionsEnabled = true,
                FollowDistance = 3.0f,
                GuardRadius = 10.0f
            };
        }

        private TTSConfig LoadTTSConfig()
        {
            string configPath = Path.Combine(_modPath, "Config", "ttsconfig.xml");

            if (!File.Exists(configPath))
            {
                Log.Warning($"TTS config file not found at {configPath}, using defaults");
                return GetDefaultTTSConfig();
            }

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(configPath);

                var config = new TTSConfig();

                // Server settings
                var serverNode = doc.SelectSingleNode("//Server");
                if (serverNode != null)
                {
                    config.Enabled = bool.Parse(GetNodeValue(serverNode, "Enabled", "true"));
                    config.Endpoint = GetNodeValue(serverNode, "Endpoint", "http://localhost:5050/synthesize");
                    config.TimeoutSeconds = int.Parse(GetNodeValue(serverNode, "TimeoutSeconds", "10"));
                }

                // Audio settings
                var audioNode = doc.SelectSingleNode("//Audio");
                if (audioNode != null)
                {
                    config.Volume = float.Parse(GetNodeValue(audioNode, "Volume", "0.8"), System.Globalization.CultureInfo.InvariantCulture);
                    config.MaxDistance = float.Parse(GetNodeValue(audioNode, "MaxDistance", "20"), System.Globalization.CultureInfo.InvariantCulture);
                    config.MinDistance = float.Parse(GetNodeValue(audioNode, "MinDistance", "2"), System.Globalization.CultureInfo.InvariantCulture);
                    config.SpeechRate = float.Parse(GetNodeValue(audioNode, "SpeechRate", "1.0"), System.Globalization.CultureInfo.InvariantCulture);
                }

                // Voice settings
                var voicesNode = doc.SelectSingleNode("//Voices");
                if (voicesNode != null)
                {
                    config.DefaultVoice = GetNodeValue(voicesNode, "DefaultVoice", "en_US-lessac-medium");
                    config.TraderVoice = GetNodeValue(voicesNode, "TraderVoice", "en_US-ryan-medium");
                    config.CompanionVoice = GetNodeValue(voicesNode, "CompanionVoice", "en_US-amy-medium");
                    config.BanditVoice = GetNodeValue(voicesNode, "BanditVoice", "en_US-ryan-medium");
                }

                Log.Out($"TTS configuration loaded - Enabled: {config.Enabled}, Default voice: {config.DefaultVoice}");
                return config;
            }
            catch (Exception ex)
            {
                Log.Error($"Error loading TTS config: {ex.Message}");
                return GetDefaultTTSConfig();
            }
        }

        private TTSConfig GetDefaultTTSConfig()
        {
            return new TTSConfig
            {
                Enabled = true,
                Endpoint = "http://localhost:5050/synthesize",
                TimeoutSeconds = 10,
                Volume = 0.8f,
                MaxDistance = 20f,
                MinDistance = 2f,
                SpeechRate = 1.0f,
                DefaultVoice = "en_US-lessac-medium",
                TraderVoice = "en_US-ryan-medium",
                CompanionVoice = "en_US-amy-medium",
                BanditVoice = "en_US-ryan-medium"
            };
        }

        private STTConfig LoadSTTConfig()
        {
            string configPath = Path.Combine(_modPath, "Config", "sttconfig.xml");

            if (!File.Exists(configPath))
            {
                Log.Warning($"STT config file not found at {configPath}, using defaults");
                return GetDefaultSTTConfig();
            }

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(configPath);

                var config = new STTConfig();

                // Server settings
                var serverNode = doc.SelectSingleNode("//Server");
                if (serverNode != null)
                {
                    config.Enabled = bool.Parse(GetNodeValue(serverNode, "Enabled", "true"));
                    config.Endpoint = GetNodeValue(serverNode, "Endpoint", "http://localhost:5051/transcribe");
                    config.TimeoutSeconds = int.Parse(GetNodeValue(serverNode, "TimeoutSeconds", "10"));
                }

                // Audio settings
                var audioNode = doc.SelectSingleNode("//Audio");
                if (audioNode != null)
                {
                    config.SampleRate = int.Parse(GetNodeValue(audioNode, "SampleRate", "16000"));
                    config.MaxRecordingSeconds = int.Parse(GetNodeValue(audioNode, "MaxRecordingSeconds", "15"));
                }

                // Input settings
                var inputNode = doc.SelectSingleNode("//Input");
                if (inputNode != null)
                {
                    config.PushToTalkKey = GetNodeValue(inputNode, "PushToTalkKey", "V");
                }

                // Whisper settings
                var whisperNode = doc.SelectSingleNode("//Whisper");
                if (whisperNode != null)
                {
                    config.Model = GetNodeValue(whisperNode, "Model", "base.en");
                    config.Language = GetNodeValue(whisperNode, "Language", "en");
                }

                Log.Out($"STT configuration loaded - Enabled: {config.Enabled}, Push-to-talk: {config.PushToTalkKey}");
                return config;
            }
            catch (Exception ex)
            {
                Log.Error($"Error loading STT config: {ex.Message}");
                return GetDefaultSTTConfig();
            }
        }

        private STTConfig GetDefaultSTTConfig()
        {
            return new STTConfig
            {
                Enabled = true,
                Endpoint = "http://localhost:5051/transcribe",
                TimeoutSeconds = 10,
                SampleRate = 16000,
                MaxRecordingSeconds = 15,
                PushToTalkKey = "V",
                Model = "base.en",
                Language = "en"
            };
        }
    }
}
