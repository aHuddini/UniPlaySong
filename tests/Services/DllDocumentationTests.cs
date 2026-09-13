using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UniPlaySong.Tests.Services
{
    // Every folder holding a .dll must say what that DLL is and where it came from.
    //
    // Binaries are the files a reader cannot inspect. One sitting in a folder with no note beside
    // it is one nobody can account for later, and "where did this come from" has no answer once
    // whoever added it has moved on.
    [TestFixture]
    public class DllDocumentationTests
    {
        // Not README.md: a folder's README is about the folder, this is the provenance record for
        // binaries in it. Separate names so both can exist and a diff shows which changed.
        private const string DllReadme = "DLL-README.md";

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "version.txt")))
            {
                dir = dir.Parent;
            }

            return dir;
        }

        [Test]
        public void EveryFolderContainingADllIsDocumented()
        {
            var root = RepoRoot();
            Assert.IsNotNull(root, "could not locate the repository root from the test directory");

            // Build output and restored packages are generated, not authored - not the
            // repository's to document, and not in source control anyway.
            var skip = new[] { "bin", "obj", ".git", "packages", "pext", "package", "Release" }
                .Select(d => Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar)
                .ToArray();

            var undocumented = Directory
                .GetFiles(root.FullName, "*.dll", SearchOption.AllDirectories)
                .Where(f => !skip.Any(s => f.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(Path.GetDirectoryName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(dir => !File.Exists(Path.Combine(dir, DllReadme)))
                .Select(dir => dir.Substring(root.FullName.Length).TrimStart(Path.DirectorySeparatorChar))
                .OrderBy(x => x)
                .ToList();

            Assert.IsEmpty(undocumented,
                "these folders contain a .dll but no " + DllReadme + ":\n  "
                + string.Join("\n  ", undocumented)
                + "\n\nAdd " + DllReadme + " naming each DLL, what it is for, where it came from and "
                + "its licence, then list the folder in docs/dev_docs/SHIPPED_BINARIES.md. The name "
                + "is fixed - a plain README.md does not satisfy this.");
        }

        // A checked-in copy of an assembly NuGet already restores is not a backup, it is a second
        // answer to the same question. lib/dll was exactly that: the packaging script preferred it,
        // so bumping a PackageReference did not change what shipped. Removed in v1.8.8 after
        // confirming the package is byte-identical without it.
        [Test]
        public void NuGetAssembliesAreNotAlsoCheckedIn()
        {
            var root = RepoRoot();
            Assert.IsNotNull(root, "could not locate the repository root from the test directory");

            var restored = new[]
            {
                "HtmlAgilityPack.dll", "MaterialDesignColors.dll",
                "MaterialDesignThemes.Wpf.dll", "Microsoft.Xaml.Behaviors.dll",
                "NAudio.dll", "Newtonsoft.Json.dll", "SkiaSharp.dll", "TagLibSharp.dll"
            };

            var skip = new[] { "bin", "obj", ".git", "packages", "pext", "package", "Release" }
                .Select(d => Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar)
                .ToArray();

            var checkedIn = Directory
                .GetFiles(root.FullName, "*.dll", SearchOption.AllDirectories)
                .Where(f => !skip.Any(s => f.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                .Where(f => restored.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => f.Substring(root.FullName.Length).TrimStart(Path.DirectorySeparatorChar))
                .OrderBy(x => x)
                .ToList();

            Assert.IsEmpty(checkedIn,
                "these are restored from NuGet but also committed:\n  "
                + string.Join("\n  ", checkedIn)
                + "\n\nDelete them. The packaging script takes managed dependencies from the build "
                + "output, so a committed copy either does nothing or silently overrides the "
                + "version pinned in src/UniPlaySong.csproj.");
        }
    }
}
