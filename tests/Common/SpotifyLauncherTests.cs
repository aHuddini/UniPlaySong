using System;
using System.Collections.Generic;
using NUnit.Framework;
using UniPlaySong.Common;

namespace UniPlaySong.Tests.Common
{
    [TestFixture]
    public class SpotifyLauncherTests
    {
        [Test]
        public void ResolveSpotifyPath_Table()
        {
            const string user = @"C:\User\Custom\Spotify.exe";
            const string appData = @"C:\AppData\Spotify\Spotify.exe";
            const string alias = @"C:\Alias\Spotify.exe";
            var both = new[] { appData, alias };
            var cases = new[]
            {
                // Explicit intent wins over auto-scan: both would exist; the user path must win.
                new { Name = "user path exists", User = user, Candidates = (IEnumerable<string>)new[] { appData }, Exists = (Func<string, bool>)(p => true), Expected = user },
                new { Name = "no user path, first candidate hit", User = "", Candidates = (IEnumerable<string>)both, Exists = (Func<string, bool>)(p => p == appData), Expected = appData },
                // Ordered fallback.
                new { Name = "first candidate miss, second hit", User = "", Candidates = (IEnumerable<string>)both, Exists = (Func<string, bool>)(p => p == alias), Expected = alias },
                new { Name = "user path and all candidates miss", User = @"C:\User\Nope.exe", Candidates = (IEnumerable<string>)both, Exists = (Func<string, bool>)(p => false), Expected = (string)null },
                // No crash on an empty list.
                new { Name = "empty user path, no candidates", User = "", Candidates = (IEnumerable<string>)new List<string>(), Exists = (Func<string, bool>)(p => false), Expected = (string)null },
                // A null candidate list is tolerated (fail-safe).
                new { Name = "null candidates", User = "", Candidates = (IEnumerable<string>)null, Exists = (Func<string, bool>)(p => false), Expected = (string)null },
            };
            Assert.Multiple(() =>
            {
                foreach (var c in cases)
                    Assert.AreEqual(c.Expected, SpotifyLauncher.ResolveSpotifyPath(
                            userConfiguredPath: c.User, autoScanCandidates: c.Candidates, fileExists: c.Exists),
                        c.Name);
            });
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
