using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Applies a Quick Start profile to the live settings object, and answers what the current state
    // is relative to the profile that was applied.
    //
    // Reflection rather than a hand-written setter per profile: the profile already declares the keys
    // it owns, so snapshot, apply and drift-detection can all be driven from that one list. Adding a
    // key to a profile then needs no matching code anywhere else.
    public class QuickStartService
    {
        private readonly FileLogger _fileLogger;

        // Undo buffer for the most recent apply — the owned keys as they were immediately before.
        // Lives for the settings session; a profile you cannot back out of is a trap.
        private Dictionary<string, object> _undoValues;
        private string _undoProfileId;

        public QuickStartService(FileLogger fileLogger = null)
        {
            _fileLogger = fileLogger;
        }

        public bool CanUndo => _undoValues != null && _undoValues.Count > 0;

        public string UndoProfileName => QuickStartProfiles.ById(_undoProfileId)?.Name;

        // Writes the profile's owned keys and nothing else. installedOnly and playThroughGames are
        // the two page-level checkboxes, applied on top of whichever tile was chosen.
        public bool Apply(
            UniPlaySongSettings settings,
            QuickStartProfile profile,
            JukeboxSource jukeboxSource,
            bool installedOnly,
            bool playThroughGames,
            bool addReverb)
        {
            if (settings == null || profile == null) return false;

            try
            {
                var values = QuickStartProfiles.IsJukebox(profile)
                    ? QuickStartProfiles.WithJukeboxSource(profile, jukeboxSource)
                    : new Dictionary<string, object>(profile.Values);

                values[QuickStartProfiles.InstalledOnlyKey] = installedOnly;

                // Only meaningful with radio: RadioPlaysThroughGames governs whether the radio keeps
                // going during a game session, and there is no radio to keep going otherwise.
                if (QuickStartProfiles.IsJukebox(profile))
                    values[QuickStartProfiles.PlayThroughGamesKey] = playThroughGames;

                foreach (var kv in QuickStartProfiles.ReverbValues(addReverb))
                    values[kv.Key] = kv.Value;

                // Every profile turns default music ON, so it has to point at a source that can
                // actually produce sound. If the user's current source needs something they have not
                // supplied — a file, a folder, a rotation list — fall back to the bundled preset
                // rather than enabling a fallback that is itself empty.
                if (!QuickStartProfiles.DefaultSourceIsUsable(settings))
                {
                    foreach (var kv in QuickStartProfiles.BundledPresetFallback())
                        values[kv.Key] = kv.Value;
                    _fileLogger?.Info(
                        $"QuickStart: default music source '{settings.DefaultMusicSourceOption}' is not configured — " +
                        "falling back to the bundled preset so the profile has something to play.");
                }

                _undoValues = Snapshot(settings, values.Keys);
                _undoProfileId = settings.ActiveQuickStartProfile;

                int written = 0;
                foreach (var kv in values)
                {
                    if (TrySet(settings, kv.Key, kv.Value)) written++;
                }

                settings.ActiveQuickStartProfile = profile.Id;
                _fileLogger?.Info($"QuickStart: applied '{profile.Name}' ({profile.Id}) — {written}/{values.Count} settings written");
                return true;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"QuickStart: failed to apply '{profile?.Id}': {ex.Message}", ex);
                return false;
            }
        }

        // Restores the values captured by the last Apply, including whichever profile was active
        // before it.
        public bool Undo(UniPlaySongSettings settings)
        {
            if (settings == null || !CanUndo) return false;

            try
            {
                foreach (var kv in _undoValues)
                    TrySet(settings, kv.Key, kv.Value);

                settings.ActiveQuickStartProfile = _undoProfileId ?? string.Empty;
                _fileLogger?.Info($"QuickStart: undo restored {_undoValues.Count} settings");
                _undoValues = null;
                _undoProfileId = null;
                return true;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"QuickStart: undo failed: {ex.Message}", ex);
                return false;
            }
        }

        // True when the active profile's owned keys no longer all match what it applied — which is
        // what lets the page say "Hover Preview (modified)" rather than claiming a clean profile.
        public bool IsModified(UniPlaySongSettings settings, JukeboxSource jukeboxSource, bool installedOnly, bool playThroughGames, bool addReverb)
        {
            var profile = QuickStartProfiles.ById(settings?.ActiveQuickStartProfile);
            if (profile == null) return false;

            var expected = QuickStartProfiles.IsJukebox(profile)
                ? QuickStartProfiles.WithJukeboxSource(profile, jukeboxSource)
                : new Dictionary<string, object>(profile.Values);

            expected[QuickStartProfiles.InstalledOnlyKey] = installedOnly;
            if (QuickStartProfiles.IsJukebox(profile))
                expected[QuickStartProfiles.PlayThroughGamesKey] = playThroughGames;

            foreach (var kv in QuickStartProfiles.ReverbValues(addReverb))
                expected[kv.Key] = kv.Value;

            foreach (var kv in expected)
            {
                var prop = Find(kv.Key);
                if (prop == null) continue;
                var current = prop.GetValue(settings);
                if (!Equals(current, kv.Value)) return true;
            }
            return false;
        }

        private static Dictionary<string, object> Snapshot(UniPlaySongSettings settings, IEnumerable<string> keys)
        {
            var snap = new Dictionary<string, object>();
            foreach (var key in keys)
            {
                var prop = Find(key);
                if (prop != null && prop.CanRead)
                    snap[key] = prop.GetValue(settings);
            }
            return snap;
        }

        // Skips silently rather than throwing when a key does not resolve: a profile naming a
        // property that no longer exists should cost that one setting, not the whole apply.
        private bool TrySet(UniPlaySongSettings settings, string key, object value)
        {
            var prop = Find(key);
            if (prop == null || !prop.CanWrite)
            {
                _fileLogger?.Debug($"QuickStart: skipped '{key}' — no writable property");
                return false;
            }

            try
            {
                // Profile literals are ints/doubles/bools/enums; convert to whatever the property
                // actually declares so a 0.3 double lands on a double, an enum on its enum type.
                var target = prop.PropertyType;
                object converted = value;
                if (value != null && !target.IsInstanceOfType(value))
                {
                    converted = target.IsEnum
                        ? Enum.ToObject(target, value)
                        : Convert.ChangeType(value, Nullable.GetUnderlyingType(target) ?? target);
                }
                prop.SetValue(settings, converted);
                return true;
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"QuickStart: could not set '{key}': {ex.Message}");
                return false;
            }
        }

        private static PropertyInfo Find(string name) =>
            typeof(UniPlaySongSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    }
}
