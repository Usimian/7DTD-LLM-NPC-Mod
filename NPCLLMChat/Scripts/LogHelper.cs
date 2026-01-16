using UnityEngine;

namespace NPCLLMChat
{
    /// <summary>
    /// Logging helper that wraps Unity's Debug logging for 7DTD compatibility
    /// </summary>
    public static class Log
    {
        private const string PREFIX = "[NPCLLMChat] ";

        public static void Out(string message)
        {
            Debug.Log(PREFIX + message);
        }

        public static void Warning(string message)
        {
            Debug.LogWarning(PREFIX + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(PREFIX + message);
        }
    }
}
