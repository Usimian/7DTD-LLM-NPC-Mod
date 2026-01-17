namespace NPCLLMChat.TTS
{
    /// <summary>
    /// Configuration for TTS service loaded from ttsconfig.xml
    /// </summary>
    public class TTSConfig
    {
        // Server settings
        public bool Enabled { get; set; } = true;
        public string Endpoint { get; set; } = "http://localhost:5050/synthesize";
        public int TimeoutSeconds { get; set; } = 10;

        // Audio settings
        public float Volume { get; set; } = 0.8f;
        public float MaxDistance { get; set; } = 20f;
        public float MinDistance { get; set; } = 2f;
        public float SpeechRate { get; set; } = 1.0f;

        // Voice settings
        public string DefaultVoice { get; set; } = "en_US-lessac-medium";
        public string TraderVoice { get; set; } = "en_US-ryan-medium";
        public string CompanionVoice { get; set; } = "en_US-amy-medium";
        public string BanditVoice { get; set; } = "en_US-ryan-medium";
    }
}
