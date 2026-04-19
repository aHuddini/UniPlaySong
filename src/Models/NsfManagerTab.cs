namespace UniPlaySong.Models
{
    // Selects which tab is active in the NSF Track Manager dialog.
    // SplitTracks = master-NSF splitter (existing v1.4.3 behavior).
    // EditLoops   = per-track loop-seconds editor for previously-split mini-NSFs.
    public enum NsfManagerTab
    {
        SplitTracks,
        EditLoops
    }
}
