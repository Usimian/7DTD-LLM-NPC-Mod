using System;

namespace NPCLLMChat.TTS
{
    /// <summary>
    /// Placeholder for Windows TTS provider.
    /// 
    /// NOTE: System.Speech is not available in Unity's Mono runtime.
    /// Windows users should use the Piper TTS server instead.
    /// 
    /// To use TTS on Windows:
    /// 1. Install Python 3.10+
    /// 2. Run: pip install piper-tts flask
    /// 3. Start server: python piper_server.py --port 5050
    /// </summary>
    public class WindowsTTSProvider
    {
        private static WindowsTTSProvider _instance;
        public static WindowsTTSProvider Instance => _instance ?? (_instance = new WindowsTTSProvider());

        // Always return false - System.Speech not available in Unity
        public bool IsAvailable => false;
        public string[] AvailableVoices => new string[0];

        private WindowsTTSProvider()
        {
            Log.Out("[NPCLLMChat] Windows SAPI TTS not available in Unity runtime");
            Log.Out("[NPCLLMChat] Use Piper TTS server instead: python piper_server.py --port 5050");
        }

        public void Synthesize(string text, string voice, float speechRate, Action<byte[]> onSuccess, Action<string> onError)
        {
            onError?.Invoke("Windows SAPI not available in Unity. Use Piper TTS server.");
        }

        public string GetVoiceInfo()
        {
            return "Not available (use Piper server)";
        }
    }
}
