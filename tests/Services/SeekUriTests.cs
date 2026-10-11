using Moq;
using NUnit.Framework;
using Playnite.SDK;
using Playnite.SDK.Events;
using UniPlaySong.Services;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Tests.Services
{
    // playnite://uniplaysong/seek/{seconds} and /seekto/{percent}: the theme-button route to skip within a song.
    [TestFixture]
    public class SeekUriTests
    {
        private Mock<IActiveMediaService> _media;
        private Mock<INotificationsAPI> _notifications;
        private ExternalControlService _service;

        [SetUp]
        public void SetUp()
        {
            _media = new Mock<IActiveMediaService>();
            _notifications = new Mock<INotificationsAPI>();
            var api = new Mock<IPlayniteAPI>();
            api.SetupGet(a => a.Notifications).Returns(_notifications.Object);
            _service = new ExternalControlService(new Mock<IMusicPlaybackService>().Object, _media.Object, api.Object, null,
                () => new UniPlaySongSettings());
        }

        private void Send(params string[] segments) => _service.HandleCommand(new PlayniteUriEventArgs { Arguments = segments });

        [TestCase("10", 10)]
        [TestCase("-10", -10)]
        [TestCase("+5", 5)]
        [TestCase("2.5", 2.5)]
        public void Seek_MovesBySignedSeconds(string arg, double seconds)
        {
            Send("seek", arg);
            _media.Verify(m => m.SeekBy(seconds), Times.Once);
        }

        [Test]
        public void SeekTo_JumpsToAPercentage()
        {
            Send("seekto", "50");
            _media.Verify(m => m.SeekTo(50), Times.Once);
        }

        [Test]
        public void Seek_WithoutAValidNumber_NotifiesAndDoesNothing()
        {
            Send("seek");
            Send("seek", "ten");
            Send("seekto");
            _media.Verify(m => m.SeekBy(It.IsAny<double>()), Times.Never);
            _media.Verify(m => m.SeekTo(It.IsAny<double>()), Times.Never);
            _notifications.Verify(n => n.Add(It.IsAny<NotificationMessage>()), Times.Exactly(3));
        }
    }
}
