using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Moq;
using NUnit.Framework;
using Playnite.SDK.Models;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Game music and default music each have their own "where it plays" setting. Before, default music with game music
    // off skipped the mode check entirely: "Play music in: Fullscreen only" still played default music in Desktop.
    [TestFixture]
    public class DefaultMusicStateTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp() => _tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySongTests", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        // SettingsService needs a live plugin to construct; the coordinator only reads Current and subscribes to
        // SettingsChanged, so an uninitialized instance with its settings field set is enough.
        private static SettingsService ServiceFor(UniPlaySongSettings settings)
        {
            var service = (SettingsService)FormatterServices.GetUninitializedObject(typeof(SettingsService));
            typeof(SettingsService).GetField("_currentSettings", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(service, settings);
            return service;
        }

        private static (MusicPlaybackCoordinator coordinator, Mock<IMusicPlaybackService> playback) Coordinator(
            UniPlaySongSettings settings, bool fullscreen, bool userStarted = false)
        {
            var playback = new Mock<IMusicPlaybackService>();
            playback.SetupGet(p => p.UserHasManuallyStartedThisSession).Returns(userStarted);
            var coordinator = new MusicPlaybackCoordinator(playback.Object, ServiceFor(settings), null, null,
                () => fullscreen, () => !fullscreen, () => null);
            return (coordinator, playback);
        }

        private static readonly Game AGame = new Game("Test Game");

        private static void VerifyNoPlay(Mock<IMusicPlaybackService> playback, string because) =>
            playback.Verify(p => p.PlayGameMusic(It.IsAny<Game>(), It.IsAny<UniPlaySongSettings>(), It.IsAny<bool>()), Times.Never, because);

        private static void VerifyPlayedOnce(Mock<IMusicPlaybackService> playback, string because) =>
            playback.Verify(p => p.PlayGameMusic(It.IsAny<Game>(), It.IsAny<UniPlaySongSettings>(), It.IsAny<bool>()), Times.Once, because);

        // The report: game music off, default music on, "Play music in: Fullscreen only" - and default music played in Desktop.
        [Test]
        public void GameMusicOff_DefaultMusicFullscreenOnly_DesktopStaysQuiet()
        {
            var settings = new UniPlaySongSettings { EnableMusic = false, EnableDefaultMusic = true, DefaultMusicState = AudioState.Fullscreen };
            var (coordinator, playback) = Coordinator(settings, fullscreen: false);

            coordinator.HandleGameSelected(AGame, isFullscreen: false);

            VerifyNoPlay(playback, "default music is Fullscreen only");
        }

        [Test]
        public void GameMusicOff_DefaultMusicAllowed_DefaultPlays()
        {
            var settings = new UniPlaySongSettings { EnableMusic = false, EnableDefaultMusic = true, DefaultMusicState = AudioState.Always };
            var (coordinator, playback) = Coordinator(settings, fullscreen: false);

            coordinator.HandleGameSelected(AGame, isFullscreen: false);

            VerifyPlayedOnce(playback, "routed to PlayGameMusic, which plays the default music");
        }

        // Game music Fullscreen only, default music both modes, in Desktop: every game plays the default music.
        [Test]
        public void GameMusicNotAllowedHere_DefaultAllowed_DefaultPlays()
        {
            var settings = new UniPlaySongSettings
            {
                EnableMusic = true, MusicState = AudioState.Fullscreen,
                EnableDefaultMusic = true, DefaultMusicState = AudioState.Always,
            };
            var (coordinator, playback) = Coordinator(settings, fullscreen: false);

            coordinator.HandleGameSelected(AGame, isFullscreen: false);

            VerifyPlayedOnce(playback, "game music is blocked here but default music is not");
        }

        // "Play automatically on first launch (Desktop only)" off means nothing starts on its own - default music
        // with game music off used to skip that lock.
        [Test]
        public void GameMusicOff_DesktopAutoPlayLock_HoldsDefaultMusic()
        {
            var settings = new UniPlaySongSettings
            {
                EnableMusic = false, EnableDefaultMusic = true, DefaultMusicState = AudioState.Always,
                AutoPlayOnFirstLaunchDesktop = false,
            };
            var (locked, lockedPlayback) = Coordinator(settings, fullscreen: false, userStarted: false);
            locked.HandleGameSelected(AGame, isFullscreen: false);
            VerifyNoPlay(lockedPlayback, "nothing plays until Play is pressed once");

            var (unlocked, unlockedPlayback) = Coordinator(settings, fullscreen: false, userStarted: true);
            unlocked.HandleGameSelected(AGame, isFullscreen: false);
            VerifyPlayedOnce(unlockedPlayback, "after a manual Play the lock is gone");
        }

        // Both game and default music Fullscreen only, in Desktop: even a game WITH songs stays silent. The service
        // used to play them, trusting the coordinator to have checked the mode. Tests run without a Playnite
        // application, which the service reads as Desktop.
        [Test]
        public void GameMusicBlockedHere_DefaultMusicBlockedToo_ServiceLoadsNothing()
        {
            var game = new Game("Has Songs");
            var folder = Path.Combine(_tempDir, game.Id.ToString());
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "song.mp3"), "");

            var player = new Mock<IMusicPlayer>();
            var service = new MusicPlaybackService(player.Object, new GameMusicFileService(_tempDir));
            service.MarkInitializationComplete();
            var settings = new UniPlaySongSettings
            {
                EnableMusic = true, MusicState = AudioState.Fullscreen,
                EnableDefaultMusic = true, DefaultMusicState = AudioState.Fullscreen,
            };

            service.PlayGameMusic(game, settings, false);

            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never, "neither music is allowed in Desktop");
        }

        // Radio at Playnite startup started in any mode. With game and default music both Fullscreen only, Desktop
        // (how tests run) must stay silent.
        [Test]
        public void RadioStart_WhereNoMusicIsAllowed_LoadsNothing()
        {
            Directory.CreateDirectory(_tempDir);
            var song = Path.Combine(_tempDir, "a.mp3");
            File.WriteAllText(song, "");
            var player = new Mock<IMusicPlayer>();
            var service = new MusicPlaybackService(player.Object, new GameMusicFileService(_tempDir));
            service.MarkInitializationComplete();
            service.SetRadioSongPoolProvider((source, s) => new System.Collections.Generic.List<string> { song });
            service.SetDefaultSongPoolProvider((source, s) => new System.Collections.Generic.List<string> { song });
            var settings = new UniPlaySongSettings
            {
                RadioModeEnabled = true,
                MusicState = AudioState.Fullscreen, DefaultMusicState = AudioState.Fullscreen,
            };

            Assert.IsFalse(service.StartRadioPlayback(settings));
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Migration_CopiesPlayMusicIn_Once()
        {
            foreach (AudioState state in Enum.GetValues(typeof(AudioState)))
            {
                var s = new UniPlaySongSettings { MusicState = state };
                Assert.IsTrue(SettingsService.MigrateDefaultMusicState(s), $"{state}: first run migrates");
                Assert.AreEqual(state, s.DefaultMusicState, $"{state}: copied");

                s.DefaultMusicState = AudioState.Never; // the user changes it afterwards
                Assert.IsFalse(SettingsService.MigrateDefaultMusicState(s), $"{state}: second run is a no-op");
                Assert.AreEqual(AudioState.Never, s.DefaultMusicState, $"{state}: the user's later choice stands");
            }
        }

        // Radio draws from pools, so it plays wherever game or default music is on and allowed - never where all is off.
        [TestCase(true, AudioState.Fullscreen, true, AudioState.Fullscreen, false, ExpectedResult = false)]
        [TestCase(true, AudioState.Desktop, true, AudioState.Fullscreen, false, ExpectedResult = true)]
        [TestCase(false, AudioState.Always, true, AudioState.Desktop, false, ExpectedResult = true)]
        [TestCase(false, AudioState.Always, false, AudioState.Always, false, ExpectedResult = false)]
        [TestCase(true, AudioState.Fullscreen, false, AudioState.Always, true, ExpectedResult = true)]
        public bool RadioFollowsGameOrDefaultMusic(bool gameOn, AudioState gameState, bool defaultOn, AudioState defaultState, bool fullscreen)
        {
            var s = new UniPlaySongSettings
            {
                EnableMusic = gameOn, MusicState = gameState,
                EnableDefaultMusic = defaultOn, DefaultMusicState = defaultState,
            };
            return s.AllowsAnyMusicInMode(fullscreen);
        }
    }
}
