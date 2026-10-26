using System;
using System.IO.MemoryMappedFiles;
using NUnit.Framework;
using UniPlaySong.Audio;

namespace UniPlaySong.Tests.Audio
{
    // The ring the MilkDrop renderer reads (native/visualizer/visualizer_shared.h, VisAudio): data lands at the
    // player's frame index modulo the capacity, and `written` moves only after the copy.
    [TestFixture]
    public class VisualizerAudioFeedTests
    {
        private static string NewName() => @"Local\UniPlaySong_VisualizerTest_" + Guid.NewGuid().ToString("N");

        [Test]
        public void Header_IsReadyForTheRenderer()
        {
            string name = NewName();
            using (var feed = VisualizerAudioFeed.Open(name))
            using (var map = MemoryMappedFile.OpenExisting(name))
            using (var view = map.CreateViewAccessor())
            {
                Assert.AreEqual(VisualizerAudioFeed.Magic, view.ReadUInt32(0));
                Assert.AreEqual(VisualizerAudioFeed.Capacity, view.ReadInt32(VisualizerAudioFeed.OffCapacity));
            }
        }

        // A block that runs past the end of the ring wraps to the start, and `written` ends after its last frame.
        [Test]
        public void Append_AcrossTheEnd_WrapsAndMovesWritten()
        {
            string name = NewName();
            using (var feed = VisualizerAudioFeed.Open(name))
            using (var map = MemoryMappedFile.OpenExisting(name))
            using (var view = map.CreateViewAccessor())
            {
                long start = VisualizerAudioFeed.Capacity - 1; // last slot, then the first
                var block = new float[] { 1f, 2f, 3f, 4f };   // two stereo frames
                feed.Append(48000, 2, start, block, 0, block.Length);

                int Slot(long frame) => VisualizerAudioFeed.OffData + (int)(frame % VisualizerAudioFeed.Capacity) * 2 * sizeof(float);
                Assert.AreEqual(1f, view.ReadSingle(Slot(start)));
                Assert.AreEqual(2f, view.ReadSingle(Slot(start) + 4));
                Assert.AreEqual(3f, view.ReadSingle(Slot(start + 1)), "second frame wrapped to slot 0");
                Assert.AreEqual(4f, view.ReadSingle(Slot(start + 1) + 4));
                Assert.AreEqual((uint)(start + 2), view.ReadUInt32(VisualizerAudioFeed.OffWritten));
                Assert.AreEqual(48000, view.ReadInt32(VisualizerAudioFeed.OffSampleRate));
            }
        }

        // The song block after the audio (visualizer_shared.h, VisSong): a stamp lands at its slot before the count
        // moves, and a request is written whole between two seq bumps (even once done).
        [Test]
        public void Stamp_AndSongRequest_LandWhereTheRendererReadsThem()
        {
            string name = NewName();
            using (var feed = VisualizerAudioFeed.Open(name))
            using (var map = MemoryMappedFile.OpenExisting(name))
            using (var view = map.CreateViewAccessor())
            {
                int song = VisualizerAudioFeed.OffSong;
                Assert.AreEqual(VisualizerAudioFeed.SongMagic, view.ReadUInt32(song));

                for (uint i = 0; i < VisualizerAudioFeed.Stamps + 1; i++) feed.Stamp(1000 + i, 7, 1.5 + i);
                Assert.AreEqual((uint)VisualizerAudioFeed.Stamps + 1, view.ReadUInt32(song + VisualizerAudioFeed.SongOffStamps));
                int slot0 = song + VisualizerAudioFeed.SongOffStamp;   // the 65th stamp wrapped to slot 0
                Assert.AreEqual(1000u + VisualizerAudioFeed.Stamps, view.ReadUInt32(slot0));
                Assert.AreEqual(7u, view.ReadUInt32(slot0 + 4));
                Assert.AreEqual(1.5 + VisualizerAudioFeed.Stamps, view.ReadDouble(slot0 + 8));

                feed.PublishSong(42, @"C:\a\VisualAnalysis\x.mp3.vab", @"C:\t\x.f32", 1234, 5678);
                Assert.AreEqual(2u, view.ReadUInt32(song + VisualizerAudioFeed.SongOffSeq), "seq ends even");
                Assert.AreEqual(42u, view.ReadUInt32(song + VisualizerAudioFeed.SongOffSong));
                Assert.AreEqual(1234L, view.ReadInt64(song + VisualizerAudioFeed.SongOffBytes));
                Assert.AreEqual(5678L, view.ReadInt64(song + VisualizerAudioFeed.SongOffTime));
                string Read(int at) { var s = new System.Text.StringBuilder(); for (int i = 0; ; i++) { char c = view.ReadChar(at + i * 2); if (c == 0) return s.ToString(); s.Append(c); } }
                Assert.AreEqual(@"C:\a\VisualAnalysis\x.mp3.vab", Read(song + VisualizerAudioFeed.SongOffAnalysis));
                Assert.AreEqual(@"C:\t\x.f32", Read(song + VisualizerAudioFeed.SongOffPcm));

                feed.PublishSong(43, @"C:\a\y.vab", null, 1, 2);   // the file is ready: no samples to analyse
                Assert.AreEqual(4u, view.ReadUInt32(song + VisualizerAudioFeed.SongOffSeq));
                Assert.AreEqual(string.Empty, Read(song + VisualizerAudioFeed.SongOffPcm));
            }
        }

