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
        public void ShouldYieldToSelectedGame_TruthTable()
        {
            var cases = new[]
            {
                new { Details = true, Songs = 3, Settings = Settings(true, true), Expected = true, Why = "both options on, Details view, game has songs" },
                new { Details = false, Songs = 3, Settings = Settings(true, true), Expected = false, Why = "list view - radio keeps playing while browsing" },
                // Yielding to nothing would drop the user into silence; the radio continues instead.
                new { Details = true, Songs = 0, Settings = Settings(true, true), Expected = false, Why = "selected game has no songs" },
                // Radio Mode alone keeps its pre-existing behaviour: it ignores game switches.
                new { Details = true, Songs = 3, Settings = Settings(false, true), Expected = false, Why = "PlayOnlyOnGameSelect off" },
                // Radio off = the normal-playback path already honours PlayOnlyOnGameSelect itself.
                new { Details = true, Songs = 3, Settings = Settings(true, false), Expected = false, Why = "radio off" },
                new { Details = true, Songs = 3, Settings = (UniPlaySongSettings)null, Expected = false, Why = "settings null" },
                // Desktop mode reaches the policy with isFullscreenDetailsView=false (GetActiveFullscreenView
                // returns null there), so Desktop radio behaviour is unchanged - pinned so a future refactor
                // of that call site can't quietly turn this into a Desktop feature.
                new { Details = false, Songs = 5, Settings = Settings(true, true), Expected = false, Why = "Desktop mode" },
            };
            Assert.Multiple(() =>
            {
                foreach (var c in cases)
                    Assert.That(RadioGameSelectPolicy.ShouldYieldToSelectedGame(
                            isFullscreenDetailsView: c.Details, selectedGameSongCount: c.Songs, settings: c.Settings),
                        Is.EqualTo(c.Expected), $"details={c.Details} songs={c.Songs}: {c.Why}");
            });
        }
    }
}
