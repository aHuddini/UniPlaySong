using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace UniPlaySong.Services
{
    // Settings backup + restore service. Three operations:
    //   - ExportToJson:   portable JSON, re-importable; strips machine-specific paths
    //   - ImportFromJson: restores a previously-exported JSON; preserves current
    //                     machine-specific paths so user doesn't lose their tool config
    //   - ExportToMarkdown: human-readable snapshot for sharing in support requests;
    //                       includes everything, redacts sensitive values, derived stats
    //
    // Design parity with the existing global-reset path at UniPlaySong.cs:5248
    // (JsonConvert.SerializeObject + PopulateObject round-trip).
    public static class SettingsBackupService
    {
        // Settings excluded from JSON export entirely — these are machine-specific
        // (tool paths, library-specific GUIDs, OAuth state) and shouldn't travel
        // between machines. On import, the importer's existing values for these
        // fields are preserved (the import is a "merge in non-machine-specific values").
        private static readonly string[] MachineSpecificFields = new[]
        {
            "YtDlpPath",
            "FFmpegPath",
            "DefaultMusicPath",
            "DefaultMusicFolderPath",
            "CustomCookiesFilePath",
            "CustomRotationGameIds",
            // Future: add LastfmSessionKey here when Feature 6 (Last.fm Scrobbling) lands
        };

        // Settings whose value should be redacted in Markdown snapshots (shown as
        // "*****"). The field name itself is still listed so support requests are
        // informative ("yes, Last.fm is configured") without leaking secrets.
        // Initially empty; Feature 6 (Last.fm Scrobbling) will add LastfmSessionKey.
        private static readonly string[] SensitiveFields = new string[0];

        // === JSON Export ===
        // Writes a portable .json file containing all settings except machine-specific fields.
        // Returns the path written on success; throws on I/O or serialization failure.
        public static string ExportToJson(UniPlaySongSettings settings, string filePath)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path required", nameof(filePath));

            // Round-trip through JObject so we can strip the machine-specific fields
            // without modifying the live settings instance.
            var jObj = JObject.FromObject(settings);

            foreach (var field in MachineSpecificFields)
            {
                jObj.Remove(field);
            }

            // Add a small header for human readers / version compatibility checks on import.
            // Stored as a top-level "_meta" property; ignored by PopulateObject during import
            // since UniPlaySongSettings has no "_meta" field.
            var meta = new JObject
            {
                ["exporter"] = "UniPlaySong",
                ["version"] = GetUpsVersion(),
                ["exported_at"] = DateTime.UtcNow.ToString("o"),
                ["excluded_fields"] = new JArray(MachineSpecificFields)
            };
            jObj.AddFirst(new JProperty("_meta", meta));

            File.WriteAllText(filePath, jObj.ToString(Formatting.Indented), Encoding.UTF8);
            return filePath;
        }

        // === JSON Import ===
        // Reads a .json file, deserializes into a fresh settings instance, then
        // preserves the current settings' machine-specific paths. Returns the merged
        // settings object ready to push through SettingsService.UpdateSettings().
        // Throws on missing file, malformed JSON, or schema-incompatible content.
        public static UniPlaySongSettings ImportFromJson(string filePath, UniPlaySongSettings currentSettings)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path required", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Settings file not found", filePath);
            if (currentSettings == null) throw new ArgumentNullException(nameof(currentSettings));

            var jsonText = File.ReadAllText(filePath, Encoding.UTF8);
            JObject jObj;
            try
            {
                jObj = JObject.Parse(jsonText);
            }
            catch (JsonReaderException ex)
            {
                throw new InvalidDataException("File is not valid JSON. Make sure you selected a UniPlaySong settings file.", ex);
            }

            // Strip _meta before merging (the settings class doesn't have a _meta field;
            // PopulateObject would silently ignore it, but explicit removal keeps the
            // import path predictable).
            jObj.Remove("_meta");

            // Strip any machine-specific fields that snuck into the export (defensive —
            // shouldn't be there if produced by ExportToJson, but a hand-edited file
            // could include them and we don't want to overwrite the user's local paths).
            foreach (var field in MachineSpecificFields)
            {
                jObj.Remove(field);
            }

            // Start from the user's current settings (preserves machine-specific paths
            // and the LastfmSessionKey-style fields), then merge the imported values on top.
            // Same JsonConvert.PopulateObject pattern as the global-reset path.
            var merged = JsonConvert.DeserializeObject<UniPlaySongSettings>(JsonConvert.SerializeObject(currentSettings));
            JsonConvert.PopulateObject(jObj.ToString(), merged);

            return merged;
        }

        // Reads just the _meta header from an exported file (without merging settings).
        // Used by import to display a "this file was exported from version X" warning
        // when the version differs from the current UPS install.
        public static string ReadExportVersion(string filePath)
        {
            try
            {
                var jsonText = File.ReadAllText(filePath, Encoding.UTF8);
                var jObj = JObject.Parse(jsonText);
                var meta = jObj["_meta"] as JObject;
                return meta?["version"]?.ToString();
            }
            catch
            {
                // If we can't read the version, return null and let the caller decide.
                // (Most likely a hand-edited file without _meta — still valid for import.)
                return null;
            }
        }

        // === Markdown Snapshot Export ===
        // Writes a human-readable .md file describing the user's UPS configuration.
        // Intended for support requests, personal notes, or "audit my UPS setup" use cases.
        // Includes derived state (game counts, music storage) that JSON intentionally excludes.
        // Sensitive values redacted as "*****".
        public static string ExportToMarkdown(
            UniPlaySongSettings settings,
            string filePath,
            int totalGamesTracked,
            int gamesWithMusic,
            long totalMusicStorageBytes,
            string musicFolderPath,
            string upsVersion,
            string osInfo,
            string modeAtExport)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path required", nameof(filePath));

            var sb = new StringBuilder();

            sb.AppendLine("# UniPlaySong Settings Snapshot");
            sb.AppendLine();
            sb.AppendLine($"**Version:** {upsVersion}");
            sb.AppendLine($"**Date:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"**Machine:** {osInfo}");
            sb.AppendLine($"**Mode at export:** {modeAtExport}");
            sb.AppendLine();

            // --- Library ---
            sb.AppendLine("## Library");
            sb.AppendLine($"- Total games tracked: {totalGamesTracked}");
            sb.AppendLine($"- Games with music: {gamesWithMusic}");
            sb.AppendLine($"- Total music storage: {FormatBytes(totalMusicStorageBytes)}");
            sb.AppendLine($"- Music folder: `{musicFolderPath ?? "(not set)"}`");
            sb.AppendLine();

            // --- Music Playback ---
            sb.AppendLine("## Music Playback");
            AppendSettingRow(sb, "Music State", settings.MusicState);
            AppendSettingRow(sb, "Enable Music", settings.EnableMusic);
            AppendSettingRow(sb, "Enable Default Music", settings.EnableDefaultMusic);
            AppendSettingRow(sb, "Default Music Source", settings.DefaultMusicSourceOption);
            AppendSettingRow(sb, "Selected Bundled Preset", settings.SelectedBundledPreset);
            AppendSettingRow(sb, "Radio Mode Enabled", settings.RadioModeEnabled);
            AppendSettingRow(sb, "Play Only On Game Select", settings.PlayOnlyOnGameSelect);
            AppendSettingRow(sb, "Music Only For Installed Games", settings.MusicOnlyForInstalledGames);
            AppendSettingRow(sb, "Randomize On Music End", settings.RandomizeOnMusicEnd);
            sb.AppendLine();

            // --- Volume & Fade ---
            sb.AppendLine("## Volume & Fade");
            AppendSettingRow(sb, "Music Volume", $"{settings.MusicVolume}%");
            AppendSettingRow(sb, "Fullscreen Volume Boost", $"{settings.FullscreenVolumeBoostPercent}%");
            AppendSettingRow(sb, "Fade In Duration", $"{settings.FadeInDuration:F2}s");
            AppendSettingRow(sb, "Fade Out Duration", $"{settings.FadeOutDuration:F2}s");
            AppendSettingRow(sb, "Fade In Curve (NAudio)", settings.NaudioFadeInCurve);
            AppendSettingRow(sb, "Fade Out Curve (NAudio)", settings.NaudioFadeOutCurve);
            AppendSettingRow(sb, "Fade Out Before Song End", settings.FadeOutBeforeSongEnd);
            AppendSettingRow(sb, "Enable True Crossfade", settings.EnableTrueCrossfade);
            sb.AppendLine();

            // --- Pauses ---
            sb.AppendLine("## Pauses");
            AppendSettingRow(sb, "Pause On Focus Loss", settings.PauseOnFocusLoss);
            AppendSettingRow(sb, "Stay Paused On Focus Restore", settings.FocusLossStayPaused);
            AppendSettingRow(sb, "Ignore Brief Focus Loss", settings.FocusLossIgnoreBrief);
            AppendSettingRow(sb, "Pause On Minimize", settings.PauseOnMinimize);
            AppendSettingRow(sb, "Pause When In System Tray", settings.PauseWhenInSystemTray);
            AppendSettingRow(sb, "Pause On External Audio", settings.PauseOnExternalAudio);
            AppendSettingRow(sb, "Keep Paused After External Audio", settings.KeepPausedAfterExternalAudio);
            AppendSettingRow(sb, "Pause On Idle", settings.PauseOnIdle);
            AppendSettingRow(sb, "Idle Timeout Minutes", settings.IdleTimeoutMinutes);
            AppendSettingRow(sb, "Pause On Trailer", settings.PauseOnTrailer);
            sb.AppendLine();

            // --- Live Effects ---
            sb.AppendLine("## Live Effects");
            AppendSettingRow(sb, "Live Effects Enabled", settings.LiveEffectsEnabled);
            AppendSettingRow(sb, "Show Spectrum Visualizer", settings.ShowSpectrumVisualizer);
            AppendSettingRow(sb, "Selected Reverb Preset", settings.SelectedReverbPreset);
            sb.AppendLine();

            // --- Integrations (v1.5.0) ---
            sb.AppendLine("## Integrations (v1.5.0)");
            // These are placeholders for Features 5 & 6; the property names may not exist yet
            // when this code runs against the v1.4.6 settings class. Use reflection so the
            // section degrades gracefully if a field isn't present.
            AppendOptionalSettingRow(sb, settings, "EnableDiscordRichPresence", "Discord Rich Presence");
            AppendOptionalSettingRow(sb, settings, "EnableLastfmScrobbling", "Last.fm Scrobbling");
            AppendOptionalSettingRow(sb, settings, "LastfmUsername", "Last.fm Username");
            sb.AppendLine();

            // --- Tool Paths ---
            sb.AppendLine("## Tool Paths (this machine)");
            sb.AppendLine($"- yt-dlp: `{settings.YtDlpPath ?? "(not set)"}` {ValidatePath(settings.YtDlpPath)}");
            sb.AppendLine($"- FFmpeg: `{settings.FFmpegPath ?? "(not set)"}` {ValidatePath(settings.FFmpegPath)}");
            if (!string.IsNullOrWhiteSpace(settings.CustomCookiesFilePath))
            {
                sb.AppendLine($"- Cookies file: `{settings.CustomCookiesFilePath}` {ValidatePath(settings.CustomCookiesFilePath)}");
            }
            sb.AppendLine();

            // --- Diff from defaults ---
            sb.AppendLine("## Diff from defaults");
            sb.AppendLine();
            var diff = ComputeDiffFromDefaults(settings);
            if (diff.Count == 0)
            {
                sb.AppendLine("_All settings at factory defaults._");
            }
            else
            {
                sb.AppendLine($"_{diff.Count} settings differ from factory defaults:_");
                sb.AppendLine();
                sb.AppendLine("| Setting | Default | Current |");
                sb.AppendLine("|---|---|---|");
                foreach (var row in diff.OrderBy(r => r.Name))
                {
                    sb.AppendLine($"| {row.Name} | `{row.DefaultValue}` | `{row.CurrentValue}` |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("_Generated by UniPlaySong. This is a one-way snapshot — not re-importable. " +
                          "For re-importable backups, use Export Settings (JSON) on the Backup tab._");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return filePath;
        }

        // === Helpers ===

        private static void AppendSettingRow(StringBuilder sb, string label, object value)
        {
            var rendered = RenderValue(value);
            sb.AppendLine($"- {label}: `{rendered}`");
        }

        // Reflection-based row appender for settings that may or may not exist yet
        // (forward-compat with Features 5/6 properties added later in the v1.5.0 cycle).
        private static void AppendOptionalSettingRow(StringBuilder sb, UniPlaySongSettings settings, string propertyName, string displayLabel)
        {
            var prop = typeof(UniPlaySongSettings).GetProperty(propertyName);
            if (prop == null) return; // Property doesn't exist yet (or was removed); skip silently
            var value = prop.GetValue(settings);
            if (SensitiveFields.Contains(propertyName))
            {
                value = "*****";
            }
            sb.AppendLine($"- {displayLabel}: `{RenderValue(value)}`");
        }

        private static string RenderValue(object value)
        {
            if (value == null) return "(null)";
            if (value is bool b) return b ? "true" : "false";
            if (value is string s) return string.IsNullOrEmpty(s) ? "(empty)" : s;
            if (value is IEnumerable enumerable && !(value is string))
            {
                var items = enumerable.Cast<object>().ToList();
                if (items.Count == 0) return "(empty list)";
                var preview = string.Join(", ", items.Take(3).Select(x => x?.ToString() ?? "null"));
                return items.Count > 3 ? $"{items.Count} items ({preview}, ...)" : $"{items.Count} items ({preview})";
            }
            return value.ToString();
        }

        private static string ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "(not set)";
            return File.Exists(path) ? "✓ Found" : "✗ Not Found";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private static string GetUpsVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        // Walks the settings type via reflection, comparing each property to its
        // factory-default value. Returns rows where current != default. Used by
        // the "Diff from defaults" section of the Markdown snapshot.
        private struct DiffRow
        {
            public string Name;
            public string DefaultValue;
            public string CurrentValue;
        }

        private static List<DiffRow> ComputeDiffFromDefaults(UniPlaySongSettings current)
        {
            var defaults = new UniPlaySongSettings();
            var diffs = new List<DiffRow>();

            foreach (var prop in typeof(UniPlaySongSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // Skip computed/read-only properties (no setter) — they're derived, not "configured"
                if (!prop.CanWrite) continue;

                // Skip machine-specific fields (they're in the Tool Paths section, not interesting in the diff)
                if (MachineSpecificFields.Contains(prop.Name)) continue;

                // Skip sensitive fields (we redact them above; don't echo them in the diff table)
                if (SensitiveFields.Contains(prop.Name)) continue;

                var defaultValue = prop.GetValue(defaults);
                var currentValue = prop.GetValue(current);

                if (!ValuesEqual(defaultValue, currentValue))
                {
                    diffs.Add(new DiffRow
                    {
                        Name = prop.Name,
                        DefaultValue = RenderValue(defaultValue),
                        CurrentValue = RenderValue(currentValue)
                    });
                }
            }

            return diffs;
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            // Collections: compare counts + elements
            if (a is IEnumerable enumA && b is IEnumerable enumB && !(a is string))
            {
                var listA = enumA.Cast<object>().ToList();
                var listB = enumB.Cast<object>().ToList();
                if (listA.Count != listB.Count) return false;
                return listA.SequenceEqual(listB);
            }

            return a.Equals(b);
        }
    }
}
