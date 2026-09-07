using System.IO;
using NUnit.Framework;

namespace UniPlaySong.Tests.Services
{
    // The Music Dashboard sidebar panel needs a Playnite restart to appear or disappear, and now
    // says so (issue #95).
    //
    // Same shape as the settings sidebar button fixed in 1.8.6: Playnite calls GetSidebarItems once
    // at startup and builds its sidebar from what comes back, so an item that was not returned then
    // cannot be summoned mid-session by setting Visible.
    [TestFixture]
    public class DashboardRestartRequiredTests
    {
        private static string Src(params string[] parts)
        {
            var all = new[] { TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "src" };
            var joined = Path.Combine(all);
            foreach (var part in parts) joined = Path.Combine(joined, part);
            return joined;
        }

        [Test]
        public void TheToggleRaisesTheRestartPrompt()
        {
            var path = Src("Controls", "Settings", "ExperimentalPage.xaml");
            if (!File.Exists(path)) Assert.Ignore("settings page not reachable from the test output directory");

            var text = File.ReadAllText(path);
            var toggle = text.IndexOf("Settings.ShowMusicDashboard");
            Assert.Greater(toggle, -1, "the dashboard toggle should be on the Experimental page");

            // The CheckBox binds the command; look within the element around the binding.
            var start = text.LastIndexOf("<CheckBox", toggle);
            var end = text.IndexOf("/>", toggle);
            Assert.Greater(start, -1);
            Assert.Greater(end, toggle);

            var element = text.Substring(start, end - start);
            StringAssert.Contains("SetRestartRequired", element,
                "enabling or disabling the dashboard needs a restart, so it must raise the prompt");
        }

        [Test]
        public void TheHintSaysARestartIsNeeded()
        {
            var path = Src("Controls", "Settings", "ExperimentalPage.xaml");
            if (!File.Exists(path)) Assert.Ignore("settings page not reachable from the test output directory");

            var text = File.ReadAllText(path);
            var toggle = text.IndexOf("Settings.ShowMusicDashboard");
            Assert.Greater(toggle, -1);

            // The hint follows the toggle; the prompt alone is easy to dismiss without reading.
            var after = text.Substring(toggle, System.Math.Min(900, text.Length - toggle));
            StringAssert.Contains("Restart required", after,
                "the hint should say so too, not only the popup");
        }

        [Test]
        public void NothingPretendsTheSidebarUpdatesLive()
        {
            // The removed handler did two wrong things: it implied a live toggle that Playnite
            // cannot honour, and it subscribed to SettingsService.Current by reference - an object a
            // settings save replaces wholesale, so it stopped firing after the first save and leaked
            // onto the discarded instance.
            var path = Src("UniPlaySong.cs");
            if (!File.Exists(path)) Assert.Ignore("plugin source not reachable from the test output directory");

            var text = File.ReadAllText(path);
            var marker = "_dashboardSidebarItem.Visible = _settings?.ShowMusicDashboard";
            Assert.IsFalse(text.Contains(marker),
                "the live visibility handler cannot work and must not be reinstated");
        }
    }
}
