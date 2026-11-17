using NUnit.Framework;
using UniPlaySong;

namespace UniPlaySong.Tests.Services
{
    // System beep was removed as a sound choice in v1.7.4, but its enum member stays as a placeholder:
    // these values persist to config.json as integers, so deleting the member outright would turn every
    // stored BundledJingle (1) into CustomFile and every CustomFile (2) into an out-of-range value.
    // MigrateSettings remaps a stored 0 to BundledJingle.
    [TestFixture]
    public class SystemBeepRemovalTests
    {
        [Test]
        public void EnumNumberingIsUnchanged()
        {
            Assert.AreEqual(0, (int)CelebrationSoundType.SystemBeep_Removed,
                "the removed member holds slot 0 so the others keep their stored values");
            Assert.AreEqual(1, (int)CelebrationSoundType.BundledJingle);
            Assert.AreEqual(2, (int)CelebrationSoundType.CustomFile);
        }
    }
}
