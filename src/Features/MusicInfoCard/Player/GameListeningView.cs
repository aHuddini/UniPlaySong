using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Features.MusicInfoCard.Player
{
    // The Details page's Listening tile: one game's totals from the listening history, and its most-played songs - the
    // tracks recorded inside its own music folder
    internal sealed class GameListeningView
    {
        public bool HasHistory { get; private set; }
        public TimeSpan Listened { get; private set; }
        public int Plays { get; private set; }
        public int? DaysSinceLastPlayed { get; private set; }
        public IReadOnlyList<(string Title, int Plays)> MostPlayed { get; private set; } = new (string, int)[0];

        // zone: whose calendar "days" are counted in; the user's own (Local) unless given
        public static GameListeningView Build(ListeningSnapshot snapshot, string gameId, string gameMusicFolder, DateTime nowUtc,
            TimeZoneInfo zone = null)
        {
            zone = zone ?? TimeZoneInfo.Local;
            var view = new GameListeningView();
            var game = ListeningInsights.ForGame(snapshot, gameId);
            if (game == null) return view;

            view.HasHistory = true;
            view.Listened = TimeSpan.FromSeconds(game.TotalSeconds);
            view.Plays = game.PlayCount;
            if (game.LastPlayedUtc != default(DateTime))
                view.DaysSinceLastPlayed = Math.Max(0, (int)(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date
                    - TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(game.LastPlayedUtc, DateTimeKind.Utc), zone).Date).TotalDays);

            if (!string.IsNullOrEmpty(gameMusicFolder) && snapshot.Tracks != null)
            {
                var folder = gameMusicFolder.TrimEnd('\\', '/') + "\\";
                view.MostPlayed = snapshot.Tracks
                    .Where(t => t.PlayCount > 0 && t.Path != null && t.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(t => t.PlayCount)
                    .Take(3)
                    .Select(t => (string.IsNullOrEmpty(t.Title) ? Path.GetFileNameWithoutExtension(t.Path) : t.Title, t.PlayCount))
                    .ToList();
            }
            return view;
        }
    }
}
