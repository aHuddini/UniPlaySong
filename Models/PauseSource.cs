namespace UniPlaySong.Models
{
    /// <summary>
    /// Represents different sources that can pause music playback.
    /// Allows multiple independent systems to pause music without conflicts.
    /// </summary>
    public enum PauseSource
    {
        /// <summary>
        /// Video playback is active (game trailers, cutscenes)
        /// </summary>
        Video,

        /// <summary>
        /// User manually paused playback
        /// </summary>
        Manual,

        /// <summary>
        /// Settings are being changed (e.g., volume adjustment)
        /// </summary>
        Settings,

        /// <summary>
        /// View mode is changing (desktop to fullscreen or vice versa)
        /// </summary>
        ViewChange,

        /// <summary>
        /// Default music position is being preserved when switching to game music
        /// </summary>
        DefaultMusicPreservation
    }
}
