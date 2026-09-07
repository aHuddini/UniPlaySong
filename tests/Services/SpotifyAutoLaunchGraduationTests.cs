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
        // Read off SettingsGroups rather than restated here: the dictionary keys are display
        // strings ("Live Effects", with the space), and a literal that drifts from the constant
        // fails as a missing key rather than as the mismatch it actually is.
        private static string GeneralKey => (string)typeof(SettingsGroups)
            .GetField("General", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();

        private static string LiveEffectsKey => (string)typeof(SettingsGroups)
            .GetField("LiveEffects", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();

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
                Assert.IsTrue(map[GeneralKey].Contains(name), $"{name} should reset with General");

                foreach (var group in map.Where(g => g.Key != GeneralKey))
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

        // Fade curves graduated the same release, onto Live Effects -> Fade Transitions.
        [Test]
        public void FadeCurvesResetWithLiveEffects()
        {
            var map = Map;
            Assert.NotNull(map);

            foreach (var name in new[]
            {
                nameof(UniPlaySongSettings.NaudioFadeInCurve),
                nameof(UniPlaySongSettings.NaudioFadeOutCurve),
            })
            {
                Assert.IsTrue(map[LiveEffectsKey].Contains(name),
                    $"{name} sits on a Live Effects page and must reset with that group");

                foreach (var group in map.Where(g => g.Key != LiveEffectsKey))
                    Assert.IsFalse(group.Value.Contains(name),
                        $"{name} must not also reset with {group.Key} — it left Advanced with the page move");
            }
        }

        [Test]
        public void FadeCurvesMovedToFadeTransitionsAndStayFolded()
        {
            var experimental = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "src", "Controls", "Settings", "ExperimentalPage.xaml");
            var fades = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "src", "Controls", "Settings", "FadeTransitionsPage.xaml");

            if (!File.Exists(experimental) || !File.Exists(fades))
                Assert.Ignore("settings pages not reachable from the test output directory");

            StringAssert.DoesNotContain("NaudioFadeInCurve", File.ReadAllText(experimental),
                "the graduated control should no longer appear on the Experimental page");

            var text = File.ReadAllText(fades);
            StringAssert.Contains("NaudioFadeInCurve", text, "it lives on Fade Transitions now");
            StringAssert.Contains("IsExpanded=\"False\"", text,
                "curve shape is a finer point than fade length, so the section opens folded");
        }
    }
}
