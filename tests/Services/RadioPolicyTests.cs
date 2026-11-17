using System;
using System.IO;
using Moq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Radio Mode's two decision policies (game-select yield, play-through-games) and the wiring that
    // makes AddPauseSource consult the play-through rule.
    [TestFixture]
    public class RadioPolicyTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySongTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        private MusicPlaybackService CreateService()
        {
            return new MusicPlaybackService(new Mock<IMusicPlayer>().Object, new GameMusicFileService(_tempDir));
        }

        private static UniPlaySongSettings SelectSettings(bool playOnSelect, bool radio) =>
            new UniPlaySongSettings { PlayOnlyOnGameSelect = playOnSelect, RadioModeEnabled = radio };

        // "Play Only on Game Select" while Radio Mode is on: before v1.7.0 both radio branches returned before the
        // PlayOnlyOnGameSelect gate ran, so the setting silently did nothing with radio enabled.
        [Test]
        public void ShouldYieldToSelectedGame_TruthTable()
        {
            var cases = new[]
            {
                new { Details = true, Songs = 3, Settings = SelectSettings(true, true), Expected = true, Why = "both options on, Details view, game has songs" },
                new { Details = false, Songs = 3, Settings = SelectSettings(true, true), Expected = false, Why = "list view - radio keeps playing while browsing" },
                // Yielding to nothing would drop the user into silence; the radio continues instead.
                new { Details = true, Songs = 0, Settings = SelectSettings(true, true), Expected = false, Why = "selected game has no songs" },
                // Radio Mode alone keeps its pre-existing behaviour: it ignores game switches.
                new { Details = true, Songs = 3, Settings = SelectSettings(false, true), Expected = false, Why = "PlayOnlyOnGameSelect off" },
                // Radio off = the normal-playback path already honours PlayOnlyOnGameSelect itself.
                new { Details = true, Songs = 3, Settings = SelectSettings(true, false), Expected = false, Why = "radio off" },
                new { Details = true, Songs = 3, Settings = (UniPlaySongSettings)null, Expected = false, Why = "settings null" },
                // Desktop reaches the policy with isFullscreenDetailsView=false, so Desktop radio is unchanged.
                new { Details = false, Songs = 5, Settings = SelectSettings(true, true), Expected = false, Why = "Desktop mode" },
            };
            Assert.Multiple(() =>
            {
                foreach (var c in cases)
                    Assert.That(RadioGameSelectPolicy.ShouldYieldToSelectedGame(
                            isFullscreenDetailsView: c.Details, selectedGameSongCount: c.Songs, settings: c.Settings),
                        Is.EqualTo(c.Expected), $"details={c.Details} songs={c.Songs}: {c.Why}");
            });
        }

        private static UniPlaySongSettings Armed(RadioMusicSource source) => new UniPlaySongSettings
        {
            RadioPlaysThroughGames = true,
            RadioModeEnabled = true,
            RadioMusicSource = source,
        };

        [Test]
        public void ShouldSuppress_TruthTable()
        {
            var pool = Armed(RadioMusicSource.FullLibrary);
            var settingOff = Armed(RadioMusicSource.FullLibrary);
            settingOff.RadioPlaysThroughGames = false;
            var radioOff = Armed(RadioMusicSource.FullLibrary);
            radioOff.RadioModeEnabled = false;
            // SpotifyRadioMode is derived (RadioModeEnabled && source == Spotify), so it cannot be set directly.
            var spotify = Armed(RadioMusicSource.Spotify);
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
                // The "radio only" guarantee: radio yielded to an installed game's own music, so game music must still pause.
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

        // The policy is consulted at the AddPauseSource choke point. Not armed (no settings): a game session pauses as it
        // always did. Armed: reached only through RefreshSettings, so this also pins that the service reads a freshly
        // saved settings object rather than the one PlayGameMusic cached.
        [TestCase(false)]
        [TestCase(true)]
        public void AddPauseSource_HonoursThePolicy(bool armed)
        {
            var service = CreateService();
            if (armed)
                service.RefreshSettings(Armed(RadioMusicSource.Spotify));
            service.SetGameSessionActive(true);

            service.AddPauseSource(PauseSource.GameStarting);

            Assert.AreEqual(!armed, service.IsPaused);
        }

        // Issue #93: GameStarting was added while radio was off, then Spotify radio was turned on mid-game. The policy
        // only filters at add time, so the stale source kept IsPaused true and Spotify control kept re-pausing Spotify.
        // Manual is not a game-session source and must survive; with play-through off nothing is dropped at all.
        [Test]
        public void RadioTurnedOnMidGame_DropsOnlyTheGameSessionSourcesItPlaysThrough()
        {
            foreach (var playThrough in new[] { true, false })
            {
                var service = CreateService();
                service.SetGameSessionActive(true);
                service.AddPauseSource(PauseSource.GameStarting);
                service.AddPauseSource(PauseSource.Manual);

                var settings = Armed(RadioMusicSource.Spotify);
                settings.RadioPlaysThroughGames = playThrough;
                service.DropSourcesNowSuppressed(settings);

                Assert.AreEqual(!playThrough, service.HasPauseSource(PauseSource.GameStarting), $"playThrough={playThrough}");
                Assert.IsTrue(service.HasPauseSource(PauseSource.Manual), $"playThrough={playThrough}");
            }
        }
    }
}
