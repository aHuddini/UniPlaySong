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
            _client.Setup(c => c.GetNowPlaying()).Returns(
                new SpotifyNowPlaying("Tokyo Rain", "CASPER", "Neon Nights", "Synthwave", TimeSpan.FromSeconds(225)));

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
            BuildPublisher(Func<string> gameCoverResolver = null)
        {
            var pb = new Mock<IMusicPlaybackService>();
            var spotify = new SpotifyControlService(pb.Object, _client.Object, () => _settings, null);
            var meta = new SongMetadataService(pb.Object, null, () => _settings);
            var pub = new NowPlayingPublisher(meta, spotify, _client.Object, _artWriter, () => _settings, null, gameCoverResolver);
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
            Assert.AreEqual("Neon Nights", _settings.NowPlayingAlbum);
            Assert.AreEqual("Synthwave", _settings.NowPlayingGenre);
            Assert.AreEqual("3:45", _settings.NowPlayingDuration); // 225s formatted m:ss
        }

        [Test]
        public void Refresh_GameMusic_ClearsSpotifyOnlyMetadata()
        {
            // Game music must not carry over a prior Spotify track's album/genre/duration.
            var songPath = System.IO.Path.Combine(_dir, "song.mp3");
            System.IO.File.WriteAllBytes(songPath, new byte[] { 0, 1, 2, 3 });

            var (pub, spotify, meta, pb) = BuildPublisher();
            pb.SetupGet(p => p.CurrentSongPath).Returns(songPath);
            meta.ResubscribeToService(pb.Object);
            pub.Refresh();

            Assert.AreEqual(string.Empty, _settings.NowPlayingAlbum);
            Assert.AreEqual(string.Empty, _settings.NowPlayingGenre);
            Assert.AreEqual(string.Empty, _settings.NowPlayingDuration);
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

        [Test]
        public void Refresh_GameMusicNoEmbeddedArt_FallsBackToGameCover()
        {
            // A game-music track with no embedded art: the dummy "audio" file makes
            // WriteFromAudioFile return "" (TagLib can't read it), so the publisher should fall
            // back to the resolved game cover path.
            var songPath = System.IO.Path.Combine(_dir, "song.mp3");
            System.IO.File.WriteAllBytes(songPath, new byte[] { 0, 1, 2, 3 }); // not valid audio → no art
            var coverPath = System.IO.Path.Combine(_dir, "cover.jpg");
            System.IO.File.WriteAllBytes(coverPath, new byte[] { 9, 9, 9 });

            var (pub, spotify, meta, pb) = BuildPublisher(() => coverPath);
            pb.SetupGet(p => p.CurrentSongPath).Returns(songPath);
            meta.ResubscribeToService(pb.Object); // populates CurrentSongInfo from songPath
            pub.Refresh();

            Assert.AreEqual(coverPath, _settings.NowPlayingAlbumArtPath);
        }

        [Test]
        public void Refresh_GameMusicNoEmbeddedArt_NoResolver_LeavesArtPathEmpty()
        {
            // Same as above but no cover resolver wired → fallback disabled → art path stays empty.
            var songPath = System.IO.Path.Combine(_dir, "song.mp3");
            System.IO.File.WriteAllBytes(songPath, new byte[] { 0, 1, 2, 3 });

            var (pub, spotify, meta, pb) = BuildPublisher(); // no resolver
            pb.SetupGet(p => p.CurrentSongPath).Returns(songPath);
            meta.ResubscribeToService(pb.Object);
            pub.Refresh();

            Assert.AreEqual(string.Empty, _settings.NowPlayingAlbumArtPath);
        }
    }
}
