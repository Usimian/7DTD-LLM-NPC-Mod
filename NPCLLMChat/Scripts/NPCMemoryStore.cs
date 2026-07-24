using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NPCLLMChat
{
    [Serializable]
    public class SavedMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class PlaceVisit
    {
        public string place;
        public int day;
        public string time;
        public int x;
        public int z;
    }

    [Serializable]
    public class NPCMemory
    {
        public string npcName;
        public List<SavedMessage> messages = new List<SavedMessage>();
        public List<PlaceVisit> placesVisited = new List<PlaceVisit>();
    }

    /// <summary>
    /// Persists per-NPC memory (conversation history + travel journal) as JSON in the
    /// current save-game folder, so NPCs remember across game restarts. One file per
    /// NPC entity id; a different save game gets its own folder automatically.
    /// </summary>
    public static class NPCMemoryStore
    {
        private static string MemoryDir => Path.Combine(GameIO.GetSaveGameDir(), "NPCLLMChat");

        private static string FileFor(int entityId) => Path.Combine(MemoryDir, $"npc_{entityId}.json");

        public static NPCMemory Load(int entityId)
        {
            try
            {
                string file = FileFor(entityId);
                if (File.Exists(file))
                {
                    var memory = JsonUtility.FromJson<NPCMemory>(File.ReadAllText(file));
                    if (memory != null)
                    {
                        Log.Out($"[NPCLLMChat] Loaded memory for NPC {entityId}: {memory.messages.Count} messages, {memory.placesVisited.Count} places");
                        return memory;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Failed to load memory for NPC {entityId}: {ex.Message}");
            }
            return null;
        }

        public static void Save(int entityId, NPCMemory memory)
        {
            try
            {
                Directory.CreateDirectory(MemoryDir);
                File.WriteAllText(FileFor(entityId), JsonUtility.ToJson(memory));
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Failed to save memory for NPC {entityId}: {ex.Message}");
            }
        }

        public static void DeleteMessages(int entityId, NPCMemory memory)
        {
            // "Clear conversation" keeps the travel journal - only the chat is forgotten
            memory.messages.Clear();
            Save(entityId, memory);
        }
    }
}
