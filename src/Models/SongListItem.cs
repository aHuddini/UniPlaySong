using System;
using System.ComponentModel;

namespace UniPlaySong.Models
{
    // Display model for a song in the Music Dashboard song list
    public class SongListItem : INotifyPropertyChanged
    {
        public int TrackNumber { get; set; }
        public string FilePath { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string GameName { get; set; }
        public TimeSpan Duration { get; set; }

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

        public string DurationText => DeskMediaControl.SongTitleCleaner.FormatDuration(Duration);

        public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
            ? System.IO.Path.GetFileNameWithoutExtension(FilePath)
            : Title;

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
