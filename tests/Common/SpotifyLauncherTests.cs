using System.Collections.Generic;
using NUnit.Framework;
using UniPlaySong.Common;

namespace UniPlaySong.Tests.Common
{
    [TestFixture]
    public class SpotifyLauncherTests
    {
        // User path exists -> returned (explicit intent wins over auto-scan).
        [Test]
        public void ResolveSpotifyPath_UserPathExists_ReturnsUserPathOverAutoScan()
        {
            var result = SpotifyLauncher.ResolveSpotifyPath(
                userConfiguredPath: @"C:\User\Custom\Spotify.exe",
                autoScanCandidates: new[] { @"C:\AppData\Spotify\Spotify.exe" },
                fileExists: p => true); // both would exist; user path must win
            Assert.AreEqual(@"C:\User\Custom\Spotify.exe", result);
        }

        // No user path, first auto-scan candidate exists -> returned.
        [Test]
        public void ResolveSpotifyPath_NoUserPath_FirstCandidateHit_ReturnsFirst()
        {
            var result = SpotifyLauncher.ResolveSpotifyPath(
                userConfiguredPath: "",
                autoScanCandidates: new[] { @"C:\AppData\Spotify\Spotify.exe", @"C:\Alias\Spotify.exe" },
                fileExists: p => p == @"C:\AppData\Spotify\Spotify.exe");
            Assert.AreEqual(@"C:\AppData\Spotify\Spotify.exe", result);
        }

        // First candidate misses, second exists -> returns the second (ordered fallback).
        [Test]
        public void ResolveSpotifyPath_FirstCandidateMiss_SecondHit_ReturnsSecond()
        {
            var result = SpotifyLauncher.ResolveSpotifyPath(
                userConfiguredPath: "",
                autoScanCandidates: new[] { @"C:\AppData\Spotify\Spotify.exe", @"C:\Alias\Spotify.exe" },
                fileExists: p => p == @"C:\Alias\Spotify.exe");
            Assert.AreEqual(@"C:\Alias\Spotify.exe", result);
        }

        // User path misses, auto-scan misses -> null.
        [Test]
        public void ResolveSpotifyPath_UserPathMiss_AllCandidatesMiss_ReturnsNull()
        {
            var result = SpotifyLauncher.ResolveSpotifyPath(
                userConfiguredPath: @"C:\User\Nope.exe",
                autoScanCandidates: new[] { @"C:\AppData\Spotify\Spotify.exe", @"C:\Alias\Spotify.exe" },
                fileExists: p => false);
            Assert.IsNull(result);
        }

        // Empty user path + empty candidate list -> null (no crash).
        [Test]
        public void ResolveSpotifyPath_EmptyUserPath_NoCandidates_ReturnsNull()
        {
            var result = SpotifyLauncher.ResolveSpotifyPath(
                userConfiguredPath: "",
                autoScanCandidates: new List<string>(),
                fileExists: p => false);
            Assert.IsNull(result);
        }

        // Null candidate list is tolerated (fail-safe) -> null when no user path.
        [Test]
        public void ResolveSpotifyPath_NullCandidates_ReturnsNull()
        {
            var result = SpotifyLauncher.ResolveSpotifyPath(
                userConfiguredPath: "",
                autoScanCandidates: null,
                fileExists: p => false);
            Assert.IsNull(result);
        }

        // GetAutoScanCandidates returns non-null, non-empty (the real %APPDATA% + Store-alias paths).
        [Test]
        public void GetAutoScanCandidates_ReturnsCandidates()
        {
            var candidates = SpotifyLauncher.GetAutoScanCandidates();
            Assert.IsNotNull(candidates);
            Assert.IsNotEmpty(candidates);
            // Every candidate ends with Spotify.exe.
            foreach (var c in candidates)
                Assert.IsTrue(c.EndsWith(@"Spotify.exe"), $"Unexpected candidate: {c}");
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
