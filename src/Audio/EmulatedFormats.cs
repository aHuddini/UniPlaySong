namespace UniPlaySong.Audio
{
    // Formats that are emulated rather than decoded: chiptune through gme.dll, PlayStation PSF
    // through psf.dll. SDL2 has no decoder for them, so they always play on the NAudio player, and
    // their readers cannot seek, so a song position is never worth remembering.
    public static class EmulatedFormats
    {
        public static bool Contains(string extension)
        {
            return GmeNative.IsGmeExtension(extension) || PsfFile.IsPsfExtension(extension);
        }
    }
}
