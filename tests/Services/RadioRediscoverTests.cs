using System;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // "Favour games you haven't played in a while": never played, or not played for six months or more
    [TestFixture]
    public class RadioRediscoverTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 28);

        [TestCase(null, true, TestName = "NeverPlayed_IsFavoured")]
        [TestCase(7, true, TestName = "SevenMonthsAgo_IsFavoured")]
        [TestCase(5, false, TestName = "FiveMonthsAgo_IsNot")]
        public void IsFavoured(int? monthsAgo, bool expected)
        {
            DateTime? last = monthsAgo == null ? (DateTime?)null : Now.AddMonths(-monthsAgo.Value);
            Assert.AreEqual(expected, RadioRediscover.IsFavoured(last, Now));
        }

        [Test]
        public void Line_SaysHowLongAgo()
        {
            Assert.AreEqual("From Okami · never played", RadioRediscover.Line("Okami", null, Now));
            Assert.AreEqual("From Okami · not played in 8 months", RadioRediscover.Line("Okami", Now.AddMonths(-8), Now));
            Assert.AreEqual("From Okami · not played in 2 years", RadioRediscover.Line("Okami", Now.AddMonths(-27), Now));
            Assert.AreEqual("From Okami · not played in 1 year", RadioRediscover.Line("Okami", Now.AddMonths(-13), Now));
        }
    }
}
