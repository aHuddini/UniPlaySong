using System.Linq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Two layers stood between a user and silence, and both could fail quietly at once.
    //
    // Reported as "it mutes at startup, when opening hub, when changing focus, and it never unmutes
    // itself". The reporter's log carried 60+ "RadioMode: pool empty for source FullLibrary" and
    // nothing else - Radio Mode on with an empty pool, and default music pointed at CustomFolder
    // with no folder ever chosen. Neither layer produced sound, and neither told the user why.
    [TestFixture]
    public class SilentFailureFallbackTests
    {
        private static UniPlaySongSettings Source(DefaultMusicSource source, string file = null, string folder = null,
                                                  bool clearLists = false)
        {
            var s = new UniPlaySongSettings
            {
                EnableDefaultMusic = true,
                DefaultMusicSourceOption = source,
                DefaultMusicPath = file,
                DefaultMusicFolderPaths = folder == null ? new System.Collections.Generic.List<string>() : new System.Collections.Generic.List<string> { folder }
            };
            if (clearLists)
            {
                s.CustomRotationGameIds = null;
                s.DefaultMusicStatusPoolIds = null;
            }
            return s;
        }

        // Unusable = the user never finished configuring it, so default music would silently produce nothing.
        // The fallback substitutes a source and tells the user their setting is unfinished; doing that to
        // somebody whose setup is fine would be worse than the silence it prevents, hence the "usable" rows.
        [Test]
        public void DefaultSourceIsUsable_TruthTable()
        {
            var cases = new[]
            {
                // Every source that needs user input must be covered, or it reports usable and stays silent.
                new { S = Source(DefaultMusicSource.CustomFile, clearLists: true), Expected = false, Why = "CustomFile with no file" },
                new { S = Source(DefaultMusicSource.CustomFolder, clearLists: true), Expected = false, Why = "CustomFolder with no folder" },
                new { S = Source(DefaultMusicSource.CustomRotation, clearLists: true), Expected = false, Why = "CustomRotation with no games" },
                new { S = Source(DefaultMusicSource.CompletionStatusPool, clearLists: true), Expected = false, Why = "CompletionStatusPool with no statuses" },
                // The fallback target must itself need no setup, or recovery has the same failure mode.
                new { S = Source(DefaultMusicSource.BundledPreset), Expected = true, Why = "BundledPreset needs no setup" },
                new { S = Source(DefaultMusicSource.RandomGame), Expected = true, Why = "RandomGame needs no setup" },
                new { S = Source(DefaultMusicSource.ActiveThemeMusic), Expected = true, Why = "ActiveThemeMusic needs no setup" },
                new { S = Source(DefaultMusicSource.CustomFile, file: @"C:\Music\ambient.mp3"), Expected = true, Why = "configured file" },
                new { S = Source(DefaultMusicSource.CustomFolder, folder: @"C:\Music"), Expected = true, Why = "configured folder" },
                // Deliberately not stat-ed: a NAS still waking or an unplugged drive is not "unconfigured".
                new { S = Source(DefaultMusicSource.CustomFolder, folder: @"Z:\definitely-not-mounted\music"), Expected = true, Why = "set but unreachable path" },
            };
            Assert.Multiple(() =>
            {
                foreach (var c in cases)
                    Assert.That(QuickStartProfiles.DefaultSourceIsUsable(c.S), Is.EqualTo(c.Expected), c.Why);
            });
        }

        [Test]
        public void TheJukeboxProfileKeepsDefaultMusicOnAsTheSafetyNet()
        {
            // The radio can always turn out to have nothing to play. Default music being on is what
            // makes that recoverable - and the fallback is only reachable because PlayGameMusic now
            // falls through on a failed radio start instead of returning.
            var jukebox = QuickStartProfiles.All.FirstOrDefault(p => p.Id == "fs-jukebox");
            Assert.NotNull(jukebox, "the Jukebox profile is the one that turns Radio Mode on");

            var values = jukebox.Values;
            Assert.IsTrue(values.ContainsKey(nameof(UniPlaySongSettings.EnableDefaultMusic)),
                "Jukebox must state EnableDefaultMusic rather than inherit it");
            Assert.AreEqual(true, values[nameof(UniPlaySongSettings.EnableDefaultMusic)],
                "turning default music off here would make an empty radio pool mean total silence");
        }

        [Test]
        public void AFreshOrFailedToLoadSettingsObjectNeverTripsTheCheck()
        {
            // The biggest false-positive risk: settings that failed to load leave a defaults object,
            // where every path and list is null. That is only harmless because the DEFAULT source is
            // BundledPreset, which needs no configuration - so a defaults object reports usable and
            // no warning fires. If that default ever changes to a configurable source, every user
            // with a failed load gets accused of misconfiguring something they never touched.
            Assert.AreEqual(DefaultMusicSource.BundledPreset, new UniPlaySongSettings().DefaultMusicSourceOption,
                "the default source must stay one that needs no setup, or the unconfigured-source " +
                "warning fires on a settings-load failure");

            Assert.IsTrue(QuickStartProfiles.DefaultSourceIsUsable(new UniPlaySongSettings()));
        }
    }
}
