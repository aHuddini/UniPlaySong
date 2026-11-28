using System;
using Moq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Models;
using UniPlaySong.Services;
using UniPlaySong.Services.ActiveMedia;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Tests.Services.ActiveMedia
{
    // Which source is active, and that transport and seeks reach it: Spotify through its client, UPS music through
    // the current playback service.
    [TestFixture]
    public class ActiveMediaServiceTests
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

        private ActiveMediaService Create(Func<IMusicPlaybackService> current = null) =>
            new ActiveMediaService(_playback.Object, _spotifyControl, _client.Object, null, current);

        // Spotify as the radio source => SpotifyControlService.IsSpotifyActive true.
        private void ArmSpotifyAsActiveSource()
        {
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _spotifyControl.Recompute();
            Assert.IsTrue(_spotifyControl.IsSpotifyActive, "guard: Spotify should be the active source");
        }

        // A paused Spotify track, 60 s into 200 s, that accepts seeks. Paused, so a seek's start position does not
        // drift with the wall clock between steps.
        private void ArrangeSeekableSpotify()
        {
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _client.SetupGet(c => c.CanSeek).Returns(true);
            _client.Setup(c => c.GetTimeline()).Returns(new SpotifyTimeline(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(200)));
        }

        // ---- source resolution ----

        [Test]
        public void Resolve_SpotifyActive_SnapshotIsSpotify()
        {
            ArmSpotifyAsActiveSource();

            var snap = _service.GetSnapshot();
            Assert.AreEqual(ActiveMediaSourceKind.Spotify, snap.SourceKind);
            Assert.IsTrue(snap.HasActiveMedia);
            Assert.AreEqual("Spotify", snap.SourceName);
        }

        // Spotify Radio yielded to a game track (Details view, or a song picked on the Music Info Card): the game track is
        // what plays, so the snapshot and Play/Pause follow it - pausing used to pause the ducked Spotify instead
        [Test]
        public void Resolve_RadioYieldedToGameMusic_UpsIsTheSource()
        {
            ArmSpotifyAsActiveSource();
            _playback.SetupGet(p => p.IsRadioYieldedToGameMusic).Returns(true);
            _playback.SetupGet(p => p.IsLoaded).Returns(true);
            _playback.SetupGet(p => p.IsPlaying).Returns(true);

            Assert.AreEqual(ActiveMediaSourceKind.Ups, _service.GetSnapshot().SourceKind);
            _service.PlayPause();
            _client.Verify(c => c.TryTogglePlayPause(), Times.Never);
        }

        [Test]
        public void SeekBy_RadioYieldedToGameMusic_SeeksTheGameTrack()
        {
            ArrangeSeekableSpotify();
            ArmSpotifyAsActiveSource();
            _playback.SetupGet(p => p.IsRadioYieldedToGameMusic).Returns(true);
            _playback.SetupGet(p => p.IsLoaded).Returns(true);
            _playback.SetupGet(p => p.CanSeek).Returns(true);
            _playback.SetupGet(p => p.CurrentTime).Returns(TimeSpan.FromSeconds(30));

            _service.SeekBy(5);

            _playback.Verify(p => p.Seek(TimeSpan.FromSeconds(35)), Times.Once);
        }

        [Test]
        public void Resolve_UpsPlaying_SnapshotIsUps()
        {
            // Spotify not the active source; UPS player is loaded and playing.
            _playback.SetupGet(p => p.IsLoaded).Returns(true);
            _playback.SetupGet(p => p.IsPlaying).Returns(true);
            _playback.SetupGet(p => p.CurrentGameSongCount).Returns(3);
            _spotifyControl.Recompute(); // IsSpotifyActive == false (no radio/default-spotify)

            var snap = _service.GetSnapshot();
            Assert.AreEqual(ActiveMediaSourceKind.Ups, snap.SourceKind);
            Assert.IsTrue(snap.HasActiveMedia);
            Assert.AreEqual("UniPlaySong", snap.SourceName);
        }

        // A song just started (position 0) shows "0:00": the position came back empty at 0, so every timeline's position
        // label vanished at the start of each song - and the music player's strip, resized by it, restarted its renderer.
        [Test]
        public void Snapshot_UpsAtTheStart_PositionReadsZero()
        {
            _playback.SetupGet(p => p.IsLoaded).Returns(true);
            _playback.SetupGet(p => p.IsPlaying).Returns(true);
            _playback.Setup(p => p.GetCurrentSongCurrentTime()).Returns(TimeSpan.Zero);
            _playback.Setup(p => p.GetCurrentSongTotalTime()).Returns(TimeSpan.FromSeconds(100));
            _spotifyControl.Recompute();

            var snap = _service.GetSnapshot();
            Assert.AreEqual("0:00", snap.PositionText);
            Assert.AreEqual("1:40", snap.DurationText);
        }

        // A theme's volume bar shows the level the user set, not the fader's passing value: paused or mid-fade the
        // player sits at 0, and a bar that copied it stayed down after the music came back. Muted still reads 0.
        [Test]
        public void Snapshot_UpsVolume_IsTheSetLevelNotTheFade()
        {
            _playback.SetupGet(p => p.IsLoaded).Returns(true);
            _playback.SetupGet(p => p.IsPlaying).Returns(true);
            _playback.Setup(p => p.GetVolume()).Returns(0.6);
            _playback.Setup(p => p.GetInternalVolume()).Returns(0.0);
            _playback.SetupProperty(p => p.IsMuted);
            _spotifyControl.Recompute();

            Assert.AreEqual(60.0, _service.GetSnapshot().Volume, 0.001);

            _service.ToggleMute();
            Assert.IsTrue(_service.GetSnapshot().IsMuted);
            Assert.AreEqual(0.0, _service.GetSnapshot().Volume, 0.001);
            _service.ToggleMute();
            Assert.AreEqual(60.0, _service.GetSnapshot().Volume, 0.001);
        }

        [Test]
        public void Resolve_NothingLoaded_SnapshotIsNone()
        {
            _playback.SetupGet(p => p.IsLoaded).Returns(false);
            _spotifyControl.Recompute();

            var snap = _service.GetSnapshot();
            Assert.AreEqual(ActiveMediaSourceKind.None, snap.SourceKind);
            Assert.IsFalse(snap.HasActiveMedia);
        }

        [Test]
        public void Snapshot_UpsActive_CanNextReflectsSongCount()
        {
            _playback.SetupGet(p => p.IsLoaded).Returns(true);
            _playback.SetupGet(p => p.IsPlaying).Returns(true);
            _playback.SetupGet(p => p.CurrentGameSongCount).Returns(1);
            _spotifyControl.Recompute();

            var snap = _service.GetSnapshot();
            Assert.IsFalse(snap.CanNext, "single song -> cannot skip");
            Assert.IsTrue(snap.CanPrevious, "loaded -> can restart");
        }

        // ---- taskbar + media-key routing ----
        // The thumbbar buttons and hardware media keys predated Spotify and drove the UPS player unconditionally: with
        // Spotify active, play/pause resumed UPS's silent-but-loaded player (game music over Spotify) and next/previous
        // acted on the UPS pool. Both now route through ActiveMediaService.

        // The reported bug: RemovePauseSource(Manual) un-paused that silent-but-loaded player.
        [Test]
        public void PlayPause_SpotifyActive_ReachesSpotify_NotTheUpsPlayer()
        {
            ArmSpotifyAsActiveSource();
            _playback.SetupGet(p => p.IsPaused).Returns(true);
            _playback.SetupGet(p => p.IsLoaded).Returns(true);

            _service.PlayPause();

            _client.Verify(c => c.TryTogglePlayPause(), Times.Once,
                "with Spotify active, play/pause must command Spotify");
            _playback.Verify(p => p.RemovePauseSource(PauseSource.Manual), Times.Never,
                "with Spotify active, play/pause must not resume the UPS player — that is the game-music-over-Spotify bug");
            _playback.Verify(p => p.NotifyManualStart(), Times.Never);
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

        // The other direction: with Spotify NOT the source, transport still drives the UPS player - the fix is a
        // routing change, not a Spotify-only rewrite.
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

        // ---- seeking ----

        [Test]
        public void SeekTo_Spotify_GoesToSpotifyAtThatShareOfTheTrack()
        {
            ArrangeSeekableSpotify();
            ArmSpotifyAsActiveSource();
            Create().SeekTo(50);
            _client.Verify(c => c.RequestSeek(TimeSpan.FromSeconds(100), It.IsAny<Action>()), Times.Once);
            _playback.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
        }

        // Past the end Spotify skips to the next track, so a seek stops a second short.
        [Test]
        public void SeekTo_Spotify_StopsASecondShortOfTheEnd()
        {
            ArrangeSeekableSpotify();
            ArmSpotifyAsActiveSource();
            Create().SeekTo(100);
            _client.Verify(c => c.RequestSeek(TimeSpan.FromSeconds(199), It.IsAny<Action>()), Times.Once);
        }

        [Test]
        public void SeekBy_Spotify_MovesFromTheCurrentPosition()
        {
            ArrangeSeekableSpotify();
            ArmSpotifyAsActiveSource();
            Create().SeekBy(-15);
            _client.Verify(c => c.RequestSeek(TimeSpan.FromSeconds(45), It.IsAny<Action>()), Times.Once);
        }

        // Spotify's reported position lags a seek; a quick second step builds on the first target, not on the stale
        // position (which would lose the first step).
        [Test]
        public void SeekBy_Spotify_QuickSteps_BuildOnTheLastTarget()
        {
            ArrangeSeekableSpotify();
            ArmSpotifyAsActiveSource();
            var service = Create();
            service.SeekBy(10);
            service.SeekBy(10);
            _client.Verify(c => c.RequestSeek(TimeSpan.FromSeconds(70), It.IsAny<Action>()), Times.Once);
            _client.Verify(c => c.RequestSeek(TimeSpan.FromSeconds(80), It.IsAny<Action>()), Times.Once);
        }

        [Test]
        public void Spotify_ThatRefusesSeeking_IsLeftAlone()
        {
            ArrangeSeekableSpotify();
            _client.SetupGet(c => c.CanSeek).Returns(false);
            ArmSpotifyAsActiveSource();
            var service = Create();

            Assert.IsFalse(service.CanSeek);
            service.SeekTo(50);
            service.SeekBy(5);
            _client.Verify(c => c.RequestSeek(It.IsAny<TimeSpan>(), It.IsAny<Action>()), Times.Never);
        }

        // A backend swap replaces the playback service; seeks must go to the current one, not the one from startup.
        [Test]
        public void SeekBy_Ups_UsesTheCurrentPlaybackService()
        {
            ArrangeSeekableSpotify();
            var current = new Mock<IMusicPlaybackService>();
            current.SetupGet(p => p.CanSeek).Returns(true);
            current.SetupGet(p => p.CurrentTime).Returns(TimeSpan.FromSeconds(30));
            _spotifyControl.Recompute();

            Create(() => current.Object).SeekBy(5);

            current.Verify(p => p.Seek(TimeSpan.FromSeconds(35)), Times.Once);
            _playback.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
        }
    }
}
