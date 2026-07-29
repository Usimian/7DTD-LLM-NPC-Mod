using System;
using System.Collections.Generic;
using UnityEngine;

namespace NPCLLMChat
{
    /// <summary>
    /// Read-only queries about the game world (time, POIs) used to ground NPC
    /// conversations in reality. All failures degrade to "don't know" - never throw.
    /// </summary>
    public static class WorldContextHelper
    {
        private static List<PrefabInstance> _poiCache;
        private static float _poiCacheTime = -9999f;
        private const float PoiCacheSeconds = 300f;

        public static void GetGameDayTime(out int day, out string time)
        {
            day = 0;
            time = "unknown";
            var world = GameManager.Instance?.World;
            if (world == null) return;

            ulong worldTime = world.worldTime;
            day = GameUtils.WorldTimeToDays(worldTime);
            int hours = GameUtils.WorldTimeToHours(worldTime);
            int minutes = GameUtils.WorldTimeToMinutes(worldTime);
            time = $"{hours:D2}:{minutes:D2}";
        }

        /// <summary>
        /// Name of the POI whose footprint contains this position, or null in the wild.
        /// </summary>
        public static string GetPOINameAt(Vector3 pos)
        {
            var pois = GetPOIs();
            if (pois == null) return null;

            const float margin = 8f;
            foreach (var poi in pois)
            {
                Vector3 min = poi.boundingBoxPosition;
                Vector3 size = poi.boundingBoxSize;
                if (pos.x >= min.x - margin && pos.x <= min.x + size.x + margin &&
                    pos.z >= min.z - margin && pos.z <= min.z + size.z + margin)
                {
                    return CleanName(poi.name);
                }
            }
            return null;
        }

        /// <summary>
        /// Human-readable list of the closest POIs, e.g. "house old ranch 01 (about 240m NE)".
        /// </summary>
        public static string DescribeNearbyPOIs(Vector3 pos, int maxCount, float radius)
        {
            var pois = GetPOIs();
            if (pois == null || pois.Count == 0) return null;

            var withDistance = new List<KeyValuePair<float, PrefabInstance>>();
            foreach (var poi in pois)
            {
                Vector3 min = poi.boundingBoxPosition;
                Vector3 size = poi.boundingBoxSize;
                Vector3 center = new Vector3(min.x + size.x / 2f, 0f, min.z + size.z / 2f);
                float dist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(center.x, center.z));
                if (dist < radius)
                {
                    withDistance.Add(new KeyValuePair<float, PrefabInstance>(dist, poi));
                }
            }
            if (withDistance.Count == 0) return null;

            withDistance.Sort((a, b) => a.Key.CompareTo(b.Key));

            var parts = new List<string>();
            for (int i = 0; i < withDistance.Count && parts.Count < maxCount; i++)
            {
                var poi = withDistance[i].Value;
                float dist = withDistance[i].Key;
                Vector3 min = poi.boundingBoxPosition;
                Vector3 size = poi.boundingBoxSize;
                string dir = CompassDir(min.x + size.x / 2f - pos.x, min.z + size.z / 2f - pos.z);
                parts.Add($"{CleanName(poi.name)} (about {Mathf.RoundToInt(dist)}m {dir})");
            }
            return string.Join(", ", parts);
        }

