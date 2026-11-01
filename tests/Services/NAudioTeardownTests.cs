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

        // After any teardown (device error, idle, lock, suspend) a stale _isInMixer = true made Play() skip adding
        // every later song to the rebuilt mixer - silence until Playnite restarted.
        [Test]
        public void TearingDownTheLayerClearsIsInMixer()
        {
            var text = ReadSource("src", "Services", "NAudioMusicPlayer.cs");

            var start = text.IndexOf("private void TearDownPersistentLayer()");
            Assert.Greater(start, -1);
            var body = text.Substring(start, text.IndexOf("_persistentLayerInitialized = false;", start) - start);
            StringAssert.Contains("_isInMixer = false;", body);
        }
    }
}
