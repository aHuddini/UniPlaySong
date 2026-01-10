namespace UniPlaySong.Models
{
    /// <summary>
    /// Represents available music download sources
    /// </summary>
    public enum Source
    {
        /// <summary>
        /// All sources
        /// </summary>
        All,

        /// <summary>
        /// KHInsider - Video game music archive
        /// </summary>
        KHInsider,

        /// <summary>
        /// Zophar - Video game music archive with emulated formats
        /// </summary>
        Zophar,

        /// <summary>
        /// YouTube - Video platform (last resort)
        /// </summary>
        YouTube
    }
}

