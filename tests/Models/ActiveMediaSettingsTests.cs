using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Models;

namespace UniPlaySong.Tests.Models
{
    [TestFixture]
    public class ActiveMediaSettingsTests
    {
        [Test]
        public void SettingActiveMediaSourceName_Null_CoalescesToEmpty()
        {
            var s = new UniPlaySongSettings();
            s.ActiveMediaSourceName = null;
            Assert.AreEqual(string.Empty, s.ActiveMediaSourceName);
        }
    }
}
