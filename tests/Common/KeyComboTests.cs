using System.Windows.Input;
using NUnit.Framework;
using UniPlaySong.Common;

namespace UniPlaySong.Tests.Common
{
    [TestFixture]
    public class KeyComboTests
    {
        // A shortcut is saved as text and registered system-wide, so it must survive the round trip, map to the right
        // Win32 values, and refuse keys that would stop working for typing in every other app
        [Test]
        public void ParsesFormatsAndRefusesTypingKeys()
        {
            Assert.IsTrue(KeyCombo.TryParse("Ctrl+Alt+P", out var c));
            Assert.AreEqual("Ctrl+Alt+P", c.ToString());
            Assert.AreEqual(0x1u | 0x2u, c.Win32Modifiers);

            Assert.IsTrue(KeyCombo.TryParse("MediaPlayPause", out var m));
            Assert.AreEqual(ModifierKeys.None, m.Modifiers);
            Assert.AreEqual(0xB3u, m.VirtualKey);

            Assert.IsTrue(KeyCombo.TryCreate(ModifierKeys.None, Key.F9, out _));
            Assert.IsFalse(KeyCombo.TryCreate(ModifierKeys.None, Key.P, out _));
            Assert.IsFalse(KeyCombo.TryCreate(ModifierKeys.Control, Key.LeftShift, out _));
            Assert.IsFalse(KeyCombo.TryParse("", out _));
        }
    }
}
