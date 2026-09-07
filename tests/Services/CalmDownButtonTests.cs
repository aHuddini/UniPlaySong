using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.DeskMediaControl;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // A moon button on the Desktop top panel that toggles Calm Down Mode.
    //
    // Fullscreen has had this on its quick menu since 1.5.0. Desktop only had the settings
    // checkbox, which is the wrong place for something reached for because the music is too much
    // right now.
    [TestFixture]
    public class CalmDownButtonTests
    {
        private static string Src(params string[] parts)
        {
            var joined = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "src");
            foreach (var part in parts) joined = Path.Combine(joined, part);
            return joined;
        }

        [Test]
        public void TheMoonGlyphIsTheOneInPlaynitesFont()
        {
            // U+EF9E is the glyph named "moon" in the icofont.ttf Playnite bundles - read out of the
            // font's cmap, not recalled. A wrong code does not fail loudly; it renders as a blank
            // box or some unrelated pictogram in everyone's top panel.
            Assert.AreEqual("", MediaControlIcons.Moon);
        }

        [Test]
        public void TheButtonIsOffByDefault()
        {
            // The top panel is shared with themes and other extensions, so a new button is offered
            // rather than imposed. Nobody's panel changes on upgrade.
            Assert.IsFalse(new UniPlaySongSettings().ShowCalmDownButton);
        }

        [Test]
        public void TheButtonAppearsWithoutARestart()
        {
            // Playnite builds its top panel from what GetTopPanelItems returns, so an item yielded
            // only when its setting is on could not be turned on again mid-session. The item is
            // always yielded and Visible carries the setting - the pattern the visualizer and peak
            // meter already use, and the reason this needs no restart prompt.
            var path = Src("DeskMediaControl", "TopPanelMediaControlViewModel.cs");
            if (!File.Exists(path)) Assert.Ignore("view model source not reachable from the test output directory");

            var text = File.ReadAllText(path);
            var method = text.IndexOf("public IEnumerable<TopPanelItem> GetTopPanelItems()");
            Assert.Greater(method, -1);

            var body = text.Substring(method, text.IndexOf("public TopPanelMediaControlViewModel(") - method);
            StringAssert.Contains("_calmDownItem.Visible = settings?.ShowCalmDownButton == true", body,
                "visibility must come from the setting on each refresh");
        }

        [Test]
        public void EnablingTheButtonRefreshesTheTopPanelImmediately()
        {
            // TopPanelItem.Visible is observable, so flipping it updates Playnite's panel live - but
            // only if something flips it. UpdateIcons ran on playback events alone, so enabling the
            // setting did nothing visible until the next song change, and the button looked like it
            // needed a restart. Reported as exactly that.
            //
            // The visualizer and peak meter never hit this because their toggles force a player
            // rebuild, which raises music events as a side effect. Calm Down changes no audio state,
            // so it needs the settings-change path to refresh the panel itself.
            var path = Src("UniPlaySong.cs");
            if (!File.Exists(path)) Assert.Ignore("plugin source not reachable from the test output directory");

            var text = File.ReadAllText(path);
            var handler = text.IndexOf("private void OnSettingsServiceChanged(");
            Assert.Greater(handler, -1, "settings changes are handled here");

            var body = text.Substring(handler, 2000);
            StringAssert.Contains("_topPanelMediaControl?.UpdateIcons()", body,
                "a settings change must refresh the top panel, or setting-gated items need a restart");
        }

        [Test]
        public void TogglingGoesThroughTheSettingsWriterNotADirectMutation()
        {
            // Turning Calm Down on while the player is SDL2 has to swap the backend to NAudio - SDL2
            // has no post-mixer hook to host CalmDownProcessor - and that swap hangs off the
            // diff-based SettingsChanged event, which only a real UpdateSettings raises. A direct
            // mutation would light the moon with nothing dimming the audio.
            var path = Src("DeskMediaControl", "TopPanelMediaControlViewModel.cs");
            if (!File.Exists(path)) Assert.Ignore("view model source not reachable from the test output directory");

            var text = File.ReadAllText(path);
            var handler = text.IndexOf("private void OnCalmDownActivated()");
            Assert.Greater(handler, -1, "the button needs an activation handler");

            var body = text.Substring(handler, 900);
            StringAssert.Contains("_updateSettings(", body, "must route through the settings writer");
            Assert.IsFalse(body.Contains("_getSettings?.Invoke().CalmDownModeEnabled ="),
                "must not mutate the live settings object directly");
        }

        [Test]
        public void TheButtonFollowsTheSettingRatherThanItsOwnLastClick()
        {
            // Calm Down is also reachable from the settings dialog, a theme binding and the
            // Fullscreen menu. If the button tracked only its own presses it would show the wrong
            // state after any of those.
            var path = Src("DeskMediaControl", "TopPanelMediaControlViewModel.cs");
            if (!File.Exists(path)) Assert.Ignore("view model source not reachable from the test output directory");

            var text = File.ReadAllText(path);
            var refresh = text.IndexOf("public void UpdateIcons()");
            Assert.Greater(refresh, -1);

            var body = text.Substring(refresh);
            StringAssert.Contains("UpdateCalmDownVisual(settings?.CalmDownModeEnabled == true)", body,
                "the periodic refresh must re-read the setting");
        }

        [Test]
        public void TheSettingResetsWithTheGroupWhosePageItIsOn()
        {
            // It lives on the Media Controls page, which resets with General. A setting whose reset
            // group disagrees with its page resets from a button the user never associated with it,
            // and SettingsResetCoverageTests would not catch it - that only checks a setting is
            // filed somewhere at all.
            var map = typeof(SettingsGroups)
                .GetField("Map", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(null) as IReadOnlyDictionary<string, string[]>;

            Assert.NotNull(map);
            Assert.IsTrue(map["General"].Contains(nameof(UniPlaySongSettings.ShowCalmDownButton)),
                "the Media Controls page resets with General");

            foreach (var group in map.Where(g => g.Key != "General"))
                Assert.IsFalse(group.Value.Contains(nameof(UniPlaySongSettings.ShowCalmDownButton)),
                    $"it must not also reset with {group.Key}");
        }
    }
}
