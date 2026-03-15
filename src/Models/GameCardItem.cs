using System;
using System.ComponentModel;
using System.Windows.Media;

namespace UniPlaySong.Models
{
    // Display model for a game card in the Music Library grid
    public class GameCardItem : INotifyPropertyChanged
    {
        public string GameId { get; set; }
        public string Name { get; set; }
        public int SongCount { get; set; }

        private TimeSpan _totalDuration;
        public TimeSpan TotalDuration
        {
            get => _totalDuration;
            set
            {
                _totalDuration = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalDuration)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtitle)));
            }
        }
        private ImageSource _coverArt;
        public ImageSource CoverArt
        {
            get => _coverArt;
            set
            {
                _coverArt = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CoverArt)));
            }
        }
        public string MusicDirectoryPath { get; set; }

        private bool _isCurrentlyPlaying;
        public bool IsCurrentlyPlaying
        {
            get => _isCurrentlyPlaying;
            set
            {
                if (_isCurrentlyPlaying != value)
                {
                    _isCurrentlyPlaying = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrentlyPlaying)));
                }
            }
        }

        public string Subtitle
        {
            get
            {
                var durationText = TotalDuration.TotalHours >= 1
                    ? $"{(int)TotalDuration.TotalHours}h {TotalDuration.Minutes}min"
                    : $"{(int)TotalDuration.TotalMinutes} min";
                return $"{SongCount} songs \u2022 {durationText}";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
