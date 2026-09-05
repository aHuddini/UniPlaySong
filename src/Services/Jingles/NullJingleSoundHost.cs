namespace UniPlaySong.Services.Jingles
{
    // The host used until the out-of-process one exists, and whenever the feature is off.
    //
    // Always declines, so every achievement sound takes the in-process path it takes today. This
    // is what makes the seam provably inert: with this host wired, UniPlaySong's behaviour is
    // identical to a build with no seam at all, and the tests assert exactly that.
    public sealed class NullJingleSoundHost : IJingleSoundHost
    {
        public static readonly NullJingleSoundHost Instance = new NullJingleSoundHost();

        public int ProcessId => 0;

        public bool TryPlay(string filePath, double volume) => false;

        public void Start() { }

        public void Stop() { }
    }
}
