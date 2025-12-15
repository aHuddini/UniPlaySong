using System;
using System.IO;
using Playnite.SDK.Data;
using UniPlaySong.Services;

namespace UniPlaySong.Common
{
    /// <summary>
    /// Manages primary song selection for games
    /// Primary songs play once on first selection (console-like preview experience)
    /// </summary>
    public static class PrimarySongManager
    {
        private const string PrimarySongFileName = ".primarysong.json";

        /// <summary>
        /// Gets the path to the primary song metadata file
        /// </summary>
        private static string GetPrimarySongFilePath(string musicDirectory)
        {
            return Path.Combine(musicDirectory, PrimarySongFileName);
        }

        /// <summary>
        /// Gets the primary song file path for a music directory
        /// </summary>
        public static string GetPrimarySong(string musicDirectory, ErrorHandlerService errorHandler = null)
        {
            if (string.IsNullOrWhiteSpace(musicDirectory) || !Directory.Exists(musicDirectory))
            {
                return null;
            }

            var metadataPath = GetPrimarySongFilePath(musicDirectory);
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            return errorHandler?.Try(
                () =>
                {
                    var json = File.ReadAllText(metadataPath);
                    var metadata = Serialization.FromJson<PrimarySongMetadata>(json);

                    if (metadata != null && !string.IsNullOrEmpty(metadata.PrimarySongFileName))
                    {
                        var primarySongPath = Path.Combine(musicDirectory, metadata.PrimarySongFileName);
                        if (File.Exists(primarySongPath))
                        {
                            return primarySongPath;
                        }
                    }
                    return null;
                },
                defaultValue: null,
                context: $"getting primary song from '{musicDirectory}'",
                showUserMessage: false
            ) ?? TryGetPrimarySongFallback(musicDirectory, metadataPath);
        }

        /// <summary>
        /// Fallback method for when ErrorHandlerService is not available
        /// </summary>
        private static string TryGetPrimarySongFallback(string musicDirectory, string metadataPath)
        {
            try
            {
                var json = File.ReadAllText(metadataPath);
                var metadata = Serialization.FromJson<PrimarySongMetadata>(json);

                if (metadata != null && !string.IsNullOrEmpty(metadata.PrimarySongFileName))
                {
                    var primarySongPath = Path.Combine(musicDirectory, metadata.PrimarySongFileName);
                    if (File.Exists(primarySongPath))
                    {
                        return primarySongPath;
                    }
                }
            }
            catch (Exception)
            {
                // If file is corrupted or unreadable, return null
            }

            return null;
        }

        /// <summary>
        /// Sets the primary song for a music directory
        /// </summary>
        public static void SetPrimarySong(string musicDirectory, string songFilePath, ErrorHandlerService errorHandler = null)
        {
            if (string.IsNullOrWhiteSpace(musicDirectory) || string.IsNullOrWhiteSpace(songFilePath))
            {
                return;
            }

            if (!Directory.Exists(musicDirectory))
            {
                Directory.CreateDirectory(musicDirectory);
            }

            var metadataPath = GetPrimarySongFilePath(musicDirectory);
            var songFileName = Path.GetFileName(songFilePath);

            var metadata = new PrimarySongMetadata
            {
                PrimarySongFileName = songFileName,
                SetDate = DateTime.Now
            };

            if (errorHandler != null)
            {
                errorHandler.Try(
                    () =>
                    {
                        var json = Serialization.ToJson(metadata);
                        File.WriteAllText(metadataPath, json);
                    },
                    context: $"setting primary song '{songFileName}' in '{musicDirectory}'",
                    showUserMessage: false
                );
            }
            else
            {
                TrySetPrimarySongFallback(metadataPath, metadata);
            }
        }

        /// <summary>
        /// Fallback method for when ErrorHandlerService is not available
        /// </summary>
        private static void TrySetPrimarySongFallback(string metadataPath, PrimarySongMetadata metadata)
        {
            try
            {
                var json = Serialization.ToJson(metadata);
                File.WriteAllText(metadataPath, json);
            }
            catch (Exception)
            {
                // Log error if needed
            }
        }

        /// <summary>
        /// Clears the primary song for a music directory
        /// </summary>
        public static void ClearPrimarySong(string musicDirectory, ErrorHandlerService errorHandler = null)
        {
            if (string.IsNullOrWhiteSpace(musicDirectory))
            {
                return;
            }

            var metadataPath = GetPrimarySongFilePath(musicDirectory);
            if (File.Exists(metadataPath))
            {
                if (errorHandler != null)
                {
                    errorHandler.Try(
                        () => File.Delete(metadataPath),
                        context: $"clearing primary song in '{musicDirectory}'",
                        showUserMessage: false
                    );
                }
                else
                {
                    TryClearPrimarySongFallback(metadataPath);
                }
            }
        }

        /// <summary>
        /// Fallback method for when ErrorHandlerService is not available
        /// </summary>
        private static void TryClearPrimarySongFallback(string metadataPath)
        {
            try
            {
                File.Delete(metadataPath);
            }
            catch (Exception)
            {
                // Log error if needed
            }
        }

        /// <summary>
        /// Checks if a song file is the primary for its directory
        /// </summary>
        public static bool IsPrimarySong(string songFilePath, ErrorHandlerService errorHandler = null)
        {
            if (string.IsNullOrWhiteSpace(songFilePath))
            {
                return false;
            }

            var musicDirectory = Path.GetDirectoryName(songFilePath);
            var primarySong = GetPrimarySong(musicDirectory, errorHandler);
            return primarySong != null &&
                   string.Equals(primarySong, songFilePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Metadata structure for storing primary song information
    /// </summary>
    internal class PrimarySongMetadata
    {
        public string PrimarySongFileName { get; set; }
        public DateTime SetDate { get; set; }
    }
}

