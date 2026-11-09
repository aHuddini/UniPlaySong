using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UniPlaySong.Tests.Services
{
    // NAudioMusicPlayer needs a real output device, so this pins the source rather than running it.
    [TestFixture]
    public class NAudioTeardownTests
    {
        private static string ReadSource(params string[] parts)
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..");
            foreach (var part in parts) path = Path.Combine(path, part);
            if (!File.Exists(path)) Assert.Ignore($"{parts.Last()} not reachable from the test output directory");
            return File.ReadAllText(path);
        }

        // After any teardown (device error, idle, lock, suspend) a stale "in the mixer" flag made Play() skip adding
        // every later song to the rebuilt mixer - silence until Playnite restarted. Deriving it from the live mixer
        // makes that state impossible; a stored bool would bring the bug back.
        [Test]
        public void InMixerIsDerivedFromTheLiveMixer()
        {
            var text = ReadSource("src", "Services", "NAudioMusicPlayer.cs");

            StringAssert.Contains("IsInMixer => _mixerHoldingSong != null && _mixerHoldingSong == _mixer;", text);
            StringAssert.DoesNotContain("bool _isInMixer", text);
        }

        // Play() after an idle/lock/suspend release used to return silently with no device, leaving the fader's
        // fade-in polling forever.
        [Test]
        public void PlayReopensAReleasedDevice()
        {
            var text = ReadSource("src", "Services", "NAudioMusicPlayer.cs");

            var start = text.IndexOf("public void Play()");
            Assert.Greater(start, -1);
            var body = text.Substring(start, text.IndexOf("AddSongToMixer();", start) - start);
            StringAssert.Contains("EnsurePersistentLayer();", body);
        }
    }
}
