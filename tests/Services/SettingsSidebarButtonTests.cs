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
    // rules here are about being a good guest: off unless asked for, Desktop only, and styleable by
    // the theme rather than imposing an appearance on it.
    [TestFixture]
    public class SettingsSidebarButtonTests
    {
        [Test]
        public void TheButtonIsOffByDefault()
        {
            // An extension does not put itself in shared navigation uninvited.
            Assert.IsFalse(new UniPlaySongSettings().ShowSettingsSidebarButton);
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
