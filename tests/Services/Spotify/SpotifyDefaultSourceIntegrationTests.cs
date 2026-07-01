using System;
using System.IO;
using Moq;
using NUnit.Framework;
using Playnite.SDK.Models;
using UniPlaySong;
using UniPlaySong.Services;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Tests.Services.Spotify
{
    // Integration test against the REAL MusicPlaybackService (not a mock) to prove that
    // DefaultMusicSource.Spotify actually engages the default-music gap. The 10 unit tests
    // stub IsPlayingDefaultMusic directly on a mock, so they cannot catch a wiring gap in
    // the engine itself. This test exercises the real resolution switch in PlayGameMusic.
    [TestFixture]
    public class SpotifyDefaultSourceIntegrationTests
    {
        private string _tempMusicDir;
        private Mock<IMusicPlayer> _player;
        private GameMusicFileService _fileService;
        private MusicPlaybackService _service;

        [SetUp]
        public void SetUp()
        {
            _tempMusicDir = Path.Combine(Path.GetTempPath(), "UPS_SpotifyIntegrationTest_" + Guid.NewGuid().ToString("N"));
            _player = new Mock<IMusicPlayer>();
            // Empty base path means no game has a music folder → GetAvailableSongs returns [].
            _fileService = new GameMusicFileService(_tempMusicDir);
            _service = new MusicPlaybackService(_player.Object, _fileService);
            // Allow playback to proceed past the initialization gate.
            _service.MarkInitializationComplete();
        }

        [TearDown]
        public void TearDown()
        {
            try { _service?.Stop(); } catch { }
            try { if (Directory.Exists(_tempMusicDir)) Directory.Delete(_tempMusicDir, true); } catch { }
        }

        [Test]
        public void DefaultSourceSpotify_NoGameMusic_MarksDefaultMusicGap()
        {
            var settings = new UniPlaySongSettings
            {
                EnableMusic = true,
                EnableDefaultMusic = true,
                DefaultMusicSourceOption = DefaultMusicSource.Spotify
            };
            var game = new Game("Game With No Music") { Id = Guid.NewGuid() };

            // Before: not in a default-music gap.
            Assert.IsFalse(_service.IsPlayingDefaultMusic, "Precondition: should not be in a default gap before play.");

            _service.PlayGameMusic(game, settings);

            // After: the engine must have marked the default-music gap so SpotifyControlService
            // computes SpotifyActive=true. This is the I-1 wiring under test.
            Assert.IsTrue(_service.IsPlayingDefaultMusic,
                "DefaultMusicSource.Spotify with a no-music game must set IsPlayingDefaultMusic=true.");
        }

        [Test]
        public void SpotifyRadioMode_GameWithMusic_DoesNotPlayGameMusic()
        {
            // Spotify Radio Mode must REPLACE all game music, including for games that HAVE songs.
            var settings = new UniPlaySongSettings { EnableMusic = true, SpotifyRadioMode = true };
            var game = new Game("Game With Music") { Id = Guid.NewGuid() };
            var gameDir = Path.Combine(_tempMusicDir, game.Id.ToString());
            Directory.CreateDirectory(gameDir);
            File.WriteAllText(Path.Combine(gameDir, "Title Screen.mp3"), "presence");

            _service.PlayGameMusic(game, settings);

            _player.Verify(p => p.Load(It.IsAny<string>()), Times.Never(),
                "SpotifyRadioMode must suppress game music — UPS should never load the game's track.");
            _player.Verify(p => p.Play(), Times.Never(),
                "SpotifyRadioMode must suppress game music — UPS should never start the game's track.");
        }

        [Test]
        public void SpotifyRadioMode_RepeatedSameGameSelect_FiresNoPlaybackStateChanged()
        {
            // FREEZE FIX: Playnite calls PlayGameMusic repeatedly for the same game at launch and on
            // recovery paths. The suppression branch must fire NO state-change events — each such event
            // synchronously drives SpotifyControlService.Recompute; the flood froze Playnite.
            var settings = new UniPlaySongSettings { EnableMusic = true, SpotifyRadioMode = true };
            var game = new Game("Game With Music") { Id = Guid.NewGuid() };
            var gameDir = Path.Combine(_tempMusicDir, game.Id.ToString());
            Directory.CreateDirectory(gameDir);
            File.WriteAllText(Path.Combine(gameDir, "Title Screen.mp3"), "presence");

            int fireCount = 0;
            _service.OnPlaybackStateChanged += () => fireCount++;

            _service.PlayGameMusic(game, settings);
            _service.PlayGameMusic(game, settings);
            _service.PlayGameMusic(game, settings);

            Assert.AreEqual(0, fireCount,
                "The SpotifyRadioMode suppression branch must never fire OnPlaybackStateChanged.");
        }

        [Test]
        public void DefaultSourceSpotify_FiresRecomputeTrigger_SoControlServiceActivates()
        {
            // End-to-end: a real MusicPlaybackService wired to a real SpotifyControlService.
            // Entering a Spotify default-music gap must drive SpotifyActive=true via the
            // OnPlaybackStateChanged recompute trigger — no manual Recompute() call.
            var settings = new UniPlaySongSettings
            {
                EnableMusic = true,
                EnableDefaultMusic = true,
                DefaultMusicSourceOption = DefaultMusicSource.Spotify
            };

            var client = new Mock<ISpotifyClient>();
            client.SetupGet(c => c.IsAvailable).Returns(true);
            client.SetupGet(c => c.IsPlaying).Returns(true);
            client.Setup(c => c.TryPause()).Returns(true);
            client.Setup(c => c.TryResume()).Returns(true);

            using (var control = new SpotifyControlService(_service, client.Object, () => settings, null))
            {
                Assert.IsFalse(settings.SpotifyActive, "Precondition: SpotifyActive starts false.");

                var game = new Game("Game With No Music") { Id = Guid.NewGuid() };
                _service.PlayGameMusic(game, settings);

                Assert.IsTrue(_service.IsPlayingDefaultMusic, "Engine must mark the default gap.");
                Assert.IsTrue(settings.SpotifyActive,
                    "SpotifyControlService must activate via the recompute trigger fired by the engine.");
            }
        }
    }
}
