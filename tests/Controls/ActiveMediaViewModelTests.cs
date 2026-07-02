using Moq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Controls;
using UniPlaySong.Models;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Tests.Controls
{
    [TestFixture]
    public class ActiveMediaViewModelTests
    {
        private Mock<IActiveMediaService> _service;
        private UniPlaySongSettings _settings;
        private ActiveMediaViewModel _vm;

        [SetUp]
        public void SetUp()
        {
            _service = new Mock<IActiveMediaService>();
            _service.Setup(s => s.GetSnapshot()).Returns(ActiveMediaSnapshot.Empty);
            _settings = new UniPlaySongSettings();
            _vm = new ActiveMediaViewModel(_settings, _service.Object);
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
    }
}
