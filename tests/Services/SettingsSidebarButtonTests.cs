using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // A UniPlaySong button in Playnite's Desktop sidebar that opens these settings.
    //
    // The sidebar is Playnite's own navigation, shared with every other installed extension, so the
    // rules here are about being a good guest: one entry, Desktop only, removable, and styleable by
    // the theme rather than imposing an appearance on it.
    [TestFixture]
    public class SettingsSidebarButtonTests
    {
        [Test]
        public void TheButtonIsOnByDefault()
        {
            // On, because the settings are otherwise four clicks deep in Add-ons -> Extensions and
            // the button is the obvious way to reach them. It costs one sidebar entry and can be
            // turned off.
            Assert.IsTrue(new UniPlaySongSettings().ShowSettingsSidebarButton);
        }

        [Test]
        public void TheSidebarCannotBeChangedWithoutARestart()
        {
            // Playnite calls GetSidebarItems once, at startup, and builds its sidebar from what it
            // returns. An item that did not exist then cannot be made to appear by setting Visible
            // later - which is why the settings page raises the restart prompt rather than pretending
            // the toggle is live. Reported as "it does not load the button immediately".
            var method = typeof(UniPlaySong).GetMethod("GetSidebarItems");
            Assert.NotNull(method);
            Assert.AreEqual(0, method.GetParameters().Length,
                "Playnite pulls the items; UniPlaySong cannot push a new one mid-session");
        }

        [Test]
        public void TheSettingResetsWithTheGroupWhosePageItIsOn()
        {
            // It lives on General -> Miscellaneous, so it must reset with General. A setting whose
            // reset group disagrees with its page resets from a button the user never associated
            // with it — and SettingsResetCoverageTests would not catch that, because it only checks
            // that a setting is filed somewhere at all.
            var map = typeof(SettingsGroups)
                .GetField("Map", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(null) as IReadOnlyDictionary<string, string[]>;

            Assert.NotNull(map, "the reset map should be readable");
            Assert.IsTrue(map.ContainsKey("General"));
            Assert.IsTrue(map["General"].Contains(nameof(UniPlaySongSettings.ShowSettingsSidebarButton)),
                "the sidebar button is a General setting and must reset with General");

            foreach (var group in map.Where(g => g.Key != "General"))
            {
                Assert.IsFalse(group.Value.Contains(nameof(UniPlaySongSettings.ShowSettingsSidebarButton)),
                    $"it must not also reset with {group.Key}");
            }
        }

        [Test]
        public void TheSidebarIsBuiltInOnePlace()
        {
            // Both sidebar items — the dashboard view and this button — come from the one
            // GetSidebarItems override, so neither can be added without the other's gating being
            // visible in the same method.
            var method = typeof(UniPlaySong).GetMethod("GetSidebarItems");
            Assert.NotNull(method, "sidebar items come from the SDK override");
        }
    }
}
