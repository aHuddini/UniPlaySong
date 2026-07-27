using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class RadioPlayThroughPolicyTests
    {
        // Settings with the feature fully armed for the UPS pool radio branch.
        private static UniPlaySongSettings ArmedPool()
        {
            return new UniPlaySongSettings
            {
                RadioPlaysThroughGames = true,
                RadioModeEnabled = true,
                RadioMusicSource = RadioMusicSource.FullLibrary,
            };
        }

        // Settings armed for the Spotify radio branch. SpotifyRadioMode is derived
        // (RadioModeEnabled && source == Spotify), so it cannot be set directly.
        private static UniPlaySongSettings ArmedSpotify()
        {
            return new UniPlaySongSettings
            {
                RadioPlaysThroughGames = true,
                RadioModeEnabled = true,
                RadioMusicSource = RadioMusicSource.Spotify,
            };
        }

        [Test]
        public void Suppresses_GameSessionSources_WhenArmed_PoolRadio()
        {
            var s = ArmedPool();
            Assert.IsTrue(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.GameStarting, true, true, s));
            Assert.IsTrue(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.FocusLoss, true, true, s));
            Assert.IsTrue(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.Minimized, true, true, s));
            Assert.IsTrue(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.SystemTray, true, true, s));
            Assert.IsTrue(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.Idle, true, true, s));
            Assert.IsTrue(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.ExternalAudio, true, true, s));
        }

        [Test]
        public void Suppresses_WhenArmed_SpotifyRadio_EvenThoughPoolFlagIsFalse()
        {
            // Spotify radio clears _isInRadioMode by construction, so isInRadioMode is false here.
            var s = ArmedSpotify();
            Assert.IsTrue(s.SpotifyRadioMode, "guard: SpotifyRadioMode should derive true");
            Assert.IsTrue(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.GameStarting, true, false, s));
        }

        [Test]
        public void NeverSuppresses_ProtectedSources()
        {
            var s = ArmedPool();
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.Manual, true, true, s));
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.SystemLock, true, true, s));
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.Video, true, true, s));
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.ThemeOverlay, true, true, s));
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.Dashboard, true, true, s));
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.Jingle, true, true, s));
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.NsfPreview, true, true, s));
        }

        [Test]
        public void DoesNotSuppress_WhenSettingOff()
        {
            var s = ArmedPool();
            s.RadioPlaysThroughGames = false;
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.GameStarting, true, true, s));
        }

        [Test]
        public void DoesNotSuppress_WhenRadioModeOff()
        {
            var s = ArmedPool();
            s.RadioModeEnabled = false;
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.GameStarting, true, true, s));
        }

        [Test]
        public void DoesNotSuppress_WhenNoGameSession()
        {
            var s = ArmedPool();
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.FocusLoss, false, true, s));
        }

        // The "radio only" guarantee: radio yielded to an installed game's own music,
        // so _isInRadioMode is false and Spotify radio is off. Game music must still pause.
        [Test]
        public void DoesNotSuppress_WhenRadioYieldedToGameMusic()
        {
            var s = ArmedPool();
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.GameStarting, true, false, s));
        }

        [Test]
        public void DoesNotSuppress_WhenSettingsNull()
        {
            Assert.IsFalse(RadioPlayThroughPolicy.ShouldSuppress(PauseSource.GameStarting, true, true, null));
        }
    }
}
