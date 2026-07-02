using System;
using Moq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Controls;
using UniPlaySong.Models;
using UniPlaySong.Services;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Tests.Controls
{
    [TestFixture]
    public class ActiveMediaViewModelTests
    {
        private Mock<IActiveMediaService> _service;
        private FakeSettingsProvider _provider;
        private UniPlaySongSettings _settings;
        private ActiveMediaViewModel _vm;

        [SetUp]
        public void SetUp()
        {
            _service = new Mock<IActiveMediaService>();
            _service.Setup(s => s.GetSnapshot()).Returns(ActiveMediaSnapshot.Empty);
            _settings = new UniPlaySongSettings();
            _provider = new FakeSettingsProvider(_settings);
            _vm = new ActiveMediaViewModel(_provider, _service.Object);
        }

        [Test]
        public void PlayPauseCommand_InvokesService()
        {
            _vm.PlayPauseCommand.Execute(null);
            _service.Verify(s => s.PlayPause(), Times.Once);
        }

        [Test]
        public void NextCommand_InvokesService()
        {
            _vm.NextCommand.Execute(null);
            _service.Verify(s => s.Next(), Times.Once);
        }

        [Test]
        public void PreviousCommand_InvokesService()
        {
            _vm.PreviousCommand.Execute(null);
            _service.Verify(s => s.Previous(), Times.Once);
        }

        [Test]
        public void Snapshot_Spotify_MirrorsIntoSettingsAndProps()
        {
            var snap = new ActiveMediaSnapshot(
                true, ActiveMediaSourceKind.Spotify, "Spotify", true, false,
                50.0, "1:00", "2:00", 0.0, true, true);
            _service.Setup(s => s.GetSnapshot()).Returns(snap);

            _vm.Attach();

            // Simulate a service change.
            _service.Raise(s => s.Changed += null);

            Assert.IsTrue(_vm.HasActiveMedia);
            Assert.AreEqual(ActiveMediaSourceKind.Spotify, _vm.SourceKind);
            Assert.AreEqual(50.0, _vm.Progress);
            Assert.IsTrue(_vm.IsPlaying);
            // {PluginSettings} mirror updated too:
            Assert.IsTrue(_settings.ActiveMediaHasMedia);
            Assert.AreEqual(ActiveMediaSourceKind.Spotify, _settings.ActiveMediaSourceKind);
            Assert.AreEqual(50.0, _settings.ActiveMediaProgress);
        }

        [Test]
        public void VolumeSetter_TwoWay_CallsServiceSetVolume()
        {
            _vm.Volume = 65.0;
            _service.Verify(s => s.SetVolume(65.0), Times.Once);
        }

        // Regression: OnServiceChanged now marshals MirrorToSettings (+ the raises)
        // through OnUi via Dispatcher.BeginInvoke instead of running them inline on
        // whatever thread raised Changed (e.g. a non-UI SMTC/WinRT callback thread).
        // Application.Current is null in the test host, so OnUi falls through to its
        // inline branch — this locks in that the marshalled path still performs the
        // full settings mirror synchronously in that fallback case.
        [Test]
        public void Changed_MirrorsSettings_EvenWhenMarshalled()
        {
            var snap = new ActiveMediaSnapshot(
                true, ActiveMediaSourceKind.Spotify, "Spotify", true, false,
                42.0, "0:42", "3:15", 77.0, true, false);
            _service.Setup(s => s.GetSnapshot()).Returns(snap);

            _vm.Attach();
            _service.Raise(s => s.Changed += null);

            Assert.AreEqual(42.0, _settings.ActiveMediaProgress);
            Assert.AreEqual("0:42", _settings.ActiveMediaPositionText);
            Assert.AreEqual("3:15", _settings.ActiveMediaDurationText);
            Assert.AreEqual(77.0, _settings.ActiveMediaVolume);
            Assert.IsTrue(_settings.ActiveMediaIsPlaying);
            Assert.AreEqual("Spotify", _settings.ActiveMediaSourceName);
            Assert.AreEqual(ActiveMediaSourceKind.Spotify, _settings.ActiveMediaSourceKind);
            Assert.IsTrue(_settings.ActiveMediaHasMedia);
            Assert.IsTrue(_settings.ActiveMediaCanNext);
            Assert.IsFalse(_settings.ActiveMediaCanPrevious);
        }

        // Regression: a settings SAVE replaces the whole settings object. The VM must move its
        // metadata subscription onto the new object and read from it (not the stale original) —
        // otherwise elements freeze on old now-playing data until a Playnite restart.
        [Test]
        public void SettingsReplaced_RepointsToNewObject_AndRepaintsMetadata()
        {
            _settings.NowPlayingTitle = "OldTrack";
            _vm.Attach();
            Assert.AreEqual("OldTrack", _vm.Title);

            // Swap in a brand-new settings object (what UpdateSettings does on save).
            var newSettings = new UniPlaySongSettings { NowPlayingTitle = "NewTrack" };
            _provider.Replace(newSettings);

            // VM now reads the new object...
            Assert.AreEqual("NewTrack", _vm.Title);

            // ...and a PropertyChanged on the NEW object reaches the VM (handler moved).
            string raised = null;
            _vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(_vm.Title)) raised = "hit"; };
            newSettings.NowPlayingTitle = "Newer";
            Assert.AreEqual("hit", raised);
            Assert.AreEqual("Newer", _vm.Title);

            // ...while the OLD object no longer drives the VM.
            raised = null;
            _settings.NowPlayingTitle = "StaleUpdate";
            Assert.IsNull(raised, "old settings object must be detached after swap");
        }
    }

    // Minimal ISettingsProvider fake: swappable Current + raisable SettingsChanged, no Playnite API.
    internal class FakeSettingsProvider : ISettingsProvider
    {
        public UniPlaySongSettings Current { get; private set; }
        public event EventHandler<SettingsChangedEventArgs> SettingsChanged;

        public FakeSettingsProvider(UniPlaySongSettings initial) => Current = initial;

        public void Replace(UniPlaySongSettings next)
        {
            var old = Current;
            Current = next;
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(old, next, "test"));
        }
    }
}
