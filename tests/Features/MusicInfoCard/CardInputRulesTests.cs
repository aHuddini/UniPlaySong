using NUnit.Framework;
using Playnite.SDK.Events;
using UniPlaySong.Features.MusicInfoCard.Player;

namespace UniPlaySong.Tests.Features.MusicInfoCard
{
    // The Fullscreen Music Info Card's controller rules: D-pad (or left stick) up/down moves through the songs, left/right
    // along the controls; whichever moved last is live and A acts on it. LB/RB switch pages, LT/RT seek, B closes.
    [TestFixture]
    public class CardInputRulesTests
    {
        [Test]
        public void StartsOnPlayer_SongsLive_ControlParkedOnPlayPause()
        {
            var r = new CardInputRules(songCount: 5, startSong: 2);
            Assert.AreEqual(CardPage.Player, r.Page);
            Assert.AreEqual(CardCursor.Songs, r.Live);
            Assert.AreEqual(2, r.SongIndex);
            Assert.AreEqual(CardControl.PlayPause, r.Control);
        }

        [Test]
        public void UpDown_MoveSongs_AndMakeThemLive_ClampedAtEnds()
        {
            var r = new CardInputRules(3, 0);
            r.Press(ControllerInput.DPadRight);
            Assert.AreEqual(CardCommandKind.Redraw, r.Press(ControllerInput.DPadDown).Kind);
            Assert.AreEqual(CardCursor.Songs, r.Live);
            Assert.AreEqual(1, r.SongIndex);
            r.Press(ControllerInput.LeftStickDown);
            r.Press(ControllerInput.DPadDown);
            Assert.AreEqual(2, r.SongIndex);
            r.Press(ControllerInput.DPadUp);
            r.Press(ControllerInput.DPadUp);
            r.Press(ControllerInput.DPadUp);
            Assert.AreEqual(0, r.SongIndex);
        }

        [Test]
        public void LeftRight_MoveControls_AndMakeThemLive_NoWrap()
        {
            var r = new CardInputRules(3, 0);
            r.Press(ControllerInput.DPadRight);
            Assert.AreEqual(CardCursor.Controls, r.Live);
            Assert.AreEqual(CardControl.Next, r.Control);
            r.Press(ControllerInput.LeftStickRight);
            r.Press(ControllerInput.DPadRight);
            r.Press(ControllerInput.DPadRight);
            Assert.AreEqual(CardControl.LiveEffects, r.Control);
            for (int i = 0; i < 6; i++) r.Press(ControllerInput.DPadLeft);
            Assert.AreEqual(CardControl.Previous, r.Control);
            Assert.AreEqual(0, r.SongIndex);
        }

        [Test]
        public void A_ActsOnTheLiveCursor()
        {
            var r = new CardInputRules(4, 1);
            var c = r.Press(ControllerInput.A);
            Assert.AreEqual(CardCommandKind.PlaySong, c.Kind);
            Assert.AreEqual(1, c.SongIndex);
            r.Press(ControllerInput.DPadLeft);
            c = r.Press(ControllerInput.A);
            Assert.AreEqual(CardCommandKind.Press, c.Kind);
            Assert.AreEqual(CardControl.Previous, c.Control);
        }

        [Test]
        public void Triggers_Seek10s_OnPlayerOnly()
        {
            var r = new CardInputRules(2, 0);
            var c = r.Press(ControllerInput.TriggerRight);
            Assert.AreEqual(CardCommandKind.Seek, c.Kind);
            Assert.AreEqual(10.0, c.SeekSeconds);
            Assert.AreEqual(-10.0, r.Press(ControllerInput.TriggerLeft).SeekSeconds);
            r.Press(ControllerInput.RightShoulder);
            Assert.AreEqual(CardCommandKind.None, r.Press(ControllerInput.TriggerRight).Kind);
        }

        [Test]
        public void Shoulders_SwitchPages_DetailsIgnoresDpadAndA()
        {
            var r = new CardInputRules(2, 0);
            Assert.AreEqual(CardCommandKind.Redraw, r.Press(ControllerInput.RightShoulder).Kind);
            Assert.AreEqual(CardPage.Details, r.Page);
            Assert.AreEqual(CardCommandKind.None, r.Press(ControllerInput.DPadDown).Kind);
            Assert.AreEqual(CardCommandKind.None, r.Press(ControllerInput.A).Kind);
            r.Press(ControllerInput.LeftShoulder);
            Assert.AreEqual(CardPage.Player, r.Page);
            Assert.AreEqual(0, r.SongIndex);
        }

        [Test]
        public void B_AndBack_Close_OnEitherPage()
        {
            var r = new CardInputRules(1, 0);
            Assert.AreEqual(CardCommandKind.Close, r.Press(ControllerInput.B).Kind);
            r.Press(ControllerInput.RightShoulder);
            Assert.AreEqual(CardCommandKind.Close, r.Press(ControllerInput.Back).Kind);
        }

        [Test]
        public void RightStickJogs_OnlyWhilePlayPauseIsLive_OnPlayer()
        {
            var r = new CardInputRules(3, 0);
            Assert.IsFalse(r.RightStickJogs);
            r.Press(ControllerInput.DPadRight);
            r.Press(ControllerInput.DPadLeft);
            Assert.IsTrue(r.RightStickJogs);
            r.Press(ControllerInput.DPadDown);
            Assert.IsFalse(r.RightStickJogs);
            r.Press(ControllerInput.DPadRight);
            r.Press(ControllerInput.DPadLeft);
            r.Press(ControllerInput.RightShoulder);
            Assert.IsFalse(r.RightStickJogs);
        }

        // Mouse: a click puts the matching cursor on what was clicked and makes it live, as a D-pad move would
        [Test]
        public void Select_PutsTheCursorThere_AndMakesItLive()
        {
            var r = new CardInputRules(5, 0);
            r.SelectControl(CardControl.CalmDown);
            Assert.AreEqual(CardCursor.Controls, r.Live);
            Assert.AreEqual(CardControl.CalmDown, r.Control);
            r.SelectSong(3);
            Assert.AreEqual(CardCursor.Songs, r.Live);
            Assert.AreEqual(3, r.SongIndex);
            r.SelectSong(99);
            Assert.AreEqual(4, r.SongIndex);   // clamped
            r.SelectPage(CardPage.Details);
            Assert.AreEqual(CardPage.Details, r.Page);
        }

        [Test]
        public void SelectSong_WithNoSongs_ChangesNothing()
        {
            var r = new CardInputRules(0, 0);
            r.SelectControl(CardControl.Next);
            r.SelectSong(2);
            Assert.AreEqual(CardCursor.Controls, r.Live);
            Assert.AreEqual(0, r.SongIndex);
        }

        [Test]
        public void NoSongs_SongCursorStays_AStillPressesControls()
        {
            var r = new CardInputRules(0, 0);
            Assert.AreEqual(CardCommandKind.None, r.Press(ControllerInput.A).Kind);
            Assert.AreEqual(CardCommandKind.None, r.Press(ControllerInput.DPadDown).Kind);
            r.Press(ControllerInput.DPadRight);
            Assert.AreEqual(CardCommandKind.Press, r.Press(ControllerInput.A).Kind);
        }
    }
}
