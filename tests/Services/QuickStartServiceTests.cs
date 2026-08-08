using System.Linq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class QuickStartServiceTests
    {
        private QuickStartService _svc;
        private UniPlaySongSettings _s;

        [SetUp]
        public void SetUp()
        {
            _svc = new QuickStartService();
            _s = new UniPlaySongSettings();
        }

        private QuickStartProfile P(string id) => QuickStartProfiles.ById(id);

        [Test]
        public void Catalogue_HasFourTilesPerMode()
        {
            Assert.AreEqual(4, QuickStartProfiles.For(QuickStartMode.Fullscreen).Count());
            Assert.AreEqual(4, QuickStartProfiles.For(QuickStartMode.Desktop).Count());
        }

        // The Short Clip / Full Track pair is the reason EnablePreviewMode must be owned explicitly.
        [Test]
        public void HoverShortClip_EnablesPreviewMode()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);

            Assert.IsTrue(_s.EnablePreviewMode);
            Assert.AreEqual(Constants.DefaultPreviewDuration, _s.PreviewDuration);
            Assert.IsFalse(_s.PlayOnlyOnGameSelect, "still hover, not select");
            Assert.IsFalse(_s.RandomizeOnMusicEnd,
                "the clip loops rather than advancing — that is what makes it read as a PS3 menu");
        }

        // The Desktop tile was only covered indirectly, and a report of "the full song plays on the
        // PS3 clip option in Desktop" turned out to be the settings taking effect on the NEXT song
        // rather than the profile being wrong — the preview timer starts in MarkSongStart, so a
        // track already playing runs to its natural end. Asserting the Desktop tile directly so
        // that distinction stays testable rather than needing to be re-derived.
        [Test]
        public void HoverShortClip_Desktop_EnablesPreviewModeAndLoops()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewDesktop), JukeboxSource.Library, false, false, false);

            Assert.IsTrue(_s.EnablePreviewMode, "Desktop clip tile must enable preview mode too");
            Assert.AreEqual(Constants.DefaultPreviewDuration, _s.PreviewDuration);
            Assert.IsFalse(_s.RandomizeOnMusicEnd, "the clip loops");
        }

        // Both modes' clip tiles must agree on the clip settings; only the trigger differs.
        [Test]
        public void BothShortClipTiles_AgreeOnClipBehaviour()
        {
            foreach (var id in new[] { QuickStartProfiles.HoverPreviewFullscreen, QuickStartProfiles.HoverPreviewDesktop })
            {
                var s = new UniPlaySongSettings();
                new QuickStartService().Apply(s, P(id), JukeboxSource.Library, false, false, false);
                Assert.IsTrue(s.EnablePreviewMode, $"{id}");
                Assert.AreEqual(Constants.DefaultPreviewDuration, s.PreviewDuration, $"{id}");
                Assert.IsFalse(s.RandomizeOnMusicEnd, $"{id}");
            }
        }

        // Only Short Clip loops. Everything else advances on song end, and must own that so the
        // loop cannot leak out of Short Clip into a tile applied afterwards.
        [Test]
        public void OnlyShortClipLoops_EveryOtherTileAdvancesOnSongEnd()
        {
            foreach (var p in QuickStartProfiles.All)
            {
                var expectLoop = p.Id == QuickStartProfiles.HoverPreviewFullscreen
                              || p.Id == QuickStartProfiles.HoverPreviewDesktop;

                Assert.IsTrue(p.Values.ContainsKey(nameof(UniPlaySongSettings.RandomizeOnMusicEnd)),
                    $"{p.Id} does not declare RandomizeOnMusicEnd");

                var s = new UniPlaySongSettings();
                new QuickStartService().Apply(s, p, JukeboxSource.Library, false, false, false);
                Assert.AreEqual(!expectLoop, s.RandomizeOnMusicEnd, $"{p.Id} has the wrong song-end behaviour");
            }
        }

        [Test]
        public void SwitchingFromShortClipToAnotherTile_StopsTheLoop()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsFalse(_s.RandomizeOnMusicEnd);

            _svc.Apply(_s, P(QuickStartProfiles.HoverFullTrackFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsTrue(_s.RandomizeOnMusicEnd, "full track should advance, not inherit the loop");
        }

        [Test]
        public void HoverFullTrack_TurnsPreviewModeOff()
        {
            _s.EnablePreviewMode = true; // user had clips on
            _svc.Apply(_s, P(QuickStartProfiles.HoverFullTrackFullscreen), JukeboxSource.Library, false, false, false);

            Assert.IsFalse(_s.EnablePreviewMode, "full track means the whole song");
            Assert.IsFalse(_s.PlayOnlyOnGameSelect);
        }

        // EnablePreviewMode persists, so a tile that stayed silent about it would inherit whatever
        // was applied before and two users would hear different things from the same tile.
        [Test]
        public void EveryProfile_OwnsPreviewModeSoItCannotLeakBetweenTiles()
        {
            foreach (var p in QuickStartProfiles.All)
                Assert.IsTrue(p.Values.ContainsKey(nameof(UniPlaySongSettings.EnablePreviewMode)),
                    $"{p.Id} does not declare EnablePreviewMode");
        }

        [Test]
        public void SwitchingFromShortClipToAnyOtherTile_ClearsPreviewMode()
        {
            foreach (var p in QuickStartProfiles.All.Where(x =>
                x.Id != QuickStartProfiles.HoverPreviewFullscreen && x.Id != QuickStartProfiles.HoverPreviewDesktop))
            {
                var s = new UniPlaySongSettings();
                var svc = new QuickStartService();
                svc.Apply(s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
                Assert.IsTrue(s.EnablePreviewMode);

                svc.Apply(s, p, JukeboxSource.Library, false, false, false);
                Assert.IsFalse(s.EnablePreviewMode, $"{p.Id} inherited the clip setting");
            }
        }

        // Background Mode is the one tile that deliberately switches game music off.
        [Test]
        public void BackgroundMode_TurnsGameMusicOffAndPinsTheBundledPreset()
        {
            _svc.Apply(_s, P(QuickStartProfiles.AmbientDesktop), JukeboxSource.Library, false, false, false);

            Assert.IsFalse(_s.EnableMusic, "game music is off so nothing interrupts the background track");
            Assert.IsTrue(_s.EnableDefaultMusic);
            Assert.AreEqual(DefaultMusicSource.BundledPreset, _s.DefaultMusicSourceOption);
            Assert.IsFalse(_s.RadioModeEnabled);
        }

        // Every other tile promises game music, so each must undo Background Mode's suppression.
        [Test]
        public void SwitchingAwayFromBackgroundMode_RestoresGameMusic()
        {
            foreach (var p in QuickStartProfiles.All.Where(x => x.Id != QuickStartProfiles.AmbientDesktop))
            {
                var s = new UniPlaySongSettings { EnableMusic = false };
                new QuickStartService().Apply(s, p, JukeboxSource.Library, false, false, false);
                Assert.IsTrue(s.EnableMusic, $"{p.Id} must turn game music back on");
            }
        }

        [Test]
        public void Ids_AreUnique()
        {
            var ids = QuickStartProfiles.All.Select(p => p.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
        }

        // PlayOnlyOnGameSelect is Fullscreen-only by design, so a Desktop profile writing it would
        // be storing a value the mode cannot act on.
        [Test]
        public void DesktopProfiles_DoNotOwnPlayOnlyOnGameSelect()
        {
            foreach (var p in QuickStartProfiles.For(QuickStartMode.Desktop))
            {
                if (QuickStartProfiles.IsJukebox(p)) continue; // radio explicitly clears it
                Assert.IsFalse(p.Values.ContainsKey(nameof(UniPlaySongSettings.PlayOnlyOnGameSelect)),
                    $"{p.Id} should not own PlayOnlyOnGameSelect");
            }
        }

        // Decided in design: volume is personal and must never be overwritten by a profile.
        [Test]
        public void NoProfile_OwnsMusicVolume()
        {
            foreach (var p in QuickStartProfiles.All)
                Assert.IsFalse(p.Values.ContainsKey(nameof(UniPlaySongSettings.MusicVolume)), $"{p.Id} owns MusicVolume");
        }

        // Every profile must state its fallback, or the same tile behaves differently per user.
        [Test]
        public void EveryProfile_DeclaresItsFallback()
        {
            foreach (var p in QuickStartProfiles.All)
                Assert.IsTrue(p.Values.ContainsKey(nameof(UniPlaySongSettings.EnableDefaultMusic)),
                    $"{p.Id} does not declare EnableDefaultMusic");
        }

        [Test]
        public void Apply_HoverPreview_SetsTriggerAndFallback()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);

            Assert.IsFalse(_s.PlayOnlyOnGameSelect, "hover means music follows the highlight");
            Assert.IsTrue(_s.EnableDefaultMusic);
            Assert.IsTrue(_s.DefaultMusicContinueSameSong, "the bed must not restart on every no-music game");
            Assert.IsFalse(_s.RadioModeEnabled, "radio would replace per-game music");
            Assert.AreEqual(QuickStartProfiles.HoverPreviewFullscreen, _s.ActiveQuickStartProfile);
        }

        [Test]
        public void Apply_SelectToPlay_TurnsOnPlayOnlyOnGameSelect()
        {
            _svc.Apply(_s, P(QuickStartProfiles.SelectToPlayFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsTrue(_s.PlayOnlyOnGameSelect);
            Assert.IsTrue(_s.EnableDefaultMusic, "browsing still needs a bed");
        }

        // SpotifyRadioMode is [JsonIgnore] and derived; a profile must produce it via the two real
        // keys rather than trying to write it.
        [Test]
        public void Apply_JukeboxSpotify_ProducesDerivedSpotifyRadioMode()
        {
            _svc.Apply(_s, P(QuickStartProfiles.JukeboxFullscreen), JukeboxSource.Spotify, false, true, false);

            Assert.IsTrue(_s.RadioModeEnabled);
            Assert.AreEqual(RadioMusicSource.Spotify, _s.RadioMusicSource);
            Assert.IsTrue(_s.SpotifyRadioMode, "derived from RadioModeEnabled + RadioMusicSource");
            Assert.IsTrue(_s.RadioPlaysThroughGames);
        }

        [Test]
        public void Apply_JukeboxLibrary_DoesNotEngageSpotify()
        {
            _svc.Apply(_s, P(QuickStartProfiles.JukeboxDesktop), JukeboxSource.Library, false, false, false);
            Assert.IsTrue(_s.RadioModeEnabled);
            Assert.AreEqual(RadioMusicSource.FullLibrary, _s.RadioMusicSource);
            Assert.IsFalse(_s.SpotifyRadioMode);
        }

        [Test]
        public void Apply_InstalledOnlyCheckbox_IsHonoured()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, true, false, false);
            Assert.IsTrue(_s.MusicOnlyForInstalledGames);

            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsFalse(_s.MusicOnlyForInstalledGames);
        }

        // The whole point of declared-keys-only: applying a profile must not disturb unrelated setup.
        [Test]
        public void Apply_LeavesUnownedSettingsAlone()
        {
            _s.MusicVolume = 42;
            _s.FFmpegPath = @"C:\tools\ffmpeg.exe";
            _s.PauseOnGameStart = false;
            _s.EnableDebugLogging = true;

            _svc.Apply(_s, P(QuickStartProfiles.SelectToPlayFullscreen), JukeboxSource.Library, false, false, false);

            Assert.AreEqual(42, _s.MusicVolume);
            Assert.AreEqual(@"C:\tools\ffmpeg.exe", _s.FFmpegPath);
            Assert.IsFalse(_s.PauseOnGameStart);
            Assert.IsTrue(_s.EnableDebugLogging);
        }

        [Test]
        public void Undo_RestoresPreviousValuesAndProfile()
        {
            _s.PlayOnlyOnGameSelect = true;
            _s.EnableDefaultMusic = false;
            _s.RadioModeEnabled = true;

            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsFalse(_s.PlayOnlyOnGameSelect);

            Assert.IsTrue(_svc.CanUndo);
            Assert.IsTrue(_svc.Undo(_s));

            Assert.IsTrue(_s.PlayOnlyOnGameSelect, "undo restores the pre-apply value");
            Assert.IsFalse(_s.EnableDefaultMusic);
            Assert.IsTrue(_s.RadioModeEnabled);
            Assert.AreEqual(string.Empty, _s.ActiveQuickStartProfile);
            Assert.IsFalse(_svc.CanUndo, "undo is single-shot");
        }

        [Test]
        public void IsModified_FalseImmediatelyAfterApply_TrueAfterDrift()
        {
            _svc.Apply(_s, P(QuickStartProfiles.SelectToPlayFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsFalse(_svc.IsModified(_s, JukeboxSource.Library, false, false, false));

            _s.PlayOnlyOnGameSelect = false; // user changes an owned setting
            Assert.IsTrue(_svc.IsModified(_s, JukeboxSource.Library, false, false, false));
        }

        [Test]
        public void IsModified_IgnoresSettingsTheProfileDoesNotOwn()
        {
            _svc.Apply(_s, P(QuickStartProfiles.SelectToPlayFullscreen), JukeboxSource.Library, false, false, false);
            _s.MusicVolume = 11;
            _s.EnableDebugLogging = true;

            Assert.IsFalse(_svc.IsModified(_s, JukeboxSource.Library, false, false, false),
                "changing an unowned setting is not profile drift");
        }

        // StartRadioPlayback returns without playing when its pool is empty, so a Jukebox profile
        // that also turned default music off would leave a user with nothing and no explanation.
        [Test]
        public void Jukebox_KeepsDefaultMusicOnAsTheSafetyNet()
        {
            _svc.Apply(_s, P(QuickStartProfiles.JukeboxFullscreen), JukeboxSource.Library, false, true, false);
            Assert.IsTrue(_s.EnableDefaultMusic, "an empty radio pool must still leave something to play");

            _svc.Apply(_s, P(QuickStartProfiles.JukeboxDesktop), JukeboxSource.Spotify, false, true, false);
            Assert.IsTrue(_s.EnableDefaultMusic);
        }

        [Test]
        public void EveryProfile_LeavesDefaultMusicEnabled()
        {
            foreach (var p in QuickStartProfiles.All)
            {
                var s = new UniPlaySongSettings();
                new QuickStartService().Apply(s, p, JukeboxSource.Library, false, false, false);
                Assert.IsTrue(s.EnableDefaultMusic, $"{p.Id} left default music off");
            }
        }

        // Enabling a fallback that is itself unconfigured is the same silent failure one layer down.
        [TestCase(DefaultMusicSource.CustomFile)]
        [TestCase(DefaultMusicSource.CustomFolder)]
        [TestCase(DefaultMusicSource.CustomRotation)]
        [TestCase(DefaultMusicSource.CompletionStatusPool)]
        public void Apply_UnconfiguredDefaultSource_FallsBackToBundledPreset(DefaultMusicSource source)
        {
            _s.DefaultMusicSourceOption = source; // left unconfigured: no path, no list

            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);

            Assert.AreEqual(DefaultMusicSource.BundledPreset, _s.DefaultMusicSourceOption,
                $"{source} has nothing behind it, so the profile should fall back to the bundled preset");
        }

        [Test]
        public void Apply_ConfiguredDefaultSource_IsLeftAlone()
        {
            _s.DefaultMusicSourceOption = DefaultMusicSource.CustomFolder;
            _s.DefaultMusicFolderPath = @"D:\Music\Ambient";

            _svc.Apply(_s, P(QuickStartProfiles.SelectToPlayFullscreen), JukeboxSource.Library, false, false, false);

            Assert.AreEqual(DefaultMusicSource.CustomFolder, _s.DefaultMusicSourceOption,
                "a source the user has actually configured must survive");
            Assert.AreEqual(@"D:\Music\Ambient", _s.DefaultMusicFolderPath);
        }

        [Test]
        public void Apply_SourcesNeedingNoSetup_AreLeftAlone()
        {
            foreach (var src in new[] { DefaultMusicSource.RandomGame, DefaultMusicSource.Spotify, DefaultMusicSource.ActiveThemeMusic })
            {
                var s = new UniPlaySongSettings { DefaultMusicSourceOption = src };
                new QuickStartService().Apply(s, P(QuickStartProfiles.HoverPreviewDesktop), JukeboxSource.Library, false, false, false);
                Assert.AreEqual(src, s.DefaultMusicSourceOption, $"{src} needs no user setup and should be kept");
            }
        }

        // In the radio branch, MusicOnlyForInstalledGames makes radio YIELD to installed games with
        // music — which would break the one thing Jukebox promises.
        [Test]
        public void Jukebox_IgnoresInstalledOnly_SoTheMixDoesNotStop()
        {
            _svc.Apply(_s, P(QuickStartProfiles.JukeboxFullscreen), JukeboxSource.Library, true, true, false);

            Assert.IsFalse(_s.MusicOnlyForInstalledGames,
                "installed-only makes radio yield to installed games, interrupting the mix");
            Assert.IsTrue(_s.RadioModeEnabled);
        }

        [Test]
        public void Jukebox_DoesNotReadAsModifiedWhenInstalledOnlyIsTicked()
        {
            _svc.Apply(_s, P(QuickStartProfiles.JukeboxDesktop), JukeboxSource.Library, true, true, false);

            Assert.IsFalse(_svc.IsModified(_s, JukeboxSource.Library, true, true, false),
                "drift detection must mirror the apply rule or Jukebox always looks modified");
        }

        [Test]
        public void NonJukebox_StillHonoursInstalledOnly()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, true, false, false);
            Assert.IsTrue(_s.MusicOnlyForInstalledGames, "the qualifier still applies to per-game tiles");
        }

        // "Add reverb" composes with every tile rather than being tiles of its own.
        [Test]
        public void Apply_AddReverb_EnablesLiveEffectsWithHuddiniRehearsal()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, true);

            Assert.IsTrue(_s.LiveEffectsEnabled);
            Assert.AreEqual(StylePreset.HuddiniRehearsal, _s.SelectedStylePreset);
        }

        [Test]
        public void Apply_WithoutReverb_LeavesLiveEffectsOff()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsFalse(_s.LiveEffectsEnabled);
        }

        // Unchecking must not strand the preset choice as the active style with effects off; only
        // the master toggle is owned when reverb is off, so the user's own preset survives.
        [Test]
        public void Apply_ReverbOff_DoesNotOverwriteTheUsersStylePreset()
        {
            _s.SelectedStylePreset = StylePreset.HuddiniRetroRadio;
            _svc.Apply(_s, P(QuickStartProfiles.SelectToPlayFullscreen), JukeboxSource.Library, false, false, false);

            Assert.IsFalse(_s.LiveEffectsEnabled);
            Assert.AreEqual(StylePreset.HuddiniRetroRadio, _s.SelectedStylePreset, "reverb-off owns only the master toggle");
        }

        [Test]
        public void Apply_ReverbWorksOnBothModes()
        {
            _svc.Apply(_s, P(QuickStartProfiles.AmbientDesktop), JukeboxSource.Library, false, false, true);
            Assert.IsTrue(_s.LiveEffectsEnabled);
            Assert.AreEqual(StylePreset.HuddiniRehearsal, _s.SelectedStylePreset);
        }

        [Test]
        public void Undo_RestoresLiveEffectsState()
        {
            _s.LiveEffectsEnabled = false;
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewDesktop), JukeboxSource.Library, false, false, true);
            Assert.IsTrue(_s.LiveEffectsEnabled);

            _svc.Undo(_s);
            Assert.IsFalse(_s.LiveEffectsEnabled, "undo must back out the backend-forcing change too");
        }

        // "Reset to my settings" steps back to before the FIRST profile, not the last one.
        [Test]
        public void RestoreOriginal_ReturnsToPreProfileSettingsAfterSeveralApplies()
        {
            _s.PlayOnlyOnGameSelect = true;
            _s.EnableDefaultMusic = false;
            _s.RadioModeEnabled = true;
            _s.MusicOnlyForInstalledGames = true;

            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
            _svc.Apply(_s, P(QuickStartProfiles.JukeboxDesktop), JukeboxSource.Spotify, false, true, true);
            _svc.Apply(_s, P(QuickStartProfiles.AmbientDesktop), JukeboxSource.Library, false, false, false);

            Assert.IsTrue(_svc.HasBaseline(_s));
            Assert.IsTrue(_svc.RestoreOriginal(_s));

            Assert.IsTrue(_s.PlayOnlyOnGameSelect, "back to the user's value, not the first tile's");
            Assert.IsFalse(_s.EnableDefaultMusic);
            Assert.IsTrue(_s.RadioModeEnabled);
            Assert.IsTrue(_s.MusicOnlyForInstalledGames);
            Assert.AreEqual(string.Empty, _s.ActiveQuickStartProfile, "no tile should be marked current");
        }

        // Restoring must cover the full owned surface, not just the last profile's keys — otherwise
        // Background Mode's EnableMusic=false would survive a reset.
        [Test]
        public void RestoreOriginal_UndoesKeysTheLastProfileDidNotOwn()
        {
            // LiveEffectsEnabled defaults to TRUE, so start from an explicitly-off user state to
            // prove the restore returns the USER's value rather than a profile's or a default.
            _s.EnableMusic = true;
            _s.LiveEffectsEnabled = false;
            _s.SelectedStylePreset = StylePreset.HuddiniRetroRadio;

            _svc.Apply(_s, P(QuickStartProfiles.AmbientDesktop), JukeboxSource.Library, false, false, true);
            Assert.IsFalse(_s.EnableMusic);
            Assert.IsTrue(_s.LiveEffectsEnabled, "the reverb checkbox turned it on");
            Assert.AreEqual(StylePreset.HuddiniRehearsal, _s.SelectedStylePreset);

            _svc.RestoreOriginal(_s);

            Assert.IsTrue(_s.EnableMusic, "the master toggle must come back");
            Assert.IsFalse(_s.LiveEffectsEnabled, "reverb goes back to how the user had it");
            Assert.AreEqual(StylePreset.HuddiniRetroRadio, _s.SelectedStylePreset, "and so does their preset");
        }

        [Test]
        public void RestoreOriginal_LeavesUnownedSettingsAlone()
        {
            _s.MusicVolume = 42;
            _s.FFmpegPath = @"C:\tools\ffmpeg.exe";

            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewDesktop), JukeboxSource.Library, false, false, false);
            _svc.RestoreOriginal(_s);

            Assert.AreEqual(42, _s.MusicVolume, "reset is not a factory reset");
            Assert.AreEqual(@"C:\tools\ffmpeg.exe", _s.FFmpegPath);
        }

        // The baseline is persisted, so a NEW service instance — a Playnite restart between two
        // applies — must still restore the user's own settings rather than the first profile's.
        // Previously the second instance captured a fresh baseline from already-profiled settings,
        // so Reset handed back a profile.
        [Test]
        public void RestoreOriginal_SurvivesANewServiceInstance()
        {
            _s.FadeInDuration = 2.5;
            _s.PlayOnlyOnGameSelect = true;
            _s.PreviewDuration = 77;

            new QuickStartService().Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);

            var afterRestart = new QuickStartService();
            afterRestart.Apply(_s, P(QuickStartProfiles.SelectToPlayFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsTrue(afterRestart.HasBaseline(_s), "the persisted baseline must be visible to a new instance");
            Assert.IsTrue(afterRestart.RestoreOriginal(_s));

            Assert.AreEqual(2.5, _s.FadeInDuration, 0.001, "the user's fade, not a profile's");
            Assert.IsTrue(_s.PlayOnlyOnGameSelect);
            Assert.AreEqual(77, _s.PreviewDuration);
        }

        // The baseline round-trips through JSON, which turns enums into numbers and doubles into
        // whatever fits — so restoring has to convert back, not just assign.
        [Test]
        public void RestoreOriginal_RoundTripsEnumsAndDoublesThroughJson()
        {
            _s.DefaultMusicSourceOption = DefaultMusicSource.RandomGame;
            _s.RadioMusicSource = RadioMusicSource.CustomRotation;
            _s.SelectedStylePreset = StylePreset.HuddiniRetroRadio;
            _s.FadeOutDuration = 1.25;

            new QuickStartService().Apply(_s, P(QuickStartProfiles.JukeboxFullscreen), JukeboxSource.Spotify, false, true, true);

            var svc = new QuickStartService();
            Assert.IsTrue(svc.RestoreOriginal(_s));

            Assert.AreEqual(DefaultMusicSource.RandomGame, _s.DefaultMusicSourceOption);
            Assert.AreEqual(RadioMusicSource.CustomRotation, _s.RadioMusicSource);
            Assert.AreEqual(StylePreset.HuddiniRetroRadio, _s.SelectedStylePreset);
            Assert.AreEqual(1.25, _s.FadeOutDuration, 0.001);
        }

        // Applying repeatedly must not move the baseline — it is captured before the FIRST profile.
        [Test]
        public void RestoreOriginal_BaselineDoesNotDriftAcrossRepeatedApplies()
        {
            _s.FadeInDuration = 2.5;
            _s.MusicOnlyForInstalledGames = true;

            for (int i = 0; i < 5; i++)
            {
                _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
                _svc.Apply(_s, P(QuickStartProfiles.JukeboxDesktop), JukeboxSource.Library, true, true, true);
            }

            _svc.RestoreOriginal(_s);

            Assert.AreEqual(2.5, _s.FadeInDuration, 0.001);
            Assert.IsTrue(_s.MusicOnlyForInstalledGames);
        }

        // After a reset the baseline is gone, so the next apply captures where the user is NOW
        // rather than dragging them back to a state they deliberately moved on from.
        [Test]
        public void RestoreOriginal_ClearsTheBaselineSoTheNextApplyStartsFresh()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
            _svc.RestoreOriginal(_s);
            Assert.IsFalse(_svc.HasBaseline(_s));

            _s.FadeInDuration = 3.0; // a new deliberate choice
            _svc.Apply(_s, P(QuickStartProfiles.JukeboxFullscreen), JukeboxSource.Library, false, false, false);
            _svc.RestoreOriginal(_s);

            Assert.AreEqual(3.0, _s.FadeInDuration, 0.001, "back to the newer choice, not the original one");
        }

        [Test]
        public void RestoreOriginal_UnavailableBeforeAnyProfileIsApplied()
        {
            Assert.IsFalse(_svc.HasBaseline(_s));
            Assert.IsFalse(_svc.RestoreOriginal(_s));
        }

        [Test]
        public void RestoreOriginal_ClearsUndoSoItCannotReapplyAForgottenProfile()
        {
            _svc.Apply(_s, P(QuickStartProfiles.SelectToPlayFullscreen), JukeboxSource.Library, false, false, false);
            _svc.RestoreOriginal(_s);

            Assert.IsFalse(_svc.CanUndo);
        }

        [Test]
        public void IsModified_FalseWhenNoProfileActive()
        {
            Assert.IsFalse(_svc.IsModified(_s, JukeboxSource.Library, false, false, false));
        }

        [Test]
        public void Apply_NullProfile_IsSafe()
        {
            Assert.IsFalse(_svc.Apply(_s, null, JukeboxSource.Library, false, false, false));
            Assert.IsFalse(_svc.Apply(null, P(QuickStartProfiles.HoverPreviewDesktop), JukeboxSource.Library, false, false, false));
        }
    }
}
