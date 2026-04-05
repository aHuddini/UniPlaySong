# UniPlaySong11

Game music preview extension for Playnite 11. Select a game, hear its music.

## How It Works

Drop audio files into a game's music folder. When you click that game in your library, the music plays with a smooth fade-in. Switch to another game and it crossfades to that game's music.

**Supported formats:** MP3, WAV, OGG, FLAC

## Getting Started

1. Right-click any game > **Open Music Folder**
2. Drop audio files into the folder that opens
3. Click the game in your library — music plays

## Features

- **Game music preview** — music plays automatically when browsing your library
- **Smooth crossfading** — configurable fade-in/out between songs and game switches
- **Auto-pause on game launch** — music fades out when you start a game, resumes when you're back
- **Default music fallback** — set a background track for games that don't have their own music
- **Radio mode** — shuffles songs from all your games as a continuous playlist
- **Skip to next** — advance tracks from the app menu
- **Per-game music folders** — each game gets its own folder, managed via right-click menu
- **Music info** — right-click a game to see how many songs it has

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Enable Music | On | Master toggle |
| Music Volume | 75% | Playback volume |
| Fade In Duration | 1500 ms | How long music takes to fade in |
| Fade Out Duration | 1500 ms | How long music takes to fade out |
| Enable Default Music | Off | Play a fallback track when game has no music |
| Default Music Path | — | Path to the fallback audio file |
| Radio Mode | Off | Shuffle all game music as a continuous playlist |

## Menu Items

**App menu:**
- Skip to Next Song
- Open Music Root Folder

**Game right-click menu:**
- Open Music Folder — creates and opens the game's music folder
- Music Info — shows song count for the game

## Technical Details

- Built for Playnite 11 (.NET 10)
- NAudio 2.3.0 persistent mixer — zero-latency song switching
- Per-sample volume ramping — artifact-free fading
- Cached file discovery with automatic invalidation
- Fisher-Yates shuffle for radio mode (no repeats until full cycle)

## Requirements

- Playnite 11 (Alpha 1+)
- Windows 10/11
