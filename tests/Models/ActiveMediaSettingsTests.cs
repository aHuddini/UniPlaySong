using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Models;

namespace UniPlaySong.Tests.Models
{
    [TestFixture]
    public class ActiveMediaSettingsTests
    {
        [Test]
        public void NewActiveMediaProps_DefaultToEmptyOrZero()
        {
            var s = new UniPlaySongSettings();
            Assert.AreEqual(0.0, s.ActiveMediaProgress);
            Assert.AreEqual(string.Empty, s.ActiveMediaPositionText);
            Assert.AreEqual(string.Empty, s.ActiveMediaDurationText);
            Assert.AreEqual(0.0, s.ActiveMediaVolume);
            Assert.IsFalse(s.ActiveMediaIsPlaying);
            Assert.AreEqual(string.Empty, s.ActiveMediaSourceName);
            Assert.AreEqual(ActiveMediaSourceKind.None, s.ActiveMediaSourceKind);
            Assert.IsFalse(s.ActiveMediaHasMedia);
            Assert.IsFalse(s.ActiveMediaCanNext);
            Assert.IsFalse(s.ActiveMediaCanPrevious);
        }

        [Test]
        public void SettingActiveMediaSourceName_Null_CoalescesToEmpty()
        {
            var s = new UniPlaySongSettings();
            s.ActiveMediaSourceName = null;
            Assert.AreEqual(string.Empty, s.ActiveMediaSourceName);
        }

        [Test]
        public void SettingActiveMediaProps_RaisesPropertyChanged()
        {
            var s = new UniPlaySongSettings();
            string raised = null;
            s.PropertyChanged += (o, e) => raised = e.PropertyName;
            s.ActiveMediaIsPlaying = true;
            Assert.AreEqual(nameof(UniPlaySongSettings.ActiveMediaIsPlaying), raised);
        }
    }
}
