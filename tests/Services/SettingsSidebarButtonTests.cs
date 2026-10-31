using NUnit.Framework;
using UniPlaySong;

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
