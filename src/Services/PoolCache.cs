using System;
using System.Collections.Generic;

namespace UniPlaySong.Services
{
    // One song pool kept between radio picks, so a pick doesn't walk the whole library again. The key names what the
    // pool was built from; a different key, or Invalidate, rebuilds it. Invalidate can come from any thread (a download
    // finishing); one that lands while a pool is being built stops that build from being kept.
    internal sealed class PoolCache
    {
        private readonly object _lock = new object();
        private string _key;
        private List<string> _pool;
        private int _generation;

        // The pool is shared: callers read it, never change it
        public List<string> Get(string key, Func<List<string>> build)
        {
            int generation;
            lock (_lock)
            {
                if (_pool != null && _key == key) return _pool;
                generation = _generation;
            }
            var pool = build();
            lock (_lock)
            {
                if (generation == _generation) { _key = key; _pool = pool; }
            }
            return pool;
        }

        public void Invalidate()
        {
            lock (_lock) { _pool = null; _generation++; }
        }
    }
}
