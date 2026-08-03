namespace NPCLLMChat.STT
{
    /// <summary>
    /// STT Provider type.
    ///
    /// No Windows Speech Recognition option, for the same reason as the TTS side: System.Speech
    /// is absent from the game's Mono runtime, so recognition cannot happen in process on any
    /// platform. Windows and Linux both talk to the Whisper server.
    /// </summary>
    public enum STTProvider
    {
        /// <summary>Pick for the platform - which is the Whisper server everywhere.</summary>
        Auto,

        /// <summary>Whisper STT HTTP server (cross-platform, requires the server running)</summary>
        Whisper
    }

    /// <summary>
    /// Configuration for STT service loaded from sttconfig.xml
    /// </summary>
    public class STTConfig
    {
        /// <summary>
        /// STT provider to use. Auto selects based on platform.
        /// </summary>
        public STTProvider Provider { get; set; } = STTProvider.Auto;

        // Enable/disable STT
        public bool Enabled { get; set; } = true;
        
        // Whisper server settings (Linux / optional on Windows)
        public string Endpoint { get; set; } = "http://localhost:5051/transcribe";
        public int TimeoutSeconds { get; set; } = 10;

        // Audio settings
        public int SampleRate { get; set; } = 16000;
        public int MaxRecordingSeconds { get; set; } = 15;

        // Input settings
        public string PushToTalkKey { get; set; } = "V";

        // Whisper model settings (for Whisper provider)
        public string Model { get; set; } = "base.en";
        public string Language { get; set; } = "en";
    }
}
