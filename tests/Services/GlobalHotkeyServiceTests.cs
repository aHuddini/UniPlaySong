using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class GlobalHotkeyServiceTests
    {
        // While the settings window is open the shortcuts are suspended: a Calm Down press there would save around the
        // dialog. A save that applies bindings meanwhile must not re-take them until Resume. A second service stands in
        // for "another app" asking Windows for the same combination.
        [Test, Apartment(ApartmentState.STA)]
        public void ApplyWhileSuspendedHoldsNothingUntilResume()
        {
            Assert.IsTrue(KeyCombo.TryParse("Ctrl+Alt+Shift+F24", out var combo));
            var bindings = new Dictionary<ShortcutAction, string> { [ShortcutAction.CalmDown] = combo.ToString() };

            using (var ours = new GlobalHotkeyService(null))
            using (var other = new GlobalHotkeyService(null))
            {
                ours.Suspend();
                ours.Apply(bindings);
                Assert.IsTrue(other.IsAvailable(combo));

                ours.Resume();
                Assert.IsFalse(other.IsAvailable(combo));
            }
        }
    }
}
