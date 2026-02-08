using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using Newtonsoft.Json;

namespace UniPlaySong.Services
{
    // Per-game cache for Dynamic visualizer colors extracted from background images.
    // Persists to JSON so colors survive across sessions without re-extraction.
    public class DynamicColorCache
    {
        private const string CacheFileName = "dynamic_colors.json";
        // Bump this when the extraction algorithm changes to invalidate stale entries
        private const int AlgorithmVersion = 8;
        private readonly string _cacheFilePath;
        private readonly object _lock = new object();
        private Dictionary<string, CachedGameColors> _cache;

        public DynamicColorCache(string extensionDataPath)
        {
            _cacheFilePath = Path.Combine(extensionDataPath, CacheFileName);
            Load();
        }

        public bool TryGetColors(string gameId, string imagePathHash,
            int minBriBottom, int minBriTop, int minSatBottom, int minSatTop, int algoVariant,
            out Color bottom, out Color top)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(gameId, out var entry)
                    && entry.PathHash == imagePathHash
                    && entry.Version == AlgorithmVersion
                    && entry.Algo == algoVariant
                    && entry.MBB == minBriBottom && entry.MBT == minBriTop
                    && entry.MSB == minSatBottom && entry.MST == minSatTop)
                {
                    bottom = Color.FromArgb(255, entry.BR, entry.BG, entry.BB);
                    top = Color.FromArgb(255, entry.TR, entry.TG, entry.TB);
                    return true;
                }
            }
            bottom = default;
            top = default;
            return false;
        }

        public void SetColors(string gameId, string imagePathHash,
            int minBriBottom, int minBriTop, int minSatBottom, int minSatTop, int algoVariant,
            Color bottom, Color top)
        {
            lock (_lock)
            {
                _cache[gameId] = new CachedGameColors
                {
                    Version = AlgorithmVersion,
                    Algo = algoVariant,
                    PathHash = imagePathHash,
                    BR = bottom.R, BG = bottom.G, BB = bottom.B,
                    TR = top.R, TG = top.G, TB = top.B,
                    MBB = minBriBottom, MBT = minBriTop,
                    MSB = minSatBottom, MST = minSatTop
                };
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
                Save();
            }
        }

        public void Save()
        {
            try
            {
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(_cacheFilePath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var tempPath = _cacheFilePath + ".tmp";
                    var json = JsonConvert.SerializeObject(_cache, Formatting.Indented);
                    File.WriteAllText(tempPath, json);

                    if (File.Exists(_cacheFilePath))
                        File.Delete(_cacheFilePath);
                    File.Move(tempPath, _cacheFilePath);
                }
            }
            catch
            {
                // Cache save failure is non-critical — colors will be re-extracted next session
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = File.ReadAllText(_cacheFilePath);
                    _cache = JsonConvert.DeserializeObject<Dictionary<string, CachedGameColors>>(json);
                }
            }
            catch
            {
                // Corrupted cache — start fresh
            }

            if (_cache == null)
                _cache = new Dictionary<string, CachedGameColors>();
        }
    }

    // Compact cache entry — color data + path hash + algo variant + tuning params for invalidation
    public class CachedGameColors
    {
        [JsonProperty("v")]
        public int Version { get; set; }
        [JsonProperty("a")]
        public int Algo { get; set; }      // 0=v6 simple, 1=v7 advanced, 2=v7+vivid
        [JsonProperty("ph")]
        public string PathHash { get; set; }
        [JsonProperty("br")]
        public byte BR { get; set; }
        [JsonProperty("bg")]
        public byte BG { get; set; }
        [JsonProperty("bb")]
        public byte BB { get; set; }
        [JsonProperty("tr")]
        public byte TR { get; set; }
        [JsonProperty("tg")]
        public byte TG { get; set; }
        [JsonProperty("tb")]
        public byte TB { get; set; }
        [JsonProperty("mbb")]
        public int MBB { get; set; }   // min brightness bottom
        [JsonProperty("mbt")]
        public int MBT { get; set; }   // min brightness top
        [JsonProperty("msb")]
        public int MSB { get; set; }   // min saturation bottom (%)
        [JsonProperty("mst")]
        public int MST { get; set; }   // min saturation top (%)
    }
}
