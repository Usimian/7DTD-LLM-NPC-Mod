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
        /// Name of the building she is standing in, or null out in the open.
        ///
        /// Town POIs sit flush against each other - Drive-Up Coffee shares its north edge with
        /// Bobcats Bar - so a generous margin plus first-match-wins put her in the wrong shop.
        /// Anything whose footprint really contains her beats anything that merely almost does,
        /// and the tightest footprint wins, since that is the building rather than its lot.
        /// </summary>
        public static string GetPOINameAt(Vector3 pos)
        {
            var pois = GetPOIs();
            if (pois == null) return null;

            PrefabInstance best = null;
            float bestArea = float.MaxValue;
            bool bestContains = false;

            const float margin = 8f;
            foreach (var poi in pois)
            {
                Vector3 min = poi.boundingBoxPosition;
                Vector3 size = poi.boundingBoxSize;

                bool contains = pos.x >= min.x && pos.x <= min.x + size.x &&
                                pos.z >= min.z && pos.z <= min.z + size.z;
                bool nearly = pos.x >= min.x - margin && pos.x <= min.x + size.x + margin &&
                              pos.z >= min.z - margin && pos.z <= min.z + size.z + margin;
                if (!contains && !nearly) continue;

                // a real hit always beats a doorstep hit, whatever their sizes
                if (bestContains && !contains) continue;

                float area = size.x * size.z;
                if (contains && !bestContains || area < bestArea)
                {
                    best = poi;
                    bestArea = area;
                    bestContains = contains;
                }
            }

            return best == null ? null : PoiName(best);
        }

        /// <summary>
        /// A place as she can actually perceive it from where she is standing. Name is null for
        /// anything too far off to identify: she can see a building is out there and roughly how
        /// big it is, and that is all she gets until the two of them walk over and look.
        /// </summary>
        public class PoiSighting
        {
            public string Name;
            public int X;
            public int Z;
            public int Distance;
            public string Direction;
            public string Size;

            /// <summary>"Pop-N-Pills Mini-Mart, about 60m NE" / "something big, about 240m NE".</summary>
            public string Describe()
            {
                string what = Name ?? $"something {Size}";
                return $"{what}, about {Distance}m {Direction}";
            }
        }

        /// <summary>
        /// How far she can pick a building out of the landscape right now. Clear daylight carries
        /// a long way; night, fog and heavy weather close it right down. She is observant, not
        /// equipped with a satellite.
        /// </summary>
        public static float VisualRange(Vector3 pos)
        {
            float range = 300f;

            int day; string time;
            GetGameDayTime(out day, out time);
            int hour = 12;
            if (!string.IsNullOrEmpty(time) && time.Length >= 2) int.TryParse(time.Substring(0, 2), out hour);
            if (hour >= 21 || hour < 5) range = Mathf.Min(range, 120f);

            try
            {
                var biome = GameManager.Instance?.World?.GetBiome((int)pos.x, (int)pos.z);
                var weather = biome != null ? WeatherManager.Instance?.FindBiomeWeather(biome.m_BiomeType) : null;
                if ((weather?.FogPercent() ?? 0f) > 0.55f) range = Mathf.Min(range, 100f);

                float rain = WeatherManager.Instance?.GetCurrentRainfallPercent() ?? 0f;
                float snow = WeatherManager.Instance?.GetCurrentSnowfallPercent() ?? 0f;
                if (rain > 0.45f || snow > 0.45f) range = Mathf.Min(range, 180f);
            }
            catch (Exception ex)
            {
                Log.Warning($"Visibility check failed: {ex.Message}");
            }

            return range;
        }

        /// <summary>
        /// Everything she can see from here, nearest first. Inside readRange she is close enough
        /// to read the sign on the front and knows what the place is; past that she gets a shape
        /// and a bearing and nothing more. Beyond visualRange it is not there as far as she is
        /// concerned - she does not get to read the map over the player's shoulder.
        /// </summary>
        public static List<PoiSighting> LookAround(Vector3 pos, float readRange, float visualRange)
        {
            var seen = new List<PoiSighting>();
            var pois = GetPOIs();
            if (pois == null || pois.Count == 0) return seen;

            foreach (var poi in pois)
            {
                Vector3 min = poi.boundingBoxPosition;
                Vector3 size = poi.boundingBoxSize;
                float cx = min.x + size.x / 2f;
                float cz = min.z + size.z / 2f;

                // How far to the building, not to the middle of it. Measuring to the centre put
                // her 30m from a motel she was standing inside, and read the sign on a big POI
                // only once she had walked halfway across its lot.
                float dist = DistanceToFootprint(pos, min, size);
                if (dist > visualRange) continue;

                seen.Add(new PoiSighting
                {
                    Name = dist <= readRange ? PoiName(poi) : null,
                    X = Mathf.RoundToInt(cx),
                    Z = Mathf.RoundToInt(cz),
                    Distance = Mathf.RoundToInt(dist),
                    Direction = CompassDir(cx - pos.x, cz - pos.z),
                    Size = DescribeBulk(size.x * size.z)
                });
            }

            seen.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return seen;
        }

        /// <summary>
        /// Ground distance from a point to the nearest edge of a footprint, zero when the point
        /// is inside it. Direction still comes off the centre, since "north-east" means the
        /// building as a whole rather than its closest corner.
        /// </summary>
        private static float DistanceToFootprint(Vector3 pos, Vector3 min, Vector3 size)
        {
            float dx = Mathf.Max(min.x - pos.x, 0f, pos.x - (min.x + size.x));
            float dz = Mathf.Max(min.z - pos.z, 0f, pos.z - (min.z + size.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Footprint area to a word, since bulk is the one thing readable at a distance.</summary>
        private static string DescribeBulk(float area)
        {
            if (area > 4000f) return "huge";
            if (area > 1200f) return "big";
            if (area > 300f) return "middling";
            return "small";
        }

        /// <summary>
        /// The game's own id for the biome underfoot ("pine_forest", "burnt_forest"). Stored
        /// rather than the display name so the hazard lookup keeps working in any language.
        /// </summary>
        public static string BiomeIdAt(Vector3 pos)
        {
            try
            {
                var biome = GameManager.Instance?.World?.GetBiome((int)pos.x, (int)pos.z);
                return biome?.m_sBiomeName;
            }
            catch (Exception ex)
            {
                Log.Warning($"Biome lookup failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>"pine_forest" as a person would say it.</summary>
        public static string BiomeLabel(string biomeId)
        {
            return string.IsNullOrEmpty(biomeId) ? biomeId : Prettify(biomeId.Replace('_', ' '));
        }

        /// <summary>
        /// What that country does to someone dressed wrong, or null where ordinary clothes are
        /// fine. This is the whole reason she cares which biome is which.
        /// </summary>
        public static string BiomeHazard(string biomeId)
        {
            if (string.IsNullOrEmpty(biomeId)) return null;
            string id = biomeId.ToLowerInvariant();
            if (id.Contains("snow")) return "cold";
            if (id.Contains("wasteland") || id.Contains("radiated")) return "heat, and the air itself is foul";
            if (id.Contains("desert") || id.Contains("burnt")) return "heat";
            return null;   // forest, pine forest, plains, city - nothing special needed
        }

        /// <summary>
        /// How well someone is protected against cold and heat, totalled over everything the
        /// game counts - worn clothing, food, perks and buffs alike.
        ///
        /// These are the effects armour actually grants: HypothermalResist against the cold,
        /// HyperthermalResist against the heat. CoreTempGain and CoreTempLoss are in the enum
        /// but nothing in the game's config grants them, so reading those returned a confident
        /// zero for a player in full winter gear.
        /// </summary>
        public static void GetThermalProtection(EntityAlive who, out float cold, out float heat)
        {
            cold = 0f;
            heat = 0f;
            if (who == null) return;
            try
            {
                cold = EffectManager.GetValue(PassiveEffects.HypothermalResist, null, 0f, who);
                heat = EffectManager.GetValue(PassiveEffects.HyperthermalResist, null, 0f, who);
            }
            catch (Exception ex)
            {
                Log.Warning($"Thermal protection read failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Every building she could plausibly recognise.
        ///
        /// NOT GetPOIPrefabs() - that list is built with AddPrefab(pi, prefab.HasQuestTag()), so
        /// it only holds POIs the trader can send you to. Drive-Up Coffee carries no quest tag,
        /// which made every coffee shop, remnant and small business invisible to her while the
        /// motel next door was not. The full list is the right source; the filter is whether the
        /// game gives the prefab a display name, which is exactly the line between a building
        /// with a sign on it and a driveway, a road tile or an empty lot.
        /// </summary>
        private static List<PrefabInstance> GetPOIs()
        {
            try
            {
                if (_poiCache != null && Time.unscaledTime - _poiCacheTime < PoiCacheSeconds)
                    return _poiCache;

                var world = GameManager.Instance?.World;
                var decorator = world?.ChunkCache?.ChunkProvider?.GetDynamicPrefabDecorator();
                var all = decorator?.GetDynamicPrefabs();
                if (all == null) return _poiCache;

                var named = new List<PrefabInstance>();
                foreach (var poi in all)
                {
                    if (HasDisplayName(poi)) named.Add(poi);
                }

                _poiCache = named;
                _poiCacheTime = Time.unscaledTime;
                Log.Out($"POI cache rebuilt: {named.Count} named buildings of {all.Count} prefabs");
                return _poiCache;
            }
            catch (Exception ex)
            {
                Log.Warning($"POI lookup failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// True when the game has a name for this prefab. Localization.Get echoes the key back
        /// when there is no entry, so "part_driveway_countrytown_02" fails and "Drive-Up Coffee"
        /// passes - and a modded POI with a proper name passes too, with nothing to maintain.
        /// </summary>
        private static bool HasDisplayName(PrefabInstance poi)
        {
            string prefabName = poi?.prefab?.PrefabName;
            if (string.IsNullOrEmpty(prefabName)) return false;

            // rwg_tile_* are the world-generation blocks a town is assembled from, and they are
            // named ("Rural", "Country Town") despite being terrain rather than anywhere. They
            // enclose every real building, so they won containment everywhere and she reported
            // standing in "Rural" while she was on the doorstep of Mason Farms.
            if (prefabName.StartsWith("rwg_tile_", StringComparison.OrdinalIgnoreCase)) return false;

            string localized = poi.prefab.LocalizedName;
            return !string.IsNullOrEmpty(localized) && localized != prefabName;
        }

        /// <summary>
        /// Human-readable inventory summary of item stacks: "120 x 9mm Round, 12 x First Aid
        /// Bandage", biggest stacks first, truncated past maxItems. Null when everything is empty.
        /// </summary>
        public static string SummarizeStacks(IEnumerable<ItemStack> slots)
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
            // Everything, never a "and 43 other kinds of item" tail: the list is sorted by count,
            // so any cap hides exactly the rare thing worth asking her about - one piece of
            // nuclear material sitting behind 184 raw meat.
            var parts = new List<string>();
            foreach (var entry in sorted)
            {
                parts.Add($"{entry.Value} x {entry.Key}");
            }
            return string.Join(", ", parts);
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
                Log.Warning($"Quest lookup failed: {ex.Message}");
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

            return string.Join(", ", parts);
        }

        /// <summary>
        /// What is actually wrong with the player, and what it means. A bare buff name tells her
        /// nothing worth saying - "Deadly Radiation" buried at the end of a list of stats read as
        /// scenery - so each affliction she can recognise comes with the treatment attached.
        /// </summary>
        /// <summary>
        /// Something wrong with the player, with what fixes it attached. The cure is the point:
        /// an affliction she can only describe is something to nag about, whereas one that names
        /// a bottle and a shop is a thing the two of them can go and do together.
        /// </summary>
        public class Affliction
        {
            public string Label;         // "Infection", as the HUD calls it
            public string Meaning;       // what it does and what settles it
            public string[] CureItems;   // fragments of item names that would fix it
            public bool FoundInTown;     // whether a chemist or hospital is where you look
        }

        public static List<Affliction> GetAfflictions(EntityPlayer player)
        {
            var found = new List<Affliction>();
            if (player?.Buffs?.ActiveBuffs == null) return found;

            var seen = new HashSet<string>();
            foreach (var buff in player.Buffs.ActiveBuffs)
            {
                var buffClass = buff.BuffClass;
                if (buffClass == null || buffClass.Hidden || !buffClass.ShowOnHUD) continue;

                string raw = buffClass.Name ?? "";      // BuffClass lowercases this at load
                var affliction = Classify(raw);
                if (affliction == null) continue;       // a perk or food bonus, not an affliction

                affliction.Label = string.IsNullOrEmpty(buffClass.LocalizedName) ? raw : buffClass.LocalizedName;
                if (seen.Add(affliction.Label)) found.Add(affliction);
            }
            return found;
        }

        /// <summary>
        /// Empty string for an affliction with nothing to be done about it, null for a buff that
        /// is not an affliction at all (perks, food bonuses, being drunk on moonshine).
        /// </summary>
        private static Affliction Classify(string buffName)
        {
            if (buffName.Contains("radiation") || buffName.Contains("radiated"))
                return Ail("he is standing in a radiated zone and it is burning through him. No pill fixes this - " +
                           "the ONLY cure is to walk out of the zone, back toward the middle of the map. " +
                           "Say so straight away, this one kills people");
            if (buffName.Contains("bleeding") || buffName.Contains("laceration") || buffName.Contains("abrasion"))
                return Ail("he is bleeding and needs a bandage on it",
                           new[] { "bandage", "first aid" });
            if (buffName.Contains("infection"))
                return Ail("an infection, and it climbs on its own. Antibiotics, or honey if there are none",
                           new[] { "antibiotic", "honey" }, inTown: true);
            if (buffName.Contains("dysentery"))
                return Ail("dysentery from bad food or water - goldenrod tea will settle it",
                           new[] { "goldenrod" });
            if (buffName.Contains("legbroken") || buffName.Contains("armbroken") || buffName.Contains("brokenlimb"))
                return Ail("a broken bone - that wants a splint or a cast before he walks it off",
                           new[] { "splint", "cast" }, inTown: true);
            if (buffName.Contains("sprained"))
                return Ail("a sprain - a splint and a bandage, and he should take it easy",
                           new[] { "splint", "bandage" });
            if (buffName.Contains("concussion"))
                return Ail("a concussion, which is why he cannot see straight",
                           new[] { "first aid kit" }, inTown: true);
            if (buffName.Contains("elementcold") || buffName.Contains("hypo"))
                return Ail("he is freezing - warmer clothes, a fire, or get indoors");
            if (buffName.Contains("elementhot") || buffName.Contains("heat"))
                return Ail("he is overheating - shade, water, less armour");
            if (buffName.Contains("crippled") || buffName.Contains("slow"))
                return Ail("his leg is wrecked and he is barely moving",
                           new[] { "splint", "cast" }, inTown: true);
            if (buffName.Contains("stunned") || buffName.Contains("knockdown") || buffName.Contains("unconscious"))
                return Ail("");  // she can see it, and there is nothing to hand him for it
            if (buffName.Contains("sprain") || buffName.Contains("injury"))
                return Ail("");
            return null;
        }

        private static Affliction Ail(string meaning, string[] cures = null, bool inTown = false)
        {
            return new Affliction
            {
                Meaning = meaning,
                CureItems = cures ?? new string[0],
                FoundInTown = inTown
            };
        }

        /// <summary>
        /// Whether a place she has written down is somewhere you would go looking for medicine.
        /// Matched on the name the game shows, so Pop-N-Pills and the Urgent Care are caught
        /// without a hand-kept list of prefabs, and a modded chemist comes along for free.
        /// </summary>
        public static bool IsMedicalPlace(string placeName)
        {
            if (string.IsNullOrEmpty(placeName)) return false;
            string name = placeName.ToLowerInvariant();
            foreach (string word in new[] { "pop-n-pills", "pop n pills", "pharmacy", "hospital",
                                            "urgent care", "clinic", "medical", "drug" })
            {
                if (name.Contains(word)) return true;
            }
            return false;
        }

        /// <summary>
        /// True when any of these name fragments is sitting in the given slots. Deliberately
        /// narrow: it answers "is the cure here", not "what is he carrying".
        /// </summary>
        public static bool CarriesAnyOf(IEnumerable<ItemStack> slots, string[] fragments)
        {
            if (slots == null || fragments == null || fragments.Length == 0) return false;
            foreach (var stack in slots)
            {
                if (stack == null || stack.IsEmpty()) continue;
                var itemClass = stack.itemValue?.ItemClass;
                if (itemClass == null) continue;

                string name = itemClass.GetLocalizedItemName();
                if (string.IsNullOrEmpty(name)) name = itemClass.GetItemName();
                if (string.IsNullOrEmpty(name)) continue;
                name = name.ToLowerInvariant();

                foreach (string fragment in fragments)
                {
                    if (name.Contains(fragment)) return true;
                }
            }
            return false;
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
                Log.Warning($"Surroundings lookup failed: {ex.Message}");
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
                Log.Warning($"Nearby people lookup failed: {ex.Message}");
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

        /// <summary>
        /// What a place is actually called - "Pop-N-Pills Mini-Mart", not "store pharmacy 01".
        /// The game keeps a display name per prefab and shows it on the compass, so she should
        /// use the same words the player is reading. Localization.Get hands the key straight
        /// back when there is no entry for it, which is how a nameless POI is detected.
        /// </summary>
        private static string PoiName(PrefabInstance poi)
        {
            if (poi == null) return null;

            var prefab = poi.prefab;
            if (prefab != null)
            {
                string prefabName = prefab.PrefabName;
                if (!string.IsNullOrEmpty(prefabName))
                {
                    string localized = prefab.LocalizedName;
                    return !string.IsNullOrEmpty(localized) && localized != prefabName
                        ? localized
                        : CleanName(prefabName);
                }
            }

            // world-generated instances carry a ".17" suffix the player never sees
            string raw = poi.name ?? "";
            int dot = raw.LastIndexOf('.');
            if (dot > 0) raw = raw.Substring(0, dot);
            return CleanName(raw);
        }
    }
}
