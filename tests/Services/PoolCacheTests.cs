using System.Collections.Generic;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Radio picks reuse the pool instead of walking the library again; a new key or an invalidation rebuilds it
    [TestFixture]
    public class PoolCacheTests
    {
        [Test]
        public void SameKey_Reuses_NewKeyOrInvalidate_Rebuilds()
        {
            var cache = new PoolCache();
            int builds = 0;
            List<string> Build() { builds++; return new List<string> { "a.mp3" }; }

            cache.Get("FullLibrary", Build);
            cache.Get("FullLibrary", Build);
            Assert.AreEqual(1, builds);

            cache.Get("CompletionStatusPool|x", Build);
            Assert.AreEqual(2, builds);

            cache.Invalidate();
            cache.Get("CompletionStatusPool|x", Build);
            Assert.AreEqual(3, builds);
        }

        // A game's music changing while the pool is being built (a download finishing) must not leave the stale pool kept
        [Test]
        public void InvalidateDuringBuild_DoesNotKeepThatBuild()
        {
            var cache = new PoolCache();
            int builds = 0;
            cache.Get("FullLibrary", () => { builds++; cache.Invalidate(); return new List<string>(); });
            cache.Get("FullLibrary", () => { builds++; return new List<string>(); });
            Assert.AreEqual(2, builds);
        }
    }
}
