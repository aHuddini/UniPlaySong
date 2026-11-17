using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Playnite.SDK.Plugins;
using UniPlaySong.Menus;

namespace UniPlaySong.Tests.Services
{
    // Settings -> UI can hide any group of game-menu entries; a separator they leave first, last or doubled in
    // its submenu must go, while separators between kept entries, and other submenus, stay as they were.
    [TestFixture]
    public class GameMenuSeparatorTests
    {
        private static GameMenuItem Item(string description, string section = "UPS") =>
            new GameMenuItem { Description = description, MenuSection = section };

        [Test]
        public void HiddenEntries_LeaveNoStraySeparators()
        {
            var items = new List<GameMenuItem>
            {
                Item("-"),                 // first in UPS: dropped
                Item("Download Music"),
                Item("-"),
                Item("-"),                 // doubled: dropped
                Item("Open Music Folder"),
                Item("Normalize", "UPS|Audio"),
                Item("-", "UPS|Audio"),    // last in its submenu: dropped
                Item("-"),                 // last in UPS: dropped
            };

            var kept = GameMenuBuilder.TidySeparators(items).Select(i => $"{i.MenuSection}:{i.Description}");

            CollectionAssert.AreEqual(
                new[] { "UPS:Download Music", "UPS:-", "UPS:Open Music Folder", "UPS|Audio:Normalize" },
                kept);
        }
    }
}
