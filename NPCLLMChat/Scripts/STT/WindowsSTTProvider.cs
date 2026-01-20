using System;

namespace NPCLLMChat.STT
{
    /// <summary>
    /// Placeholder for Windows STT provider.
    /// 
    /// NOTE: System.Speech is not available in Unity's Mono runtime.
    /// Windows users should use the Whisper STT server instead.
    /// 
    /// To use STT on Windows:
    /// 1. Install Python 3.10+
    /// 2. Create venv and install: pip install faster-whisper flask
    /// 3. Start server: python whisper_server.py --port 5051 --preload
    /// </summary>
    public class WindowsSTTProvider
    {
        private static WindowsSTTProvider _instance;
        public static WindowsSTTProvider Instance => _instance ?? (_instance = new WindowsSTTProvider());

        // Always return false - System.Speech not available in Unity
        public bool IsAvailable => false;
        public bool IsProcessing => false;

        private WindowsSTTProvider()
        {
            Log.Out("[NPCLLMChat] Windows Speech Recognition not available in Unity runtime");
            Log.Out("[NPCLLMChat] Use Whisper STT server instead: python whisper_server.py --port 5051");
        }

        public void Transcribe(byte[] wavData, Action<string> onSuccess, Action<string> onError)
        {
            onError?.Invoke("Windows Speech Recognition not available in Unity. Use Whisper STT server.");
        }

        public string GetProviderInfo()
        {
            return "Not available (use Whisper server)";
        }
    }
}
