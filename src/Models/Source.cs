namespace UniPlaySong.Models
{
    // Available music download sources
    public enum Source
    {
        All,
        KHInsider,                  // Video game music archive
        Zophar,                     // Video game music archive with emulated formats
        YouTube,                    // Video platform (last resort)
        SoundCloud                  // Music streaming platform (hints-only, no search)
    }
}
