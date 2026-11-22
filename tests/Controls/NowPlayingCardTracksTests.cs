using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniPlaySong.DeskMediaControl;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Controls
{
    [TestFixture]
    public class NowPlayingCardTracksTests
    {
        // The card's list always means "what you can play from here": the game's songs, Radio's queue behind the song
        // that's on, Spotify's history (not playable from here), or nothing for default music
        [Test]
        public void ListFollowsWhatIsPlaying()
        {
            var upNext = new List<RecentTrack> { new RecentTrack("Next One", "Game B", "Game B", default(DateTime), @"C:\m\b.mp3") };
            var spotify = new List<RecentTrack> { new RecentTrack("Song", "Band", "Album", DateTime.Now, "Song|Band|Album") };
            var gameSongs = new List<string> { @"C:\m\a1.mp3", @"C:\m\a2.mp3" };

            var radio = NowPlayingCardTracks.Build(false, true, false, @"C:\m\x.mp3", "Current", gameSongs, upNext, spotify);
            Assert.AreEqual(NowPlayingCardContext.RadioUpNext, radio.Context);
            Assert.IsTrue(radio.Rows[0].IsCurrent && radio.Rows[0].Title == "Current");
            Assert.IsTrue(radio.Rows[1].Playable && radio.Rows[1].Path == @"C:\m\b.mp3");

            var fallback = NowPlayingCardTracks.Build(false, false, true, @"C:\d\x.mp3", "Ambient", gameSongs, upNext, spotify);
            Assert.AreEqual(NowPlayingCardContext.None, fallback.Context);
            Assert.AreEqual(0, fallback.Rows.Count);

            var sp = NowPlayingCardTracks.Build(true, true, false, null, "Song", gameSongs, upNext, spotify);
            Assert.AreEqual(NowPlayingCardContext.SpotifyRecent, sp.Context);
            Assert.IsFalse(sp.Rows.Any(r => r.Playable));

            var game = NowPlayingCardTracks.Build(false, false, false, @"C:\m\a2.mp3", "a2", gameSongs, upNext, spotify, @"C:\M\A1.mp3");
            Assert.AreEqual(NowPlayingCardContext.GameSongs, game.Context);
            Assert.AreEqual(new[] { false, true }, game.Rows.Select(r => r.IsCurrent).ToArray());
            Assert.AreEqual(new[] { true, false }, game.Rows.Select(r => r.IsPrimary).ToArray()); // path case ignored
        }
    }
}
