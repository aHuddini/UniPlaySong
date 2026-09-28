using System;
using System.Linq;
using System.Threading.Tasks;
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
            var track = new SpotifyNowPlaying("Tokyo Rain", "CASPER", "Neon Nights", "Synthwave", TimeSpan.FromSeconds(225));
            var artBytes = new byte[] { 1, 2, 3 };
            // Invoke callbacks synchronously so publisher tests run end-to-end without a real dispatcher.
            _client.Setup(c => c.RequestNowPlaying(It.IsAny<Action<SpotifyNowPlaying>>()))
                .Callback<Action<SpotifyNowPlaying>>(cb => cb(track));
            _client.Setup(c => c.RequestAlbumArt(It.IsAny<Action<byte[]>>()))
                .Callback<Action<byte[]>>(cb => cb(artBytes));

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
            BuildPublisher(Func<string, string> gameCoverResolver = null, Action<Action> runInBackground = null)
        {
            var pb = new Mock<IMusicPlaybackService>();
            var spotify = new SpotifyControlService(pb.Object, _client.Object, () => _settings, null);
            var meta = new SongMetadataService(pb.Object, null, () => _settings);
            // The UPS art step normally runs on a worker thread; inline here so the asserts can follow Refresh.
            var pub = new NowPlayingPublisher(meta, spotify, _client.Object, _artWriter, () => _settings, null,
                gameCoverResolver, runInBackground ?? (work => work()));
            return (pub, spotify, meta, pb);
        }

        private string MakeSong(string name)
        {
            var path = System.IO.Path.Combine(_dir, name);
            System.IO.File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3 }); // not valid audio: no embedded art
            return path;
        }

        // The art step (TagLib + write) must not run on the caller's thread - Refresh is called on the UI
        // thread - and nothing is published until it lands, so title and art never disagree on screen.
        [Test]
        public void UpsArtStep_RunsOnTheBackgroundRunner_AndPublishesWhenItLands()
        {
            var queued = new System.Collections.Generic.List<Action>();
            var songPath = MakeSong("a.mp3");
            var (pub, spotify, meta, pb) = BuildPublisher(runInBackground: queued.Add);
            pb.SetupGet(p => p.CurrentSongPath).Returns(songPath);

            meta.ResubscribeToService(pb.Object); // raises OnSongInfoChanged → Refresh

            Assert.IsNotEmpty(queued, "the art step ran inline instead of on the runner");
            Assert.AreEqual(string.Empty, _settings.NowPlayingTitle, "published before the art step ran");

            foreach (var work in queued.ToArray()) work();
            Assert.AreEqual("a", _settings.NowPlayingTitle);
        }

        // A slow art read for the previous song must not land on top of the current one.
        [Test]
        public void StaleArtStep_IsNotPublished()
        {
            var queued = new System.Collections.Generic.List<Action>();
            var first = MakeSong("first.mp3");
            var second = MakeSong("second.mp3");
            var (pub, spotify, meta, pb) = BuildPublisher(runInBackground: queued.Add);

            pb.SetupGet(p => p.CurrentSongPath).Returns(first);
            meta.ResubscribeToService(pb.Object);
            var firstWork = queued.ToArray();
            queued.Clear();

            pb.SetupGet(p => p.CurrentSongPath).Returns(second);
            meta.ResubscribeToService(pb.Object);

            foreach (var work in queued.ToArray()) work();   // current song lands first
            foreach (var work in firstWork) work();          // then the stale one finishes

            Assert.AreEqual("second", _settings.NowPlayingTitle);
        }

        // The deadlock found in 1.8.10 testing: the art step published from a worker while holding the publish
        // lock; a settings PropertyChanged handler read Playnite's MainView.SelectedGames, which is a synchronous
        // Dispatcher.Invoke, while the UI thread sat in Refresh() waiting for that lock. Playnite froze for good.
        // The rule that prevents it: the settings object only ever changes on the UI thread. Checked with a real
        // dispatcher thread and a real worker, recording the thread of every PropertyChanged.
        [Test]
        public void SettingsOnlyChangeOnTheUiThread_EvenWhenTheArtStepRunsOnAWorker()
        {
            var songPath = MakeSong("a.mp3");
            System.Windows.Threading.Dispatcher ui = null;
            var ready = new System.Threading.ManualResetEventSlim();
            var uiThread = new System.Threading.Thread(() =>
            {
                ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            });
            uiThread.SetApartmentState(System.Threading.ApartmentState.STA);
            uiThread.IsBackground = true;
            uiThread.Start();
            ready.Wait();

            try
            {
                var raisedOn = new System.Collections.Concurrent.ConcurrentBag<int>();
                var published = new System.Threading.ManualResetEventSlim();
                _settings.PropertyChanged += (s, e) =>
                {
                    raisedOn.Add(System.Threading.Thread.CurrentThread.ManagedThreadId);
                    // What the real handler does via SelectedGames: a synchronous hop to the UI thread.
                    ui.Invoke(() => { });
                    if (e.PropertyName == nameof(UniPlaySongSettings.NowPlayingTitle)) published.Set();
                };

                NowPlayingPublisher pub = null;
                ui.Invoke(() =>
                {
                    var pb = new Mock<IMusicPlaybackService>();
                    var spotify = new SpotifyControlService(pb.Object, _client.Object, () => _settings, null);
                    var meta = new SongMetadataService(pb.Object, null, () => _settings);
                    pub = new NowPlayingPublisher(meta, spotify, _client.Object, _artWriter, () => _settings,
                        null, null, work => Task.Run(work), work => ui.BeginInvoke(work));
                    pb.SetupGet(p => p.CurrentSongPath).Returns(songPath);
                    meta.ResubscribeToService(pb.Object); // song change → Refresh on the UI thread
                });

                Assert.IsTrue(published.Wait(TimeSpan.FromSeconds(10)),
                    "nothing was published within 10 s - the UI thread and the art worker deadlocked");
                // Refresh again on the UI thread while the worker may be publishing: must not hang.
                Assert.IsTrue(ui.InvokeAsync(() => pub.Refresh()).Wait(TimeSpan.FromSeconds(10)) == System.Windows.Threading.DispatcherOperationStatus.Completed,
                    "Refresh on the UI thread hung");

                CollectionAssert.AreEqual(new[] { uiThread.ManagedThreadId }, raisedOn.Distinct().ToArray(),
                    "a settings property was changed off the UI thread");
            }
            finally
            {
                ui.InvokeShutdown();
            }
        }

        // The metadata service raises every song twice (filename, then tags), and pause/resume refresh
        // again. The art is resolved once per song, not once per refresh.
        [Test]
        public void RepeatedRefreshOfTheSameSong_ResolvesArtOnce()
        {
            int resolverCalls = 0;
            var coverPath = System.IO.Path.Combine(_dir, "cover.jpg");
            System.IO.File.WriteAllBytes(coverPath, new byte[] { 9 });
            var songPath = MakeSong("a.mp3");
            var (pub, spotify, meta, pb) = BuildPublisher(p => { resolverCalls++; return coverPath; });
            pb.SetupGet(p => p.CurrentSongPath).Returns(songPath);
            meta.ResubscribeToService(pb.Object);

            pub.Refresh();
            pub.Refresh();

            Assert.AreEqual(1, resolverCalls);
            Assert.AreEqual(coverPath, _settings.NowPlayingAlbumArtPath);
        }

        [Test]
        public void Refresh_SpotifyActive_PublishesSpotifyTitleArtistAndArt()
        {
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
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
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            // Override the default art-bytes stub to return null — simulates no thumbnail available.
            _client.Setup(c => c.RequestAlbumArt(It.IsAny<Action<byte[]>>()))
                .Callback<Action<byte[]>>(cb => cb(null));
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

            string resolverReceivedPath = null;
            var (pub, spotify, meta, pb) = BuildPublisher(p => { resolverReceivedPath = p; return coverPath; });
            pb.SetupGet(p => p.CurrentSongPath).Returns(songPath);
            meta.ResubscribeToService(pb.Object); // populates CurrentSongInfo from songPath
            pub.Refresh();

            Assert.AreEqual(coverPath, _settings.NowPlayingAlbumArtPath);
            // The resolver must receive the TRACK's path so it can derive the owning game
            // (Games\{GameId}\...) — the right cover for pool/radio songs, and works when
            // CurrentGame is null (radio never sets it).
            Assert.AreEqual(songPath, resolverReceivedPath);
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
