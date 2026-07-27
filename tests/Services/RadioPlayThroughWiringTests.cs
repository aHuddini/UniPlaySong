using System;
using System.IO;
using Moq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Pins the wiring between AddPauseSource and RadioPlayThroughPolicy. The policy's own
    // unit tests prove the rule; these prove the rule is actually consulted at the choke point.
    [TestFixture]
    public class RadioPlayThroughWiringTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySongTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        private MusicPlaybackService CreateService()
        {
            return new MusicPlaybackService(new Mock<IMusicPlayer>().Object, new GameMusicFileService(_tempDir));
        }

        // Backward-compatibility guarantee: with no settings the policy can't arm, so a game
        // session pauses exactly as it always did. A user who never enables the feature is unaffected.
        [Test]
        public void AddPauseSource_StillPauses_WhenFeatureNotArmed()
        {
            var service = CreateService();
            service.SetGameSessionActive(true);

            service.AddPauseSource(PauseSource.GameStarting);

            Assert.IsTrue(service.IsPaused);
        }

        // The armed path, reached only through RefreshSettings — so this also pins that the
        // service reads a freshly saved settings object rather than the one PlayGameMusic cached.
        [Test]
        public void AddPauseSource_Suppressed_WhenArmedViaRefreshSettings()
        {
            var service = CreateService();
            service.RefreshSettings(new UniPlaySongSettings
            {
                RadioPlaysThroughGames = true,
                RadioModeEnabled = true,
                RadioMusicSource = RadioMusicSource.Spotify,
            });
            service.SetGameSessionActive(true);

            service.AddPauseSource(PauseSource.GameStarting);

            Assert.IsFalse(service.IsPaused);
        }
    }
}
