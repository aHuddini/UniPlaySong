using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moq;
using NUnit.Framework;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Music status tags: Playnite only refreshes a game's tags when TagIds is reassigned, and a tag
    // deleted in Playnite must be re-created rather than its dead ID written to games.
    [TestFixture]
    public class GameMusicTagServiceTests
    {
        private string _root;
        private List<Tag> _tags;
        private Mock<IItemCollection<Game>> _games;
        private GameMusicFileService _fileService;
        private GameMusicTagService _service;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ups_tag_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            _tags = new List<Tag>();
            var tags = new Mock<IItemCollection<Tag>>();
            tags.As<IEnumerable<Tag>>().Setup(t => t.GetEnumerator()).Returns(() => _tags.ToList().GetEnumerator());
            tags.Setup(t => t.Get(It.IsAny<Guid>())).Returns<Guid>(id => _tags.FirstOrDefault(t => t.Id == id));
            tags.Setup(t => t.Add(It.IsAny<Tag>())).Callback<Tag>(t => _tags.Add(t));

            _games = new Mock<IItemCollection<Game>>();
            var db = new Mock<IGameDatabaseAPI>();
            db.SetupGet(d => d.Tags).Returns(tags.Object);
            db.SetupGet(d => d.Games).Returns(_games.Object);
            var api = new Mock<IPlayniteAPI>();
            api.SetupGet(a => a.Database).Returns(db.Object);

            _fileService = new GameMusicFileService(_root);
            _service = new GameMusicTagService(api.Object, _fileService);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private Game GameWithMusic()
        {
            var game = new Game("Test Game") { TagIds = new List<Guid>() };
            var dir = Directory.CreateDirectory(_fileService.GetGameMusicDirectory(game)).FullName;
            File.WriteAllText(Path.Combine(dir, "song.mp3"), "");
            return game;
        }

        [Test]
        public void Update_ReassignsTagIds_SoPlayniteSeesTheChange()
        {
            var game = GameWithMusic();
            var originalList = game.TagIds;
            var changed = new List<string>();
            game.PropertyChanged += (s, e) => changed.Add(e.PropertyName);

            Assert.That(_service.UpdateGameMusicTag(game), Is.True);

            Assert.That(game.TagIds, Is.Not.SameAs(originalList));
            Assert.That(changed, Does.Contain(nameof(Game.Tags)));
            var hasMusic = _tags.Single(t => t.Name == Constants.TagHasMusic);
            Assert.That(game.TagIds, Is.EquivalentTo(new[] { hasMusic.Id }));
            _games.Verify(g => g.Update(game), Times.Once);
        }

        [Test]
        public void Update_SwapsNoMusicForHasMusic_AndSkipsWhenAlreadyCorrect()
        {
            var game = new Game("Test Game") { TagIds = new List<Guid>() };
            _service.UpdateGameMusicTag(game);
            var noMusic = _tags.Single(t => t.Name == Constants.TagNoMusic);
            Assert.That(game.TagIds, Does.Contain(noMusic.Id));

            var dir = Directory.CreateDirectory(_fileService.GetGameMusicDirectory(game)).FullName;
            File.WriteAllText(Path.Combine(dir, "song.mp3"), "");
            Assert.That(_service.UpdateGameMusicTag(game), Is.True);
            Assert.That(game.TagIds, Does.Not.Contain(noMusic.Id));

            Assert.That(_service.UpdateGameMusicTag(game), Is.False);
        }

        [Test]
        public void Update_RecreatesATagDeletedInPlaynite()
        {
            var game = GameWithMusic();
            _service.UpdateGameMusicTag(game);
            var deleted = _tags.Single(t => t.Name == Constants.TagHasMusic);
            _tags.Clear();
            game.TagIds = new List<Guid>();

            _service.UpdateGameMusicTag(game);

            var recreated = _tags.Single(t => t.Name == Constants.TagHasMusic);
            Assert.That(recreated.Id, Is.Not.EqualTo(deleted.Id));
            Assert.That(game.TagIds, Is.EquivalentTo(new[] { recreated.Id }));
        }

        [Test]
        public void InvalidateCacheForGame_RaisesGameMusicChanged()
        {
            var game = new Game("Test Game");
            Game raised = null;
            _fileService.GameMusicChanged += g => raised = g;

            _fileService.InvalidateCacheForGame(game);

            Assert.That(raised, Is.SameAs(game));
        }
    }
}
