using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Spotify auto-launch graduated out of Experimental in 1.8.7 and now lives on
    // General -> Miscellaneous.
    //
    // Graduating a setting is mostly a move, and a move is exactly where a setting ends up resetting
    // from a button the user never associated with it: SettingsResetCoverageTests only checks that a
    // setting is filed somewhere, not that its group matches the page it appears on.
    [TestFixture]
    public class SpotifyAutoLaunchGraduationTests
    {
        private static IReadOnlyDictionary<string, string[]> Map =>
            typeof(SettingsGroups)
                .GetField("Map", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(null) as IReadOnlyDictionary<string, string[]>;

        [Test]
        public void ItStaysOffByDefault()
        {
            // Launching another application on startup is not something to opt someone into.
            Assert.IsFalse(new UniPlaySongSettings().AutoLaunchSpotifyOnStartup);
        }

        [Test]
        public void BothSettingsResetWithGeneral()
        {
            // The toggle and its optional path are one block on one page, so they must reset together
            // and with the group that page belongs to.
            var map = Map;
            Assert.NotNull(map);

            foreach (var name in new[]
            {
                nameof(UniPlaySongSettings.AutoLaunchSpotifyOnStartup),
                nameof(UniPlaySongSettings.SpotifyExePath),
            })
            {
                Assert.IsTrue(map["General"].Contains(name), $"{name} should reset with General");

                foreach (var group in map.Where(g => g.Key != "General"))
                    Assert.IsFalse(group.Value.Contains(name),
                        $"{name} must not also reset with {group.Key} — it left Advanced when it left the Experimental page");
            }
        }

        [Test]
        public void ItIsOffTheExperimentalPage()
        {
            // Leaving the control behind on Experimental would give the same setting two homes, and
            // the two copies would disagree about which reset button clears it.
            var experimental = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "src", "Controls", "Settings", "ExperimentalPage.xaml");
            var general = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "src", "Controls", "Settings", "GeneralMiscellaneousPage.xaml");

            if (!File.Exists(experimental) || !File.Exists(general))
                Assert.Ignore("settings pages not reachable from the test output directory");

            StringAssert.DoesNotContain("AutoLaunchSpotifyOnStartup", File.ReadAllText(experimental),
                "the graduated setting should no longer appear on the Experimental page");
            StringAssert.Contains("AutoLaunchSpotifyOnStartup", File.ReadAllText(general),
                "it lives on General -> Miscellaneous now");
        }
    }
}
