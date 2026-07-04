using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UniPlaySong.Services
{
    public class BundledJingleInfo
    {
        [JsonProperty("file")]
        public string File { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("artist")]
        public string Artist { get; set; }

        [JsonProperty("console")]
        public string Console { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("license")]
        public string License { get; set; }

        // Optional category tag: "celebration" (default, fanfare for Completed/Beaten)
        // or "abandoned" (resigned jingles for the Abandoned status trigger).
        // Missing/empty category is treated as "celebration" for backward compatibility.
        [JsonProperty("category")]
        public string Category { get; set; }

        // Display: "Game - Song Title (Console)" e.g. "Sonic the Hedgehog 3 - Act Complete (Sega Genesis)"
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
                return string.IsNullOrWhiteSpace(Console) ? name : $"{name} ({Console})";
            }
        }
    }

    public static class BundledJingleService
    {
        private const string CategoryCelebration = "celebration";
        private const string CategoryAbandoned = "abandoned";
        private const string CategoryAchievement = "achievement";

        private static List<BundledJingleInfo> _jingles;
        private static string _jinglesDirectory;

        // Call once at startup with the extension install path
        public static void Initialize(string extensionInstallPath)
        {
            _jinglesDirectory = Path.Combine(extensionInstallPath, "Jingles");
            _jingles = null;
        }

        // Returns all jingles in the "celebration" category (the default — fanfare for
        // Completed/Beaten). Jingles without an explicit category tag are treated as
        // celebration for backward compatibility with pre-v1.4.1 manifests.
        public static List<BundledJingleInfo> GetJingles()
        {
            return GetAllJingles()
                .Where(j => string.IsNullOrWhiteSpace(j.Category)
                         || string.Equals(j.Category, CategoryCelebration, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Returns jingles tagged as "abandoned" — the parallel set used by the
        // Abandoned-status trigger (resigned / game-over themed tracks).
        public static List<BundledJingleInfo> GetAbandonedJingles()
        {
            return GetAllJingles()
                .Where(j => string.Equals(j.Category, CategoryAbandoned, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Returns jingles tagged "achievement" — the bundled "Default" achievement/trophy pack
        // (master default + platinum), shown in the achievement-sound pickers.
        public static List<BundledJingleInfo> GetAchievementJingles()
        {
            return GetAllJingles()
                .Where(j => string.Equals(j.Category, CategoryAchievement, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // The master/default achievement sound filename ("Trophy Notif"). Falls back to the first
        // achievement jingle, then empty, if the expected file is missing.
        public const string DefaultAchievementJingle = "Achievements/Trophy_Notif.mp3";

        // Full path to the bundled PA Starter Pack sound for a rarity (common|uncommon|rare|
        // ultrarare|capstone), or null if the file is missing. Used as the pack default and as the
        // per-rarity fallback for the Theme and Custom packs.
        public static string GetPAStarterPackPath(string rarity)
        {
            var r = (rarity ?? string.Empty).Trim().ToLowerInvariant();
            switch (r)
            {
                case "common":
                case "uncommon":
                case "rare":
                case "ultrarare":
                case "capstone":
                    return ResolveJinglePath($"Achievements/PAStarterPack/{r}.mp3");
                default:
                    return null;
            }
        }

        private static List<BundledJingleInfo> GetAllJingles()
        {
            if (_jingles != null) return _jingles;

            _jingles = new List<BundledJingleInfo>();

            if (string.IsNullOrEmpty(_jinglesDirectory) || !Directory.Exists(_jinglesDirectory))
                return _jingles;

            var manifestPath = Path.Combine(_jinglesDirectory, "jingles.json");
            if (!System.IO.File.Exists(manifestPath))
                return _jingles;

            try
            {
                var json = System.IO.File.ReadAllText(manifestPath);
                var loaded = JsonConvert.DeserializeObject<List<BundledJingleInfo>>(json);
                if (loaded != null)
                {
                    _jingles = loaded
                        .Where(j => !string.IsNullOrWhiteSpace(j.File)
                                 && System.IO.File.Exists(Path.Combine(_jinglesDirectory, j.File)))
                        .ToList();
                }
            }
            catch
            {
                // Silently fail — jingles are optional
            }

            return _jingles;
        }

        // Resolves a jingle filename to its full path, or null if not found
        public static string ResolveJinglePath(string jingleFilename)
        {
            if (string.IsNullOrWhiteSpace(jingleFilename) || string.IsNullOrEmpty(_jinglesDirectory))
                return null;

            var path = Path.Combine(_jinglesDirectory, jingleFilename);
            return System.IO.File.Exists(path) ? path : null;
        }

        // Gets the first available celebration jingle filename, or empty string if none
        public static string GetDefaultJingleFilename()
        {
            var jingles = GetJingles();
            return jingles.Count > 0 ? jingles[0].File : string.Empty;
        }

        // Gets the first available abandoned jingle filename, or empty string if none
        public static string GetDefaultAbandonedJingleFilename()
        {
            var jingles = GetAbandonedJingles();
            return jingles.Count > 0 ? jingles[0].File : string.Empty;
        }
    }
}
