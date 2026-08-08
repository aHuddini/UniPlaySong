using System.Linq;
using NUnit.Framework;
using UniPlaySong;
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
        public void Catalogue_HasThreeTilesPerMode()
        {
            Assert.AreEqual(3, QuickStartProfiles.For(QuickStartMode.Fullscreen).Count());
            Assert.AreEqual(3, QuickStartProfiles.For(QuickStartMode.Desktop).Count());
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

        // Ambient means the bed never breaks. EnableMusic=false + EnableDefaultMusic=true is the
        // engine's supported "default music only" path.
        [Test]
        public void Ambient_PlaysDefaultMusicWithoutGameMusicInterrupting()
        {
            _svc.Apply(_s, P(QuickStartProfiles.AmbientDesktop), JukeboxSource.Library, false, false, false);

            Assert.IsFalse(_s.EnableMusic, "game music must not take over the bed");
            Assert.IsTrue(_s.EnableDefaultMusic);
            Assert.IsTrue(_s.DefaultMusicContinueSameSong, "the bed must not restart");
            Assert.IsFalse(_s.RadioModeEnabled);
        }

        // Every other tile promises game music, so each must undo Ambient's suppression.
        [Test]
        public void SwitchingAwayFromAmbient_RestoresGameMusic()
        {
            _svc.Apply(_s, P(QuickStartProfiles.AmbientDesktop), JukeboxSource.Library, false, false, false);
            Assert.IsFalse(_s.EnableMusic);

            foreach (var id in new[] { QuickStartProfiles.HoverPreviewDesktop, QuickStartProfiles.HoverPreviewFullscreen,
                                       QuickStartProfiles.SelectToPlayFullscreen, QuickStartProfiles.JukeboxDesktop })
            {
                var s = new UniPlaySongSettings { EnableMusic = false };
                new QuickStartService().Apply(s, P(id), JukeboxSource.Library, false, false, false);
                Assert.IsTrue(s.EnableMusic, $"{id} must turn game music back on");
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
