namespace NPCLLMChat.TTS
{
    /// <summary>
    /// TTS Provider type
    /// </summary>
    public enum TTSProvider
    {
        /// <summary>
        /// Auto-detect based on platform:
        /// - Windows: Use Windows SAPI (built-in)
        /// - Linux: Use Piper HTTP server
        /// </summary>
        Auto,
        
        /// <summary>Windows built-in SAPI (Windows only)</summary>
        Windows,
        
        /// <summary>Piper TTS HTTP server (cross-platform, requires server)</summary>
        Piper
    }

    /// <summary>
    /// Configuration for TTS service loaded from ttsconfig.xml
    /// </summary>
    public class TTSConfig
    {
        /// <summary>
        /// TTS provider to use. Auto selects based on platform.
        /// </summary>
        public TTSProvider Provider { get; set; } = TTSProvider.Auto;

        // Enable/disable TTS
        public bool Enabled { get; set; } = true;
        
        // Piper server settings (Linux / optional on Windows)
        public string Endpoint { get; set; } = "http://localhost:5050/synthesize";
        public int TimeoutSeconds { get; set; } = 10;

        // Audio settings
        public float Volume { get; set; } = 0.8f;
        public float MaxDistance { get; set; } = 20f;
        public float MinDistance { get; set; } = 2f;
        public float SpeechRate { get; set; } = 1.0f;

        // Voice settings
        // On Windows: "male" or "female" selects appropriate Windows voice
        // On Linux: Use Piper voice IDs like "en_US-lessac-medium"
        public string DefaultVoice { get; set; } = "en_US-lessac-medium";
        public string TraderVoice { get; set; } = "en_US-ryan-medium";
        public string CompanionVoice { get; set; } = "en_US-amy-medium";
        public string BanditVoice { get; set; } = "en_US-ryan-medium";
    }
}
