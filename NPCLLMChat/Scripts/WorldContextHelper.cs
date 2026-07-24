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
