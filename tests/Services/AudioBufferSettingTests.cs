using System.Linq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Common;

namespace UniPlaySong.Tests.Services
{
    // The buffer feeds Mix_OpenAudio directly. A value it rejects doesn't degrade playback — the
    // device fails to open and ALL audio dies for the session, so the guard matters more than the
    // feature does.
    [TestFixture]
    public class AudioBufferSettingTests
    {
        [Test]
        public void DefaultIsTheHistoricalBufferSize()
        {
            Assert.AreEqual(2048, Constants.DefaultAudioBufferSamples,
                "2048 is the pre-v1.7.2 behavior; changing the default changes every user's audio");
            Assert.AreEqual(Constants.DefaultAudioBufferSamples, new UniPlaySongSettings().AudioBufferSamples);
        }

        // Every value the dropdown can produce must be one Mix_OpenAudio accepts.
        [Test]
        public void EveryOfferedOption_IsAPowerOfTwoInRange()
        {
            var options = AudioBufferOption.All;

            CollectionAssert.IsNotEmpty(options);
            foreach (var option in options)
            {
                Assert.IsTrue(option.Samples >= 256 && option.Samples <= 8192,
                    $"{option.Samples} is outside the range the player accepts");
                Assert.AreEqual(0, option.Samples & (option.Samples - 1),
                    $"{option.Samples} is not a power of two — Mix_OpenAudio would fail the open");
                Assert.IsFalse(string.IsNullOrWhiteSpace(option.Label));
            }
        }

        [Test]
        public void TheDefaultIsOneOfTheOfferedOptions()
        {
            var options = AudioBufferOption.All;

            CollectionAssert.Contains(options.Select(o => o.Samples).ToList(),
                Constants.DefaultAudioBufferSamples,
                "a default missing from the dropdown would render the combo box blank");
        }
    }
}
