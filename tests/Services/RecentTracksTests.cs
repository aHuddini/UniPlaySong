using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class RecentTracksTests
    {
        // Saved, read back newest first and trimmed to the newest Max; the same track reported again on top (a game song by
        // file name, then by its tags) updates that row rather than adding one
        [Test]
        public void RecentTracks_OutliveARestart_NewestFirst_Trimmed()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ups-recent-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var store = new RecentTracks("Test.txt");
                store.Load(dir);
                for (int i = 0; i < RecentTracks.Max + 5; i++)
                    store.Add("song" + i, "Artist", "Game", "path" + i, null);
                store.Add("Song Title", "Artist", "Game", "path" + (RecentTracks.Max + 4), null);

                var again = new RecentTracks("Test.txt");
                again.Load(dir);

                Assert.AreEqual(RecentTracks.Max, again.Items.Count);
                Assert.AreEqual("Song Title", again.Items.First().Title);
                Assert.AreEqual("song5", again.Items.Last().Title);
                Assert.AreEqual(RecentTracks.Max, File.ReadAllLines(Path.Combine(dir, "Test.txt")).Length);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // Playnite's settings serializer skips only its own DontSerialize (and its own Newtonsoft's JsonIgnore, not UPS's):
        // written to config.json, a list's cover image can't be read back and UPS fails to load
        [Test]
        public void LiveValues_NeverReachPlaynitesSettingsFile()
        {
            foreach (var name in new[] { nameof(UniPlaySongSettings.SpotifyRecentTracks), nameof(UniPlaySongSettings.GameRecentTracks),
                nameof(UniPlaySongSettings.NowPlayingArtColor1), nameof(UniPlaySongSettings.NowPlayingArtColor2) })
                Assert.IsTrue(Attribute.IsDefined(typeof(UniPlaySongSettings).GetProperty(name),
                    typeof(Playnite.SDK.Data.DontSerializeAttribute)), name);
        }
    }
}
