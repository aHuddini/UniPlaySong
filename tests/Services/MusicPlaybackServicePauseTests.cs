using System;
using System.IO;
using Moq;
using NUnit.Framework;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class MusicPlaybackServicePauseTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp() => _tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySongTests", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        // External Audio pauses instantly; the play buttons resume through the faded path. That path only restarted
        // the player when the fader was paused, and an instant pause left it unpaused - so play ramped the volume of a
        // paused player: silence, and after an idle/lock device release the play button did nothing at all.
        [Test]
        public void AnInstantPause_IsResumedByTheFadedPath()
        {
            var player = new Mock<IMusicPlayer>();
            player.SetupGet(p => p.IsLoaded).Returns(true);
            player.SetupGet(p => p.IsActive).Returns(true);
            var service = new MusicPlaybackService(player.Object, new GameMusicFileService(_tempDir));

            service.AddPauseSourceImmediate(PauseSource.ExternalAudio);
            service.RemovePauseSource(PauseSource.ExternalAudio);

            player.Verify(p => p.Resume(It.IsAny<Action>()), Times.Once);
        }


        // Lock and suspend are separate pauses. Win+L, then sleep, then wake while still locked: the wake must not
        // unpause music behind the lock screen - only the unlock may.
        [Test]
        public void LockThenSuspend_WakeKeepsTheLockPause()
        {
            var player = new Mock<IMusicPlayer>();
            player.SetupGet(p => p.IsLoaded).Returns(true);
            player.SetupGet(p => p.IsActive).Returns(true);
            var service = new MusicPlaybackService(player.Object, new GameMusicFileService(_tempDir));

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
            var service = new MusicPlaybackService(new Mock<IMusicPlayer>().Object, new GameMusicFileService(_tempDir));
            service.AddPauseSourceImmediate(PauseSource.Suspended);

            service.ClearAllPauseSources();

            Assert.IsTrue(service.HasPauseSource(PauseSource.Suspended));
        }

        // Idle belongs to IdleMonitor. Removing it from outside left the monitor believing the user was still idle, so
        // it never paused again until keyboard or mouse input - controller-only users stayed unpaused.
        [Test]
        public void ThePluginNeverRemovesTheIdlePauseBehindIdleMonitorsBack()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "src", "UniPlaySong.cs");
            if (!File.Exists(path)) Assert.Ignore("UniPlaySong.cs not reachable from the test output directory");

            StringAssert.DoesNotContain("RemovePauseSource(Models.PauseSource.Idle)", File.ReadAllText(path),
                "clear idle through IdleMonitor.ClearIdleState()");
        }
    }
}
