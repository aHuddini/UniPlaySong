using System;
using System.IO;
using Moq;
using NUnit.Framework;
using Playnite.SDK.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // The Music Info Card plays a clicked song through PlaySong. The pick must beat the normal choice (the
    // alphabetical first song here), and must not linger to override the next ordinary selection.
    [TestFixture]
    public class MusicPlaybackServicePlaySongTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp() => _tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySongTests", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        [Test]
        public void PlaySong_PlaysThePickedSong_Once()
        {
            var fileService = new GameMusicFileService(_tempDir);
            var game = new Game("Test Game") { Id = Guid.NewGuid() };
            var dir = fileService.GetGameMusicDirectory(game);
            Directory.CreateDirectory(dir);
            var first = Path.Combine(dir, "a.mp3");
            var picked = Path.Combine(dir, "b.mp3");
            File.WriteAllBytes(first, new byte[] { 0 });
            File.WriteAllBytes(picked, new byte[] { 0 });

            var player = new Mock<IMusicPlayer>();
            var service = new MusicPlaybackService(player.Object, fileService);
            service.MarkInitializationComplete(); // as at the end of startup; before that, plays are deferred

            service.PlaySong(game, picked);

            Assert.Multiple(() =>
            {
                player.Verify(p => p.Load(picked), Times.Once, "the clicked song is the one loaded");
                player.Verify(p => p.Load(first), Times.Never, "not the song the normal rules would pick");
            });
        }
    }
}
