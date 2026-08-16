using Moq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Models;
using UniPlaySong.Services;
using UniPlaySong.Services.ActiveMedia;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Tests.Services.ActiveMedia
{
    // The taskbar thumbbar buttons and the hardware media keys share three handlers in
    // UniPlaySong.cs. Those handlers predated the Spotify integration and drove the UPS
    // player unconditionally, so with Spotify as the active source play/pause resumed
    // UPS's silent-but-loaded player (game music over Spotify) and next/previous acted on
    // the UPS pool instead of Spotify's queue. Both now route through ActiveMediaService.
    //
    // These tests pin the routing contract the handlers depend on: with Spotify active,
    // transport reaches Spotify and NOT the UPS player.
    [TestFixture]
    public class TaskbarAndMediaKeyRoutingTests
    {
        private Mock<IMusicPlaybackService> _playback;
        private Mock<ISpotifyClient> _client;
        private SpotifyControlService _spotifyControl;
        private UniPlaySongSettings _settings;
        private ActiveMediaService _service;

        [SetUp]
        public void SetUp()
        {
            _playback = new Mock<IMusicPlaybackService>();
            _client = new Mock<ISpotifyClient>();
            _settings = new UniPlaySongSettings();

            _client.SetupGet(c => c.IsAvailable).Returns(true);
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _client.Setup(c => c.TryTogglePlayPause()).Returns(true);
            _client.Setup(c => c.TrySkipNext()).Returns(true);
            _client.Setup(c => c.TrySkipPrevious()).Returns(true);

            _spotifyControl = new SpotifyControlService(_playback.Object, _client.Object, () => _settings, null);
            _service = new ActiveMediaService(_playback.Object, _spotifyControl, _client.Object, null);
        }

        // Spotify as the radio source => SpotifyControlService.IsSpotifyActive true.
        private void ArmSpotifyAsActiveSource()
        {
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _spotifyControl.Recompute();
            Assert.IsTrue(_spotifyControl.IsSpotifyActive, "guard: Spotify should be the active source");
        }

        // The reported bug: play/pause resumed UPS's own player on top of Spotify.
        // RemovePauseSource(Manual) is what un-paused that silent-but-loaded player.
        [Test]
        public void PlayPause_SpotifyActive_DoesNotResumeUpsPlayer()
        {
            ArmSpotifyAsActiveSource();
            _playback.SetupGet(p => p.IsPaused).Returns(true);
            _playback.SetupGet(p => p.IsLoaded).Returns(true);

            _service.PlayPause();

            _playback.Verify(p => p.RemovePauseSource(PauseSource.Manual), Times.Never,
                "with Spotify active, play/pause must not resume the UPS player — that is the game-music-over-Spotify bug");
            _playback.Verify(p => p.NotifyManualStart(), Times.Never);
        }

        [Test]
        public void PlayPause_SpotifyActive_ReachesSpotify()
        {
            ArmSpotifyAsActiveSource();

            _service.PlayPause();

            _client.Verify(c => c.TryTogglePlayPause(), Times.Once,
                "with Spotify active, play/pause must command Spotify");
        }

        [Test]
        public void Next_SpotifyActive_SkipsSpotifyNotUpsPool()
        {
            ArmSpotifyAsActiveSource();

            _service.Next();

            _client.Verify(c => c.TrySkipNext(), Times.Once);
            _playback.Verify(p => p.SkipToNextSong(), Times.Never,
                "with Spotify active, next must not advance the UPS pool");
        }

        [Test]
        public void Previous_SpotifyActive_SkipsSpotifyNotUpsPool()
        {
            ArmSpotifyAsActiveSource();

            _service.Previous();

            _client.Verify(c => c.TrySkipPrevious(), Times.Once);
            _playback.Verify(p => p.RestartCurrentSong(), Times.Never,
                "with Spotify active, previous must not restart the UPS song");
        }

        // The other direction: with Spotify NOT the source, transport must still drive the
        // UPS player exactly as before. This pins that the fix is a routing change, not a
        // Spotify-only rewrite.
        [Test]
        public void Next_SpotifyInactive_StillSkipsUpsPool()
        {
            _playback.SetupGet(p => p.IsLoaded).Returns(true);
            _spotifyControl.Recompute();
            Assert.IsFalse(_spotifyControl.IsSpotifyActive, "guard: Spotify should not be active");

            _service.Next();

            _playback.Verify(p => p.SkipToNextSong(), Times.Once);
            _client.Verify(c => c.TrySkipNext(), Times.Never);
        }

        [Test]
        public void Previous_SpotifyInactive_StillRestartsUpsSong()
        {
            _playback.SetupGet(p => p.IsLoaded).Returns(true);
            _spotifyControl.Recompute();

            _service.Previous();

            _playback.Verify(p => p.RestartCurrentSong(), Times.Once);
            _client.Verify(c => c.TrySkipPrevious(), Times.Never);
        }
    }
}
