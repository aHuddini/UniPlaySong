using System.Collections.Generic;
using Playnite.SDK.Models;

namespace UniPlaySong.Models
{
    // Represents music associated with a game
    public class GameMusic
    {
        public Game Game { get; set; }

        public string MusicDirectory { get; set; }

        public List<Song> AvailableSongs { get; set; } = new List<Song>();

        // Primary song that plays on first selection (console-like preview)
        public Song PrimarySong { get; set; }
    }
}

