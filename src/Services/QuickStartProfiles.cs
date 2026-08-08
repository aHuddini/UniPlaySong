using System;
using System.Collections.Generic;
using System.Linq;

namespace UniPlaySong.Services
{
    // Which UPS mode a profile is written for. PlayOnlyOnGameSelect is Fullscreen-only by design,
    // so the two columns genuinely differ rather than being a cosmetic split.
    public enum QuickStartMode
    {
        Fullscreen,
        Desktop
    }

    // Where a Jukebox profile takes its continuous mix from.
    public enum JukeboxSource
    {
        Library,
        Spotify
    }

    // One tile on the Quick Start page: a distinct way of listening, not a bundle of checkboxes.
    // Variations that differ by a single setting are the page's own checkboxes (installed-only,
    // keep-playing-during-games), which is what keeps this to three tiles per mode.
    public class QuickStartProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public QuickStartMode Mode { get; set; }
        public string Summary { get; set; }

        // The settings this profile OWNS. Applying writes only these; everything else the user
        // configured is left alone. Keyed by property name so the applier can snapshot for undo
        // and detect drift without a hand-written list per profile.
        public Dictionary<string, object> Values { get; set; } = new Dictionary<string, object>();
    }

    // The catalogue. Pure data plus the two page-level modifiers — no settings object, no Playnite
    // API, no UI, so the whole thing is testable without a running plugin.
    public static class QuickStartProfiles
    {
        public const string InstalledOnlyKey = nameof(UniPlaySongSettings.MusicOnlyForInstalledGames);
        public const string PlayThroughGamesKey = nameof(UniPlaySongSettings.RadioPlaysThroughGames);

        // Ids are persisted in ActiveQuickStartProfile, so they must stay stable even if a display
        // name changes.
        public const string HoverPreviewFullscreen = "fs-hover";
        public const string SelectToPlayFullscreen = "fs-select";
        public const string LibraryBackgroundFullscreen = "fs-libbg";
        public const string JukeboxFullscreen = "fs-jukebox";
        public const string HoverPreviewDesktop = "dt-hover";
        // Id kept as dt-ambient though the tile is now "Background Mode (Default Music)" — ids are
        // persisted, so renaming one would orphan ActiveQuickStartProfile on existing installs.
        public const string AmbientDesktop = "dt-ambient";
        public const string JukeboxDesktop = "dt-jukebox";

        public static IReadOnlyList<QuickStartProfile> All => _all;

        public static IEnumerable<QuickStartProfile> For(QuickStartMode mode) =>
            _all.Where(p => p.Mode == mode);

        public static QuickStartProfile ById(string id) =>
            string.IsNullOrEmpty(id) ? null : _all.FirstOrDefault(p => p.Id == id);

        // Shared by every non-radio profile: radio off (it would replace per-game music wholesale),
        // and a default-music bed that does not restart every time you land on a game with no music.
        // Stating the fallback explicitly is deliberate — leaving EnableDefaultMusic unowned would
        // mean the same profile behaves differently for two users.
        private static Dictionary<string, object> PerGameBase() => new Dictionary<string, object>
        {
            // Owned explicitly so a profile always leaves the plugin switched on. A brief earlier
            // build had Ambient Background set this false; anyone who applied it gets repaired by
            // applying any profile now, rather than being left with UPS reading as disabled.
            { nameof(UniPlaySongSettings.EnableMusic), true },
            { nameof(UniPlaySongSettings.RadioModeEnabled), false },
            { nameof(UniPlaySongSettings.EnableDefaultMusic), true },
            { nameof(UniPlaySongSettings.DefaultMusicContinueSameSong), true },
            { nameof(UniPlaySongSettings.StopAfterSongEnds), false },
            { nameof(UniPlaySongSettings.RandomizeOnEverySelect), true },
        };

        private static readonly List<QuickStartProfile> _all = new List<QuickStartProfile>
        {
            new QuickStartProfile
            {
                Id = HoverPreviewFullscreen,
                Name = "Hover Preview (PS3 style)",
                Mode = QuickStartMode.Fullscreen,
                Summary = "Music follows the highlight as you browse. Games with no music of their own fall back to your default music.",
                Values = Merge(PerGameBase(), new Dictionary<string, object>
                {
                    { nameof(UniPlaySongSettings.PlayOnlyOnGameSelect), false },
                    // Short fades: browsing changes tracks often, so long fades smear together.
                    { nameof(UniPlaySongSettings.FadeInDuration), 0.3 },
                    { nameof(UniPlaySongSettings.FadeOutDuration), 0.2 },
                })
            },
            new QuickStartProfile
            {
                Id = SelectToPlayFullscreen,
                Name = "Select to Play",
                Mode = QuickStartMode.Fullscreen,
                Summary = "Browsing stays on your default music. A game's own music starts when you open it.",
                Values = Merge(PerGameBase(), new Dictionary<string, object>
                {
                    { nameof(UniPlaySongSettings.PlayOnlyOnGameSelect), true },
                    // Longer than Hover: a track change here is a deliberate act, not a side effect
                    // of moving the highlight, so it can afford to breathe.
                    { nameof(UniPlaySongSettings.FadeInDuration), Common.Constants.DefaultFadeInDuration },
                    { nameof(UniPlaySongSettings.FadeOutDuration), Common.Constants.DefaultFadeOutDuration },
                })
            },
            new QuickStartProfile
            {
                Id = LibraryBackgroundFullscreen,
                Name = "Library Background (Default Music), Game Music In Details",
                Mode = QuickStartMode.Fullscreen,
                Summary = "A bundled ambient track plays while you browse the library. Opening a game switches to its own music, and backing out returns to the background track.",
                // Select-to-play, but pinned to the bundled preset as the browsing bed rather than
                // leaving it to whatever default source the user happens to have. That pairing is
                // the point of the tile: Select to Play alone does not say what you hear while
                // browsing, which is most of the time.
                //
                // Fullscreen only: PlayOnlyOnGameSelect is gated on GetActiveFullscreenView(), which
                // returns null in Desktop, so this trigger cannot fire there at all.
                Values = Merge(PerGameBase(), new Dictionary<string, object>
                {
                    { nameof(UniPlaySongSettings.PlayOnlyOnGameSelect), true },
                    { nameof(UniPlaySongSettings.DefaultMusicSourceOption), DefaultMusicSource.BundledPreset },
                    { nameof(UniPlaySongSettings.RandomizeDefaultMusicOnEnd), true },
                    { nameof(UniPlaySongSettings.FadeInDuration), Common.Constants.DefaultFadeInDuration },
                    { nameof(UniPlaySongSettings.FadeOutDuration), Common.Constants.DefaultFadeOutDuration },
                })
            },
            new QuickStartProfile
            {
                Id = JukeboxFullscreen,
                Name = "Radio Mode (Random Game Music)",
                Mode = QuickStartMode.Fullscreen,
                Summary = "One continuous mix instead of per-game music. Pick your library or Spotify as the source. Default music stays on in case the mix has nothing to play.",
                Values = JukeboxValues()
            },

            new QuickStartProfile
            {
                Id = HoverPreviewDesktop,
                Name = "Hover Preview (PS3 style)",
                Mode = QuickStartMode.Desktop,
                Summary = "Music follows your selection. Games with no music of their own fall back to your default music.",
                Values = Merge(PerGameBase(), new Dictionary<string, object>
                {
                    // Not owned on Desktop — PlayOnlyOnGameSelect is Fullscreen-only, so setting it
                    // here would write a value the mode cannot act on.
                    { nameof(UniPlaySongSettings.FadeInDuration), 0.3 },
                    { nameof(UniPlaySongSettings.FadeOutDuration), 0.2 },
                })
            },
            new QuickStartProfile
            {
                Id = AmbientDesktop,
                Name = "Background Mode (Default Music)",
                Mode = QuickStartMode.Desktop,
                Summary = "One bundled ambient track plays the whole time. Game music is off, so nothing interrupts it.",
                // Game music genuinely off. EnableMusic=false + EnableDefaultMusic=true is the
                // engine's supported "default music only" path — PlayGameMusic clears the game's
                // songs and falls through to the default source.
                //
                // Known cost, accepted deliberately: the General tab's "Enable Music" checkbox then
                // reads as unchecked, which looks like the plugin is off rather than deliberately in
                // background mode. The alternative was a new persisted DefaultMusicOnly flag; using
                // the existing toggle was chosen instead. Every other tile owns EnableMusic=true, so
                // switching away from this one restores game music.
                //
                // ForceDefaultMusicOverride is NOT the tool for this: it is [JsonIgnore] runtime-only
                // state driven by theme XAML, reset on every load, and ignored outside Fullscreen.
                Values = Merge(PerGameBase(), new Dictionary<string, object>
                {
                    { nameof(UniPlaySongSettings.EnableMusic), false },
                    { nameof(UniPlaySongSettings.DefaultMusicSourceOption), DefaultMusicSource.BundledPreset },
                    { nameof(UniPlaySongSettings.RandomizeDefaultMusicOnEnd), true },
                    // Slow fades suit a background bed; the point is not to be noticed.
                    { nameof(UniPlaySongSettings.FadeInDuration), 1.0 },
                    { nameof(UniPlaySongSettings.FadeOutDuration), 0.8 },
                })
            },
            new QuickStartProfile
            {
                Id = JukeboxDesktop,
                Name = "Radio Mode (Random Game Music)",
                Mode = QuickStartMode.Desktop,
                Summary = "One continuous mix instead of per-game music. Pick your library or Spotify as the source. Default music stays on in case the mix has nothing to play.",
                Values = JukeboxValues()
            },
        };

        // Radio is the continuous bed, but default music stays ON as the safety net.
        //
        // Turning it off looked right — two beds layered would be wrong — but StartRadioPlayback
        // BAILS on an empty pool ("RadioMode: pool empty for source X") and simply returns. A user
        // with nothing downloaded yet, or FullLibrary against an empty library, or CustomFolder with
        // no folder chosen, then gets silence and no way to tell why. Default music is what covers
        // that, and it does not double up: the radio owns playback whenever its pool has anything in
        // it, so the fallback only surfaces when the radio genuinely cannot play.
        private static Dictionary<string, object> JukeboxValues() => new Dictionary<string, object>
        {
            // Jukebox does not build on PerGameBase, so it owns EnableMusic for the same reason.
            { nameof(UniPlaySongSettings.EnableMusic), true },
            { nameof(UniPlaySongSettings.RadioModeEnabled), true },
            { nameof(UniPlaySongSettings.EnableDefaultMusic), true },
            { nameof(UniPlaySongSettings.PlayOnlyOnGameSelect), false },
            { nameof(UniPlaySongSettings.RadioMusicSource), RadioMusicSource.FullLibrary },
        };

        // Sources that need something the user has to supply first. Applying a profile must not
        // leave default music pointed at a source with nothing behind it — that is the same silent
        // failure as the empty radio pool, one layer down.
        public static bool DefaultSourceIsUsable(UniPlaySongSettings s)
        {
            if (s == null) return false;
            switch (s.DefaultMusicSourceOption)
            {
                case DefaultMusicSource.CustomFile:
                    return !string.IsNullOrWhiteSpace(s.DefaultMusicPath);
                case DefaultMusicSource.CustomFolder:
                    return !string.IsNullOrWhiteSpace(s.DefaultMusicFolderPath);
                case DefaultMusicSource.CustomRotation:
                    return s.CustomRotationGameIds != null && s.CustomRotationGameIds.Count > 0;
                case DefaultMusicSource.CompletionStatusPool:
                    return s.DefaultMusicStatusPoolIds != null && s.DefaultMusicStatusPoolIds.Count > 0;
                default:
                    // BundledPreset, RandomGame, ActiveThemeMusic, DeferToTrailerAudio and Spotify
                    // all work without the user configuring a path or list first.
                    return true;
            }
        }

        // BundledPreset ships with the plugin, so it is the one source guaranteed to produce sound
        // on a fresh install.
        public static Dictionary<string, object> BundledPresetFallback() => new Dictionary<string, object>
        {
            { nameof(UniPlaySongSettings.DefaultMusicSourceOption), DefaultMusicSource.BundledPreset },
        };

        // Applies the Jukebox tile's source choice. Spotify radio is RadioModeEnabled +
        // RadioMusicSource == Spotify; SpotifyRadioMode itself is [JsonIgnore] and read-only, so it
        // must never be written — it follows from these two.
        public static Dictionary<string, object> WithJukeboxSource(QuickStartProfile profile, JukeboxSource source)
        {
            var values = new Dictionary<string, object>(profile.Values);
            values[nameof(UniPlaySongSettings.RadioMusicSource)] =
                source == JukeboxSource.Spotify ? RadioMusicSource.Spotify : RadioMusicSource.FullLibrary;
            return values;
        }

        public static bool IsJukebox(QuickStartProfile profile) =>
            profile != null && (profile.Id == JukeboxFullscreen || profile.Id == JukeboxDesktop);

        // "Add reverb" — a page-level checkbox rather than more tiles, since it composes with every
        // profile in both modes. Turns on the live-effects chain and picks HuddiniRehearsal, the
        // preset described as "wide stereo, rich reverb, live rehearsal room feel" — the closest
        // thing UPS ships to a concert-hall space.
        //
        // Consequence worth surfacing in the UI rather than applying silently: live effects force
        // the NAudio backend, so this is not a purely cosmetic toggle.
        public static Dictionary<string, object> ReverbValues(bool enabled) =>
            enabled
                ? new Dictionary<string, object>
                {
                    { nameof(UniPlaySongSettings.LiveEffectsEnabled), true },
                    { nameof(UniPlaySongSettings.SelectedStylePreset), StylePreset.HuddiniRehearsal },
                }
                : new Dictionary<string, object>
                {
                    { nameof(UniPlaySongSettings.LiveEffectsEnabled), false },
                };

        private static Dictionary<string, object> Merge(Dictionary<string, object> baseValues, Dictionary<string, object> overrides)
        {
            foreach (var kv in overrides)
                baseValues[kv.Key] = kv.Value;
            return baseValues;
        }
    }
}
