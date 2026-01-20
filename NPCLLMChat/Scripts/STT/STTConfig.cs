namespace NPCLLMChat.STT
{
    /// <summary>
    /// STT Provider type
    /// </summary>
    public enum STTProvider
    {
        /// <summary>
        /// Auto-detect based on platform:
        /// - Windows: Use Windows Speech Recognition (built-in)
        /// - Linux: Use Whisper HTTP server
        /// </summary>
        Auto,
        
        /// <summary>Windows built-in Speech Recognition (Windows only)</summary>
        Windows,
        
        /// <summary>Whisper STT HTTP server (cross-platform, requires server)</summary>
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
