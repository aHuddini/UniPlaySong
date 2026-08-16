using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Reset moved from per-tab to per left-rail group, and stopped restating default values: a
    // group reset copies from a pristine UniPlaySongSettings, so the backing fields are the only
    // place a default is written.
    //
    // The point of this fixture is the first test. The hand-written handlers it replaces covered
    // 193 settings and silently missed 71 — nothing failed, the settings simply could not be
    // reset. Coverage is now proven rather than remembered: file a new setting nowhere and the
    // build tells you its name.
    [TestFixture]
    public class SettingsResetCoverageTests
    {
        private static IEnumerable<string> AllResettable() =>
            SettingsResetService.ResettableProperties().Select(p => p.Name);

        private static IEnumerable<string> AllMapped() =>
            SettingsGroups.Map.Values.SelectMany(v => v);

        [Test]
        public void EverySettingIsClassifiedExactlyOnce()
        {
            var mapped = new HashSet<string>(AllMapped());
            var unclassified = AllResettable()
                .Where(n => !mapped.Contains(n) && !SettingsGroups.NeverReset.Contains(n))
                .OrderBy(n => n)
                .ToList();

            Assert.That(unclassified, Is.Empty,
                "These settings belong to no reset group and are not in NeverReset, so nothing " +
                "restores them. Add each to a group in SettingsGroups.Map, or to NeverReset if it " +
                "is a machine-specific path or live runtime state:\n  " +
                string.Join("\n  ", unclassified));
        }

        [Test]
        public void NoSettingBelongsToTwoGroups()
        {
            var dupes = AllMapped()
                .GroupBy(n => n)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .OrderBy(n => n)
                .ToList();

            Assert.That(dupes, Is.Empty,
                "A setting in two groups gets reset by both, so which group owns it is ambiguous:\n  " +
                string.Join("\n  ", dupes));
        }

        [Test]
        public void NoGroupClaimsSomethingThatIsNotAResettableSetting()
        {
            var real = new HashSet<string>(AllResettable());
            var bogus = AllMapped().Concat(SettingsGroups.NeverReset)
                .Where(n => !real.Contains(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            // Catches a renamed or removed property, and read-only computed properties that some
            // XAML binds for IsEnabled but that cannot be assigned.
            Assert.That(bogus, Is.Empty,
                "Named in the reset map but not a public read/write property:\n  " +
                string.Join("\n  ", bogus));
        }

        [Test]
        public void NeverResetAndTheGroupsDoNotOverlap()
        {
            var mapped = new HashSet<string>(AllMapped());
            var both = SettingsGroups.NeverReset.Where(mapped.Contains).OrderBy(n => n).ToList();

            Assert.That(both, Is.Empty,
                "Listed as never-reset yet owned by a group:\n  " + string.Join("\n  ", both));
        }

        [Test]
        public void ResetGroupRestoresTheShippedDefault()
        {
            var settings = new UniPlaySongSettings();
            var defaults = new UniPlaySongSettings();

            settings.RandomizeOnEverySelect = !defaults.RandomizeOnEverySelect;
            SettingsResetService.ResetGroup(settings, SettingsGroups.Playback);

            Assert.That(settings.RandomizeOnEverySelect, Is.EqualTo(defaults.RandomizeOnEverySelect));
        }

        [Test]
        public void VolumeAndFadeBelongToLiveEffectsNotPlayback()
        {
            // Deliberate, and easy to undo by accident: the rail plan moves the Volume and Fade
            // sections off the Playback page into the Live Effects group, so their reset moved too.
            var liveEffects = SettingsGroups.Map[SettingsGroups.LiveEffects];
            var playback = SettingsGroups.Map[SettingsGroups.Playback];

            Assert.That(liveEffects, Contains.Item(nameof(UniPlaySongSettings.MusicVolume)));
            Assert.That(liveEffects, Contains.Item(nameof(UniPlaySongSettings.FadeInDuration)));
            Assert.That(liveEffects, Contains.Item(nameof(UniPlaySongSettings.FadeOutDuration)));
            Assert.That(playback, Has.No.Member(nameof(UniPlaySongSettings.MusicVolume)));
        }

        [Test]
        public void ResetGroupLeavesOtherGroupsAlone()
        {
            var settings = new UniPlaySongSettings();
            var defaults = new UniPlaySongSettings();

            settings.RandomizeOnEverySelect = !defaults.RandomizeOnEverySelect;  // Playback
            settings.PauseOnMinimize = !defaults.PauseOnMinimize;                // Pauses

            SettingsResetService.ResetGroup(settings, SettingsGroups.Pauses);

            Assert.That(settings.PauseOnMinimize, Is.EqualTo(defaults.PauseOnMinimize), "Pauses should be restored");
            Assert.That(settings.RandomizeOnEverySelect, Is.Not.EqualTo(defaults.RandomizeOnEverySelect),
                "Playback should be untouched");
        }

        [Test]
        public void ResetRestoresTheShippedIconGlowAndVisualizerDefaults()
        {
            // Two things the old handlers got wrong, now correct by construction. ResetIconGlow
            // set EnableIconGlow = true against a shipped default of false, and the Live Effects
            // handler wrote the Default preset's numbers while labelling the selection Punchy.
            var settings = new UniPlaySongSettings
            {
                EnableIconGlow = true,
                SelectedVizPreset = VizPreset.Cinematic,
                VizGravity = 7,
            };

            SettingsResetService.ResetGroup(settings, SettingsGroups.Advanced);
            SettingsResetService.ResetGroup(settings, SettingsGroups.LiveEffects);

            var defaults = new UniPlaySongSettings();
            Assert.That(settings.EnableIconGlow, Is.False);
            Assert.That(settings.SelectedVizPreset, Is.EqualTo(defaults.SelectedVizPreset));
            Assert.That(settings.VizGravity, Is.EqualTo(defaults.VizGravity),
                "the tuning numbers must agree with the preset the dropdown names");
        }

        [Test]
        public void ResetGroupNeverRestoresToolPaths()
        {
            var settings = new UniPlaySongSettings
            {
                YtDlpPath = @"D:\tools\yt-dlp.exe",
                FFmpegPath = @"D:\tools\ffmpeg.exe",
            };

            foreach (var group in SettingsResetService.GroupNames)
                SettingsResetService.ResetGroup(settings, group);

            // Resetting every group in turn must still leave the machine-specific paths intact —
            // the old handlers preserved these deliberately and that has to survive the rework.
            Assert.That(settings.YtDlpPath, Is.EqualTo(@"D:\tools\yt-dlp.exe"));
            Assert.That(settings.FFmpegPath, Is.EqualTo(@"D:\tools\ffmpeg.exe"));
        }

        [Test]
        public void ResetGivesEachCallItsOwnCollectionInstances()
        {
            var a = new UniPlaySongSettings();
            var b = new UniPlaySongSettings();

            SettingsResetService.ResetGroup(a, SettingsGroups.Playback);
            SettingsResetService.ResetGroup(b, SettingsGroups.Playback);

            // Reset copies from a pristine instance. Were that instance cached, both resets would
            // hand out the same List and mutating one game pool would silently change the other.
            var collections = SettingsGroups.Map[SettingsGroups.Playback]
                .Select(n => typeof(UniPlaySongSettings).GetProperty(n))
                .Where(p => p != null && typeof(IEnumerable).IsAssignableFrom(p.PropertyType)
                            && p.PropertyType != typeof(string))
                .ToList();

            Assert.That(collections, Is.Not.Empty, "expected Playback to own at least one collection setting");

            foreach (var p in collections)
            {
                var av = p.GetValue(a, null);
                var bv = p.GetValue(b, null);
                if (av == null || bv == null) continue;
                Assert.That(ReferenceEquals(av, bv), Is.False, p.Name + " was shared between two resets");
            }
        }

        [Test]
        public void GroupsWithoutSettingsAreReportedAsSuch()
        {
            // About and Quick Start are informational; they get no reset button rather than an
            // inert one, and the strip template keys off this.
            Assert.That(SettingsResetService.HasResettableSettings(SettingsGroups.About), Is.False);
            Assert.That(SettingsResetService.HasResettableSettings(SettingsGroups.QuickStart), Is.False);
            Assert.That(SettingsResetService.HasResettableSettings(SettingsGroups.Playback), Is.True);
        }
    }
}
