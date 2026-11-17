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

        // Both modes' clip tiles must agree on the clip settings; only the trigger differs. The
        // Short Clip / Full Track pair is the reason EnablePreviewMode must be owned explicitly, and
        // the clip loops rather than advancing - that is what makes it read as a PS3 menu.
        // The Desktop tile was once only covered indirectly, and a report of "the full song plays on
        // the PS3 clip option in Desktop" turned out to be the settings taking effect on the NEXT
        // song rather than the profile being wrong - the preview timer starts in MarkSongStart, so a
        // track already playing runs to its natural end. Asserting the Desktop tile directly keeps
        // that distinction testable rather than needing to be re-derived.
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

        // Rules every tile must keep, checked across all of them so a new tile cannot skip one:
        // - Only Short Clip loops; every other tile advances on song end and owns that, so the loop (and the clip
        //   setting) cannot leak out of Short Clip into a tile applied afterwards.
        // - Every tile but Background Mode promises game music, so each must undo Background Mode's suppression.
        // - Every tile declares its fallback and leaves default music ON: StartRadioPlayback returns without playing
        //   on an empty pool, so a Jukebox that also turned default music off would leave silence and no explanation.
        // - Volume is personal: only Huddini Showcase (a complete configuration, volume included) owns MusicVolume.
        // - PlayOnlyOnGameSelect is Fullscreen-only, so a Desktop tile writing it stores a value the mode can't use.
        [Test]
        public void EveryProfile_KeepsTheTileInvariants()
        {
            CollectionAssert.AllItemsAreUnique(QuickStartProfiles.All.Select(p => p.Id).ToList());

            Assert.Multiple(() =>
            {
                foreach (var p in QuickStartProfiles.All)
                {
                    var isClip = p.Id == QuickStartProfiles.HoverPreviewFullscreen || p.Id == QuickStartProfiles.HoverPreviewDesktop;

                    Assert.IsTrue(p.Values.ContainsKey(nameof(UniPlaySongSettings.RandomizeOnMusicEnd)),
                        $"{p.Id} does not declare RandomizeOnMusicEnd");
                    Assert.IsTrue(p.Values.ContainsKey(nameof(UniPlaySongSettings.EnableDefaultMusic)),
                        $"{p.Id} does not declare EnableDefaultMusic");
                    Assert.AreEqual(QuickStartProfiles.IsShowcase(p), p.Values.ContainsKey(nameof(UniPlaySongSettings.MusicVolume)),
                        $"{p.Id} has the wrong MusicVolume ownership");
                    if (QuickStartProfiles.For(QuickStartMode.Desktop).Contains(p) && !QuickStartProfiles.IsJukebox(p)) // radio explicitly clears it
                        Assert.IsFalse(p.Values.ContainsKey(nameof(UniPlaySongSettings.PlayOnlyOnGameSelect)),
                            $"{p.Id} should not own PlayOnlyOnGameSelect");

                    var s = new UniPlaySongSettings();
                    new QuickStartService().Apply(s, p, JukeboxSource.Library, false, false, false);
                    Assert.AreEqual(!isClip, s.RandomizeOnMusicEnd, $"{p.Id} has the wrong song-end behaviour");
                    Assert.IsTrue(s.EnableDefaultMusic, $"{p.Id} left default music off");

                    if (!isClip)
                    {
                        var fromClip = new UniPlaySongSettings();
                        var svc = new QuickStartService();
                        svc.Apply(fromClip, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
                        Assert.IsTrue(fromClip.EnablePreviewMode);
                        svc.Apply(fromClip, p, JukeboxSource.Library, false, false, false);
                        Assert.IsFalse(fromClip.EnablePreviewMode, $"{p.Id} inherited the clip setting");
                    }

                    if (p.Id != QuickStartProfiles.AmbientDesktop)
                    {
                        var fromBackground = new UniPlaySongSettings { EnableMusic = false };
                        new QuickStartService().Apply(fromBackground, p, JukeboxSource.Library, false, false, false);
                        Assert.IsTrue(fromBackground.EnableMusic, $"{p.Id} must turn game music back on");
                    }
                }
            });
        }

        // Every value a tile declares must survive its property's clamp, or the tile would advertise settings it
        // does not actually apply. The defaults here (Library source, boxes clear, usable default source) leave
        // the declared values unaltered by the page-level modifiers.
        [Test]
        public void EveryProfile_AppliesEveryValueItDeclares()
        {
            Assert.Multiple(() =>
            {
                foreach (var p in QuickStartProfiles.All)
                {
                    var s = new UniPlaySongSettings();
                    new QuickStartService().Apply(s, p, JukeboxSource.Library, false, false, false);

                    foreach (var kv in p.Values)
                    {
                        var prop = typeof(UniPlaySongSettings).GetProperty(kv.Key);
                        Assert.IsNotNull(prop, $"{p.Id} declares {kv.Key}, which is not a setting");
                        if (prop == null) continue;

                        var actual = prop.GetValue(s);
                        if (actual is double d)
                            Assert.AreEqual(System.Convert.ToDouble(kv.Value), d, 0.001, $"{p.Id}.{kv.Key}");
                        else
                            Assert.AreEqual(kv.Value, actual, $"{p.Id}.{kv.Key}");
                    }
                }
            });
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

        // Offered in both columns, and both must configure identically — it is one setup, not two.
        // The showcase declares installed-only itself, so an unticked page checkbox must not undo it.
        [Test]
        public void Showcase_IsIdenticalInBothModes()
        {
            var fs = new UniPlaySongSettings();
            var dt = new UniPlaySongSettings();
            new QuickStartService().Apply(fs, P(QuickStartProfiles.HuddiniShowcaseFullscreen), JukeboxSource.Library, false, false, false);
            new QuickStartService().Apply(dt, P(QuickStartProfiles.HuddiniShowcaseDesktop), JukeboxSource.Library, false, false, false);

            Assert.AreEqual(fs.MusicVolume, dt.MusicVolume);
            Assert.AreEqual(fs.CrossfadeDurationSeconds, dt.CrossfadeDurationSeconds);
            Assert.AreEqual(fs.SelectedStylePreset, dt.SelectedStylePreset);
            Assert.AreEqual(fs.MusicState, dt.MusicState);
            Assert.IsTrue(dt.MusicOnlyForInstalledGames, "installed-only is part of this profile even with the checkbox clear");
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

            var fullTrack = new UniPlaySongSettings { PlayOnlyOnGameSelect = true };
            new QuickStartService().Apply(fullTrack, P(QuickStartProfiles.HoverFullTrackFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsFalse(fullTrack.PlayOnlyOnGameSelect, "Full Track is a hover tile too");
        }

        [Test]
        public void Apply_SelectToPlay_TurnsOnPlayOnlyOnGameSelect()
        {
            _svc.Apply(_s, P(QuickStartProfiles.SelectToPlayFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsTrue(_s.PlayOnlyOnGameSelect);
            Assert.IsTrue(_s.EnableDefaultMusic, "browsing still needs a bed");
        }

        // SpotifyRadioMode is [JsonIgnore] and derived; a profile must produce it via the two real
        // keys rather than trying to write it. The Library source must not engage Spotify.
        [Test]
        public void Apply_Jukebox_DerivesSpotifyRadioModeFromTheSource()
        {
            _svc.Apply(_s, P(QuickStartProfiles.JukeboxFullscreen), JukeboxSource.Spotify, false, true, false);

            Assert.IsTrue(_s.RadioModeEnabled);
            Assert.AreEqual(RadioMusicSource.Spotify, _s.RadioMusicSource);
            Assert.IsTrue(_s.SpotifyRadioMode, "derived from RadioModeEnabled + RadioMusicSource");
            Assert.IsTrue(_s.RadioPlaysThroughGames);

            var lib = new UniPlaySongSettings();
            new QuickStartService().Apply(lib, P(QuickStartProfiles.JukeboxDesktop), JukeboxSource.Library, false, false, false);
            Assert.IsTrue(lib.RadioModeEnabled);
            Assert.AreEqual(RadioMusicSource.FullLibrary, lib.RadioMusicSource);
            Assert.IsFalse(lib.SpotifyRadioMode, "the Library source does not engage Spotify");
        }

        [Test]
        public void Apply_InstalledOnlyCheckbox_IsHonoured()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, true, false, false);
            Assert.IsTrue(_s.MusicOnlyForInstalledGames);

            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
            Assert.IsFalse(_s.MusicOnlyForInstalledGames);
        }

        // The whole point of declared-keys-only: applying a profile must not disturb unrelated setup, changing an
        // unowned setting is not profile drift, and "Reset to my settings" is not a factory reset.
        [Test]
        public void UnownedSettings_AreLeftAloneByApplyIsModifiedAndRestore()
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

            _s.MusicVolume = 11;
            Assert.IsFalse(_svc.IsModified(_s, JukeboxSource.Library, false, false, false),
                "changing an unowned setting is not profile drift");

            // MusicVolume is in the restore surface (Showcase owns it), so it returns to the user's 42, not a default.
            _svc.RestoreOriginal(_s);
            Assert.AreEqual(42, _s.MusicVolume, "reset is not a factory reset");
            Assert.AreEqual(@"C:\tools\ffmpeg.exe", _s.FFmpegPath);
            Assert.IsTrue(_s.EnableDebugLogging);
        }

        // Undo backs out every owned key, including the reverb checkbox's backend-forcing Live Effects change.
        [Test]
        public void Undo_RestoresPreviousValuesAndProfile()
        {
            _s.PlayOnlyOnGameSelect = true;
            _s.EnableDefaultMusic = false;
            _s.RadioModeEnabled = true;
            _s.LiveEffectsEnabled = false;

            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, true);
            Assert.IsFalse(_s.PlayOnlyOnGameSelect);
            Assert.IsTrue(_s.LiveEffectsEnabled);

            Assert.IsTrue(_svc.CanUndo);
            Assert.IsTrue(_svc.Undo(_s));

            Assert.IsTrue(_s.PlayOnlyOnGameSelect, "undo restores the pre-apply value");
            Assert.IsFalse(_s.EnableDefaultMusic);
            Assert.IsTrue(_s.RadioModeEnabled);
            Assert.IsFalse(_s.LiveEffectsEnabled, "undo must back out the backend-forcing change too");
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

        // Enabling a fallback that is itself unconfigured is the same silent failure one layer down: an unconfigured
        // source falls back to the bundled preset; a configured one, or one needing no setup, is kept. One source
        // is enough to pin the Apply wiring; SilentFailureFallbackTests covers that every source needing user
        // input is recognised as unusable.
        [Test]
        public void Apply_DefaultSource_FallsBackOnlyWhenUnconfigured()
        {
            var cases = new[]
            {
                new { Source = DefaultMusicSource.CustomFolder, Folder = (string)null, Profile = QuickStartProfiles.HoverPreviewFullscreen, Expected = DefaultMusicSource.BundledPreset },
                new { Source = DefaultMusicSource.CustomFolder, Folder = @"D:\Music\Ambient", Profile = QuickStartProfiles.SelectToPlayFullscreen, Expected = DefaultMusicSource.CustomFolder },
                new { Source = DefaultMusicSource.RandomGame, Folder = (string)null, Profile = QuickStartProfiles.HoverPreviewDesktop, Expected = DefaultMusicSource.RandomGame },
                new { Source = DefaultMusicSource.Spotify, Folder = (string)null, Profile = QuickStartProfiles.HoverPreviewDesktop, Expected = DefaultMusicSource.Spotify },
                new { Source = DefaultMusicSource.ActiveThemeMusic, Folder = (string)null, Profile = QuickStartProfiles.HoverPreviewDesktop, Expected = DefaultMusicSource.ActiveThemeMusic },
            };
            Assert.Multiple(() =>
            {
                foreach (var c in cases)
                {
                    var s = new UniPlaySongSettings { DefaultMusicSourceOption = c.Source };
                    if (c.Folder != null) s.DefaultMusicFolderPath = c.Folder;

                    new QuickStartService().Apply(s, P(c.Profile), JukeboxSource.Library, false, false, false);

                    Assert.AreEqual(c.Expected, s.DefaultMusicSourceOption, $"{c.Source} (folder: {c.Folder ?? "none"})");
                    if (c.Folder != null) Assert.AreEqual(c.Folder, s.DefaultMusicFolderPath, "the configured folder survives");
                }
            });
        }

        // In the radio branch, MusicOnlyForInstalledGames makes radio YIELD to installed games with
        // music — which would break the one thing Jukebox promises. Drift detection must mirror that rule.
        [Test]
        public void Jukebox_IgnoresInstalledOnly_AndDoesNotReadAsModifiedForIt()
        {
            foreach (var id in new[] { QuickStartProfiles.JukeboxFullscreen, QuickStartProfiles.JukeboxDesktop })
            {
                var s = new UniPlaySongSettings();
                var svc = new QuickStartService();
                svc.Apply(s, P(id), JukeboxSource.Library, true, true, false);

                Assert.IsFalse(s.MusicOnlyForInstalledGames,
                    $"{id}: installed-only makes radio yield to installed games, interrupting the mix");
                Assert.IsTrue(s.RadioModeEnabled, id);
                Assert.IsFalse(svc.IsModified(s, JukeboxSource.Library, true, true, false),
                    $"{id}: drift detection must mirror the apply rule or Jukebox always looks modified");
            }
        }

        // "Add reverb" composes with every tile, in both modes, rather than being tiles of its own.
        [Test]
        public void Apply_AddReverb_EnablesLiveEffectsWithHuddiniRehearsal()
        {
            foreach (var id in new[] { QuickStartProfiles.HoverPreviewFullscreen, QuickStartProfiles.AmbientDesktop })
            {
                var s = new UniPlaySongSettings();
                new QuickStartService().Apply(s, P(id), JukeboxSource.Library, false, false, true);
                Assert.IsTrue(s.LiveEffectsEnabled, id);
                Assert.AreEqual(StylePreset.HuddiniRehearsal, s.SelectedStylePreset, id);
            }
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

        // After a reset the baseline is gone, so the next apply captures where the user is NOW
        // rather than dragging them back to a state they deliberately moved on from. Undo goes too,
        // so it cannot reapply a forgotten profile.
        [Test]
        public void RestoreOriginal_ClearsTheBaselineAndUndoSoTheNextApplyStartsFresh()
        {
            _svc.Apply(_s, P(QuickStartProfiles.HoverPreviewFullscreen), JukeboxSource.Library, false, false, false);
            _svc.RestoreOriginal(_s);
            Assert.IsFalse(_svc.HasBaseline(_s));
            Assert.IsFalse(_svc.CanUndo);

            _s.FadeInDuration = 3.0; // a new deliberate choice
            _svc.Apply(_s, P(QuickStartProfiles.JukeboxFullscreen), JukeboxSource.Library, false, false, false);
            _svc.RestoreOriginal(_s);

            Assert.AreEqual(3.0, _s.FadeInDuration, 0.001, "back to the newer choice, not the original one");
        }

        [Test]
        public void BeforeAnyProfile_NothingToRestoreAndNothingModified()
        {
            Assert.IsFalse(_svc.HasBaseline(_s));
            Assert.IsFalse(_svc.RestoreOriginal(_s));
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
