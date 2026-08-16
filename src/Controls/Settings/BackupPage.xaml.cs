using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using UniPlaySong.Services;

namespace UniPlaySong.Controls.Settings
{
    // Settings Import/Export (v1.5.0). All three handlers share the same shape: get settings, ask
    // the user for a path via Playnite SDK Dialogs, delegate the actual work to
    // SettingsBackupService, surface a result dialog. Errors are caught here and surfaced friendlier
    // to the user than letting an exception bubble into the settings host.
    public partial class BackupPage : UserControl
    {
        public BackupPage()
        {
            InitializeComponent();
        }

        private void ExportSettingsJson_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm?.Settings == null) return;

            var filePath = vm.PlayniteApi.Dialogs.SaveFile("JSON files|*.json", true);
            if (string.IsNullOrWhiteSpace(filePath)) return;

            // If user didn't include extension, append .json
            if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                filePath += ".json";
            }

            try
            {
                SettingsBackupService.ExportToJson(vm.Settings, filePath);
                vm.PlayniteApi.Dialogs.ShowMessage(
                    $"Settings exported to:\n{filePath}\n\n" +
                    "Tool paths and Custom Rotation game IDs were excluded — those stay machine-specific.",
                    "Export Successful");
            }
            catch (Exception ex)
            {
                vm.PlayniteApi.Dialogs.ShowErrorMessage($"Export failed: {ex.Message}", "Settings Export Error");
            }
        }

        private void ImportSettingsJson_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm?.Settings == null) return;

            var filePath = vm.PlayniteApi.Dialogs.SelectFile("JSON files|*.json");
            if (string.IsNullOrWhiteSpace(filePath)) return;

            // Read the export version first (informational; not blocking on mismatch)
            var exportVersion = SettingsBackupService.ReadExportVersion(filePath);
            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

            if (!string.IsNullOrWhiteSpace(exportVersion) && exportVersion != currentVersion)
            {
                var proceed = vm.PlayniteApi.Dialogs.ShowMessage(
                    $"This settings file was exported from UniPlaySong v{exportVersion}.\n" +
                    $"You are currently running v{currentVersion}.\n\n" +
                    "Settings unknown to the current version will be ignored. Continue?",
                    "Version Mismatch",
                    MessageBoxButton.YesNo);
                if (proceed != MessageBoxResult.Yes) return;
            }

            try
            {
                var merged = SettingsBackupService.ImportFromJson(filePath, vm.Settings);
                // Apply the merged settings via the plugin's settings service so all
                // downstream subscribers (coordinator, dashboard, etc.) react properly.
                vm.Plugin.ApplyImportedSettings(merged);

                vm.PlayniteApi.Dialogs.ShowMessage(
                    "Settings imported successfully.\n\n" +
                    "The following are machine-specific and were not transferred — please verify they're set correctly on this machine:\n\n" +
                    "  • yt-dlp path\n" +
                    "  • FFmpeg path\n" +
                    "  • Custom default music file path (if any)\n" +
                    "  • Custom default music folder path (if any)\n" +
                    "  • Custom cookies file path (if any)\n" +
                    "  • Custom Rotation game pool (re-add games)",
                    "Import Successful");
            }
            catch (FileNotFoundException ex)
            {
                vm.PlayniteApi.Dialogs.ShowErrorMessage($"File not found: {ex.FileName}", "Settings Import Error");
            }
            catch (InvalidDataException ex)
            {
                vm.PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Settings Import Error");
            }
            catch (Exception ex)
            {
                vm.PlayniteApi.Dialogs.ShowErrorMessage($"Import failed: {ex.Message}", "Settings Import Error");
            }
        }

        private void ExportSettingsMarkdown_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm?.Settings == null) return;

            var filePath = vm.PlayniteApi.Dialogs.SaveFile("Markdown files|*.md", true);
            if (string.IsNullOrWhiteSpace(filePath)) return;

            if (!filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                filePath += ".md";
            }

            try
            {
                // Derived stats for the Markdown snapshot. These come from the plugin's
                // existing services (file enumeration, version reflection, API queries).
                var stats = vm.Plugin.GetSnapshotStats();

                SettingsBackupService.ExportToMarkdown(
                    vm.Settings,
                    filePath,
                    totalGamesTracked: stats.totalGamesTracked,
                    gamesWithMusic: stats.gamesWithMusic,
                    totalMusicStorageBytes: stats.totalMusicStorageBytes,
                    musicFolderPath: stats.musicFolderPath,
                    upsVersion: stats.upsVersion,
                    osInfo: stats.osInfo,
                    modeAtExport: stats.modeAtExport);

                vm.PlayniteApi.Dialogs.ShowMessage(
                    $"Snapshot exported to:\n{filePath}\n\n" +
                    "This is a one-way snapshot — paste it into GitHub issues, Discord, or keep for personal reference. " +
                    "It is NOT re-importable. Use Export Settings (JSON) for re-importable backups.",
                    "Snapshot Saved");
            }
            catch (Exception ex)
            {
                vm.PlayniteApi.Dialogs.ShowErrorMessage($"Snapshot export failed: {ex.Message}", "Snapshot Export Error");
            }
        }
    }
}
