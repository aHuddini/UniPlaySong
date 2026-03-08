using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UniPlaySong.Services
{
    public class BundledPresetInfo
    {
        [JsonProperty("file")]
        public string File { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("artist")]
        public string Artist { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("license")]
        public string License { get; set; }

        // Display name for UI: "Artist - Title"
        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
    }

    public static class BundledPresetService
    {
        private static List<BundledPresetInfo> _presets;
        private static string _presetsDirectory;

        // Loads presets from the DefaultMusic folder inside the extension install directory.
        // Call once at startup with the extension install path.
        public static void Initialize(string extensionInstallPath)
        {
            _presetsDirectory = Path.Combine(extensionInstallPath, "DefaultMusic");
            _presets = null; // Reset cache so next access reloads
        }

        public static List<BundledPresetInfo> GetPresets()
        {
            if (_presets != null) return _presets;

            _presets = new List<BundledPresetInfo>();

            if (string.IsNullOrEmpty(_presetsDirectory) || !Directory.Exists(_presetsDirectory))
                return _presets;

            var manifestPath = Path.Combine(_presetsDirectory, "presets.json");
            if (!System.IO.File.Exists(manifestPath))
                return _presets;

            try
            {
                var json = System.IO.File.ReadAllText(manifestPath);
                var loaded = JsonConvert.DeserializeObject<List<BundledPresetInfo>>(json);
                if (loaded != null)
                {
                    // Only include presets whose files actually exist
                    _presets = loaded
                        .Where(p => !string.IsNullOrWhiteSpace(p.File)
                                 && System.IO.File.Exists(Path.Combine(_presetsDirectory, p.File)))
                        .ToList();
                }
            }
            catch
            {
                // Silently fail — presets are optional
            }

            return _presets;
        }

        // Resolves a preset filename to its full path, or null if not found
        public static string ResolvePresetPath(string presetFilename)
        {
            if (string.IsNullOrWhiteSpace(presetFilename) || string.IsNullOrEmpty(_presetsDirectory))
                return null;

            var path = Path.Combine(_presetsDirectory, presetFilename);
            return System.IO.File.Exists(path) ? path : null;
        }

        // Gets the first available preset filename, or empty string if none
        public static string GetDefaultPresetFilename()
        {
            var presets = GetPresets();
            return presets.Count > 0 ? presets[0].File : string.Empty;
        }
    }
}
