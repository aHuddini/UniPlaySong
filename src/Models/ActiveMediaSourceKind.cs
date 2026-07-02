namespace UniPlaySong.Models
{
    // Which source is currently the audible "active media" that the unified
    // media controller targets. Ups = UniPlaySong's own internal player;
    // Spotify = the external Spotify SMTC session. None = nothing active.
    public enum ActiveMediaSourceKind
    {
        None,
        Ups,
        Spotify
    }
}
