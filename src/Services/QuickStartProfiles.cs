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
        public const string HoverFullTrackFullscreen = "fs-hover-full";
        public const string SelectToPlayFullscreen = "fs-select";
        public const string JukeboxFullscreen = "fs-jukebox";
        public const string HoverPreviewDesktop = "dt-hover";
        public const string HoverFullTrackDesktop = "dt-hover-full";
        // Id kept as dt-ambient though the tile is now "Background Mode (Default Music)" — ids are
        // persisted, so renaming one would orphan ActiveQuickStartProfile on existing installs.
        public const string AmbientDesktop = "dt-ambient";
        public const string JukeboxDesktop = "dt-jukebox";
        // Offered in both columns — the only tile that is, because it is a showcase of what the
        // plugin can do rather than a way of listening tied to one mode.
        public const string HuddiniShowcaseFullscreen = "fs-showcase";
        public const string HuddiniShowcaseDesktop = "dt-showcase";

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
            // Owned so a tile cannot inherit the clip setting from a Hover tile applied earlier.
            // Hover Short Clip overrides this back to true via ClipValues.
            { nameof(UniPlaySongSettings.EnablePreviewMode), false },
            // Advance to a different track when one finishes. Hover Short Clip overrides this to
            // false so its clip loops instead — see ClipValues.
            { nameof(UniPlaySongSettings.RandomizeOnMusicEnd), true },
        };

        // Hover: music follows the highlight, with short fades because browsing changes tracks often
        // and long fades smear into each other.
        private static Dictionary<string, object> HoverBase() => Merge(PerGameBase(), new Dictionary<string, object>
        {
            { nameof(UniPlaySongSettings.PlayOnlyOnGameSelect), false },
            { nameof(UniPlaySongSettings.FadeInDuration), 0.3 },
            { nameof(UniPlaySongSettings.FadeOutDuration), 0.2 },
        });

        // Same, minus PlayOnlyOnGameSelect — it is Fullscreen-only, so writing it in Desktop would
        // store a value the mode cannot act on.
        private static Dictionary<string, object> HoverBaseDesktop() => Merge(PerGameBase(), new Dictionary<string, object>
        {
            { nameof(UniPlaySongSettings.FadeInDuration), 0.3 },
            { nameof(UniPlaySongSettings.FadeOutDuration), 0.2 },
        });

        // Clip vs full track. Owned EXPLICITLY either way rather than left alone: EnablePreviewMode
        // persists, so a tile that stayed silent about it would inherit whatever the user last had
        // and two people applying the same tile would hear different things. Being explicit is also
        // what makes the Short Clip / Full Track pair mean anything — otherwise they would differ by
        // a setting neither of them owns.
        private static Dictionary<string, object> ClipValues(bool clip) => clip
            ? new Dictionary<string, object>
            {
                { nameof(UniPlaySongSettings.EnablePreviewMode), true },
                { nameof(UniPlaySongSettings.PreviewDuration), Common.Constants.DefaultPreviewDuration },
                // The clip loops rather than advancing to another track, which is what makes this
                // read as a PS3 menu: the same snippet repeats for as long as the highlight sits on
                // a game. With RandomizeOnMusicEnd left on, each clip would end and jump to a
                // different song from the same game, so the highlight would sit still while the
                // music kept changing.
                { nameof(UniPlaySongSettings.RandomizeOnMusicEnd), false },
            }
            : new Dictionary<string, object>
            {
                { nameof(UniPlaySongSettings.EnablePreviewMode), false },
            };

        // Huddini's personal setup, offered in both modes. Unlike the other tiles this is not a
        // minimal "way of listening" — it deliberately turns on the things that show what UPS does,
        // so it owns more keys than anything else in the catalogue.
        //
        // It is the ONE profile that sets MusicVolume, which every other tile is forbidden from
        // touching. That is the point of a showcase: it is a complete configuration, and the volume
        // and boost are part of what it is demonstrating. Every value here is inside its property's
        // clamp — volume 0-100, boost 0-20, song-end fade 0.2-5.0, crossfade 1-10, fades 0.10-5.0 —
        // checked rather than assumed, because a setter that silently clamps would make the tile
        // apply something other than what it advertises.
        private static Dictionary<string, object> ShowcaseValues() => new Dictionary<string, object>
        {
            // Plays in both modes — the showcase is not mode-specific.
            { nameof(UniPlaySongSettings.MusicState), AudioState.Always },
            { nameof(UniPlaySongSettings.EnableMusic), true },

            { nameof(UniPlaySongSettings.MusicVolume), 90 },
            { nameof(UniPlaySongSettings.FullscreenVolumeBoostPercent), 3 },

            { nameof(UniPlaySongSettings.FadeInDuration), 0.35 },
            { nameof(UniPlaySongSettings.FadeOutDuration), 0.65 },
            { nameof(UniPlaySongSettings.FadeOutBeforeSongEndDuration), 3.0 },

            // True crossfade needs the NAudio backend, which Live Effects below also forces — the
            // two are consistent rather than fighting each other.
            { nameof(UniPlaySongSettings.EnableTrueCrossfade), true },
            { nameof(UniPlaySongSettings.CrossfadeDurationSeconds), 7 },

            { nameof(UniPlaySongSettings.RandomizeOnEverySelect), true },
            { nameof(UniPlaySongSettings.RandomizeOnMusicEnd), true },

            { nameof(UniPlaySongSettings.EnableDefaultMusic), true },
            { nameof(UniPlaySongSettings.DefaultMusicContinueSameSong), true },
            { nameof(UniPlaySongSettings.RandomizeDefaultMusicOnEnd), true },
            { nameof(UniPlaySongSettings.DefaultMusicSourceOption), DefaultMusicSource.BundledPreset },
            { nameof(UniPlaySongSettings.RandomizeBundledTrackOnStartup), true },

            { nameof(UniPlaySongSettings.MusicOnlyForInstalledGames), true },

            // Full tracks, not clips — the showcase is about the crossfade and the effects, which a
            // 30-second snippet would cut off before they land.
            { nameof(UniPlaySongSettings.EnablePreviewMode), false },
            { nameof(UniPlaySongSettings.StopAfterSongEnds), false },

            // Radio would replace per-game music, which is what the rest of this is demonstrating.
            { nameof(UniPlaySongSettings.RadioModeEnabled), false },

            { nameof(UniPlaySongSettings.LiveEffectsEnabled), true },
            { nameof(UniPlaySongSettings.SelectedStylePreset), StylePreset.HuddiniRehearsal },
        };

        public static bool IsShowcase(QuickStartProfile profile) =>
            profile != null && (profile.Id == HuddiniShowcaseFullscreen || profile.Id == HuddiniShowcaseDesktop);

        private static readonly List<QuickStartProfile> _all = new List<QuickStartProfile>
        {
            new QuickStartProfile
            {
                Id = HoverPreviewFullscreen,
                Name = "Hover Preview, Short Clip (PS3 style)",
                Mode = QuickStartMode.Fullscreen,
                Summary = "Music follows the highlight, looping a 30-second clip of each game's track.",
                Values = Merge(HoverBase(), ClipValues(true))
            },
            new QuickStartProfile
            {
                Id = HoverFullTrackFullscreen,
                Name = "Hover Preview, Full Track",
                Mode = QuickStartMode.Fullscreen,
                Summary = "Music follows the highlight, playing each game's track in full.",
                Values = Merge(HoverBase(), ClipValues(false))
            },
            new QuickStartProfile
            {
                Id = SelectToPlayFullscreen,
                Name = "Select to Play",
                Mode = QuickStartMode.Fullscreen,
                Summary = "Default music while you browse; a game's own music when you open it.",
                // A separate "Library Background" tile was tried and removed: it differed from this
                // one by exactly two settings — the default source and its randomize flag — which is
                // a checkbox, not a way of listening.
                //
                // Its bundled-preset pin was NOT folded in here. Doing that overwrote a CustomFolder
                // the user had deliberately chosen, which the declared-keys principle exists to
                // prevent, and a test caught it. Apply already guarantees a playable fallback: an
                // unconfigured source falls back to the bundled preset on its own, so a user with no
                // default music set still gets one, and a user who picked their own keeps it.
                Values = Merge(PerGameBase(), new Dictionary<string, object>
                {
                    { nameof(UniPlaySongSettings.PlayOnlyOnGameSelect), true },
                    { nameof(UniPlaySongSettings.RandomizeDefaultMusicOnEnd), true },
                    // Longer than Hover: a track change here is a deliberate act, not a side effect
                    // of moving the highlight, so it can afford to breathe.
                    { nameof(UniPlaySongSettings.FadeInDuration), Common.Constants.DefaultFadeInDuration },
                    { nameof(UniPlaySongSettings.FadeOutDuration), Common.Constants.DefaultFadeOutDuration },
                })
            },
            new QuickStartProfile
            {
                Id = HuddiniShowcaseFullscreen,
                Name = "Huddini Showcase",
                Mode = QuickStartMode.Fullscreen,
                Summary = "Everything on: crossfades, live effects, volume boost, installed games only. Also sets your volume.",
                Values = ShowcaseValues()
            },
            new QuickStartProfile
            {
                Id = JukeboxFullscreen,
                Name = "Radio Mode (Random Game Music)",
                Mode = QuickStartMode.Fullscreen,
                Summary = "One continuous mix instead of per-game music. Library or Spotify.",
                Values = JukeboxValues()
            },

            new QuickStartProfile
            {
                Id = HoverPreviewDesktop,
                Name = "Hover Preview, Short Clip (PS3 style)",
                Mode = QuickStartMode.Desktop,
                Summary = "Music follows your selection, looping a 30-second clip of each game's track.",
                Values = Merge(HoverBaseDesktop(), ClipValues(true))
            },
            new QuickStartProfile
            {
                Id = HoverFullTrackDesktop,
                Name = "Hover Preview, Full Track",
                Mode = QuickStartMode.Desktop,
                Summary = "Music follows your selection, playing each game's track in full.",
                Values = Merge(HoverBaseDesktop(), ClipValues(false))
            },
            new QuickStartProfile
            {
                Id = AmbientDesktop,
                Name = "Background Mode (Default Music)",
                Mode = QuickStartMode.Desktop,
                Summary = "One ambient track the whole time. Game music off, so nothing interrupts it.",
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
                Id = HuddiniShowcaseDesktop,
                Name = "Huddini Showcase",
                Mode = QuickStartMode.Desktop,
                Summary = "Everything on: crossfades, live effects, volume boost, installed games only. Also sets your volume.",
                Values = ShowcaseValues()
            },
            new QuickStartProfile
            {
                Id = JukeboxDesktop,
                Name = "Radio Mode (Random Game Music)",
                Mode = QuickStartMode.Desktop,
                Summary = "One continuous mix instead of per-game music. Library or Spotify.",
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
            { nameof(UniPlaySongSettings.EnablePreviewMode), false },
            // Owned for the same reason — otherwise applying Jukebox after Hover Short Clip would
            // inherit its loop, and the "continuous mix" would be one track repeating.
            { nameof(UniPlaySongSettings.RandomizeOnMusicEnd), true },
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
