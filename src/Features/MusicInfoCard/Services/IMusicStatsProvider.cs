using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK.Models;
using UniPlaySong.Features.MusicInfoCard.Models;

namespace UniPlaySong.Features.MusicInfoCard.Services
{
    // Computes a Game's music statistics. The async signature is the
    // testable boundary — dialog views depend on this interface, not the
    // concrete service, so a mock provider can drive the UI in tests
    // without touching disk or TagLib.
    public interface IMusicStatsProvider
    {
        // Builds a MusicStats snapshot for the given game. The result is
        // a fresh object each call — no caching at this layer (the
        // dialog only opens on user action, so the read is rare).
        //
        // The implementation must:
        //   - Read TagLib metadata for standard audio files
        //   - Open chiptune files (.hes, .vgm, .nsf, etc.) via GmeReader
        //     to get duration when TagLib can't
        //   - Expand .hes files with sibling M3U sidecars into per-track
        //     entries for longest/shortest computation
        //   - Catch and count exceptions per file (UnreadableCount)
        //   - Honor the cancellation token between files
        Task<MusicStats> ComputeAsync(Game game, CancellationToken cancellationToken);
    }
}
