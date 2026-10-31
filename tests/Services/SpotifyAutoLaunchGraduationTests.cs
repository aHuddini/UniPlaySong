using System.IO;
using NUnit.Framework;

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
