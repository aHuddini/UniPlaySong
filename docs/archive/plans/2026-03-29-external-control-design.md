# External Control via URI Handler — Design

**Date:** 2026-03-29
**Version:** v1.3.10
**Status:** Approved

## Goal

Allow external tools (Stream Deck, AutoHotkey, PowerShell, desktop shortcuts, etc.) to control UniPlaySong playback via Playnite's `playnite://` URI protocol.

## Architecture

```
URI Event (Playnite SDK)
    │
    ▼
UniPlaySong.cs ── RegisterSource("uniplaysong", handler)
    │                 handler = (args) => _externalControlService.HandleCommand(args)
    │
    ▼
ExternalControlService
    │  - Parses args.Arguments[0] as command name
    │  - Parses additional args (e.g. volume value)
    │  - Validates input
    │  - On error → PlayniteApi.Notifications.Add(...)
    │
    ▼
IMusicPlaybackService
    - Resume(), Pause(), SkipToNextSong(), etc.
    - No awareness of URI system
```

## Command Set (v1)

| URI | Action | Notes |
|-----|--------|-------|
| `playnite://uniplaysong/play` | Resume playback | No-op if already playing |
| `playnite://uniplaysong/pause` | Pause playback | No-op if already paused |
| `playnite://uniplaysong/playpausetoggle` | Toggle play/pause | Checks `IsPlaying` to decide |
| `playnite://uniplaysong/skip` | Skip to next song | Calls `SkipToNextSong()` |
| `playnite://uniplaysong/restart` | Restart current song | Calls `RestartCurrentSong()` |
| `playnite://uniplaysong/stop` | Stop playback entirely | Calls `Stop()` |
| `playnite://uniplaysong/volume/{0-100}` | Set volume to value | Validates range |

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Unknown command (`/foo`) | Notification: "UniPlaySong: Unknown command "foo"" |
| Volume missing value (`/volume`) | Notification: "UniPlaySong: Volume requires a value (0-100)" |
| Volume non-numeric (`/volume/abc`) | Notification: "UniPlaySong: Invalid volume value "abc"" |
| Volume out of range (`/volume/150`) | Notification: "UniPlaySong: Volume must be between 0 and 100" |
| No arguments | Notification: "UniPlaySong: No command specified" |
| Valid command, successful | Silent — no notification |

Notifications use `PlayniteApi.Notifications.Add()` with a unique ID to prevent stacking.

## Registration Lifecycle

- **Register:** `OnApplicationStarted()` → `PlayniteApi.UriHandler.RegisterSource("uniplaysong", handler)`
- **Unregister:** `Dispose()` → `PlayniteApi.UriHandler.RemoveSource("uniplaysong")`
- **Always on:** No setting gate

## Files

- **New:** `src/Services/ExternalControlService.cs`
- **Modified:** `src/UniPlaySong.cs` (register/unregister + service init)
- **Updated:** `docs/dev_docs/ARCHITECTURE.md`

## Design Decisions

- **No interface for service** — single implementation, YAGNI
- **No settings toggle** — zero config, no performance cost, localhost-only protocol
- **Absolute volume only** — `volume/{value}` sets exact level, no relative up/down
- **`playpausetoggle` naming** — explicit, avoids ambiguity with separate `play`/`pause`
- **Approach chosen:** Dedicated service class over inline handler or command pattern — clean separation without over-engineering

## Target Audience

Power users first (Stream Deck, scripting), extension developers later. Future input sources (Bridge API, named pipes) would route through the same `ExternalControlService`.
