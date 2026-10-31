namespace UniPlaySong.Models
{
    // Selects which tab is active in the NSF Track Manager dialog.
    // SplitTracks = master-NSF splitter.
    // EditLoops   = per-track loop-seconds editor for previously-split mini-NSFs.
    public enum NsfManagerTab
    {
        SplitTracks,
        EditLoops
    }
}
