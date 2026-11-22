using System;
using System.Collections.Generic;

namespace UniPlaySong.Services
{
    // Radio Mode's next songs, picked ahead so themes can show them and "Play Next in Radio" can jump the line. Every
    // radio pick (song end, Next, crossfade) takes the head; the rest is topped back up from the pool.
    public class RadioUpNextQueue
    {
        public const int Size = 5;

        private readonly List<string> _paths = new List<string>();
        private readonly Func<IList<string>, string, string> _pick;   // a random pool song other than the one given
        private readonly Func<string, bool> _exists;

        public RadioUpNextQueue(Func<IList<string>, string, string> pick, Func<string, bool> exists)
        {
            _pick = pick;
            _exists = exists;
        }

        public IReadOnlyList<string> Paths => _paths;

        // The song to play now. Queued songs whose file is gone are dropped. Returns null when the queue is empty
        // and the pool has nothing.
        // ponytail: a pick stays queued after its game leaves the pool (Custom Rotation edit); it plays once, then the
        // pool takes over. Check pool membership here if that ever matters.
        public string Take(IList<string> pool, string avoid)
        {
            _paths.RemoveAll(p => !_exists(p));
            if (_paths.Count == 0) Fill(pool, avoid);
            if (_paths.Count == 0) return null;

            var next = _paths[0];
            _paths.RemoveAt(0);
            Fill(pool, next);
            return next;
        }

        public void PlayNext(string path) => _paths.Insert(0, path);

        public bool Remove(string path) => _paths.Remove(path);

        public void Clear() => _paths.Clear();

        private void Fill(IList<string> pool, string avoid)
        {
            if (pool == null || pool.Count == 0) return;
            while (_paths.Count < Size)
                _paths.Add(_pick(pool, _paths.Count > 0 ? _paths[_paths.Count - 1] : avoid));
        }
    }
}
