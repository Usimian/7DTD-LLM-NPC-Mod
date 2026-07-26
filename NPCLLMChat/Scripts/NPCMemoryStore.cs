using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

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
    public class MarkedPlace
    {
        public string label;
        public string poi;
        public int day;
        public string time;
        public int x;
        public int z;
    }

    [Serializable]
    public class CargoSnapshot
    {
        public string name;    // "Motorcycle", "supply drone", "storage at Trader Rekt"
        public int day;        // in-game day the contents were last seen
        public string time;
        public string summary; // "120 x 9mm Round, 12 x First Aid Bandage"
        public int x;          // where it was standing when last seen
        public int z;
    }

    [Serializable]
    public class NPCMemory
    {
        public string npcName;
        public List<SavedMessage> messages = new List<SavedMessage>();
        public List<PlaceVisit> placesVisited = new List<PlaceVisit>();

        // Hand-written character sheet (backstory, mannerisms, fears). The mod only
        // READS this - edit it directly in the JSON file; the summarizer never touches it.
        public string persona;
        // Distilled facts from conversation that scrolled out of the context window
        public string longTermMemory;
        // Expired messages awaiting the next summarization pass
        public List<SavedMessage> pendingSummary = new List<SavedMessage>();
        // Locations the player explicitly asked the NPC to remember
        public List<MarkedPlace> markedPlaces = new List<MarkedPlace>();
        // Last-seen contents of the player's vehicles, drone, and storage containers
        public List<CargoSnapshot> cargoSnapshots = new List<CargoSnapshot>();
        // Where the NPC actually was the last time it was loaded, refreshed as it moves.
        // The travel journal only records POI arrivals, which loses NPCs between landmarks.
        public int lastSeenX;
        public int lastSeenZ;
        public int lastSeenDay;
        public string lastSeenTime;
    }

    /// <summary>
    /// Persists per-NPC memory (conversation history + travel journal) as JSON in the
    /// current save-game folder, so NPCs remember across game restarts. Keyed by NPC
    /// name, not entity id: despawned/respawned/rehired NPCs keep the same identity as
    /// long as they keep the same name. NPCs sharing a name share one memory.
    /// </summary>
    public static class NPCMemoryStore
    {
        private static string MemoryDir => Path.Combine(GameIO.GetSaveGameDir(), "NPCLLMChat");

        private static string FileFor(string npcName) => Path.Combine(MemoryDir, $"npc_{Sanitize(npcName)}.json");

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return sb.ToString();
        }

        public static NPCMemory Load(string npcName)
        {
            try
            {
                string file = FileFor(npcName);
                if (!File.Exists(file))
                {
                    MigrateLegacyFile(npcName, file);
                }
                if (File.Exists(file))
                {
                    var memory = JsonConvert.DeserializeObject<NPCMemory>(File.ReadAllText(file));
                    if (memory != null)
                    {
                        Log.Out($"[NPCLLMChat] Loaded memory for {npcName}: {memory.messages.Count} messages, {memory.placesVisited.Count} places");
                        return memory;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Failed to load memory for {npcName}: {ex.Message}");
            }
            return null;
        }

        public static void Save(string npcName, NPCMemory memory)
        {
            try
            {
                Directory.CreateDirectory(MemoryDir);
                File.WriteAllText(FileFor(npcName), JsonConvert.SerializeObject(memory));
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Failed to save memory for {npcName}: {ex.Message}");
            }
        }

        public static void DeleteMessages(string npcName, NPCMemory memory)
        {
            // "Clear conversation" keeps the travel journal - only the chat is forgotten
            memory.messages.Clear();
            Save(npcName, memory);
        }

        public static void DeleteFile(string npcName)
        {
            try
            {
                string file = FileFor(npcName);
                if (File.Exists(file)) File.Delete(file);
            }
            catch { }
        }

        /// <summary>
        /// Adopt memory written by the earlier entity-id-keyed format (npc_12345.json):
        /// the newest file whose stored npcName matches becomes this NPC's memory, and
        /// all matching legacy files are removed.
        /// </summary>
        private static void MigrateLegacyFile(string npcName, string targetFile)
        {
            if (!Directory.Exists(MemoryDir)) return;

            string newest = null;
            DateTime newestTime = DateTime.MinValue;
            var matches = new List<string>();

            foreach (string file in Directory.GetFiles(MemoryDir, "npc_*.json"))
            {
                string stem = Path.GetFileNameWithoutExtension(file).Substring(4);
                if (!long.TryParse(stem, out _)) continue; // only id-keyed legacy files

                try
                {
                    var memory = JsonConvert.DeserializeObject<NPCMemory>(File.ReadAllText(file));
                    if (memory?.npcName == npcName)
                    {
                        matches.Add(file);
                        DateTime written = File.GetLastWriteTimeUtc(file);
                        if (written > newestTime)
                        {
                            newestTime = written;
                            newest = file;
                        }
                    }
                }
                catch { }
            }

            if (newest == null) return;

            File.Copy(newest, targetFile);
            foreach (string file in matches)
            {
                File.Delete(file);
            }
            Log.Out($"[NPCLLMChat] Migrated legacy memory {Path.GetFileName(newest)} -> {Path.GetFileName(targetFile)} ({matches.Count} legacy file(s) removed)");
        }
    }
}
