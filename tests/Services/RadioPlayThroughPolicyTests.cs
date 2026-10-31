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
        public void ShouldSuppress_TruthTable()
        {
            var pool = ArmedPool();
            var settingOff = ArmedPool();
            settingOff.RadioPlaysThroughGames = false;
            var radioOff = ArmedPool();
            radioOff.RadioModeEnabled = false;
            var spotify = ArmedSpotify();
            Assert.IsTrue(spotify.SpotifyRadioMode, "guard: SpotifyRadioMode should derive true");

            var cases = new[]
            {
                // Game-session sources are suppressed when armed for pool radio.
                new { Source = PauseSource.GameStarting, Session = true, InRadio = true, S = pool, Expected = true, Why = "pool radio armed" },
                new { Source = PauseSource.FocusLoss, Session = true, InRadio = true, S = pool, Expected = true, Why = "pool radio armed" },
                new { Source = PauseSource.Minimized, Session = true, InRadio = true, S = pool, Expected = true, Why = "pool radio armed" },
                new { Source = PauseSource.SystemTray, Session = true, InRadio = true, S = pool, Expected = true, Why = "pool radio armed" },
                new { Source = PauseSource.Idle, Session = true, InRadio = true, S = pool, Expected = true, Why = "pool radio armed" },
                new { Source = PauseSource.ExternalAudio, Session = true, InRadio = true, S = pool, Expected = true, Why = "pool radio armed" },
                // Spotify radio clears _isInRadioMode by construction, so isInRadioMode is false here.
                new { Source = PauseSource.GameStarting, Session = true, InRadio = false, S = spotify, Expected = true, Why = "Spotify radio armed even though the pool flag is false" },
                // Protected sources are never suppressed.
                new { Source = PauseSource.Manual, Session = true, InRadio = true, S = pool, Expected = false, Why = "protected source" },
                new { Source = PauseSource.SystemLock, Session = true, InRadio = true, S = pool, Expected = false, Why = "protected source" },
                new { Source = PauseSource.Video, Session = true, InRadio = true, S = pool, Expected = false, Why = "protected source" },
                new { Source = PauseSource.ThemeOverlay, Session = true, InRadio = true, S = pool, Expected = false, Why = "protected source" },
                new { Source = PauseSource.Dashboard, Session = true, InRadio = true, S = pool, Expected = false, Why = "protected source" },
                new { Source = PauseSource.Jingle, Session = true, InRadio = true, S = pool, Expected = false, Why = "protected source" },
                new { Source = PauseSource.NsfPreview, Session = true, InRadio = true, S = pool, Expected = false, Why = "protected source" },
                new { Source = PauseSource.GameStarting, Session = true, InRadio = true, S = settingOff, Expected = false, Why = "setting off" },
                new { Source = PauseSource.GameStarting, Session = true, InRadio = true, S = radioOff, Expected = false, Why = "radio mode off" },
                new { Source = PauseSource.FocusLoss, Session = false, InRadio = true, S = pool, Expected = false, Why = "no game session" },
                // The "radio only" guarantee: radio yielded to an installed game's own music,
                // so _isInRadioMode is false and Spotify radio is off. Game music must still pause.
                new { Source = PauseSource.GameStarting, Session = true, InRadio = false, S = pool, Expected = false, Why = "radio yielded to game music" },
                new { Source = PauseSource.GameStarting, Session = true, InRadio = true, S = (UniPlaySongSettings)null, Expected = false, Why = "settings null" },
            };
            Assert.Multiple(() =>
            {
                foreach (var c in cases)
                    Assert.That(RadioPlayThroughPolicy.ShouldSuppress(c.Source, c.Session, c.InRadio, c.S),
                        Is.EqualTo(c.Expected), $"{c.Source} session={c.Session} inRadio={c.InRadio}: {c.Why}");
            });
        }
    }
}
