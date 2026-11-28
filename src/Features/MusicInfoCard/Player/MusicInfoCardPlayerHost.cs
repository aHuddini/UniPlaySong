using System;
using Playnite.SDK.Models;
using UniPlaySong.Models;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Features.MusicInfoCard.Player
{
    // Everything the Fullscreen player card reads or does, handed in by the plugin: the card reaches existing services only
    // through these delegates (as the Desktop hover card does through NowPlayingCardHost).
    internal sealed class MusicInfoCardPlayerHost
    {
        public Func<ActiveMediaSnapshot> Snapshot;                 // whatever plays: position, length, playing, source
        public Func<string> PlayingPath;                           // UPS's current song file; null while Spotify plays
        public Func<(string Title, string Artist)> NowPlaying;     // the published title/artist (UPS or Spotify)
        public Func<Game, string> PrimaryPath;                     // the game's primary song, or null
        public Action PlayPause, Next, Previous;
        public Action<double> SeekBy;                              // seconds, relative
        public Action<double> SeekTo;                              // percent of the song, 0-100
        public Action<Game, string> PlaySong;
        public Action<Game> LeavePlayer;                           // the card closes after playing a song: Radio Mode takes back over
        public Func<bool> CalmDownOn, LiveEffectsOn;
        public Action ToggleCalmDown, ToggleLiveEffects;
        public Func<ListeningSnapshot> Listening;
        public Func<Game, string> GameMusicFolder;
        public Func<Game, string> Rediscover;                      // "Not played in 2 years" while radio favours the game; else null
    }
}
