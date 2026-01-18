namespace NPCLLMChat.STT
{
    /// <summary>
    /// Configuration for STT service loaded from sttconfig.xml
    /// </summary>
    public class STTConfig
    {
        // Server settings
        public bool Enabled { get; set; } = true;
        public string Endpoint { get; set; } = "http://localhost:5051/transcribe";
        public int TimeoutSeconds { get; set; } = 10;

        // Audio settings
        public int SampleRate { get; set; } = 16000;
        public int MaxRecordingSeconds { get; set; } = 15;

        // Input settings
        public string PushToTalkKey { get; set; } = "V";

        // Whisper settings
        public string Model { get; set; } = "base.en";
        public string Language { get; set; } = "en";
    }
}
