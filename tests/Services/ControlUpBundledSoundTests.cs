using System.IO;
using System.Linq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Pins the bundled ControlUp sound to what actually ships. The default filename is a string
    // constant matched against a manifest entry and a file on disk; a typo in any of the three
    // resolves to null and the feature goes silent with nothing in the log.
    [TestFixture]
    public class ControlUpBundledSoundTests
    {
        // src/Jingles as laid out in the repo — the same tree the packaging script copies verbatim.
        private static string JinglesRoot()
        {
            var dir = TestContext.CurrentContext.TestDirectory;   // tests/bin/<cfg>/<tfm>
            var root = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "src"));
            return Path.Combine(root, "Jingles");
        }

        [SetUp]
        public void SetUp()
        {
            BundledJingleService.Initialize(Path.GetDirectoryName(JinglesRoot()));
        }

        [Test]
        public void TheBundledSound_ExistsOnDisk()
        {
            var path = Path.Combine(JinglesRoot(), BundledJingleService.DefaultControlUpJingle);

            Assert.IsTrue(File.Exists(path),
                $"the shipped ControlUp sound must exist at {path} — a rename here silences the feature");
        }

        [Test]
        public void TheBundledSound_IsListedUnderTheControlUpCategory()
        {
            var listed = BundledJingleService.GetControlUpJingles();

            Assert.AreEqual(1, listed.Count,
                "exactly one bundled ControlUp sound ships: the user picks this or their own file");
            Assert.AreEqual(BundledJingleService.DefaultControlUpJingle, listed[0].File);
        }

        // The category must not leak the other jingle sets, which is what keeps the ControlUp
        // sound independent of the celebration/abandoned/achievement entries in the same manifest.
        [Test]
        public void TheControlUpCategory_ExcludesEveryOtherJingle()
        {
            var controlUp = BundledJingleService.GetControlUpJingles().Select(j => j.File).ToList();

            CollectionAssert.IsNotEmpty(BundledJingleService.GetJingles(), "precondition: other categories exist");

            CollectionAssert.IsEmpty(
                BundledJingleService.GetJingles().Select(j => j.File).Intersect(controlUp));
            CollectionAssert.IsEmpty(
                BundledJingleService.GetAbandonedJingles().Select(j => j.File).Intersect(controlUp));
            CollectionAssert.IsEmpty(
                BundledJingleService.GetAchievementJingles().Select(j => j.File).Intersect(controlUp));
        }

        // What the URI actually plays: the settings default must resolve to a real file through the
        // same call JingleService makes, or the event fires and nothing is heard.
        [Test]
        public void TheSettingsDefault_ResolvesToTheBundledFile()
        {
            var settings = new UniPlaySongSettings();

            Assert.AreEqual(BundledJingleService.DefaultControlUpJingle, settings.SelectedControlUpDetectJingle,
                "a fresh install must point at the shipped sound, not an empty picker");
            Assert.AreEqual(CelebrationSoundType.BundledJingle, settings.ControlUpDetectSoundType);

            var resolved = BundledJingleService.ResolveJinglePath(settings.SelectedControlUpDetectJingle);
            Assert.IsNotNull(resolved, "the default selection must resolve through the manifest");
            Assert.IsTrue(File.Exists(resolved));
        }

        // Belt and braces for a settings file written before this sound shipped: its stored selection
        // is empty, and JingleService falls back to the first entry rather than playing nothing.
        [Test]
        public void AnEmptySelection_FallsBackToTheBundledSound()
        {
            Assert.AreEqual(BundledJingleService.DefaultControlUpJingle,
                BundledJingleService.GetDefaultControlUpJingleFilename());
        }
    }
}
