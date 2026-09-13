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

        // The scan record: SHA-256 plus VirusTotal report for the binaries in that folder.
        private const string AuditDoc = "VIRUSTOTAL-AUDIT.md";

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

        // Each VIRUSTOTAL-AUDIT.md records a SHA-256 and scan report for the binaries beside it.
        // Rebuild one and its hash changes, so the recorded report then describes a file that is no
        // longer shipped - while still reading as current, which is the worst kind of wrong for a
        // provenance record. Checked against the audit in the DLL's OWN folder, so the record
        // cannot drift to somewhere nobody looks.
        [Test]
        public void ScanReportsMatchTheCommittedBinaries()
        {
            var root = RepoRoot();
            Assert.IsNotNull(root, "could not locate the repository root from the test directory");

            var stale = new System.Collections.Generic.List<string>();

            foreach (var dll in CommittedDlls(root))
            {
                var audit = Path.Combine(Path.GetDirectoryName(dll), AuditDoc);

                var actual = Sha256(dll);

                if (!File.Exists(audit) || !File.ReadAllText(audit).Contains(actual))
                {
                    stale.Add($"{Path.GetFileName(dll)}: {actual}");
                }
            }

            Assert.IsEmpty(stale,
                "these committed DLLs have no matching SHA-256 in the " + AuditDoc + " beside them:\n  "
                + string.Join("\n  ", stale)
                + "\n\nThe binary changed since it was scanned. Re-submit it to VirusTotal, then "
                + "update the hash and link in that " + AuditDoc + ".");
        }

        private static string Sha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static System.Collections.Generic.IEnumerable<string> CommittedDlls(DirectoryInfo root)
        {
            var skip = new[] { "bin", "obj", ".git", "packages", "pext", "package", "Release" }
                .Select(d => Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar)
                .ToArray();

            return Directory
                .GetFiles(root.FullName, "*.dll", SearchOption.AllDirectories)
                .Where(f => !skip.Any(s => f.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(f => f);
        }

        // A checked-in copy of an assembly NuGet already restores is not a backup, it is a second
        // answer to the same question - and the packaging script would prefer it, so bumping a
        // PackageReference would not change what ships.
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
