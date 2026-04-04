using Playnite;

namespace UniPlaySong.Models;

class PlaybackState
{
    public Game? CurrentGame { get; set; }
    public string? CurrentSongPath { get; set; }
    public string[]? CurrentGameSongs { get; set; }
    public int CurrentSongIndex { get; set; }
    public bool IsPlayingDefaultMusic { get; set; }
    public bool IsRadioMode { get; set; }

    public void Clear()
    {
        CurrentGame = null;
        CurrentSongPath = null;
        CurrentGameSongs = null;
        CurrentSongIndex = 0;
        IsPlayingDefaultMusic = false;
        IsRadioMode = false;
    }
}
