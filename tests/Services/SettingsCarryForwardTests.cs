using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class SettingsCarryForwardTests
    {
        // A save carries live state onto the new settings object, but not a property that is only a view of saved
        // ones: copying the old "Radio plays Spotify" back undid every switch made through a save (the hover card's
        // Spotify button did nothing). Live state still carries.
        [Test]
        public void CarryRuntimeState_KeepsLiveState_NotViewsOfSavedSettings()
        {
            var old = new UniPlaySongSettings { RadioModeEnabled = true, RadioMusicSource = RadioMusicSource.CustomRotation, NowPlayingTitle = "Area C Painting" };
            var saved = new UniPlaySongSettings { RadioModeEnabled = true, RadioMusicSource = RadioMusicSource.CustomRotation };
            saved.SwitchRadioMode = true;
            saved.CookieMode = CookieMode.Firefox;

            SettingsService.CarryRuntimeState(old, saved);

            Assert.AreEqual(RadioMusicSource.Spotify, saved.RadioMusicSource);
            Assert.AreEqual(CookieMode.Firefox, saved.CookieMode);
            Assert.AreEqual("Area C Painting", saved.NowPlayingTitle);
        }
    }
}
