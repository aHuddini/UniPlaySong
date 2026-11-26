using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class SettingsBackupServiceTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySongTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        // Paths and library IDs only mean something on the PC / Playnite library they came from.
        [Test]
        public void Export_LeavesOutPathsAndLibraryIds()
        {
            var settings = new UniPlaySongSettings
            {
                RadioCustomFolderPath = @"C:\Music\Radio",
                CelebrationSoundPath = @"C:\Sounds\yay.wav",
                SpotifyExePath = @"C:\Spotify\Spotify.exe",
            };
            var file = Path.Combine(_tempDir, "export.json");

            SettingsBackupService.ExportToJson(settings, file);
            var json = JObject.Parse(File.ReadAllText(file));

            settings.RadioCustomFolderPaths = new System.Collections.Generic.List<string> { @"C:\Music\Radio" };
            settings.DefaultMusicFolderPaths = new System.Collections.Generic.List<string> { @"C:\Music\Default" };
            SettingsBackupService.ExportToJson(settings, file);
            json = JObject.Parse(File.ReadAllText(file));

            foreach (var field in new[] { "RadioCustomFolderPath", "RadioCustomFolderPaths", "DefaultMusicFolderPaths", "CelebrationSoundPath", "SpotifyExePath", "NostalgiaStatusIds", "GamePropFilterGenreIds" })
                Assert.IsFalse(json.ContainsKey(field), $"{field} must not travel in an export");
            Assert.IsTrue(json.ContainsKey(nameof(UniPlaySongSettings.DefaultMusicState)), "ordinary settings still export");
        }

        // A 1.9.3 export has "Play music in" but no default-music setting: importing it must give default music the
        // same mode, as a fresh 1.9.4 install of those settings would, not keep whatever this install had.
        [Test]
        public void Import_OfAnExportFromBeforeTheDefaultMusicSetting_CopiesPlayMusicIn()
        {
            var file = Path.Combine(_tempDir, "old.json");
            File.WriteAllText(file, "{ \"MusicState\": \"Fullscreen\" }");
            var current = new UniPlaySongSettings { DefaultMusicState = AudioState.Always, DefaultMusicStateMigrated = true };

            var merged = SettingsBackupService.ImportFromJson(file, current);

            Assert.AreEqual(AudioState.Fullscreen, merged.MusicState);
            Assert.AreEqual(AudioState.Fullscreen, merged.DefaultMusicState);
            Assert.IsTrue(merged.DefaultMusicStateMigrated);
        }

        // A current export carries its own default-music setting, which wins.
        [Test]
        public void Import_OfACurrentExport_KeepsItsDefaultMusicSetting()
        {
            var source = new UniPlaySongSettings { MusicState = AudioState.Fullscreen, DefaultMusicState = AudioState.Desktop, DefaultMusicStateMigrated = true };
            var file = Path.Combine(_tempDir, "new.json");
            SettingsBackupService.ExportToJson(source, file);

            var merged = SettingsBackupService.ImportFromJson(file, new UniPlaySongSettings { DefaultMusicStateMigrated = true });

            Assert.AreEqual(AudioState.Desktop, merged.DefaultMusicState);
        }

        // The snapshot's per-group listing is generated, so every filed setting appears - including ones added later.
        [Test]
        public void Markdown_ListsEverySetting()
        {
            var file = Path.Combine(_tempDir, "snapshot.md");
            SettingsBackupService.ExportToMarkdown(new UniPlaySongSettings(), file, 0, 0, 0, _tempDir, "test", "test", "Desktop");
            var text = File.ReadAllText(file);

            var missing = SettingsGroups.Map.Values.SelectMany(n => n)
                .Where(n => typeof(UniPlaySongSettings).GetProperty(n) != null)
                .Where(n => !text.Contains($"- {SettingsBackupService.Humanize(n)}: "))
                .ToList();
            CollectionAssert.IsEmpty(missing);
            StringAssert.Contains("- Default Music State: `Always`", text);
        }
    }
}
