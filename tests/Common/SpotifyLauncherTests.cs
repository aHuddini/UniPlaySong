using NUnit.Framework;
using UniPlaySong.Common;

namespace UniPlaySong.Tests.Common
{
    [TestFixture]
    public class SpotifyLauncherTests
    {
        // Auto-scan candidate exists -> returned, user path ignored.
        [Test]
        public void ResolveSpotifyPath_AutoScanHit_ReturnsAutoScan()
        {
            var result = SpotifyLauncher.ResolveSpotifyPath(
                userConfiguredPath: @"C:\User\Custom\Spotify.exe",
                autoScanCandidate: @"C:\AppData\Spotify\Spotify.exe",
                fileExists: p => p == @"C:\AppData\Spotify\Spotify.exe");
            Assert.AreEqual(@"C:\AppData\Spotify\Spotify.exe", result);
        }

        // Auto-scan misses, user path exists -> user path (covers .exe and .lnk equally).
        [Test]
        public void ResolveSpotifyPath_AutoScanMiss_UserPathExists_ReturnsUserPath()
        {
            var result = SpotifyLauncher.ResolveSpotifyPath(
                userConfiguredPath: @"C:\User\Spotify.lnk",
                autoScanCandidate: @"C:\AppData\Spotify\Spotify.exe",
                fileExists: p => p == @"C:\User\Spotify.lnk");
            Assert.AreEqual(@"C:\User\Spotify.lnk", result);
        }

        // Both miss -> null.
        [Test]
        public void ResolveSpotifyPath_BothMiss_ReturnsNull()
        {
            var result = SpotifyLauncher.ResolveSpotifyPath(
                userConfiguredPath: @"C:\User\Nope.exe",
                autoScanCandidate: @"C:\AppData\Spotify\Spotify.exe",
                fileExists: p => false);
            Assert.IsNull(result);
        }

        // Empty/null user path + auto-scan miss -> null (no crash on empty).
        [Test]
        public void ResolveSpotifyPath_EmptyUserPath_AutoScanMiss_ReturnsNull()
        {
            var result = SpotifyLauncher.ResolveSpotifyPath(
                userConfiguredPath: "",
                autoScanCandidate: @"C:\AppData\Spotify\Spotify.exe",
                fileExists: p => false);
            Assert.IsNull(result);
        }

        // Launch on a definitely-invalid path returns false, never throws (fail-safe contract).
        [Test]
        public void Launch_InvalidPath_ReturnsFalseNeverThrows()
        {
            Assert.DoesNotThrow(() =>
            {
                var ok = SpotifyLauncher.Launch(@"Z:\definitely\not\real\nothing.exe");
                Assert.IsFalse(ok);
            });
        }
    }
}
