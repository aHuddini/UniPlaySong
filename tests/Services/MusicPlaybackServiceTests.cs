using System;
using System.IO;
using Moq;
using NUnit.Framework;
using Playnite.SDK.Models;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class MusicPlaybackServiceTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp() => _tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySongTests", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        private MusicPlaybackService CreateService(IMusicPlayer player) => new MusicPlaybackService(player, new GameMusicFileService(_tempDir));

        // --- Pause sources ---

        // External Audio pauses instantly; the play buttons resume through the faded path. That path only restarted
        // the player when the fader was paused, and an instant pause left it unpaused - so play ramped the volume of a
        // paused player: silence, and after an idle/lock device release the play button did nothing at all.
        // Mute is a factor in every volume UPS sets, not a zeroed player level the next fade or volume write overwrote
        // (Aniki's and the hover card's mute came back on their own)
        [Test]
        public void Mute_HoldsThroughVolumeChanges_AndUnmuteRestoresTheLevel()
        {
            var player = new Mock<IMusicPlayer>();
            player.SetupProperty(p => p.Volume);
            player.SetupGet(p => p.IsLoaded).Returns(true);
            player.SetupGet(p => p.IsActive).Returns(true);
            var service = CreateService(player.Object);
            service.SetVolume(0.6);

            service.IsMuted = true;
            Assert.AreEqual(0.0, player.Object.Volume, 1e-9);

            service.SetVolume(0.8);
            Assert.AreEqual(0.0, player.Object.Volume, 1e-9, "a volume change unmuted the sound");

            service.IsMuted = false;
            Assert.AreEqual(0.8, player.Object.Volume, 1e-9);
        }

        [Test]
        public void AnInstantPause_IsResumedByTheFadedPath()
        {
            var player = new Mock<IMusicPlayer>();
            player.SetupGet(p => p.IsLoaded).Returns(true);
            player.SetupGet(p => p.IsActive).Returns(true);
            var service = CreateService(player.Object);

            service.AddPauseSourceImmediate(PauseSource.ExternalAudio);
            service.RemovePauseSource(PauseSource.ExternalAudio);

            player.Verify(p => p.Resume(It.IsAny<Action>()), Times.Once);
        }

        // Radio started at Playnite startup goes straight to StartRadioPlayback, never through PlayGameMusic, so the
        // service had no settings: Next skipped the radio branch and threw on the default-pool branch (a theme's Next
        // button through ActiveMediaService). Toggling radio "fixed" it only because a save refreshed the settings.
        [Test]
        public void RadioStartedCold_NextSkipsWithinTheRadioPool()
        {
            Directory.CreateDirectory(_tempDir);
            var songs = new System.Collections.Generic.List<string>();
            foreach (var name in new[] { "a.mp3", "b.mp3", "c.mp3" })
            {
                var path = Path.Combine(_tempDir, name);
                File.WriteAllText(path, "");
                songs.Add(path);
            }
            var player = new Mock<IMusicPlayer>();
            var service = CreateService(player.Object);
            service.MarkInitializationComplete();
            service.SetRadioSongPoolProvider((source, s) => songs);
            service.SetDefaultSongPoolProvider((source, s) => songs); // the plugin always registers both

            Assert.IsTrue(service.StartRadioPlayback(new UniPlaySongSettings { RadioModeEnabled = true }));
            Assert.DoesNotThrow(() => service.SkipToNextSong());

            player.Verify(p => p.Load(It.IsAny<string>()), Times.Exactly(2), "Next picks a second radio song");
        }

        // Lock and suspend are separate pauses. Win+L, then sleep, then wake while still locked: the wake must not
        // unpause music behind the lock screen - only the unlock may.
        [Test]
        public void LockThenSuspend_WakeKeepsTheLockPause()
        {
            var player = new Mock<IMusicPlayer>();
            player.SetupGet(p => p.IsLoaded).Returns(true);
            player.SetupGet(p => p.IsActive).Returns(true);
            var service = CreateService(player.Object);

            service.AddPauseSourceImmediate(PauseSource.SystemLock);   // Win+L
            service.AddPauseSourceImmediate(PauseSource.Suspended);    // sleep
            service.RemovePauseSourceImmediate(PauseSource.Suspended); // wake, still locked

            Assert.IsTrue(service.IsPaused, "still locked, so music stays paused");
            player.Verify(p => p.Resume(It.IsAny<Action>()), Times.Never);

            service.RemovePauseSourceImmediate(PauseSource.SystemLock); // unlock
            player.Verify(p => p.Resume(It.IsAny<Action>()), Times.Once);
        }

        // A song starting clears the transient pauses; being asleep is not one of them.
        [Test]
        public void Suspended_SurvivesClearAllPauseSources()
        {
            var service = CreateService(new Mock<IMusicPlayer>().Object);
            service.AddPauseSourceImmediate(PauseSource.Suspended);

            service.ClearAllPauseSources();

            Assert.IsTrue(service.HasPauseSource(PauseSource.Suspended));
        }

        // --- PlaySong ---

        // The Music Info Card plays a clicked song through PlaySong. The pick must beat the normal choice (the
        // alphabetical first song here), and must not linger to override the next ordinary selection.
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

        // --- ReplacePlayer ---

        // A backend swap (Live Effects / Calm Down / EQ toggle, chiptune switch) replaces only the player. The service
        // lives for the whole session because ~20 consumers hold it; rebuilding it left them on a discarded instance.
        [Test]
        public void ReplacePlayer_MovesTheEventsAndKeepsThePauseSources()
        {
            var oldPlayer = new Mock<IMusicPlayer>();
            var newPlayer = new Mock<IMusicPlayer>();
            var service = CreateService(oldPlayer.Object);
            service.AddPauseSourceImmediate(PauseSource.Manual);

            service.ReplacePlayer(newPlayer.Object);

            oldPlayer.VerifyRemove(p => p.MediaEnded -= It.IsAny<EventHandler>(), Times.Once, "the discarded player must not drive song-end");
            newPlayer.VerifyAdd(p => p.MediaEnded += It.IsAny<EventHandler>(), Times.Once);
            Assert.IsTrue(service.HasPauseSource(PauseSource.Manual), "a pause the user set survives the swap");
        }

        // --- Seek ---

        private static Mock<IMusicPlayer> SeekablePlayer(bool canSeek = true, double? totalSeconds = 100)
        {
            var player = new Mock<IMusicPlayer>();
            player.SetupGet(p => p.CanSeek).Returns(canSeek);
            player.SetupGet(p => p.TotalTime).Returns(totalSeconds.HasValue ? TimeSpan.FromSeconds(totalSeconds.Value) : (TimeSpan?)null);
            return player;
        }

        // Seek keeps the target inside the song; a seek onto the last samples would just end it, so it stops a second short.
        [TestCase(42, 42)]    // inside the song
        [TestCase(500, 99)]   // past the end
        [TestCase(-12, 0)]    // before the start
        public void Seek_ClampsIntoTheSong(double requested, double expected)
        {
            var player = SeekablePlayer();

            CreateService(player.Object).Seek(TimeSpan.FromSeconds(requested));

            player.Verify(p => p.Seek(TimeSpan.FromSeconds(expected)), Times.Once);
        }

        // Chiptunes, PSF and crossfades report CanSeek = false, and a song of unknown length can't be clamped;
        // either way the request must not reach the player at all.
        [TestCase(false, 100.0)]
        [TestCase(true, null)]
        public void Seek_WhenThePlayerCannotSeekOrHasNoLength_DoesNothing(bool canSeek, double? totalSeconds)
        {
            var player = SeekablePlayer(canSeek, totalSeconds);
            var service = CreateService(player.Object);

            Assert.IsFalse(service.CanSeek);
            service.Seek(TimeSpan.FromSeconds(10));
            player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
        }
    }
}
