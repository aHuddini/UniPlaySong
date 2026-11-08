using NUnit.Framework;
using UniPlaySong.DeskMediaControl;

namespace UniPlaySong.Tests.Services
{
    // The Desktop top panel's show/hide rule (issue #50): per-button choices under the master switch, and auto-hide
    // for controls that can't be used. Auto-hide must react to "nothing loaded", never to a pause, or Play could not be
    // pressed to resume.
    [TestFixture]
    public class TopPanelVisibilityTests
    {
        private static UniPlaySongSettings Settings(bool master = true, bool play = true, bool skip = true, bool calm = false,
                                                    bool autoHide = false, bool spectrum = true, bool peak = false,
                                                    bool progress = false, bool nowPlaying = true, bool forDefault = false) =>
            new UniPlaySongSettings
            {
                ShowDesktopMediaControls = master, ShowPlayPauseButton = play, ShowSkipButton = skip,
                ShowCalmDownButton = calm, AutoHideMediaControls = autoHide, ShowSpectrumVisualizer = spectrum,
                ShowPeakMeter = peak, ShowProgressBar = progress, ShowNowPlayingInTopPanel = nowPlaying,
                AutoHideForDefaultMusic = forDefault, AutoHidePlayPause = true, AutoHideCalmDown = true,
            };

        private static TopPanelMediaControlViewModel.PanelVisibility Decide(UniPlaySongSettings s, bool loaded = true,
                                                                           bool canSkip = true, bool hideNowPlaying = false,
                                                                           bool defaultMusic = false) =>
            TopPanelMediaControlViewModel.Decide(s, loaded, canSkip, hideNowPlaying, defaultMusic);

        [Test]
        public void ShowsWhatIsChosenAndHidesWhatCannotBeUsed()
        {
            Assert.Multiple(() =>
            {
                var v = Decide(Settings());
                Assert.IsTrue(v.PlayPause && v.Skip && v.Spectrum && v.NowPlaying, "defaults: buttons, spectrum, ticker");
                Assert.IsFalse(v.CalmDown || v.PeakMeter || v.Progress, "off by default stays off");

                v = Decide(Settings(skip: false));
                Assert.IsTrue(v.PlayPause, "buttons are chosen one by one");
                Assert.IsFalse(v.Skip, "Skip can be turned off on its own");

                v = Decide(Settings(master: false, calm: true));
                Assert.IsFalse(v.PlayPause || v.Skip || v.CalmDown, "the master switch hides every button");
                Assert.IsTrue(v.Spectrum, "the visualizers have their own switches");

                v = Decide(Settings(), canSkip: false);
                Assert.IsTrue(v.Skip, "without auto-hide, an unusable Skip stays (dimmed)");

                v = Decide(Settings(autoHide: true, peak: true, progress: true), loaded: false);
                Assert.IsFalse(v.PlayPause || v.Skip || v.Spectrum || v.PeakMeter || v.Progress,
                    "auto-hide: nothing loaded collapses buttons and visualizers");
                Assert.IsTrue(v.NowPlaying, "Now Playing keeps its own rule");

                v = Decide(Settings(autoHide: true), loaded: true, canSkip: false);
                Assert.IsTrue(v.PlayPause, "loaded (playing or paused) keeps Play reachable");
                Assert.IsFalse(v.Skip, "auto-hide: Skip goes when there is only one song");

                v = Decide(Settings(autoHide: true), defaultMusic: true);
                Assert.IsTrue(v.PlayPause && v.Spectrum, "default music counts as music unless asked otherwise");

                v = Decide(Settings(autoHide: true, forDefault: true), defaultMusic: true);
                Assert.IsFalse(v.PlayPause || v.Skip || v.Spectrum, "'also hide for default music' collapses them");

                v = Decide(Settings(forDefault: true), defaultMusic: true);
                Assert.IsTrue(v.PlayPause, "the default-music option only works with auto-hide on");

                var pick = Settings(autoHide: true, peak: true);
                pick.AutoHidePlayPause = false;
                pick.AutoHideSpectrum = false;
                v = Decide(pick, loaded: false, canSkip: false);
                Assert.IsTrue(v.PlayPause && v.Spectrum, "items left unticked under 'Hide:' stay while idle");
                Assert.IsFalse(v.Skip || v.PeakMeter, "ticked items still collapse");

                pick = Settings(autoHide: true);
                pick.AutoHideSkip = false;
                v = Decide(pick, loaded: true, canSkip: false);
                Assert.IsTrue(v.Skip, "unticking Skip also keeps it with one song");

                v = Decide(Settings(), hideNowPlaying: true);
                Assert.IsFalse(v.NowPlaying, "Now Playing hides for default music when that option asks");
            });
        }
    }
}
