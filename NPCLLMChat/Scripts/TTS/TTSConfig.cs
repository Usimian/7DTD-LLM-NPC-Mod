namespace NPCLLMChat.TTS
{
    /// <summary>
    /// TTS Provider type.
    ///
    /// There is no Windows SAPI option and there cannot be one: System.Speech does not exist in
    /// the Mono runtime the game embeds, so the voice cannot be synthesised inside the process on
    /// any platform. Windows and Linux both talk to the Piper server, which is what
    /// setup_servers.bat and setup_servers.sh build. A "Windows" value used to sit here backed by
    /// a stub whose every method returned "not available".
    /// </summary>
    public enum TTSProvider
    {
        /// <summary>Pick for the platform - which is the Piper server everywhere.</summary>
        Auto,

        /// <summary>Piper TTS HTTP server (cross-platform, requires the server running)</summary>
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
        // Written word -> how to spell it so the synthesizer says it right. Piper guesses
        // pronunciation from spelling and loses on place names: "Tucson" becomes "TUCK-sun".
        public System.Collections.Generic.Dictionary<string, string> Pronunciations { get; }
            = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public string DefaultVoice { get; set; } = "en_US-lessac-medium";
        public string TraderVoice { get; set; } = "en_US-ryan-medium";
        public string CompanionVoice { get; set; } = "en_US-amy-medium";
        public string BanditVoice { get; set; } = "en_US-ryan-medium";
    }
}
