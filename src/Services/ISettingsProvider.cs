using System;

namespace UniPlaySong.Services
{
    // Minimal seam over SettingsService for controls/view-models that need the LIVE settings
    // object across saves. A settings save replaces the whole settings object (UpdateSettings),
    // so consumers must read Current on demand and re-wire on SettingsChanged rather than
    // capture the object once. Depending on this interface (not the concrete SettingsService)
    // keeps those consumers unit-testable without the Playnite API.
    public interface ISettingsProvider
    {
        // The current settings object. May change identity after any save/load.
        UniPlaySongSettings Current { get; }

        // Raised when the settings object is replaced (OldSettings -> NewSettings).
        event EventHandler<SettingsChangedEventArgs> SettingsChanged;
    }
}
