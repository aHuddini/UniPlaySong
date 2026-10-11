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
    // Seeks reach the active source: Spotify through its client, UPS music through the current playback service.
    [TestFixture]
    public class ActiveMediaSeekTests
    {
        private Mock<IMusicPlaybackService> _playback;
        private Mock<ISpotifyClient> _client;
        private UniPlaySongSettings _settings;
        private SpotifyControlService _spotifyControl;

        [SetUp]
        public void SetUp()
        {
            _playback = new Mock<IMusicPlaybackService>();
            _client = new Mock<ISpotifyClient>();
            _settings = new UniPlaySongSettings();
            _client.SetupGet(c => c.IsAvailable).Returns(true);
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _client.SetupGet(c => c.CanSeek).Returns(true);
            _client.Setup(c => c.GetTimeline()).Returns(new SpotifyTimeline(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(200)));
            _spotifyControl = new SpotifyControlService(_playback.Object, _client.Object, () => _settings, null);
        }

        private ActiveMediaService Create(Func<IMusicPlaybackService> current = null) =>
            new ActiveMediaService(_playback.Object, _spotifyControl, _client.Object, null, current);

        private void MakeSpotifyTheSource()
        {
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _spotifyControl.Recompute();
        }

        [Test]
        public void SeekTo_Spotify_GoesToSpotifyAtThatShareOfTheTrack()
        {
            MakeSpotifyTheSource();
            Create().SeekTo(50);
            _client.Verify(c => c.RequestSeek(TimeSpan.FromSeconds(100), It.IsAny<Action>()), Times.Once);
            _playback.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
        }

        // Past the end Spotify skips to the next track, so a seek stops a second short.
        [Test]
        public void SeekTo_Spotify_StopsASecondShortOfTheEnd()
        {
            MakeSpotifyTheSource();
            Create().SeekTo(100);
            _client.Verify(c => c.RequestSeek(TimeSpan.FromSeconds(199), It.IsAny<Action>()), Times.Once);
        }

        [Test]
        public void SeekBy_Spotify_MovesFromTheCurrentPosition()
        {
            MakeSpotifyTheSource();
            Create().SeekBy(-15);
            _client.Verify(c => c.RequestSeek(TimeSpan.FromSeconds(45), It.IsAny<Action>()), Times.Once);
        }

        // Spotify's reported position lags a seek; a quick second step builds on the first target, not on the stale
        // position (which would lose the first step).
        [Test]
        public void SeekBy_Spotify_QuickSteps_BuildOnTheLastTarget()
        {
            MakeSpotifyTheSource();
            var service = Create();
            service.SeekBy(10);
            service.SeekBy(10);
            _client.Verify(c => c.RequestSeek(TimeSpan.FromSeconds(70), It.IsAny<Action>()), Times.Once);
            _client.Verify(c => c.RequestSeek(TimeSpan.FromSeconds(80), It.IsAny<Action>()), Times.Once);
        }

        [Test]
        public void Spotify_ThatRefusesSeeking_IsLeftAlone()
        {
            _client.SetupGet(c => c.CanSeek).Returns(false);
            MakeSpotifyTheSource();
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
