using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace UniPlaySong.Tests.Controls
{
    // The settings pages need a running Playnite to render, so these pin the source instead.
    [TestFixture]
    public class SettingsPageRenderingTests
    {
        private static string SrcDir()
        {
            // Normalized: an unresolved "bin\Release\..\.." path would trip the bin/obj filter below on every file.
            var dir = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "src"));
            if (!Directory.Exists(dir)) Assert.Ignore("src not reachable from the test output directory");
            return dir;
        }

        // A DropShadowEffect on UpsCard rendered every card to an offscreen bitmap: over the translucent glass
        // fill that cost ClearType, and at 125%/150% scaling the bitmap was resampled - card text read blurry on
        // Windows 10 while shadowless boxes beside it stayed sharp.
        [Test]
        public void SettingsCardsCarryNoEffect()
        {
            var text = File.ReadAllText(Path.Combine(SrcDir(), "Controls", "Settings", "SettingsResources.xaml"));
            var start = text.IndexOf("<Style x:Key=\"UpsCard\"");
            Assert.Greater(start, -1, "UpsCard style should exist");
            var card = text.Substring(start, text.IndexOf("</Style>", start) - start);

            StringAssert.DoesNotContain("Property=\"Effect\"", card, "no Effect on UpsCard - it blurs every label inside a card");
        }

        // A plain <ResourceDictionary Source=...> parses the file again for each owner; with 41 pages merging
        // SettingsResources.xaml that was 42 parses per settings open. Pages must merge the shared instance.
        [Test]
        public void SettingsResourcesAreMergedThroughTheSharedDictionary()
        {
            var plain = new Regex(@"<ResourceDictionary\s+Source=""[^""]*SettingsResources\.xaml""");
            var offenders = Directory.GetFiles(SrcDir(), "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .Where(f => plain.IsMatch(File.ReadAllText(f)))
                .Select(Path.GetFileName)
                .ToList();

            CollectionAssert.IsEmpty(offenders, "merge SettingsResources.xaml with <ups:SharedResourceDictionary Source=.../>");
        }
    }
}
