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

        [Test]
        public void Interleave_MonoIsDoubled_ExtraChannelsAreDropped()
        {
            var stereo = new float[4];
            VisualizerAudioFeed.Interleave(new[] { 0.5f, -0.5f }, 0, 2, 1, stereo);
            CollectionAssert.AreEqual(new[] { 0.5f, 0.5f, -0.5f, -0.5f }, stereo);

            VisualizerAudioFeed.Interleave(new[] { 1f, 2f, 9f, 3f, 4f, 9f }, 0, 2, 3, stereo);
            CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f }, stereo);
        }
    }
}
