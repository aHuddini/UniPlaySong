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

        // Whether a pre-profile baseline exists. Read from the SETTINGS rather than a field: the
        // baseline is persisted, so it survives a Playnite restart. Holding it in memory meant a
        // restart between two applies made the second capture the first profile's values, and
        // "Reset to my settings" then restored a profile rather than the user's own configuration.
        public bool HasBaseline(UniPlaySongSettings settings) =>
            !string.IsNullOrWhiteSpace(settings?.QuickStartOriginalSettings);

        // Union of every key any profile can touch, plus the page-level modifiers. Restoring has to
        // cover the whole surface rather than just the last profile's keys — otherwise switching
        // from a tile that owns EnableMusic to one that does not would leave that key stranded.
        private static IEnumerable<string> AllOwnedKeys()
        {
            var keys = new HashSet<string>();
            foreach (var p in QuickStartProfiles.All)
                foreach (var k in p.Values.Keys)
                    keys.Add(k);
            keys.Add(QuickStartProfiles.InstalledOnlyKey);
            keys.Add(QuickStartProfiles.PlayThroughGamesKey);
            foreach (var k in QuickStartProfiles.ReverbValues(true).Keys) keys.Add(k);
            return keys;
        }

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

                if (QuickStartProfiles.IsJukebox(profile))
                {
                    // Only meaningful with radio: RadioPlaysThroughGames governs whether the radio
                    // keeps going during a game session, and there is no radio to keep going
                    // otherwise.
                    values[QuickStartProfiles.PlayThroughGamesKey] = playThroughGames;

                    // installed-only is deliberately NOT applied to Jukebox. In the radio branch,
                    // MusicOnlyForInstalledGames makes radio YIELD to any installed game that has
                    // its own music ("RadioMode: yielding to installed game ..."), which breaks the
                    // one thing this tile promises — a mix that does not stop. The checkbox is a
                    // per-game qualifier and Jukebox has no per-game playback to qualify.
                    values[QuickStartProfiles.InstalledOnlyKey] = false;
                }
                else if (!values.ContainsKey(QuickStartProfiles.InstalledOnlyKey))
                {
                    // Only fill this in when the profile has not already decided. Huddini Showcase
                    // declares installed-only as part of what it is, so an unticked page checkbox
                    // must not quietly undo it.
                    values[QuickStartProfiles.InstalledOnlyKey] = installedOnly;
                }

                // The reverb checkbox only fills in keys the profile has not already decided.
                // Huddini Showcase turns Live Effects on with its own style preset as part of what
                // it is; an unticked checkbox must not silently switch that back off.
                foreach (var kv in QuickStartProfiles.ReverbValues(addReverb))
                {
                    if (!values.ContainsKey(kv.Key))
                        values[kv.Key] = kv.Value;
                }

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

                // Captured once, before the first profile is ever applied, across the FULL owned
                // surface, and PERSISTED — so the baseline survives a restart and "Reset to my
                // settings" returns to what the user had rather than to whichever tile they
                // happened to apply first.
                if (!HasBaseline(settings))
                {
                    var baseline = Snapshot(settings, AllOwnedKeys());
                    settings.QuickStartOriginalSettings =
                        Newtonsoft.Json.JsonConvert.SerializeObject(baseline);
                    _fileLogger?.Info($"QuickStart: captured a {baseline.Count}-setting baseline before the first profile");
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

        // Puts every profile-owned setting back to what the user had before the first profile was
        // applied, and clears the active profile so no tile is marked as current. Distinct from
        // Undo, which steps back exactly one apply: this is "forget I touched Quick Start".
        //
        // Deliberately NOT a factory reset — it restores the USER's settings, not UPS defaults, and
        // touches only the keys profiles can write. Volume, tool paths and pause rules are as
        // untouched here as they are during an apply.
        public bool RestoreOriginal(UniPlaySongSettings settings)
        {
            if (settings == null || !HasBaseline(settings)) return false;

            try
            {
                var baseline = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<Dictionary<string, object>>(settings.QuickStartOriginalSettings);
                if (baseline == null) return false;

                foreach (var kv in baseline)
                    TrySet(settings, kv.Key, kv.Value);

                settings.ActiveQuickStartProfile = string.Empty;

                // Cleared so the next apply captures a fresh baseline from whatever the user has
                // now — otherwise a stale one would keep dragging them back to a state they have
                // already deliberately moved on from.
                settings.QuickStartOriginalSettings = string.Empty;

                // A further undo would put back a profile the user has just asked to forget.
                _undoValues = null;
                _undoProfileId = null;

                _fileLogger?.Info($"QuickStart: restored {baseline.Count} settings to their pre-profile values");
                return true;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"QuickStart: restore failed: {ex.Message}", ex);
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

            if (QuickStartProfiles.IsJukebox(profile))
            {
                expected[QuickStartProfiles.PlayThroughGamesKey] = playThroughGames;
                expected[QuickStartProfiles.InstalledOnlyKey] = false;
            }
            else if (!expected.ContainsKey(QuickStartProfiles.InstalledOnlyKey))
            {
                expected[QuickStartProfiles.InstalledOnlyKey] = installedOnly;
            }

            foreach (var kv in QuickStartProfiles.ReverbValues(addReverb))
            {
                if (!expected.ContainsKey(kv.Key))
                    expected[kv.Key] = kv.Value;
            }

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