        [Test]
        public void Cover_LandsWhereTheRendererReadsIt()
        {
            // VisSong in native/visualizer/visualizer_shared.h (pack 8): coverSeq at 2096, coverOn 2100, coverA 2104, coverB
            // 2116, coverC 2128, 2144 bytes in all (2140 rounded to 8) - the renderer reads these offsets, so a drift here
            // would hand it garbage colours
            Assert.AreEqual(2096, VisualizerAudioFeed.SongOffCoverSeq);
            Assert.AreEqual(2100, VisualizerAudioFeed.SongOffCoverOn);
            Assert.AreEqual(2104, VisualizerAudioFeed.SongOffCoverA);
            Assert.AreEqual(2116, VisualizerAudioFeed.SongOffCoverB);
            Assert.AreEqual(2128, VisualizerAudioFeed.SongOffCoverC);
            Assert.AreEqual(2144, VisualizerAudioFeed.Bytes - VisualizerAudioFeed.OffSong);
            string name = NewName();
            using (var feed = VisualizerAudioFeed.Open(name))
            using (var map = MemoryMappedFile.OpenExisting(name))
            using (var view = map.CreateViewAccessor())
            {
                int song = VisualizerAudioFeed.OffSong;
                feed.PublishCover(true, System.Windows.Media.Color.FromRgb(255, 0, 51), System.Windows.Media.Color.FromRgb(0, 102, 255),
                    System.Windows.Media.Color.FromRgb(0, 255, 153));
                Assert.AreEqual(2u, view.ReadUInt32(song + VisualizerAudioFeed.SongOffCoverSeq), "seq ends even");
                Assert.AreEqual(1u, view.ReadUInt32(song + VisualizerAudioFeed.SongOffCoverOn));
                Assert.AreEqual(1f, view.ReadSingle(song + VisualizerAudioFeed.SongOffCoverA), 1e-6);
                Assert.AreEqual(0.2f, view.ReadSingle(song + VisualizerAudioFeed.SongOffCoverA + 8), 1e-6);
                Assert.AreEqual(0.4f, view.ReadSingle(song + VisualizerAudioFeed.SongOffCoverB + 4), 1e-6);
                Assert.AreEqual(0.6f, view.ReadSingle(song + VisualizerAudioFeed.SongOffCoverC + 8), 1e-6);
                feed.PublishCover(false, default(System.Windows.Media.Color), default(System.Windows.Media.Color), default(System.Windows.Media.Color));   // no art
                Assert.AreEqual(4u, view.ReadUInt32(song + VisualizerAudioFeed.SongOffCoverSeq));
                Assert.AreEqual(0u, view.ReadUInt32(song + VisualizerAudioFeed.SongOffCoverOn));
            }
        }

        [Test]
        public void Interleave_MonoIsDoubled_ExtraChannelsAreDropped()
        {
            var stereo = new float[4];
            VisualizerAudioFeed.Interleave(new[] { 0.5f, -0.5f }, 0, 2, 1, stereo);
            CollectionAssert.AreEqual(new[] { 0.5f, 0.5f, -0.5f, -0.5f }, stereo);

            VisualizerAudioFeed.Interleave(new[] { 1f, 2f, 9f, 3f, 4f, 9f }, 0, 2, 3, stereo);
            CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f }, stereo);
        }

        [Test]
        public void Audible_SilenceAndDitherAreNot_AnythingAboveMinus80dBIs()
        {
            Assert.IsFalse(VisualizerAudioFeed.Audible(new float[512], 512));
            Assert.IsFalse(VisualizerAudioFeed.Audible(new[] { 5e-5f, -5e-5f }, 2));
            Assert.IsTrue(VisualizerAudioFeed.Audible(new[] { 0f, -2e-4f }, 2));
            Assert.IsFalse(VisualizerAudioFeed.Audible(new[] { 0f, 0.5f }, 1)); // only the first `count` samples
        }

        [Test]
        public void Append_OnlySoundMovesTheLastSoundTick()
        {
            using (var feed = VisualizerAudioFeed.Open(NewName()))
            {
                int start = feed.LastSoundTick;
                System.Threading.Thread.Sleep(60); // TickCount moves in ~16 ms steps
                feed.Append(48000, 2, 0, new float[256], 0, 256);
                Assert.AreEqual(start, feed.LastSoundTick, "silence must not count as sound");
                feed.Append(48000, 2, 128, new[] { 0.3f, 0.3f }, 0, 2);
                Assert.Greater(unchecked(feed.LastSoundTick - start), 30);
            }
        }
    }
}
