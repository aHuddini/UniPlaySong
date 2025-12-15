using System.Collections.Generic;
using Playnite.SDK.Models;

namespace UniPlaySong.Models
{
    /// <summary>
    /// Represents music associated with a game
    /// </summary>
    public class GameMusic
    {
        /// <summary>
        /// The game this music is associated with
        /// </summary>
        public Game Game { get; set; }

        /// <summary>
        /// Directory path where game music is stored
        /// </summary>
        public string MusicDirectory { get; set; }

        /// <summary>
        /// Available songs for this game
        /// </summary>
        public List<Song> AvailableSongs { get; set; } = new List<Song>();

        /// <summary>
        /// Primary song that plays on first selection (console-like preview)
        /// </summary>
        public Song PrimarySong { get; set; }
    }
}

