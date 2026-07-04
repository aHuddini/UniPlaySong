using System.Collections.Generic;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class AudioDeviceRegistryTests
    {
        // Minimal test holder: records release calls; reports open/closed.
        private class FakeHolder : IAudioDeviceHolder
        {
            public bool Open;
            public int ReleaseCalls;
            public bool Throws;
            public string Label;
            public FakeHolder(string label, bool open) { Label = label; Open = open; }
            public void ReleaseAudioDevice()
            {
                ReleaseCalls++;
                if (Throws) throw new System.InvalidOperationException("boom");
                Open = false;
            }
            public void PrewarmAudioDevice() { Open = true; }
            public bool IsAudioDeviceOpen => Open;
            public string AudioDeviceLabel => Label;
        }

        [Test]
        public void ReleaseAllDevices_ClosesEveryOpenHolder_ReturnsCount()
        {
            var reg = new AudioDeviceRegistry(null);
            var a = new FakeHolder("A", true);
            var b = new FakeHolder("B", true);
            var c = new FakeHolder("C", false); // already closed
            reg.Register(a); reg.Register(b); reg.Register(c);

            int released = reg.ReleaseAllDevices("test");

            Assert.AreEqual(1, a.ReleaseCalls);
            Assert.AreEqual(1, b.ReleaseCalls);
            Assert.IsFalse(a.IsAudioDeviceOpen);
            Assert.IsFalse(b.IsAudioDeviceOpen);
            // c was already closed; ReleaseAllDevices should not call release on a closed holder
            Assert.AreEqual(0, c.ReleaseCalls);
            Assert.AreEqual(2, released); // only the two that were open
        }

        [Test]
        public void ReleaseAllDevices_IsIdempotent_SecondCallReleasesNothing()
        {
            var reg = new AudioDeviceRegistry(null);
            var a = new FakeHolder("A", true);
            reg.Register(a);
            reg.ReleaseAllDevices("first");
            int second = reg.ReleaseAllDevices("second");
            Assert.AreEqual(0, second);
            Assert.AreEqual(1, a.ReleaseCalls);
        }

        [Test]
        public void ReleaseAllDevices_AThrowingHolder_DoesNotAbortOthers()
        {
            var reg = new AudioDeviceRegistry(null);
            var bad = new FakeHolder("bad", true) { Throws = true };
            var good = new FakeHolder("good", true);
            reg.Register(bad); reg.Register(good);

            Assert.DoesNotThrow(() => reg.ReleaseAllDevices("test"));
            Assert.AreEqual(1, good.ReleaseCalls); // good still released despite bad throwing
            Assert.IsFalse(good.IsAudioDeviceOpen);
        }

        [Test]
        public void Unregister_RemovesHolder_NoLongerReleased()
        {
            var reg = new AudioDeviceRegistry(null);
            var a = new FakeHolder("A", true);
            reg.Register(a);
            reg.Unregister(a);
            int released = reg.ReleaseAllDevices("test");
            Assert.AreEqual(0, a.ReleaseCalls);
            Assert.AreEqual(0, released);
        }

        [Test]
        public void IsAnyDeviceOpen_ReflectsHolders()
        {
            var reg = new AudioDeviceRegistry(null);
            Assert.IsFalse(reg.IsAnyDeviceOpen); // no holders
            var a = new FakeHolder("A", false);
            reg.Register(a);
            Assert.IsFalse(reg.IsAnyDeviceOpen);
            a.Open = true;
            Assert.IsTrue(reg.IsAnyDeviceOpen);
        }

        [Test]
        public void ReleaseAllDevices_NoHolders_DoesNotThrow_ReturnsZero()
        {
            var reg = new AudioDeviceRegistry(null);
            Assert.AreEqual(0, reg.ReleaseAllDevices("test"));
        }
    }
}
