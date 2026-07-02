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
            _service = new ActiveMediaService(_playback.Object, _spotifyControl, _client.Object, () => _settings, null);
        }

        [Test]
        public void Resolve_SpotifyActive_SnapshotIsSpotify()
        {
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _spotifyControl.Recompute();

            var snap = _service.GetSnapshot();
            Assert.AreEqual(ActiveMediaSourceKind.Spotify, snap.SourceKind);
            Assert.IsTrue(snap.HasActiveMedia);
            Assert.AreEqual("Spotify", snap.SourceName);
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
        public void PlayPause_SpotifyActive_TogglesSpotify()
        {
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _spotifyControl.Recompute();

            _service.PlayPause();
            _client.Verify(c => c.TryTogglePlayPause(), Times.Once);
        }

        [Test]
        public void Next_SpotifyActive_SkipsSpotify()
        {
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _spotifyControl.Recompute();

            _service.Next();
            _client.Verify(c => c.TrySkipNext(), Times.Once);
            _playback.Verify(p => p.SkipToNextSong(), Times.Never);
        }

        [Test]
        public void Next_UpsActive_SkipsUpsSong()
        {
            _playback.SetupGet(p => p.IsLoaded).Returns(true);
            _playback.SetupGet(p => p.IsPlaying).Returns(true);
            _playback.SetupGet(p => p.CurrentGameSongCount).Returns(3);
            _spotifyControl.Recompute();

            _service.Next();
            _playback.Verify(p => p.SkipToNextSong(), Times.Once);
            _client.Verify(c => c.TrySkipNext(), Times.Never);
        }

        [Test]
        public void Previous_UpsActive_RestartsCurrentSong()
        {
            _playback.SetupGet(p => p.IsLoaded).Returns(true);
            _playback.SetupGet(p => p.IsPlaying).Returns(true);
            _spotifyControl.Recompute();

            _service.Previous();
            _playback.Verify(p => p.RestartCurrentSong(), Times.Once);
            _client.Verify(c => c.TrySkipPrevious(), Times.Never);
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
    }
}
