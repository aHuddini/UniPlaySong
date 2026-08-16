using System;
using NUnit.Framework;
using UniPlaySong;

namespace UniPlaySong.Tests.Services
{
    // System beep was removed as a sound choice in v1.7.4. Two things have to stay true for that
    // removal not to break existing installs, and neither is visible from the UI:
    //
    //   1. The enum's NUMBERING must not shift. These persist to config.json as integers, so
    //      deleting the member outright would turn every stored BundledJingle (1) into CustomFile
    //      and every CustomFile (2) into an out-of-range value.
    //   2. A user who had system beep selected must land on a real sound, or their notification
    //      silently stops working with nothing in the UI to explain why.
    [TestFixture]
    public class SystemBeepRemovalTests
    {
        // The wire format. If these numbers change, every existing config.json is reinterpreted.
        [Test]
        public void EnumNumberingIsUnchanged()
        {
            Assert.AreEqual(0, (int)CelebrationSoundType.SystemBeep_Removed,
                "the removed member holds slot 0 so the others keep their stored values");
            Assert.AreEqual(1, (int)CelebrationSoundType.BundledJingle);
            Assert.AreEqual(2, (int)CelebrationSoundType.CustomFile);
        }

        // Nothing should ever select the removed value on a fresh install.
        [Test]
        public void FreshSettings_NeverSelectTheRemovedValue()
        {
            var s = new UniPlaySongSettings();

            Assert.AreEqual(CelebrationSoundType.BundledJingle, s.CelebrationSoundType);
            Assert.AreEqual(CelebrationSoundType.BundledJingle, s.AbandonedSoundType);
            Assert.AreEqual(CelebrationSoundType.BundledJingle, s.AchievementSoundType);
            Assert.AreEqual(CelebrationSoundType.BundledJingle, s.ControlUpDetectSoundType);
        }

        // What an upgrading user's config deserializes to: a stored 0 is still a valid enum value,
        // so it arrives intact and the migration is what has to catch it.
        [Test]
        public void AStoredZero_StillDeserializesToTheRemovedMember()
        {
            var s = new UniPlaySongSettings
            {
                CelebrationSoundType = (CelebrationSoundType)0
            };

            Assert.AreEqual(CelebrationSoundType.SystemBeep_Removed, s.CelebrationSoundType,
                "the value survives the round trip, which is exactly why MigrateSettings must remap it");
        }
    }
}
