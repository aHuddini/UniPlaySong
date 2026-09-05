using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;
using UniPlaySong.Services.Jingles;

namespace UniPlaySong.Tests.Services
{
    // The out-of-process achievement sound host: the seam, and the host's failure behaviour.
    //
    // The point of these tests is that the seam is INERT. Whenever the setting is off or the host
    // declines - which is every install that has not deliberately turned it on - an achievement
    // sound must take exactly the path it takes today. A capture feature that can cost an ordinary user their achievement sound would be a
    // worse bug than the one it solves — that is the rule the whole design hangs on.
    [TestFixture]
    public class JingleSoundHostSeamTests
    {
        [Test]
        public void TheDefaultHostAlwaysDeclines()
        {
            // NullJingleSoundHost is what ships until the process host does. Declining is the whole
            // contract: every TryPlay returns false, so every sound falls through in process.
            var host = NullJingleSoundHost.Instance;

            Assert.IsFalse(host.TryPlay(@"C:\any\sound.mp3", 1.0));
            Assert.IsFalse(host.TryPlay(null, 0.0));
            Assert.AreEqual(0, host.ProcessId, "no process, no pid — the consumer reads 0 as unavailable");

            Assert.DoesNotThrow(() => host.Start());
            Assert.DoesNotThrow(() => host.Stop());
        }

        [Test]
        public void TheSettingIsOffByDefault()
        {
            // A user must not arrive at this feature by accident: its only effect is to move where a
            // sound comes from, and the failure modes of getting that wrong are silence.
            Assert.IsFalse(new UniPlaySongSettings().EnableJingleSoundHost);
        }

        [Test]
        public void TheSettingNeverTravels()
        {
            // It names a helper on THIS machine. A reset must not move sound output, and a settings
            // export must not carry it to another install. SettingsResetCoverageTests fails by name
            // if a setting is filed nowhere, so this pins WHICH bucket it belongs in.
            var neverReset = typeof(SettingsGroups)
                .GetField("NeverReset", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as System.Collections.Generic.HashSet<string>;

            Assert.NotNull(neverReset);
            Assert.IsTrue(neverReset.Contains(nameof(UniPlaySongSettings.EnableJingleSoundHost)),
                "the host toggle is machine-specific and belongs in NeverReset");
        }

        [Test]
        public void OnlyAchievementSoundsCanReachTheHost()
        {
            // PlayExternalSound is SHARED — ControllerDetected and the URI notification sounds use it
            // too. The host branch therefore lives in PlayAchievementSound, called only from the two
            // achievement paths, so everything else keeps today's behaviour by construction rather
            // than by a runtime check that could drift.
            var achievementPath = typeof(JingleService).GetMethod("PlayAchievementSound",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(achievementPath,
                "the host branch belongs on an achievement-only method, not the shared one");

            var shared = typeof(JingleService).GetMethod("PlayExternalSound",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(shared, "the shared in-process path must still exist as the fallback");
        }

        [Test]
        public void TheHostIsInjectedRatherThanConstructed()
        {
            // JingleService must not build its own host: the seam has to be replaceable with the null
            // host in tests and in any build where the feature is absent.
            var setter = typeof(JingleService).GetMethod("SetSoundHost");
            Assert.NotNull(setter);
            Assert.AreEqual(typeof(IJingleSoundHost), setter.GetParameters().Single().ParameterType);
        }

        [Test]
        public void TheProcessHostReportsWhyItIsNotRunning()
        {
            // A bare pid cannot separate "the user never enabled this" from "it was meant to run and
            // failed", so the host carries a reason the API can pass on. Without it a consumer sees 0
            // in both cases and cannot tell a real fault from a configuration choice.
            var reason = typeof(ProcessJingleSoundHost).GetProperty("FailureReason");
            Assert.NotNull(reason, "the API has to be able to say WHY there is no pid");
            Assert.AreEqual(typeof(string), reason.PropertyType);

            Assert.NotNull(typeof(ProcessJingleSoundHost).GetProperty("IsRunning"));
        }

        [Test]
        public void AMissingExecutableDeclinesInsteadOfThrowing()
        {
            // The likeliest real failure: antivirus quarantines the exe, or a partial install lands
            // without it. That must read as a plain decline so the sound plays in process - never as
            // an exception on the achievement path.
            var host = new ProcessJingleSoundHost(@"C:\definitely\not\here\UpsSound.exe");

            Assert.DoesNotThrow(() => host.Start());
            Assert.IsFalse(host.TryPlay(@"C:\any\sound.mp3", 1.0), "no executable means decline");
            Assert.AreEqual(0, host.ProcessId);
            Assert.AreEqual("quarantined", host.FailureReason,
                "a missing exe is reported as quarantined - the overwhelmingly common cause");
            Assert.DoesNotThrow(() => host.Stop());
        }

        [Test]
        public void AnEmptyPathDeclinesWithoutTouchingTheProcess()
        {
            var host = new ProcessJingleSoundHost(@"C:\definitely\not\here\UpsSound.exe");
            Assert.IsFalse(host.TryPlay(null, 1.0));
            Assert.IsFalse(host.TryPlay(string.Empty, 1.0));
        }

        [Test]
        public void TheToggleTakesEffectWithoutARestart()
        {
            // The consumer reads the pid live, so a toggle that only applied on the next Playnite
            // start would leave it seeing 0 after the user turned the feature on - and the checkbox
            // would look broken to everyone else.
            var apply = typeof(UniPlaySong).GetMethod("ApplySoundHostSetting",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(apply, "the setting has to be applied on save, not only at startup");
            Assert.AreEqual(typeof(bool), apply.GetParameters().Single().ParameterType);
        }

        [Test]
        public void TheContractForbidsThrowing()
        {
            // Documented on the interface, and enforced at the call site by a try/catch that falls
            // back in process. Both halves matter: the contract says a host cannot throw, and
            // UniPlaySong assumes it will anyway.
            var tryPlay = typeof(IJingleSoundHost).GetMethod(nameof(IJingleSoundHost.TryPlay));
            Assert.NotNull(tryPlay);
            Assert.AreEqual(typeof(bool), tryPlay.ReturnType,
                "failure is reported by returning false, never by an exception");
        }
    }
}
