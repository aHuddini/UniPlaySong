// The memory UniPlaySong and its visualizer renderer share. Fixed-width fields only: the renderer is x64, UniPlaySong
// runs in Playnite's x86 process, and both must agree byte for byte. The C# side mirrors these offsets in
// src/Controls/MusicVisualControl.cs (frames) and src/Audio/VisualizerAudioFeed.cs (audio).
//
// Names are per running renderer, built from the channel name UniPlaySong passes on the command line:
//   <channel>_Frames  frames, created by the renderer
//   <channel>_Tick    auto-reset event: UniPlaySong signals once per displayed frame, the renderer draws one frame
//   <channel>_Audio   audio, created by UniPlaySong before it starts the renderer
#pragma once
#include <stdint.h>

// ---- frames: renderer -> UniPlaySong -------------------------------------------------------------------------
#define VIS_FRAMES_MAGIC 0x31585456u /* "VTX1" */
#define VIS_BUFFERS 3

#pragma pack(push, 8)
typedef struct VisFrames
{
    uint32_t magic;
    uint32_t width;
    uint32_t height;
    uint32_t buffers;
    // DXGI legacy shared handles of the three GPU textures. Global kernel names, not process handles, so the x86
    // side can open what the x64 side shared; the value fits 32 bits either way.
    uint64_t gpuHandles[VIS_BUFFERS];
    // Written after a buffer is complete: the index, then the frame counter (the reader reads the counter first).
    volatile int32_t latest;
    // The Jukebox Fountain, each frame: 0 reacting live, 1 the song is being analysed, 2 following the analysed song.
    volatile int32_t songState;
    volatile int64_t frame;
    // QueryPerformanceCounter when each buffer was finished (system-wide clock, for measuring delay).
    volatile int64_t producedQpc[VIS_BUFFERS];
    volatile int32_t stop; // UniPlaySong sets 1 to end the renderer
    int32_t pad1;
} VisFrames;
#pragma pack(pop)

// CPU staging for the copy path (GL readback -> D3D11 upload): VIS_BUFFERS premultiplied BGRA frames from here.
#define VIS_CPU_OFFSET 4096

// ---- audio: UniPlaySong -> renderer --------------------------------------------------------------------------
// A ring of the music UniPlaySong is playing, as stereo float, indexed by the player's own frame count so "the frame
// being heard now" (played) and "the newest frame written" (written) are in the same units. The output queue holds
// ~0.35 s, so written runs ahead of played; the renderer feeds projectM up to played, keeping the visuals on the
// sound rather than on audio that hasn't been heard yet.
#define VIS_AUDIO_MAGIC 0x31445541u    /* "AUD1" */
#define VIS_AUDIO_CAPACITY 65536u      /* stereo frames, a power of two: ~1.4 s at 44.1/48 kHz */
#define VIS_AUDIO_DATA_OFFSET 32

#pragma pack(push, 4)
typedef struct VisAudio
{
    uint32_t magic;
    uint32_t sampleRate;
    uint32_t capacity;       // VIS_AUDIO_CAPACITY
    uint32_t pad0;
    volatile uint32_t written; // frames written in total (wraps); UniPlaySong's audio thread
    volatile uint32_t played;  // the frame being heard now (wraps); UniPlaySong's UI thread, once per displayed frame
    uint32_t pad1[2];
    // capacity interleaved stereo float frames at VIS_AUDIO_DATA_OFFSET: frame n lives at n % capacity
} VisAudio;
#pragma pack(pop)

#define VIS_AUDIO_BYTES (VIS_AUDIO_DATA_OFFSET + VIS_AUDIO_CAPACITY * 2 * sizeof(float))

// ---- the song: UniPlaySong -> renderer, after the audio in the same mapping ---------------------------------------
// For the Jukebox Fountain (fountain.h), which follows the song's beats and sections. Two parts:
//   stamps: the audio thread stamps each block it hands over with where the song was when the block ended (the
//     reader's position, so seeks, pauses, crossfades and the slowed-down effect all stay right); the renderer reads
//     the song's time at `played` from the stamps around it.
//   request: the song the analysis is for, where its analysis file is (or goes), and - when that file isn't there
//     yet - the song as mono 48 kHz float for the renderer to analyse into it (analysis.h). A seqlock: odd while
//     UniPlaySong writes it, so the renderer never takes half of one.
#define VIS_SONG_MAGIC 0x31474E53u     /* "SNG1" */
#define VIS_STAMPS 64                  /* ~10 s of 150 ms blocks */
#define VIS_PATH_CHARS 260
#define VIS_ANALYSIS_VERSION 2         /* analysis.h's file format and results; a change re-analyses every song */

#pragma pack(push, 8)
typedef struct VisStamp
{
    uint32_t frame;   // the block ended at this frame (the audio ring's count, as `played`)
    uint32_t song;    // which song: an id per song the player opens, 0 = not a song file (no analysis)
    double seconds;   // where the song was then, < 0 = unknown
} VisStamp;
typedef struct VisSong
{
    uint32_t magic;
    volatile uint32_t stamps;          // stamps written in total; stamp n at n % VIS_STAMPS (the audio thread)
    volatile uint32_t seq;             // the request's version: odd while UniPlaySong writes it (the UI thread)
    uint32_t song;                     // the song the request is for (0 = none)
    int64_t sourceBytes, sourceTime;   // the song file's size and last write (FILETIME), recorded in its analysis
    VisStamp stamp[VIS_STAMPS];
    uint16_t analysis[VIS_PATH_CHARS]; // the analysis file, UTF-16, 0-terminated
    uint16_t pcm[VIS_PATH_CHARS];      // the song to analyse into it (mono 48 kHz float), empty when the file is ready
} VisSong;
#pragma pack(pop)

#define VIS_SONG_OFFSET VIS_AUDIO_BYTES
#define VIS_AUDIO_MAPPING (VIS_SONG_OFFSET + sizeof(VisSong))
