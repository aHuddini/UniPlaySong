namespace UniPlaySong.Services
{
    // What UPS would play for one achievement rarity, and where that file came from.
    //
    // Returned to other plugins as JSON rather than as this type, deliberately. A caller that had
    // to reference this class would need UniPlaySong.dll at compile time and would break on any
    // signature change; JSON costs them a parse and survives fields being added.
    public class AchievementSoundInfo
    {
        public string Rarity { get; set; }

        // Absolute path to the audio file, or null when nothing resolves.
        public string Path { get; set; }

        // Where it came from:
        //   "UserCustom"  - a file the user picked for this rarity
        //   "Theme"       - shipped by the active Playnite theme
        //   "StarterPack" - UPS's bundled PlayniteAchievements starter pack
        public string Source { get; set; }

        // True when the user's chosen pack had nothing for this rarity and the starter pack
        // answered instead. The caller usually does not care - but it explains why a user who
        // configured a custom sound is hearing a bundled one, which is otherwise a confusing
        // support conversation.
        public bool FellBack { get; set; }

        // The master achievement-sound switch. False means UPS would stay silent for this event
        // regardless of what Path says, so a caller mirroring UPS should stay silent too.
        public bool Enabled { get; set; }

        // Checked at the moment of the call. A user can delete a custom sound at any time.
        public bool Exists { get; set; }
    }
}
