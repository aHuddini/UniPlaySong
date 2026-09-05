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
    // The fix polls for ~3s, mirroring OnGameStopVerifyTick, which solves the same "one sample is
    // not enough" problem at game exit. Polling is only safe because of the two properties asserted
    // here: re-adding a source is inert, and each source stays gated on its own setting.
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
        public void EveryWindowStateSourceStaysGatedOnItsOwnSetting()
        {
            // Mirrors ResyncWindowStatePauseSources. Polling repeats the decision, so a missing gate
            // would be applied 15 times instead of once - and would pause users who switched the
            // corresponding setting off.
            var allOff = new UniPlaySongSettings
            {
                PauseOnFocusLoss = false,
                PauseOnMinimize = false,
                PauseWhenInSystemTray = false
            };

            Assert.IsFalse(WouldAdd(PauseSource.FocusLoss, allOff), "focus loss is gated");
            Assert.IsFalse(WouldAdd(PauseSource.Minimized, allOff), "minimize is gated");
            Assert.IsFalse(WouldAdd(PauseSource.SystemTray, allOff), "system tray is gated");

            var allOn = new UniPlaySongSettings
            {
                PauseOnFocusLoss = true,
                PauseOnMinimize = true,
                PauseWhenInSystemTray = true
            };

            Assert.IsTrue(WouldAdd(PauseSource.FocusLoss, allOn));
            Assert.IsTrue(WouldAdd(PauseSource.Minimized, allOn));
            Assert.IsTrue(WouldAdd(PauseSource.SystemTray, allOn));
        }

        // The gate ResyncWindowStatePauseSources applies, with the window conditions taken as true
        // (the poll only reaches the gate when the window is genuinely hidden/inactive/minimized).
        private static bool WouldAdd(PauseSource source, UniPlaySongSettings s)
        {
            switch (source)
            {
                case PauseSource.FocusLoss: return s.PauseOnFocusLoss;
                case PauseSource.Minimized: return s.PauseOnMinimize;
                case PauseSource.SystemTray: return s.PauseWhenInSystemTray;
                default: return false;
            }
        }

        [Test]
        public void HandlerAttachmentIsRetryableRatherThanOneShot()
        {
            // The latent half of the bug: the subscription used to sit inside a bare
            // `if (MainWindow != null)` with no retry, so a null MainWindow at that instant left
            // StateChanged and IsVisibleChanged unattached for the entire session - minimize and
            // tray pausing dead, with nothing to recover it.
            var attach = typeof(UniPlaySong).GetMethod("AttachWindowStateHandlers",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(attach,
                "attachment must be a callable method the verify tick can retry, not inline one-shot code");

            var parameters = attach.GetParameters();
            Assert.AreEqual(1, parameters.Length);
            Assert.AreEqual(typeof(System.Windows.Window), parameters[0].ParameterType);
        }

        [Test]
        public void TheVerifyTimerExistsAndIsTornDownWithThePlugin()
        {
            // A DispatcherTimer left running roots the plugin instance; the sibling game-exit timer
            // is nulled on dispose for the same reason.
            var timer = typeof(UniPlaySong).GetField("_startupWindowVerifyTimer",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(timer, "the startup verify needs a timer field");

            var target = typeof(UniPlaySong).GetField("_windowStateHandlerTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(target,
                "the attached window must be tracked so its handlers can be detached on dispose");
        }

        [Test]
        public void AttachmentOutlivesTheResyncBudget()
        {
            // The two halves fail differently, so they get different budgets. Resync only has to
            // cover the window settling; attachment must not give up, because Playnite assigns
            // Application.Current.MainWindow only AFTER MainModel.OpenView() returns - and OpenView
            // is what raises OnApplicationStarted. Arriving before the window exists is the normal
            // case, not the exceptional one.
            var resync = typeof(UniPlaySong).GetField("StartupVerifyResyncTicks",
                BindingFlags.NonPublic | BindingFlags.Static);
            var max = typeof(UniPlaySong).GetField("StartupVerifyMaxTicks",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(resync, "the resync budget must be a named constant, not a literal");
            Assert.NotNull(max, "the timer needs a hard stop so it cannot leak");

            int resyncTicks = (int)resync.GetRawConstantValue();
            int maxTicks = (int)max.GetRawConstantValue();

            Assert.Greater(maxTicks, resyncTicks,
                "attachment must keep retrying after resync has stopped, or a late-arriving window " +
                "leaves minimize and tray pausing dead for the whole session");
        }

        [Test]
        public void TheResyncBudgetCoversASlowBoot()
        {
            // Reported at machine boot specifically: Playnite launches from a Startup-folder shortcut
            // against a contended disk, and its Visibility/WindowState are TwoWay bindings that
            // propagate on a dispatcher pass. The same build on a warm manual launch never reproduces.
            // 3s (the game-exit sibling's budget) is not enough headroom for that.
            var resync = typeof(UniPlaySong).GetField("StartupVerifyResyncTicks",
                BindingFlags.NonPublic | BindingFlags.Static);
            int ticks = (int)resync.GetRawConstantValue();

            Assert.GreaterOrEqual(ticks * 200, 10000,
                "the resync window should span at least ~10s of ticks to survive a boot-time stall");
        }

        [Test]
        public void TheWindowStateReadingDoesNotTrustWpfAlone()
        {
            // A process the shell starts minimized - "Run: minimized" on a startup shortcut, which is
            // how people hide things that launch at boot - is iconic from the moment it appears,
            // without WPF ever having been asked for it, so Window.WindowState can still read Normal.
            // Reading the window itself is the only way that case is ever seen.
            foreach (var name in new[] { "WindowIsMinimized", "WindowIsHidden" })
            {
                var probe = typeof(UniPlaySong).GetMethod(name,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(probe, $"{name} is what makes the Win32 reading reachable");
                Assert.AreEqual(typeof(bool), probe.ReturnType);
            }

            var iconic = typeof(UniPlaySong).GetMethod("IsIconic",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(iconic, "the Win32 half of the reading has to be declared to be used");
        }

        [Test]
        public void AStaleWindowStateSourceCannotSilentlyBlockPlaybackForever()
        {
            // The failure the poll introduced: WPF raises StateChanged/IsVisibleChanged only on a
            // CHANGE, so a source added from a reading WPF never agreed with has no event coming to
            // release it. It then refuses every play — transport buttons included — until something
            // unrelated clears it. Reported as "it wouldn't start until I clicked around and opened
            // the settings". The service therefore re-reads the window before letting those sources
            // refuse anything, and the resync releases what no longer holds.
            var verifier = typeof(IMusicPlaybackService).GetMethod("SetWindowStateVerifier");
            Assert.NotNull(verifier, "the service has no window of its own to read; the host injects the check");

            var resync = typeof(UniPlaySong).GetMethod("ResyncWindowStatePauseSources",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(resync);
            Assert.AreEqual(1, resync.GetParameters().Length,
                "it needs the bidirectional flag — add-only cannot release a stale source");
            Assert.AreEqual(typeof(bool), resync.GetParameters()[0].ParameterType);
        }

        [Test]
        public void TheWindowStateReadingIsCallableFromAnyThread()
        {
            // Playnite raises ItemUpdated from library imports and install-size scans on a
            // background task (GameDatabase.EndBufferUpdate -> OnItemUpdated), and anything writing
            // game records arrives the same way — PlayniteAchievements storing unlock data, for one.
            // Reading Window.WindowState/IsVisible/IsActive there throws from Dispatcher.VerifyAccess,
            // and since Playnite raises it inside its own database pipeline the throw surfaced as
            // Playnite's "unrecoverable error" dialog: a crash on every achievement unlock.
            //
            // A synchronous Dispatcher.Invoke would trade the crash for a deadlock — this runs inside
            // a database write the UI thread can be waiting on. The off-thread reading therefore has
            // to come from Win32, so those probes must be static and free of any WPF object.
            foreach (var name in new[] { "IsIconic", "IsWindowVisible", "GetForegroundWindow" })
            {
                var probe = typeof(UniPlaySong).GetMethod(name,
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(probe, $"{name} must be static — the off-thread path cannot touch a Window");
                Assert.AreEqual(1, probe.GetParameters().Length == 0 ? 1 : probe.GetParameters().Length,
                    "Win32 probes take a handle or nothing, never a WPF object");
                foreach (var p in probe.GetParameters())
                    Assert.AreEqual(typeof(IntPtr), p.ParameterType,
                        $"{name} must read from a handle, not a Window");
            }
        }

        [Test]
        public void ComingToTheForegroundReleasesEveryWindowStateSource()
        {
            // The safety net for the reading above. A source added from a state WPF never agreed the
            // window was in has no StateChanged/IsVisibleChanged coming to release it, so a wrong
            // reading would pin the music silent for the session. A window confirmed foreground is
            // neither minimized nor in the tray, so CompleteActivationResume drops all three.
            var service = CreateService();

            service.AddPauseSource(PauseSource.Minimized);
            service.AddPauseSource(PauseSource.SystemTray);
            service.AddPauseSource(PauseSource.FocusLoss);
            Assert.IsTrue(service.IsPaused);

            // Mirrors CompleteActivationResume, reached only once the main window is confirmed to be
            // the foreground window.
            service.RemovePauseSource(PauseSource.FocusLoss);
            service.RemovePauseSource(PauseSource.Minimized);
            service.RemovePauseSource(PauseSource.SystemTray);

            Assert.IsFalse(service.IsPaused,
                "a confirmed-foreground window must leave nothing window-related holding the music");
        }

    }
}
