using System.Threading;
using Moq;
using NUnit.Framework;
using Playnite.SDK;
using Playnite.SDK.Events;
using UniPlaySong.Services;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Tests.Services
{
    // Pins the controlup/detecttrigger URI contract. ControlUp fires this fire-and-forget and does
    // NOT throttle its side, so the burst guard here is the only thing between a flaky USB reconnect
    // and a stutter of overlapping dings.
    //
    // These exercise HandleCommand's routing and debounce, not playback: JingleService is concrete
    // and needs an audio device, so a null service is passed and the assertions are about which
    // paths are reached (no throw, no unknown-command notification) rather than about sound.
    [TestFixture]
    public class ControlUpUriTests
    {
        private Mock<IPlayniteAPI> _api;
        private Mock<INotificationsAPI> _notifications;

        [SetUp]
        public void SetUp()
        {
            _notifications = new Mock<INotificationsAPI>();
            _api = new Mock<IPlayniteAPI>();
            _api.SetupGet(a => a.Notifications).Returns(_notifications.Object);
        }

        private ExternalControlService CreateService()
        {
            return new ExternalControlService(
                new Mock<IMusicPlaybackService>().Object,
                new Mock<IActiveMediaService>().Object,
                _api.Object,
                null,                                  // JingleService is concrete; playback isn't under test
                () => new UniPlaySongSettings());
        }

        private static PlayniteUriEventArgs Uri(params string[] segments)
        {
            return new PlayniteUriEventArgs { Arguments = segments };
        }

        // "controlup" must be a known command — reaching the default branch would pop a user-facing
        // "Unknown command" notification every time a controller connects.
        [Test]
        public void ControlUpCommand_IsRecognized()
        {
            CreateService().HandleCommand(Uri("controlup", "detecttrigger"));

            _notifications.Verify(n => n.Add(It.IsAny<NotificationMessage>()), Times.Never,
                "a recognized command must not notify");
        }

        // A future ControlUp adding events must not make this build play the wrong sound.
        [Test]
        public void UnknownEventSegment_NoOpsWithoutNotifying()
        {
            var service = CreateService();

            Assert.DoesNotThrow(() => service.HandleCommand(Uri("controlup", "bogusevent")));
            Assert.DoesNotThrow(() => service.HandleCommand(Uri("controlup")), "missing segment");

            _notifications.Verify(n => n.Add(It.IsAny<NotificationMessage>()), Times.Never);
        }

        // The burst case this guard exists for: a swallowed fire must leave the previous fire's clock
        // running rather than restarting it, so three rapid fires stay one sound. Reading the debounce
        // stopwatch is the honest observation available here — with a null JingleService a passing
        // fire and a swallowed one are both silent, so asserting on "no throw" would prove nothing.
        [Test]
        public void FireInsideTheWindow_DoesNotRestartTheClock()
        {
            var service = CreateService();

            service.HandleCommand(Uri("controlup", "detecttrigger"));
            Thread.Sleep(DebounceWindowMs / 4);   // comfortably inside the window

            long beforeSecond = DebounceElapsed(service);
            service.HandleCommand(Uri("controlup", "detecttrigger"));
            long afterSecond = DebounceElapsed(service);

            Assert.GreaterOrEqual(afterSecond, beforeSecond,
                "a swallowed fire must not restart the window, or a steady burst would hold it open forever");
        }

        // The guard must not wedge shut: once the window elapses the next real reconnect has to sound.
        // A debounce that never reopens is indistinguishable from a broken feature.
        [Test]
        public void AfterTheWindowElapses_TheClockRestarts()
        {
            var service = CreateService();

            service.HandleCommand(Uri("controlup", "detecttrigger"));
            Thread.Sleep(DebounceWindowMs + 100);

            service.HandleCommand(Uri("controlup", "detecttrigger"));

            Assert.Less(DebounceElapsed(service), DebounceWindowMs,
                "the fire after the window must pass and restart the clock, not stay latched");
        }

        // A deliberate press shortly after a controller connect must still sound. The window was
        // sized for millisecond-scale hardware bursts, and it also gates the hotkey now that both
        // triggers share this handler — plug a controller in, press the hotkey, hear both.
        [Test]
        public void APressAQuarterSecondAfterAConnect_IsNotSwallowed()
        {
            Assert.LessOrEqual(DebounceWindowMs, 250,
                "a window longer than this eats deliberate hotkey presses that follow a connect");
        }

        // Read from the source of truth so the timings above can't drift from the shipped value.
        private static int DebounceWindowMs =>
            (int)typeof(ExternalControlService)
                .GetField("ControlUpDebounceMs",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetRawConstantValue();

        // Elapsed time on the private debounce stopwatch. Reflection because the field is an internal
        // implementation detail that doesn't warrant an interface seam for one feature.
        private static long DebounceElapsed(ExternalControlService service)
        {
            var field = typeof(ExternalControlService).GetField(
                "_controlUpLastFire",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var watch = (System.Diagnostics.Stopwatch)field.GetValue(service);
            return watch.ElapsedMilliseconds;
        }
    }
}
