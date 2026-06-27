using System;
using Moq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class NowPlayingPublisherTests
    {
        private UniPlaySongSettings _settings;
        private Mock<ISpotifyClient> _client;
        private string _dir;
        private NowPlayingArtWriter _artWriter;

        // We use a REAL SpotifyControlService + SongMetadataService is hard to mock (no interface),
        // so these tests drive the publisher's resolution logic through a thin seam: the publisher
        // exposes Refresh(), and we set state on the collaborators we CAN control. For metadata we
        // use a real SongMetadataService with a Mock<IMusicPlaybackService>; for Spotify-active we
        // use a real SpotifyControlService with a mocked ISpotifyClient + settings toggles.

        [SetUp]
        public void SetUp()
        {
            _settings = new UniPlaySongSettings();
            _client = new Mock<ISpotifyClient>();
            _client.SetupGet(c => c.IsAvailable).Returns(true);
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _client.Setup(c => c.TryPause()).Returns(true);
            _client.Setup(c => c.TryResume()).Returns(true);
            _client.Setup(c => c.TryGetAlbumArtBytes()).Returns(new byte[] { 1, 2, 3 });
            _client.Setup(c => c.GetNowPlaying()).Returns(new SpotifyNowPlaying("Tokyo Rain", "CASPER"));

            _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ups_pub_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_dir);
            _artWriter = new NowPlayingArtWriter(_dir, null);
        }

        [TearDown]
        public void TearDown()
        {
            try { System.IO.Directory.Delete(_dir, true); } catch { }
        }

        private (NowPlayingPublisher pub, SpotifyControlService spotify, SongMetadataService meta, Mock<IMusicPlaybackService> pb)
            BuildPublisher()
        {
            var pb = new Mock<IMusicPlaybackService>();
            var spotify = new SpotifyControlService(pb.Object, _client.Object, () => _settings, null);
            var meta = new SongMetadataService(pb.Object, null, () => _settings);
            var pub = new NowPlayingPublisher(meta, spotify, _client.Object, _artWriter, () => _settings, null);
            return (pub, spotify, meta, pb);
        }

        [Test]
        public void Refresh_SpotifyActive_PublishesSpotifyTitleArtistAndArt()
        {
            _settings.RadioModeEnabled = true;
            _settings.SpotifyRadioMode = true;
            var (pub, spotify, meta, pb) = BuildPublisher();
            spotify.Recompute();             // makes SpotifyActive true
            pub.Refresh();
            Assert.AreEqual("Tokyo Rain", _settings.NowPlayingTitle);
            Assert.AreEqual("CASPER", _settings.NowPlayingArtist);
            Assert.AreEqual(_artWriter.ArtFilePath, _settings.NowPlayingAlbumArtPath);
        }

        [Test]
        public void Refresh_NothingActive_ClearsAll()
        {
            // Spotify not active, no UPS song.
            var (pub, spotify, meta, pb) = BuildPublisher();
            pub.Refresh();
            Assert.AreEqual(string.Empty, _settings.NowPlayingTitle);
            Assert.AreEqual(string.Empty, _settings.NowPlayingArtist);
            Assert.AreEqual(string.Empty, _settings.NowPlayingAlbumArtPath);
        }

        [Test]
        public void Refresh_SpotifyActiveButNoArtBytes_LeavesArtPathEmpty()
        {
            _settings.RadioModeEnabled = true;
            _settings.SpotifyRadioMode = true;
            _client.Setup(c => c.TryGetAlbumArtBytes()).Returns((byte[])null);
            var (pub, spotify, meta, pb) = BuildPublisher();
            spotify.Recompute();
            pub.Refresh();
            Assert.AreEqual("Tokyo Rain", _settings.NowPlayingTitle);
            Assert.AreEqual(string.Empty, _settings.NowPlayingAlbumArtPath);
        }
    }
}