        private static List<PrefabInstance> GetPOIs()
        {
            try
            {
                if (_poiCache != null && Time.unscaledTime - _poiCacheTime < PoiCacheSeconds)
                    return _poiCache;

                var world = GameManager.Instance?.World;
                var decorator = world?.ChunkCache?.ChunkProvider?.GetDynamicPrefabDecorator();
                _poiCache = decorator?.GetPOIPrefabs();
                _poiCacheTime = Time.unscaledTime;
                return _poiCache;
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] POI lookup failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Human-readable inventory summary of item stacks: "120 x 9mm Round, 12 x First Aid
        /// Bandage", biggest stacks first, truncated past maxItems. Null when everything is empty.
        /// </summary>
        public static string SummarizeStacks(IEnumerable<ItemStack> slots, int maxItems = 20)
        {
            if (slots == null) return null;
            var totals = new Dictionary<string, int>();
            foreach (var stack in slots)
            {
                if (stack == null || stack.IsEmpty()) continue;
                var itemClass = stack.itemValue?.ItemClass;
                if (itemClass == null) continue;
                string name = itemClass.GetLocalizedItemName();
                if (string.IsNullOrEmpty(name)) name = itemClass.GetItemName();
                name = Prettify(name);
                totals.TryGetValue(name, out int count);
                totals[name] = count + stack.count;
            }
            if (totals.Count == 0) return null;

            var sorted = new List<KeyValuePair<string, int>>(totals);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            for (int i = 0; i < sorted.Count && i < maxItems; i++)
            {
                parts.Add($"{sorted[i].Value} x {sorted[i].Key}");
            }
            string summary = string.Join(", ", parts);
            if (sorted.Count > maxItems)
            {
                summary += $" and {sorted.Count - maxItems} other kinds of item";
            }
            return summary;
        }

        /// <summary>
        /// Jobs the player is carrying: accepted-but-unstarted, in progress, or ready to hand
        /// back in, with who gave it and where it is from the NPC's position. Null when the
        /// journal is empty, so a companion who knows of no work says nothing about work.
        /// </summary>
        public static string DescribeQuests(EntityPlayer player, Vector3 fromPos)
        {
            try
            {
                var journal = player?.QuestJournal;
                if (journal?.quests == null || journal.quests.Count == 0) return null;

                var lines = new List<string>();
                foreach (var quest in journal.quests)
                {
                    if (quest == null) continue;
                    if (quest.CurrentState == Quest.QuestState.Completed ||
                        quest.CurrentState == Quest.QuestState.Failed) continue;

                    string state = quest.CurrentState == Quest.QuestState.NotStarted ? "accepted but not started yet"
                        : quest.CurrentState == Quest.QuestState.ReadyForTurnIn ? "done, just needs handing in"
                        : "underway";

                    string name = quest.QuestClass?.Name;
                    if (string.IsNullOrEmpty(name)) name = quest.ID ?? "a job";

                    string from = TraderNameById(quest.QuestGiverID);
                    string where = "";
                    if (quest.GetPositionData(out Vector3 pos, Quest.PositionDataTypes.POIPosition) &&
                        (pos.x != 0f || pos.z != 0f))
                    {
                        string poi = GetPOINameAt(pos);
                        where = $", at {(string.IsNullOrEmpty(poi) ? "map position " + (int)pos.x + " E/W, " + (int)pos.z + " N/S" : poi)}" +
                                $" - {DescribeRelative(fromPos, pos.x, pos.z)}";
                    }

                    lines.Add($"- \"{name}\"{(from == null ? "" : $" from {from}")} ({state}){where}");
                }
                return lines.Count == 0 ? null : string.Join("\n", lines);
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Quest lookup failed: {ex.Message}");
                return null;
            }
        }

        private static string TraderNameById(int entityId)
        {
            if (entityId <= 0) return null;
            var entity = GameManager.Instance?.World?.GetEntity(entityId) as EntityAlive;
            if (entity == null) return null;
            string name = entity.EntityName ?? "";
            if (name.StartsWith("npcTrader")) name = name.Substring("npcTrader".Length);
            return string.IsNullOrEmpty(name) ? null : $"trader {name}";
        }

        /// <summary>
        /// The player's condition as a companion reads it at a glance: health/food/water
        /// bands (no exact HUD numbers) plus visible status effects by name.
        /// </summary>
        public static string DescribePlayerCondition(EntityPlayer player)
        {
            if (player == null || player.Stats == null) return null;

            var parts = new List<string>
            {
                Band(player.Stats.Health, "looking healthy", "a bit banged up", "in rough shape", "critically hurt"),
                Band(player.Stats.Food, "well fed", "could use a meal", "properly hungry", "starving"),
                Band(player.Stats.Water, "well hydrated", "could use a drink", "properly thirsty", "badly dehydrated")
            };

            var ailments = new List<string>();
            if (player.Buffs?.ActiveBuffs != null)
            {
                foreach (var buff in player.Buffs.ActiveBuffs)
                {
                    var buffClass = buff.BuffClass;
                    if (buffClass == null || buffClass.Hidden || !buffClass.ShowOnHUD) continue;
                    ailments.Add(string.IsNullOrEmpty(buffClass.LocalizedName) ? buffClass.Name : buffClass.LocalizedName);
                }
            }

            string condition = string.Join(", ", parts);
            if (ailments.Count > 0)
            {
                condition += ". Conditions you can see on them: " + string.Join(", ", ailments);
            }
            return condition;
        }

        /// <summary>
        /// NPC-only items have no localization entry, so their raw ids reach the prompt
        /// ("gunNPCM60", "ammoNPC9mmBulletBall"). Turn those into words she can say.
        /// </summary>
        private static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOf(' ') >= 0) return name;

