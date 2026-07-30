using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // "Play Only on Game Select" while Radio Mode is on: radio is the browsing layer, the selected
    // game's music takes over in Details view. Before v1.7.0 both radio branches returned before the
    // PlayOnlyOnGameSelect gate ever ran, so the setting silently did nothing with radio enabled.
    [TestFixture]
    public class RadioGameSelectPolicyTests
    {
        private static UniPlaySongSettings Settings(bool playOnSelect, bool radio) =>
            new UniPlaySongSettings { PlayOnlyOnGameSelect = playOnSelect, RadioModeEnabled = radio };

        [Test]
        public void Yields_WhenBothOptionsOn_InDetailsView_WithSongs()
        {
            Assert.IsTrue(RadioGameSelectPolicy.ShouldYieldToSelectedGame(
                isFullscreenDetailsView: true, selectedGameSongCount: 3,
                settings: Settings(playOnSelect: true, radio: true)));
        }

        [Test]
        public void DoesNotYield_InListView_SoRadioKeepsPlayingWhileBrowsing()
        {
            Assert.IsFalse(RadioGameSelectPolicy.ShouldYieldToSelectedGame(
                isFullscreenDetailsView: false, selectedGameSongCount: 3,
                settings: Settings(playOnSelect: true, radio: true)));
        }

        [Test]
        public void DoesNotYield_WhenSelectedGameHasNoSongs()
        {
            // Yielding to nothing would drop the user into silence; the radio continues instead.
            Assert.IsFalse(RadioGameSelectPolicy.ShouldYieldToSelectedGame(
                isFullscreenDetailsView: true, selectedGameSongCount: 0,
                settings: Settings(playOnSelect: true, radio: true)));
        }

        [Test]
        public void DoesNotYield_WhenPlayOnlyOnGameSelectIsOff()
        {
            // Radio Mode alone keeps its pre-existing behaviour: it ignores game switches.
            Assert.IsFalse(RadioGameSelectPolicy.ShouldYieldToSelectedGame(
                isFullscreenDetailsView: true, selectedGameSongCount: 3,
                settings: Settings(playOnSelect: false, radio: true)));
        }

        [Test]
        public void DoesNotYield_WhenRadioIsOff()
        {
            // Radio off = the normal-playback path already honours PlayOnlyOnGameSelect itself.
            Assert.IsFalse(RadioGameSelectPolicy.ShouldYieldToSelectedGame(
                isFullscreenDetailsView: true, selectedGameSongCount: 3,
                settings: Settings(playOnSelect: true, radio: false)));
        }

        [Test]
        public void DoesNotYield_WhenSettingsNull()
        {
            Assert.IsFalse(RadioGameSelectPolicy.ShouldYieldToSelectedGame(true, 3, null));
        }

        // Desktop mode reaches the policy with isFullscreenDetailsView=false (GetActiveFullscreenView
        // returns null there), so Desktop radio behaviour is unchanged — pinned so a future refactor
        // of that call site can't quietly turn this into a Desktop feature.
        [Test]
        public void DoesNotYield_InDesktopMode()
        {
            Assert.IsFalse(RadioGameSelectPolicy.ShouldYieldToSelectedGame(
                isFullscreenDetailsView: false, selectedGameSongCount: 5,
                settings: Settings(playOnSelect: true, radio: true)));
        }
    }
}
