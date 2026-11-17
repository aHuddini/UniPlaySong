using System;
using System.IO;
using System.Reflection;
using Moq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Pins the fix for "Playnite starts minimized to the tray but UPS plays anyway; open it and
    // minimise again and it pauses fine".
    //
    // The startup window state was sampled ONCE, in OnApplicationStarted. A Playnite told to start
    // minimized or in the tray reaches that state after the sample is taken, and a window BORN
    // hidden raises no StateChanged/IsVisibleChanged to correct it later - so nothing ever added the
    // pause source. Re-minimizing by hand worked because that produces a real transition.
    //
    // The fix polls, mirroring OnGameStopVerifyTick, which solves the same "one sample is not
    // enough" problem at game exit. Polling is only safe because re-adding a source is inert.
    [TestFixture]
    public class StartupWindowStateVerifyTests
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

        private MusicPlaybackService CreateService() =>
            new MusicPlaybackService(new Mock<IMusicPlayer>().Object, new GameMusicFileService(_tempDir));

        [Test]
        public void ReAddingTheSameSourceIsInert()
        {
            // The whole safety case for polling. The tick calls ResyncWindowStatePauseSources up to
            // 15 times, so if a repeated add stacked, one restore could not clear it and the poll
            // would pin the music silent - turning this fix into a worse bug than the one it fixes.
            var service = CreateService();

            for (int i = 0; i < 15; i++)
                service.AddPauseSource(PauseSource.SystemTray);

            Assert.IsTrue(service.IsPaused, "the source should pause once");

            service.RemovePauseSource(PauseSource.SystemTray);

            Assert.IsFalse(service.IsPaused,
                "a single restore must clear it however many times the tick re-added it");
        }

        [Test]
        public void OtherSourcesAreUnaffectedByTheRepeatedAdds()
        {
            // Polling must not disturb a pause the user actually caused.
            var service = CreateService();

            service.AddPauseSource(PauseSource.Manual);
            for (int i = 0; i < 15; i++)
                service.AddPauseSource(PauseSource.Minimized);

            service.RemovePauseSource(PauseSource.Minimized);

            Assert.IsTrue(service.IsPaused,
                "restoring the window must not resume music the user had manually paused");
        }

        [Test]
        public void TheVerifyBudgetsSurviveASlowBootAndALateWindow()
        {
            // Resync must span ~10s: at machine boot Playnite launches from a Startup-folder shortcut against a
            // contended disk, and its Visibility/WindowState bindings settle late. Attachment must outlive resync,
            // because Playnite assigns MainWindow only AFTER OpenView() returns - which is what raises
            // OnApplicationStarted - so a late window would otherwise leave minimize and tray pausing dead.
            var resync = typeof(UniPlaySong).GetField("StartupVerifyResyncTicks",
                BindingFlags.NonPublic | BindingFlags.Static);
            var max = typeof(UniPlaySong).GetField("StartupVerifyMaxTicks",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(resync, "the resync budget must be a named constant, not a literal");
            Assert.NotNull(max, "the timer needs a hard stop so it cannot leak");

            int resyncTicks = (int)resync.GetRawConstantValue();
            int maxTicks = (int)max.GetRawConstantValue();

            Assert.GreaterOrEqual(resyncTicks * 200, 10000,
                "the resync window should span at least ~10s of ticks to survive a boot-time stall");
            Assert.Greater(maxTicks, resyncTicks,
                "attachment must keep retrying after resync has stopped");
        }
    }
}