            string s = name;
            foreach (string prefix in new[] { "gunNPC", "ammoNPC", "meleeNPC", "gun", "ammo", "melee", "food", "drink", "medical" })
            {
                if (s.StartsWith(prefix, StringComparison.Ordinal) && s.Length > prefix.Length)
                {
                    s = s.Substring(prefix.Length);
                    break;
                }
            }
            // split camelCase / digit boundaries: "9mmBulletBall" -> "9mm Bullet Ball"
            s = System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z0-9])(?=[A-Z])", " ");
            return s.Trim();
        }

        private static string Band(Stat stat, string high, string mid, string low, string critical)
        {
            float pct = stat != null ? stat.ValuePercentUI : 1f;
            if (pct >= 0.85f) return high;
            if (pct >= 0.55f) return mid;
            if (pct >= 0.25f) return low;
            return critical;
        }

        /// <summary>
        /// Everything she can see and feel standing there: the biome, the temperature, what is
        /// falling out of the sky and how hard the wind is blowing. Real values, so she stops
        /// guessing at weather she is standing in.
        /// </summary>
        public static string DescribeSurroundings(Vector3 pos)
        {
            try
            {
                var world = GameManager.Instance?.World;
                var biome = world?.GetBiome((int)pos.x, (int)pos.z);

                var parts = new List<string>();
                if (biome != null)
                {
                    string name = string.IsNullOrEmpty(biome.LocalizedName) ? biome.m_sBiomeName : biome.LocalizedName;
                    if (!string.IsNullOrEmpty(name)) parts.Add($"{name.ToLowerInvariant()} biome");
                }

                float temperature = WeatherManager.GetTemperature();
                string howItFeels = temperature < 32f ? "freezing"
                    : temperature < 50f ? "cold"
                    : temperature < 75f ? "mild"
                    : temperature < 95f ? "hot"
                    : "brutally hot";
                parts.Add($"{Mathf.RoundToInt(temperature)} degrees and {howItFeels}");

                float rain = WeatherManager.Instance?.GetCurrentRainfallPercent() ?? 0f;
                float snow = WeatherManager.Instance?.GetCurrentSnowfallPercent() ?? 0f;
                float wind = WeatherManager.GetWindSpeed();
                float clouds = WeatherManager.GetCloudThickness();

                var biomeWeather = biome != null ? WeatherManager.Instance?.FindBiomeWeather(biome.m_BiomeType) : null;
                float fog = biomeWeather?.FogPercent() ?? 0f;
                if (fog > 0.55f) parts.Add("thick fog, you cannot see far at all");
                else if (fog > 0.3f) parts.Add("hazy with fog");

                if (snow > 0.5f) parts.Add("heavy snow falling");
                else if (snow > 0.1f) parts.Add("light snow");
                else if (rain > 0.5f) parts.Add("rain hammering down");
                else if (rain > 0.1f) parts.Add("light rain");
                else if (clouds > 0.66f) parts.Add("heavy overcast");
                else if (clouds > 0.33f) parts.Add("cloudy");
                else parts.Add("clear sky");

                if (wind > 0.6f) parts.Add("the wind is howling");
                else if (wind > 0.3f) parts.Add("a stiff breeze");

                string line = string.Join(", ", parts);

                // Biome storms are their own thing: a scheduled event with a build-up phase
                // (stormState 1) before it lands (stormState 2), not just heavy rain.
                int stormState = biomeWeather?.stormState ?? 0;
                if (stormState >= 2)
                {
                    line += ". A BIOME STORM is on you right now - this is the dangerous kind, get under cover";
                }
                else if (stormState == 1)
                {
                    line += ". A biome storm is building and will hit shortly - worth finding shelter before it does";
                }
                return line;
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Surroundings lookup failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The other people in sight. She has eyes, so a survivor standing twenty metres away
        /// is something she can see and remark on - traders behind their counters, allies the
        /// player has already hired, and strangers who are standing around waiting to be.
        /// Null when the two of you are alone, so she never invents company.
        /// </summary>
        public static string DescribePeopleNearby(Vector3 from, int selfId, int playerId, float radius = 45f)
        {
            try
            {
                var world = GameManager.Instance?.World;
                if (world == null) return null;

                var lines = new List<string>();
                foreach (var entity in world.Entities.list)
                {
                    if (!(entity is EntityAlive alive) || alive.IsDead()) continue;
                    if (alive.entityId == selfId || alive.entityId == playerId) continue;
                    if (!NPCLLMChatMod.IsNPC(alive)) continue;

                    float dist = Vector3.Distance(from, alive.position);
                    if (dist > radius) continue;

                    string where = dist < 8f
                        ? "right next to you"
                        : $"{Mathf.RoundToInt(dist)}m {CompassDir(alive.position.x - from.x, alive.position.z - from.z)}";

                    // A vanilla trader is the shopkeeper; an SCore NPC derives from EntityTrader
                    // too, so the type name has to settle which one this is.
                    bool isSDX = alive.GetType().Name.Contains("SDX");
                    if (alive is EntityTrader && !isSDX)
                    {
                        lines.Add($"- {PersonName(alive)}, the trader, {where}");
                        continue;
                    }

                    bool hired = alive.Buffs != null &&
                                 ((alive.Buffs.HasCustomVar("Leader") && alive.Buffs.GetCustomVar("Leader") > 0f) ||
                                  (alive.Buffs.HasCustomVar("Owner") && alive.Buffs.GetCustomVar("Owner") > 0f));
                    lines.Add(hired
                        ? $"- {PersonName(alive)}, working for the player like you are, {where}"
                        : $"- {PersonName(alive)}, a survivor standing around loose - not hired by anyone, {where}");
                }

                return lines.Count == 0 ? null : string.Join("\n", lines);
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] Nearby people lookup failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Entity names come through as "npcWhiteRiverGuard" or "WastelandRaider5Axe".</summary>
        public static string PersonName(EntityAlive entity)
        {
            string name = entity?.EntityName;
            if (string.IsNullOrEmpty(name)) return "someone";
            if (name.StartsWith("npcTrader")) name = name.Substring("npcTrader".Length);
            else if (name.StartsWith("npc")) name = name.Substring("npc".Length);
            return Prettify(name);
        }

        /// <summary>
        /// Distance and compass direction from an observer to a point, phrased for the
        /// prompt: "about 420m NE of you", or "right here" when on top of it.
        /// </summary>
        public static string DescribeRelative(Vector3 from, float x, float z)
        {
            float dist = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(x, z));
            if (dist < 40f) return "right here";
            return $"about {Mathf.RoundToInt(dist)}m {CompassDir(x - from.x, z - from.z)} of you";
        }

        // World +Z = map north, +X = map east
        private static string CompassDir(float dx, float dz)
        {
            string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            float angle = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;
            return dirs[Mathf.RoundToInt(angle / 45f) % 8];
        }

        private static string CleanName(string raw)
        {
            return string.IsNullOrEmpty(raw) ? raw : raw.Replace('_', ' ');
        }
    }
}
