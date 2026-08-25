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
        [Test]
        public void ACustomFolderWithNoFolderIsNotUsable()
        {
            var s = new UniPlaySongSettings
            {
                EnableDefaultMusic = true,
                DefaultMusicSourceOption = DefaultMusicSource.CustomFolder,
                DefaultMusicFolderPath = null
            };

            Assert.IsFalse(QuickStartProfiles.DefaultSourceIsUsable(s),
                "a source the user never finished configuring must be recognised as unusable, or " +
                "default music silently produces nothing");
        }

        [Test]
        public void TheBundledPresetIsAlwaysUsable()
        {
            // The fallback has to be a source that cannot itself be unconfigured, or the recovery
            // path has the same failure mode as the thing it is recovering from.
            var s = new UniPlaySongSettings
            {
                EnableDefaultMusic = true,
                DefaultMusicSourceOption = DefaultMusicSource.BundledPreset
            };

            Assert.IsTrue(QuickStartProfiles.DefaultSourceIsUsable(s));
        }

        [Test]
        public void EverySourceNeedingUserInputIsCoveredByTheUsabilityCheck()
        {
            // The check is only as good as its coverage: a source that needs configuration but is
            // not listed reports "usable" and reintroduces the silent failure for that source only.
            var needsInput = new[]
            {
                DefaultMusicSource.CustomFile,
                DefaultMusicSource.CustomFolder,
                DefaultMusicSource.CustomRotation,
                DefaultMusicSource.CompletionStatusPool
            };

            foreach (var source in needsInput)
            {
                var s = new UniPlaySongSettings
                {
                    EnableDefaultMusic = true,
                    DefaultMusicSourceOption = source,
                    DefaultMusicPath = null,
                    DefaultMusicFolderPath = null,
                    CustomRotationGameIds = null,
                    DefaultMusicStatusPoolIds = null
                };

                Assert.IsFalse(QuickStartProfiles.DefaultSourceIsUsable(s),
                    $"{source} needs the user to supply something, so an unconfigured one must " +
                    "report unusable");
            }
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

        // ---- False-positive guards -------------------------------------------------------------
        // The fallback substitutes a source and tells the user their setting is unfinished. Doing
        // that to somebody whose setup is fine would be worse than the silence it prevents.

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

        [Test]
        public void AProperlyConfiguredSourceIsLeftAlone()
        {
            var file = new UniPlaySongSettings
            {
                EnableDefaultMusic = true,
                DefaultMusicSourceOption = DefaultMusicSource.CustomFile,
                DefaultMusicPath = @"C:\Musicmbient.mp3"
            };
            var folder = new UniPlaySongSettings
            {
                EnableDefaultMusic = true,
                DefaultMusicSourceOption = DefaultMusicSource.CustomFolder,
                DefaultMusicFolderPath = @"C:\Music"
            };

            Assert.IsTrue(QuickStartProfiles.DefaultSourceIsUsable(file));
            Assert.IsTrue(QuickStartProfiles.DefaultSourceIsUsable(folder));
        }

        [Test]
        public void APathIsNotCheckedForExistence()
        {
            // Deliberate: the check asks "did the user finish setting this up", not "is the media
            // reachable right now". Stat-ing the path would fire on a NAS that has not woken, an
            // unplugged external drive, or a folder mid-copy - accusing a correct setup of being
            // unconfigured, which is exactly the false positive to avoid. An unreachable-but-set
            // path stays the user's own business.
            var s = new UniPlaySongSettings
            {
                EnableDefaultMusic = true,
                DefaultMusicSourceOption = DefaultMusicSource.CustomFolder,
                DefaultMusicFolderPath = @"Z:\definitely-not-mounted\music"
            };

            Assert.IsTrue(QuickStartProfiles.DefaultSourceIsUsable(s));
        }

        [Test]
        public void SourcesThatNeedNoSetupAreNeverFlagged()
        {
            var noSetupNeeded = new[]
            {
                DefaultMusicSource.BundledPreset,
                DefaultMusicSource.RandomGame,
                DefaultMusicSource.ActiveThemeMusic
            };

            foreach (var source in noSetupNeeded)
            {
                var s = new UniPlaySongSettings
                {
                    EnableDefaultMusic = true,
                    DefaultMusicSourceOption = source,
                    DefaultMusicPath = null,
                    DefaultMusicFolderPath = null
                };

                Assert.IsTrue(QuickStartProfiles.DefaultSourceIsUsable(s),
                    $"{source} needs nothing from the user, so it must never be reported unconfigured");
            }
        }

        [Test]
        public void TheWarningReachesTheUserAndNotOnlyTheLog()
        {
            // MusicPlaybackService has no Playnite API, so the host injects the notifier. Without
            // this seam the substitution is log-only and the user hears music from a source they did
            // not pick with no idea why.
            var setter = typeof(MusicPlaybackService).GetMethod("SetUserWarningHandler");
            Assert.NotNull(setter, "the service needs a way to warn the user, not just _fileLogger");
        }

    }
}
