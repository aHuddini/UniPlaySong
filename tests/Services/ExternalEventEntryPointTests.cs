using Moq;
using NUnit.Framework;
using Playnite.SDK;
using Playnite.SDK.Events;
using UniPlaySong.Services;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Tests.Services
{
    // The in-process entry point other plugins call by reflection instead of firing the URI. Its
    // return value is a contract: a caller uses false to fall back to the URI against an older
    // UniPlaySong, so returning the wrong thing either loses the event or doubles it.
    [TestFixture]
    public class ExternalEventEntryPointTests
    {
        private Mock<INotificationsAPI> _notifications;

        private ExternalControlService CreateService()
        {
            _notifications = new Mock<INotificationsAPI>();
            var api = new Mock<IPlayniteAPI>();
            api.SetupGet(a => a.Notifications).Returns(_notifications.Object);

            return new ExternalControlService(
                new Mock<IMusicPlaybackService>().Object,
                new Mock<IActiveMediaService>().Object,
                api.Object,
                null,
                () => new UniPlaySongSettings());
        }

        [Test]
        public void KnownSources_ReportHandled()
        {
            var service = CreateService();

            Assert.IsTrue(service.HandleExternalEvent("controlup", "detecttrigger"));
            Assert.IsTrue(service.HandleExternalEvent("playniteachievements", "rareachievement"));
        }

        // Case must not decide whether the event lands — a caller shouldn't have to guess.
        [Test]
        public void SourceMatchingIsCaseInsensitive()
        {
            Assert.IsTrue(CreateService().HandleExternalEvent("ControlUp", "detecttrigger"));
        }

        // False is the caller's signal to fall back to the URI, so an unknown source must say so
        // rather than silently swallowing the event.
        [Test]
        public void UnknownSource_ReportsUnhandled()
        {
            var service = CreateService();

            Assert.IsFalse(service.HandleExternalEvent("someotherplugin", "whatever"));
            Assert.IsFalse(service.HandleExternalEvent(null, null));
        }

        // The in-process path can hand over nulls the URI never could — its segments were always
        // non-null strings. A throw here would land in the CALLER's event handler, on their UI thread.
        [Test]
        public void NullEventName_DoesNotThrow()
        {
            var service = CreateService();

            Assert.DoesNotThrow(() => service.HandleExternalEvent("controlup", null));
            Assert.DoesNotThrow(() => service.HandleExternalEvent("playniteachievements", null));
        }

        // Handled means routed, not "made a sound": the settings gate lives deeper, and a caller must
        // not retry over the URI just because the user has the sound switched off.
        [Test]
        public void HandledIsReportedEvenWhenTheSoundSettingIsOff()
        {
            var service = new ExternalControlService(
                new Mock<IMusicPlaybackService>().Object,
                new Mock<IActiveMediaService>().Object,
                new Mock<IPlayniteAPI>().Object,
                null,
                () => new UniPlaySongSettings { EnableControlUpDetectSound = false });

            Assert.IsTrue(service.HandleExternalEvent("controlup", "detecttrigger"));
        }

        // Both entry points must share one debounce, or a caller mixing them (or falling back
        // mid-burst) would get the double-ding the guard exists to prevent.
        [Test]
        public void TheDebounceIsSharedWithTheUriPath()
        {
            var service = CreateService();

            service.HandleExternalEvent("controlup", "detecttrigger");
            long afterDirect = DebounceElapsed(service);

            service.HandleCommand(new PlayniteUriEventArgs
            {
                Arguments = new[] { "controlup", "detecttrigger" }
            });

            Assert.GreaterOrEqual(DebounceElapsed(service), afterDirect,
                "a URI fire right after a direct call must be swallowed, not restart the window");
        }

        private static long DebounceElapsed(ExternalControlService service)
        {
            var field = typeof(ExternalControlService).GetField(
                "_controlUpLastFire",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return ((System.Diagnostics.Stopwatch)field.GetValue(service)).ElapsedMilliseconds;
        }
    }
}
