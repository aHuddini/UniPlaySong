using Newtonsoft.Json;
using System;
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

        // v1.5.0: session-scoped random pick used when RandomizeBundledTrackOnStartup
        // is enabled. Picked once at first access (per Playnite session) and held
        // across game switches so the ambient track stays consistent. Cleared on
        // Initialize() — i.e. on Playnite startup — so each new session re-randomizes.
        private static string _sessionRandomPresetFilename;
        // Seed from Guid so consecutive Playnite restarts (which can happen within the
        // same Environment.TickCount window) don't roll the same seed and pick the same
        // preset. new Random() defaults to TickCount which collides on fast restarts.
        private static readonly Random _random = new Random(Guid.NewGuid().GetHashCode());

        // Callback for persisting settings after a fresh random pick. Wired up by
        // UniPlaySong during construction so the service stays decoupled from the
        // plugin/Playnite SDK. Without persistence, LastRandomizedBundledPreset
        // wouldn't survive Playnite restart, defeating the anti-repeat logic.
        private static Action<UniPlaySongSettings> _persistSettingsCallback;
        public static void SetPersistSettingsCallback(Action<UniPlaySongSettings> callback)
        {
            _persistSettingsCallback = callback;
        }

        // Loads presets from the DefaultMusic folder inside the extension install directory.
        // Call once at startup with the extension install path.
        public static void Initialize(string extensionInstallPath)
        {
            _presetsDirectory = Path.Combine(extensionInstallPath, "DefaultMusic");
            _presets = null; // Reset cache so next access reloads
            _sessionRandomPresetFilename = null; // Reset session random pick — new session, fresh roll
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

        // v1.5.0: returns the effective preset filename for the current session.
        // When RandomizeBundledTrackOnStartup is on, picks one preset randomly the
        // FIRST time this is called per session and caches it for the rest of the
        // session — so the user gets variety across Playnite restarts but consistent
        // ambient music within a single session. When the flag is off, returns the
        // user's manually-picked SelectedBundledPreset.
        //
        // Falls back to SelectedBundledPreset if the random pool is empty or the
        // pick fails for any reason — defensive default.
        public static string GetEffectivePresetFilename(UniPlaySongSettings settings)
        {
            if (settings == null) return null;

            if (!settings.RandomizeBundledTrackOnStartup)
            {
                return settings.SelectedBundledPreset;
            }

            // Random mode is on. Lazily pick once per session.
            if (_sessionRandomPresetFilename != null)
            {
                return _sessionRandomPresetFilename;
            }

            var presets = GetPresets();
            if (presets.Count == 0)
            {
                // No presets available — fall back to the manual pick (which may also be empty).
                return settings.SelectedBundledPreset;
            }

            // Avoid repeating the previous session's pick when more than one preset
            // is available. This makes "random" feel actually random to the user —
            // without it, a 6-preset pool has ~17% chance of repeat each session.
            string previousPick = settings.LastRandomizedBundledPreset;
            List<BundledPresetInfo> candidates;
            if (!string.IsNullOrEmpty(previousPick) && presets.Count > 1)
            {
                candidates = presets.Where(p => !string.Equals(p.File, previousPick, StringComparison.OrdinalIgnoreCase)).ToList();
                if (candidates.Count == 0) candidates = presets; // Shouldn't happen but be safe
            }
            else
            {
                candidates = presets;
            }

            _sessionRandomPresetFilename = candidates[_random.Next(candidates.Count)].File;
            // Persist for next session's repeat-avoidance. Setter fires PropertyChanged
            // but the change isn't user-meaningful so the diff handler ignores it.
            settings.LastRandomizedBundledPreset = _sessionRandomPresetFilename;
            try { _persistSettingsCallback?.Invoke(settings); } catch { /* persistence best-effort */ }
            return _sessionRandomPresetFilename;
        }

        // v1.5.0: clears the cached session random pick so the next call to
        // GetEffectivePresetFilename re-rolls. Called by the settings-change handler
        // when RandomizeBundledTrackOnStartup is toggled so each toggle ON gets a
        // fresh roll (instead of reusing a stale cached pick from earlier in the
        // session).
        public static void ResetSessionRandomPick()
        {
            _sessionRandomPresetFilename = null;
        }
    }
}
