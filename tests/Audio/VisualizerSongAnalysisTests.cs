using System;
using System.IO;
using NUnit.Framework;
using UniPlaySong.Audio;

namespace UniPlaySong.Tests.Audio
{
    // Where a song's analysis file lives, and when it still counts (native/visualizer/analysis.h writes the files).
    [TestFixture]
    public class VisualizerSongAnalysisTests
    {
        [Test]
        public void AnalysisPath_GameSong_GoesInThatGamesVisualAnalysisFolder()
        {
            string game = Path.Combine(@"C:\P\ExtraMetadata\UniPlaySong\Games", Guid.NewGuid().ToString());
            Assert.AreEqual(Path.Combine(game, "VisualAnalysis", "Theme.mp3.vab"),
                VisualizerSongAnalysis.AnalysisPath(Path.Combine(game, "Theme.mp3"), @"C:\other"));
        }

        [Test]
        public void AnalysisPath_OtherMusic_GoesInTheSharedFolder_SameNameForTheSameSong()
        {
            string a = VisualizerSongAnalysis.AnalysisPath(@"D:\Music\Default\Menu.ogg", @"C:\other");
            Assert.AreEqual(@"C:\other", Path.GetDirectoryName(a));
            StringAssert.EndsWith(".vab", a);
            Assert.AreEqual(a, VisualizerSongAnalysis.AnalysisPath(@"D:\MUSIC\Default\menu.ogg", @"C:\other"), "case doesn't matter on Windows");
            Assert.AreNotEqual(a, VisualizerSongAnalysis.AnalysisPath(@"D:\Music\Default\Battle.ogg", @"C:\other"));
        }

        [Test]
        public void Analysable_EmulatedFormatsStayLiveOnly()
        {
            Assert.IsTrue(VisualizerSongAnalysis.Analysable(@"C:\x\song.mp3"));
            Assert.IsTrue(VisualizerSongAnalysis.Analysable(@"C:\x\song.ogg"));
            Assert.IsFalse(VisualizerSongAnalysis.Analysable(@"C:\x\song.nsf"));
            Assert.IsFalse(VisualizerSongAnalysis.Analysable(@"C:\x\song.psf"));
        }

        // The header analysis.h writes: magic, version, the song file's size and last write - any mismatch re-analyses.
        [Test]
        public void IsCurrent_OnlyForThisVersionAndThisStateOfTheSong()
        {
            string file = Path.Combine(Path.GetTempPath(), "ups_vab_" + Guid.NewGuid().ToString("N") + ".vab");
            try
            {
                void Write(uint magic, int version, long bytes, long time)
                {
                    using (var w = new BinaryWriter(File.Create(file))) { w.Write(magic); w.Write(version); w.Write(bytes); w.Write(time); w.Write(new byte[16]); }
                }
                Assert.IsFalse(VisualizerSongAnalysis.IsCurrent(file, 10, 20), "no file");
                Write(VisualizerSongAnalysis.FileMagic, VisualizerSongAnalysis.AnalysisVersion, 10, 20);
                Assert.IsTrue(VisualizerSongAnalysis.IsCurrent(file, 10, 20));
                Assert.IsFalse(VisualizerSongAnalysis.IsCurrent(file, 11, 20), "the song file changed size");
                Assert.IsFalse(VisualizerSongAnalysis.IsCurrent(file, 10, 21), "the song file was rewritten");
                Write(VisualizerSongAnalysis.FileMagic, VisualizerSongAnalysis.AnalysisVersion + 1, 10, 20);
                Assert.IsFalse(VisualizerSongAnalysis.IsCurrent(file, 10, 20), "another version's analysis");
            }
            finally { File.Delete(file); }
        }
    }
}
