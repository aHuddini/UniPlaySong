using System.IO;
using NUnit.Framework;

namespace UniPlaySong.Tests.Services
{
    // Opening the settings dialog must show the settings actually in effect.
    //
    // Playnite calls GetSettings once and the view model is cached for the life of the plugin, so
    // its settings object is whatever was loaded at startup. Everything that changes settings from
    // outside the dialog REPLACES that object instead of mutating it -- the Fullscreen quick menu,
    // a theme's {PluginSettings} binding, the Desktop Calm Down button, all of which go through
    // SettingsService.UpdateSettings. The view model kept editing the startup copy, and EndEdit
    // wrote it back over the newer one.
    //
    // Reported as "saving any setting turns Calm Down off": the moon lit, the audio dimmed, and the
    // next unrelated save in the dialog silently reverted it. Calm Down is only the change you can
    // hear -- every out-of-dialog setting had the same fate.
    [TestFixture]
    public class SettingsDialogAdoptsLiveSettingsTests
    {
        private static string ReadSource(string relative)
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", relative);
            if (!File.Exists(path)) Assert.Ignore($"{relative} not reachable from the test output directory");
            return File.ReadAllText(path);
        }

        [Test]
        public void BeginEditTakesTheLiveSettingsObject()
        {
            var text = ReadSource(Path.Combine("src", "UniPlaySongSettingsViewModel.cs"));

            var begin = text.IndexOf("public void BeginEdit()");
            Assert.Greater(begin, -1, "the dialog-open hook should exist");

            var body = text.Substring(begin, 1600);
            StringAssert.Contains("GetSettingsService()?.Current", body,
                "the dialog must adopt what is in effect, not the copy loaded at startup");
            StringAssert.Contains("Settings = live", body,
                "assigned through the property so the PropertyChanged subscription moves with it");
        }

        [Test]
        public void TheViewModelIsCachedWhichIsWhyThisIsNeeded()
        {
            // If GetSettings ever starts returning a fresh view model per open, the adoption above
            // becomes redundant rather than wrong -- but while it returns a cached one, removing it
            // brings the bug straight back.
            var text = ReadSource(Path.Combine("src", "UniPlaySong.cs"));

            var getter = text.IndexOf("public override ISettings GetSettings(");
            Assert.Greater(getter, -1);

            var body = text.Substring(getter, 400);
            StringAssert.Contains("_settingsViewModel", body,
                "the same view model instance is handed back for every settings dialog");
        }

        [Test]
        public void TheSubscriptionMovesWithTheObject()
        {
            // The Settings setter unsubscribes the old object and subscribes the new one. Adopting
            // by assigning the FIELD would leave the startup object subscribed forever -- the leak
            // CancelEdit's comment already warns about.
            var text = ReadSource(Path.Combine("src", "UniPlaySongSettingsViewModel.cs"));

            var begin = text.IndexOf("public void BeginEdit()");
            var body = text.Substring(begin, 1600);

            Assert.IsFalse(body.Contains("settings = live;"),
                "assign through the Settings property, not the backing field");
        }
    }
}
