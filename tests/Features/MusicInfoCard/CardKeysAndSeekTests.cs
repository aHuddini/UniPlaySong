using System.Windows.Input;
using NUnit.Framework;
using Playnite.SDK.Events;
using UniPlaySong.Features.MusicInfoCard.Player;

namespace UniPlaySong.Tests.Features.MusicInfoCard
{
    [TestFixture]
    public class CardKeysAndSeekTests
    {
        // In Fullscreen Playnite fakes PageUp/PageDown for LT/RT (and arrows for the D-pad): mapping them would make
        // every trigger also switch the page (it did: RT jumped to Details). Keyboard pages are Q/E, seeking [ and ].
        [Test]
        public void KeyMap_LeavesTheTriggersFakeKeysAlone()
        {
            Assert.IsNull(CardKeyMap.ToInput(Key.PageUp));
            Assert.IsNull(CardKeyMap.ToInput(Key.PageDown));
            Assert.AreEqual(ControllerInput.LeftShoulder, CardKeyMap.ToInput(Key.Q));
            Assert.AreEqual(ControllerInput.RightShoulder, CardKeyMap.ToInput(Key.E));
            Assert.AreEqual(ControllerInput.TriggerLeft, CardKeyMap.ToInput(Key.OemOpenBrackets));
            Assert.AreEqual(ControllerInput.TriggerRight, CardKeyMap.ToInput(Key.OemCloseBrackets));
            Assert.AreEqual(ControllerInput.DPadDown, CardKeyMap.ToInput(Key.Down));
            Assert.AreEqual(ControllerInput.A, CardKeyMap.ToInput(Key.Enter));
            Assert.AreEqual(ControllerInput.B, CardKeyMap.ToInput(Key.Escape));
        }

        // Holding a trigger: Playnite resends the press (after 0.7 s, then every 80 ms) on top of the card's own repeat,
        // which made the seek speed jump. While held, a resent press in the same direction is ignored.
        [Test]
        public void HeldSeek_IgnoresResentPressesWhileHeld()
        {
            var held = new HeldSeek();
            Assert.IsTrue(held.Begin(10));
            Assert.IsFalse(held.Begin(10));    // Playnite's repeat
            Assert.IsTrue(held.Begin(-10));    // the other trigger
            held.End();
            Assert.IsFalse(held.IsHeld);
            Assert.IsTrue(held.Begin(10));
        }
    }
}
