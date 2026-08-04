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
        // Places the two of them got close enough to identify but never went into. She is a
        // note taker, not a cartographer: a building she has only squinted at from 300m away
        // never lands here, and she is not told it exists.
        public List<PlaceVisit> placesSeen = new List<PlaceVisit>();
        // Biomes she has personally stood in, and where she crossed into each one. She has no
        // idea what is out past the country the two of them have actually walked.
        public List<PlaceVisit> biomesSeen = new List<PlaceVisit>();

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
        // How the player has treated her, -1 (prickly) to +1 (close). Relationships carry over
        // between sessions; this is the one number that says where the two of them stand.
        public float rapport;
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

        // The copy from before the most recent write. The game keeps .bak beside every save file
        // it owns and SCore does the same; this is the one file the mod exists to protect and it
        // had none.
        private static string BackupFor(string npcName) => FileFor(npcName) + ".bak";

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returning null here means the caller starts her blank and the next save writes that
        /// blank over whatever was on disk - so a file that exists but will not parse must never
        /// simply fail. It is set aside under its own name, the backup is tried, and only a save
        /// with genuinely nothing in it returns null.
        /// </summary>
        public static NPCMemory Load(string npcName)
        {
            string file = FileFor(npcName);
            if (!File.Exists(file))
            {
                try { MigrateLegacyFile(npcName, file); }
                catch (Exception ex) { Log.Warning($"Legacy memory migration failed for {npcName}: {ex.Message}"); }
            }

            var memory = ReadOrNull(file);
            if (memory != null)
            {
                Log.Out($"Loaded memory for {npcName}: {memory.messages.Count} messages, {memory.placesVisited.Count} places");
                return memory;
            }

            // It is there and unreadable. Move it out of the way rather than let it be
            // overwritten - a half-written file is still most of her, and hand-recoverable.
            if (File.Exists(file)) Quarantine(file);

            memory = ReadOrNull(BackupFor(npcName));
            if (memory != null)
            {
                Log.Warning($"Recovered {npcName} from backup: {memory.messages.Count} messages, " +
                            $"{memory.placesVisited.Count} places. The main file would not parse and has been " +
                            "kept alongside it.");
                return memory;
            }

            return null;
        }

        private static NPCMemory ReadOrNull(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<NPCMemory>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not read {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        private static void Quarantine(string file)
        {
            try
            {
                string kept = file + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(file, kept);
                Log.Warning($"Set aside unreadable memory as {Path.GetFileName(kept)}");
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not set aside {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        /// <summary>
        /// Written to one side and swapped into place, so an interrupted write cannot leave a
        /// half-file where her memory used to be. The displaced copy becomes the backup.
        /// </summary>
        public static void Save(string npcName, NPCMemory memory)
        {
            try
            {
                Directory.CreateDirectory(MemoryDir);
                string file = FileFor(npcName);
                string temp = file + ".tmp";

                File.WriteAllText(temp, JsonConvert.SerializeObject(memory));

                if (File.Exists(file)) File.Replace(temp, file, BackupFor(npcName));
                else File.Move(temp, file);
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to save memory for {npcName}: {ex.Message}");
            }
        }

        public static void DeleteMessages(string npcName, NPCMemory memory)
        {
            // "Clear conversation" keeps the travel journal - only the chat is forgotten
            memory.messages.Clear();
            Save(npcName, memory);
        }

        /// <summary>
        /// The backup goes with it. Leaving one behind would let Load resurrect a memory that had
        /// been deliberately folded into another and deleted - the rename and hire paths both do
        /// exactly that.
        /// </summary>
        public static void DeleteFile(string npcName)
        {
            string file = FileFor(npcName);
            foreach (string path in new[] { file, BackupFor(npcName), file + ".tmp" })
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception ex) { Log.Warning($"Could not delete {Path.GetFileName(path)}: {ex.Message}"); }
            }
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
            Log.Out($"Migrated legacy memory {Path.GetFileName(newest)} -> {Path.GetFileName(targetFile)} ({matches.Count} legacy file(s) removed)");
        }
    }
}
