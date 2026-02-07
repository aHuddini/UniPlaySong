using System.Collections.Generic;
using Playnite.SDK.Models;

namespace UniPlaySong.Models
{
    /// <summary>
    /// Represents music associated with a game
    /// </summary>
    public class GameMusic
    {
        public Game Game { get; set; }

        public string MusicDirectory { get; set; }

        public List<Song> AvailableSongs { get; set; } = new List<Song>();

        /// <summary>
        /// Primary song that plays on first selection (console-like preview)
        /// </summary>
        public Song PrimarySong { get; set; }
    }
}

