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
    // Design parity with the global-reset path
    // (JsonConvert.SerializeObject + PopulateObject round-trip).
    public static class SettingsBackupService
    {
        // Settings excluded from JSON export entirely — these are machine-specific (tool paths, library-specific
        // GUIDs, OAuth state) and shouldn't travel between machines. On import, the importer's existing values for
        // these fields are preserved (the import is a "merge in non-machine-specific values").
        private static readonly string[] MachineSpecificFields = new[]
        {
            // Tool and file paths on this PC
            nameof(UniPlaySongSettings.YtDlpPath),
            nameof(UniPlaySongSettings.FFmpegPath),
            nameof(UniPlaySongSettings.SpotifyExePath),
            nameof(UniPlaySongSettings.DefaultMusicPath),
            nameof(UniPlaySongSettings.DefaultMusicFolderPath),
            nameof(UniPlaySongSettings.BackupCustomMusicPath),
            nameof(UniPlaySongSettings.RadioCustomFolderPath),
            nameof(UniPlaySongSettings.CustomCookiesFilePath),
            nameof(UniPlaySongSettings.CustomHintsDatabasePath),
            nameof(UniPlaySongSettings.AchievementSoundPath),
            nameof(UniPlaySongSettings.CommonAchievementSoundPath),
            nameof(UniPlaySongSettings.UncommonAchievementSoundPath),
            nameof(UniPlaySongSettings.RareAchievementSoundPath),
            nameof(UniPlaySongSettings.UltraRareAchievementSoundPath),
            nameof(UniPlaySongSettings.HiddenAchievementSoundPath),
            nameof(UniPlaySongSettings.CapstoneAchievementSoundPath),
            nameof(UniPlaySongSettings.CelebrationSoundPath),
            nameof(UniPlaySongSettings.AbandonedSoundPath),
            nameof(UniPlaySongSettings.ControlUpDetectSoundPath),
            // IDs from this Playnite library: another library's games, statuses, genres etc. have different IDs
            nameof(UniPlaySongSettings.CustomRotationGameIds),
            nameof(UniPlaySongSettings.NostalgiaStatusIds),
            nameof(UniPlaySongSettings.DefaultMusicStatusPoolIds),
            nameof(UniPlaySongSettings.GamePropFilterGenreIds),
            nameof(UniPlaySongSettings.GamePropFilterPlatformIds),
            nameof(UniPlaySongSettings.GamePropFilterSourceIds),
        };

        // One-time migrations keyed by their flag. An export made before a migration existed lacks the flag, so on
        // import the migration runs again on the imported values (e.g. a 1.9.3 "Play music in: Fullscreen only"
        // export also sets default music to Fullscreen only, as a fresh 1.9.4 install of it would).
        private static readonly (string Flag, Action<UniPlaySongSettings> Rerun)[] ImportMigrations =
        {
            (nameof(UniPlaySongSettings.ExternalAudioResumeDelayMigrated), s =>
            {
                s.ExternalAudioResumeDelayMigrated = false;
                SettingsService.MigrateExternalAudioResumeDelay(s);
            }),
            (nameof(UniPlaySongSettings.DefaultMusicStateMigrated), s =>
            {
                s.DefaultMusicStateMigrated = false;
                SettingsService.MigrateDefaultMusicState(s);
            }),
        };

        // Settings whose value should be redacted in Markdown snapshots (shown as "*****"). The field name itself is
        // still listed so support requests are informative ("yes, Last.fm is configured") without leaking secrets.
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
        // Reads a .json file, deserializes into a fresh settings instance, then preserves the current settings'
        // machine-specific paths. Returns the merged settings object ready to push through
        // SettingsService.UpdateSettings(). Throws on missing file, malformed JSON, or schema-incompatible content.
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

            foreach (var (flag, rerun) in ImportMigrations)
            {
                if (!jObj.ContainsKey(flag)) rerun(merged);
            }

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
        // Writes a human-readable .md file describing the user's UPS configuration. Intended for support requests,
        // personal notes, or "audit my UPS setup" use cases. Includes derived state (game counts, music storage) that
        // JSON intentionally excludes. Sensitive values redacted as "*****".
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
            sb.AppendLine($"- Music folder: `{SanitizePath(musicFolderPath)}`");
            sb.AppendLine();

            // --- Tool Paths ---
            sb.AppendLine("## Tool Paths (this machine)");
            sb.AppendLine($"- yt-dlp: `{SanitizePath(settings.YtDlpPath)}` {ValidatePath(settings.YtDlpPath)}");
            sb.AppendLine($"- FFmpeg: `{SanitizePath(settings.FFmpegPath)}` {ValidatePath(settings.FFmpegPath)}");
            if (!string.IsNullOrWhiteSpace(settings.CustomCookiesFilePath))
            {
                sb.AppendLine($"- Cookies file: `{SanitizePath(settings.CustomCookiesFilePath)}` {ValidatePath(settings.CustomCookiesFilePath)}");
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

            // --- Every setting, by settings group ---
            // Generated from SettingsGroups.Map, which SettingsResetCoverageTests keeps complete, so a new setting
            // appears here without this file being edited. (The hand-written sections this replaces had stopped at 1.5.)
            sb.AppendLine("## All settings");
            sb.AppendLine();
            foreach (var group in RailOrder)
            {
                if (!SettingsGroups.Map.TryGetValue(group, out var names) || names.Length == 0) continue;
                sb.AppendLine($"### {group}");
                foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
                {
                    var prop = typeof(UniPlaySongSettings).GetProperty(name);
                    if (prop == null) continue;
                    object value = prop.GetValue(settings);
                    if (SensitiveFields.Contains(name)) value = "*****";
                    else if (value is string path && MachineSpecificFields.Contains(name)) value = SanitizePath(path);
                    AppendSettingRow(sb, Humanize(name), value);
                }
                sb.AppendLine();
            }

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

        // The settings window's left rail, top to bottom.
        private static readonly string[] RailOrder =
        {
            SettingsGroups.About, SettingsGroups.QuickStart, SettingsGroups.Setup, SettingsGroups.General,
            SettingsGroups.Playback, SettingsGroups.Pauses, SettingsGroups.LiveEffects, SettingsGroups.Gamification,
            SettingsGroups.Library, SettingsGroups.UI, SettingsGroups.Advanced,
        };

        // "PauseOnFocusLoss" -> "Pause On Focus Loss", "FFmpegPath" -> "FFmpeg Path", "PS5ThemeCompatMode" -> "PS5 Theme Compat Mode".
        internal static string Humanize(string name) =>
            System.Text.RegularExpressions.Regex.Replace(name, @"(?<=[a-z0-9])(?=[A-Z])", " ");

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

        // Sanitize a path for human-readable Markdown export: replace user-specific prefixes with environment variable
        // placeholders so users can safely share the snapshot without leaking their Windows username or custom install
        // location. The placeholders are conventional (%AppData%, %UserProfile%) and recognizable to anyone reading a
        // support request.
        //
        // Replacement order matters — longest prefix first so we match the most
        // specific known path before falling back to a shorter prefix.
        private static string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "(not set)";

            try
            {
                var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                // Most specific replacements first (longer paths) to avoid partial-match
                // surprises (e.g. UserProfile is a prefix of AppData; if we replaced
                // UserProfile first we'd never get the more-specific %AppData% token).
                var replacements = new[]
                {
                    (roamingAppData, "%AppData%"),
                    (localAppData, "%LocalAppData%"),
                    (programFilesX86, "%ProgramFiles(x86)%"),
                    (programFiles, "%ProgramFiles%"),
                    (userProfile, "%UserProfile%"),
                };

                foreach (var (prefix, token) in replacements)
                {
                    if (string.IsNullOrEmpty(prefix)) continue;
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return token + path.Substring(prefix.Length);
                    }
                }

                // Path doesn't match a known user-specific prefix (e.g. it's on D:\Tools\)
                // — return as-is. Drives like D:\Games\ don't leak the username.
                return path;
            }
            catch
            {
                // If anything goes wrong (rare — only Environment.GetFolderPath can throw),
                // fall back to returning the path unchanged. Better to show a slightly
                // un-sanitized path than crash the Markdown export.
                return path;
            }
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
