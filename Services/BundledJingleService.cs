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
        private static List<BundledJingleInfo> _jingles;
        private static string _jinglesDirectory;

        // Call once at startup with the extension install path
        public static void Initialize(string extensionInstallPath)
        {
            _jinglesDirectory = Path.Combine(extensionInstallPath, "Jingles");
            _jingles = null;
        }

        public static List<BundledJingleInfo> GetJingles()
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

        // Gets the first available jingle filename, or empty string if none
        public static string GetDefaultJingleFilename()
        {
            var jingles = GetJingles();
            return jingles.Count > 0 ? jingles[0].File : string.Empty;
        }
    }
}
