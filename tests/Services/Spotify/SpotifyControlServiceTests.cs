using System;
using Moq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Tests.Services.Spotify
{
    [TestFixture]
    public class SpotifyControlServiceTests
    {
        private Mock<IMusicPlaybackService> _playback;
        private Mock<ISpotifyClient> _client;
        private UniPlaySongSettings _settings;
        private SpotifyControlService _service;

        [SetUp]
        public void SetUp()
        {
            _playback = new Mock<IMusicPlaybackService>();
            _client = new Mock<ISpotifyClient>();
            _settings = new UniPlaySongSettings();
            // Default: Spotify available, playing, controllable.
            _client.SetupGet(c => c.IsAvailable).Returns(true);
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _client.Setup(c => c.TryPause()).Returns(true);
            _client.Setup(c => c.TryResume()).Returns(true);
            _service = new SpotifyControlService(_playback.Object, _client.Object, () => _settings, null);
        }

        [Test]
        public void RadioModeOn_SpotifyAvailable_IsActive()
        {
            _settings.RadioModeEnabled = true;
            _settings.SpotifyRadioMode = true;
            _service.Recompute();
            Assert.IsTrue(_service.IsSpotifyActive);
            Assert.IsTrue(_settings.SpotifyActive);
        }

        [Test]
        public void RadioModeOff_SpotifyRadioOn_NotActive()
        {
            _settings.RadioModeEnabled = false;
            _settings.SpotifyRadioMode = true;
            _service.Recompute();
            Assert.IsFalse(_service.IsSpotifyActive);
        }

        [Test]
        public void SpotifyUnavailable_NotActive_EvenInRadioMode()
        {
            _client.SetupGet(c => c.IsAvailable).Returns(false);
            _settings.RadioModeEnabled = true;
            _settings.SpotifyRadioMode = true;
            _service.Recompute();
            Assert.IsFalse(_service.IsSpotifyActive);
        }

        [Test]
        public void DefaultSource_InDefaultGap_IsActive()
        {
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();
            Assert.IsTrue(_service.IsSpotifyActive);
        }

        [Test]
        public void DefaultSource_NotInGap_NotActive()
        {
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(false);
            _service.Recompute();
            Assert.IsFalse(_service.IsSpotifyActive);
        }

        [Test]
        public void RadioWinsOverDefaultSource_WhenBothSet()
        {
            // Radio on, but not "in a gap": radio precedence still makes it active.
            _settings.RadioModeEnabled = true;
            _settings.SpotifyRadioMode = true;
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(false);
            _service.Recompute();
            Assert.IsTrue(_service.IsSpotifyActive);
        }

        [Test]
        public void Active_AndUpsPaused_PausesSpotify_WhenPlaying()
        {
            _settings.RadioModeEnabled = true;
            _settings.SpotifyRadioMode = true;
            _playback.SetupGet(p => p.IsPaused).Returns(true);
            _service.Recompute();
            _client.Verify(c => c.TryPause(), Times.Once);
        }

        [Test]
        public void Active_UpsResumes_ResumesSpotify_OnlyIfUpsPausedIt()
        {
            _settings.RadioModeEnabled = true;
            _settings.SpotifyRadioMode = true;
            // First: UPS pauses (UPS takes ownership).
            _playback.SetupGet(p => p.IsPaused).Returns(true);
            _service.Recompute();
            // Then: UPS unpauses → should resume Spotify.
            _playback.SetupGet(p => p.IsPaused).Returns(false);
            _service.Recompute();
            _client.Verify(c => c.TryResume(), Times.Once);
        }

        [Test]
        public void Active_UpsNotPaused_DoesNotResume_IfUpsNeverPaused()
        {
            // User may have paused Spotify themselves; UPS must not auto-resume.
            _settings.RadioModeEnabled = true;
            _settings.SpotifyRadioMode = true;
            _client.SetupGet(c => c.IsPlaying).Returns(false); // user already paused it
            _playback.SetupGet(p => p.IsPaused).Returns(false);
            _service.Recompute();
            _client.Verify(c => c.TryResume(), Times.Never);
        }

        [Test]
        public void BecomesInactive_ReleasesUpsOwnedPause()
        {
            _settings.RadioModeEnabled = true;
            _settings.SpotifyRadioMode = true;
            _playback.SetupGet(p => p.IsPaused).Returns(true);
            _service.Recompute(); // UPS pauses Spotify, takes ownership
            // Now Spotify mode turns off entirely.
            _settings.SpotifyRadioMode = false;
            _service.Recompute();
            _client.Verify(c => c.TryResume(), Times.Once); // ownership released → resume
            Assert.IsFalse(_service.IsSpotifyActive);
        }
    }
}
