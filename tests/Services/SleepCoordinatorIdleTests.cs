using System;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class SleepCoordinatorIdleTests
    {
        private class FakeHolder : IAudioDeviceHolder
        {
            public bool Open = true;
            public int ReleaseCalls;
            public void ReleaseAudioDevice() { ReleaseCalls++; Open = false; }
            public void PrewarmAudioDevice() { Open = true; }
            public bool IsAudioDeviceOpen => Open;
            public string AudioDeviceLabel => "Fake";
        }

        // Build a coordinator with controllable audible-state + idle-minutes.
        private static (SleepCoordinator c, FakeHolder h, bool[] audibleBox) Build(int idleMinutes)
        {
            var reg = new AudioDeviceRegistry(null);
            var h = new FakeHolder();
            reg.Register(h);
            var audibleBox = new bool[] { false };
            var c = new SleepCoordinator(reg, () => audibleBox[0], () => idleMinutes, null);
            return (c, h, audibleBox);
        }

        [Test]
        public void IdleTick_PausedCountsTowardIdle_ReleasesAfterThreshold()
        {
            var (c, h, audible) = Build(5);
            audible[0] = false; // paused/stopped — the bug case that previously reset the clock
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            // First tick establishes the idle baseline; not yet at threshold
            Assert.IsFalse(c.IdleTick(t0));
            // 5 minutes later, still paused → release fires
            Assert.IsTrue(c.IdleTick(t0.AddMinutes(5)));
            Assert.AreEqual(1, h.ReleaseCalls);
        }

        [Test]
        public void IdleTick_ActivePlayback_ResetsClock_NeverReleases()
        {
            var (c, h, audible) = Build(5);
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            audible[0] = true; // actively playing
            Assert.IsFalse(c.IdleTick(t0));
            Assert.IsFalse(c.IdleTick(t0.AddMinutes(10))); // still playing → clock keeps resetting
            Assert.AreEqual(0, h.ReleaseCalls);
        }

        [Test]
        public void IdleTick_ZeroMinutes_DisablesIdleRelease()
        {
            var (c, h, audible) = Build(0);
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            audible[0] = false;
            Assert.IsFalse(c.IdleTick(t0));
            Assert.IsFalse(c.IdleTick(t0.AddMinutes(60)));
            Assert.AreEqual(0, h.ReleaseCalls);
        }

        [Test]
        public void IdleTick_ResumingPlaybackResetsIdleClock()
        {
            var (c, h, audible) = Build(5);
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            audible[0] = false;                      // paused at t0
            Assert.IsFalse(c.IdleTick(t0));
            audible[0] = true;                       // user resumes at t0+3min
            Assert.IsFalse(c.IdleTick(t0.AddMinutes(3)));
            audible[0] = false;                      // paused again at t0+4min
            Assert.IsFalse(c.IdleTick(t0.AddMinutes(4))); // only 1min idle since reset
            Assert.IsFalse(c.IdleTick(t0.AddMinutes(8))); // 4min idle — still < 5
            Assert.IsTrue(c.IdleTick(t0.AddMinutes(9)));   // 5min since the t0+4 baseline → release
            Assert.AreEqual(1, h.ReleaseCalls);
        }

        [Test]
        public void OnLockOrSuspend_ReleasesImmediately_RegardlessOfIdleSetting()
        {
            var (c, h, audible) = Build(0); // idle disabled
            c.OnLockOrSuspend("suspend");
            Assert.AreEqual(1, h.ReleaseCalls); // lock/suspend ignore the idle 0-disable
        }
    }
}
