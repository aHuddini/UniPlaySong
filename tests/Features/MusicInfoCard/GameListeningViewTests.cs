using System;
using System.Collections.Generic;
using NUnit.Framework;
using UniPlaySong.Features.MusicInfoCard.Player;
using UniPlaySong.Models;

namespace UniPlaySong.Tests.Features.MusicInfoCard
{
    // The Details page's Listening tile: this game's totals, and its most-played songs from its own folder only
    [TestFixture]
    public class GameListeningViewTests
    {
        private static TrackListeningRecord Track(string title, string path, int plays)
            => new TrackListeningRecord { Title = title, Path = path, PlayCount = plays };

        [Test]
        public void Build_TotalsTop3_FromThisGamesFolderOnly()
        {
            var snap = new ListeningSnapshot
            {
                Games = new List<GameListeningRecord>
                {
                    new GameListeningRecord { GameId = "g1", TotalSeconds = 22320, PlayCount = 143,
                        LastPlayedUtc = new DateTime(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc) }
                },
                Tracks = new List<TrackListeningRecord>
                {
                    Track("Sequoia", @"C:\games\g1\s.mp3", 31),
                    Track("Painting", @"C:\games\g1\p.mp3", 24),
                    Track("Overpass", @"C:\games\g1\o.mp3", 17),
                    Track("Grotto", @"C:\games\g1\gr.mp3", 5),
                    Track("Other", @"C:\games\g2\x.mp3", 99),
                    Track("Sibling", @"C:\games\g1x\y.mp3", 80),   // a folder that merely starts with "g1"
                }
            };

            var v = GameListeningView.Build(snap, "g1", @"C:\games\g1", new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc));

            Assert.IsTrue(v.HasHistory);
            Assert.AreEqual(TimeSpan.FromSeconds(22320), v.Listened);
            Assert.AreEqual(143, v.Plays);
            Assert.AreEqual(2, v.DaysSinceLastPlayed);
            CollectionAssert.AreEqual(new[] { ("Sequoia", 31), ("Painting", 24), ("Overpass", 17) }, v.MostPlayed);
        }

        [Test]
        public void NoRecord_HasNoHistory()
        {
            var v = GameListeningView.Build(new ListeningSnapshot(), "g1", @"C:\games\g1", DateTime.UtcNow);
            Assert.IsFalse(v.HasHistory);
            Assert.IsEmpty(v.MostPlayed);
        }
    }
}
