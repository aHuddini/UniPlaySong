using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace UniPlaySong.Audio
{
    // Reads and writes nsf-loops.json in a game's music folder.
    // Schema: { "filename.nsf": <loopSeconds>, ... }.
    // Corrupt or missing files silently produce no overrides — never throws
    // from ReadMillisecondsFor (called from GmeReader on the hot path).
    public static class NsfLoopManifest
    {
        public const string ManifestFileName = "nsf-loops.json";

        // Returns the loop override in milliseconds for the given NSF file,
        // or null when no override applies (no manifest, missing key, corrupt JSON).
        public static int? ReadMillisecondsFor(string nsfPath)
        {
            try
            {
                var directory = Path.GetDirectoryName(nsfPath);
                if (string.IsNullOrEmpty(directory)) return null;

                var manifestPath = Path.Combine(directory, ManifestFileName);
                if (!File.Exists(manifestPath)) return null;

                var json = File.ReadAllText(manifestPath);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                if (dict == null) return null;

                var fileName = Path.GetFileName(nsfPath);
                if (!dict.TryGetValue(fileName, out int seconds)) return null;

                return seconds * 1000;
            }
            catch
            {
                // Any I/O or parse error: silently fall through to default.
                return null;
            }
        }

        // Writes the manifest. Empty overrides dict deletes the manifest entirely
        // (no empty {} artifact). Atomic via temp-file-then-rename.
        public static void Save(string gameFolder, IDictionary<string, int> overrides)
        {
            if (string.IsNullOrEmpty(gameFolder))
                throw new ArgumentException("gameFolder is required", nameof(gameFolder));

            var manifestPath = Path.Combine(gameFolder, ManifestFileName);

            if (overrides == null || overrides.Count == 0)
            {
                if (File.Exists(manifestPath)) File.Delete(manifestPath);
                return;
            }

            var json = JsonConvert.SerializeObject(overrides, Formatting.Indented);
            var tempPath = manifestPath + ".tmp";

            File.WriteAllText(tempPath, json);
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
            File.Move(tempPath, manifestPath);
        }
    }
}
