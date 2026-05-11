# UniPlaySong Feature Ideas

Comprehensive collection of potential features, ranging from basic QoL improvements to ambitious experimental concepts. Organized by category with rough effort/impact estimates.

**Legend:** Effort: `Low` (hours) / `Medium` (days) / `High` (week+) | Impact: `Low` / `Medium` / `High` | ~~Strikethrough~~ = Shipped

---

## Shipped in v1.5.0 (in development)

| Feature | Category | Version |
|---------|----------|---------|
| ~~Settings Backup tab (JSON Import/Export + Markdown Snapshot)~~ | Settings / UX | v1.5.0 |
| ~~Randomize bundled track every startup (Default Music Randomization)~~ | Playback | v1.5.0 |
| ~~Calm Down Mode (Fullscreen)~~ | Playback / Theme Integration | v1.5.0 |

## Shipped in v1.4.6

| Feature | Category | Version |
|---------|----------|---------|
| ~~PC Engine / TurboGrafx-16 (.hes) chiptune support~~ | Audio & Effects | v1.4.6 |
| ~~"Split HES Tracks" menu action~~ | Library Management | v1.4.6 |
| ~~`{PluginSettings}` quick-options framework for theme integration (validated against Aniki ReMake)~~ | Theme Integration | v1.4.6 |
| ~~Two new Bundled Ambient tracks from Mike Aniki (Hub OST, Login OST)~~ | Playback | v1.4.6 |
| ~~`Enable Game Music` + `Enable Default Music` toggles in Fullscreen Extensions menu~~ | Controller Mode / UX | v1.4.6 |
| ~~LGPL §6 paperwork for bundled GME (NOTICES.txt, source tarball pin)~~ | Documentation / Compliance | v1.4.6 |

## Shipped in v1.4.5

| Feature | Category | Version |
|---------|----------|---------|
| ~~yt-dlp version display in Settings → Downloads~~ | Settings / UX | v1.4.5 |
| ~~Fullscreen search-variant buttons (OST / Soundtrack / Music / Theme)~~ | Controller Mode / UX | v1.4.5 |
| ~~FINISH button in download dialog~~ | Downloads | v1.4.5 |
| ~~YouTube downloads ~30-50% faster (sleep-requests, single-pass MP3 encoding default)~~ | Downloads (Performance) | v1.4.5 |
| ~~Cookie mode + Deno = audio-only streams (~2x faster)~~ | Downloads (Performance) | v1.4.5 |
| ~~"Open Music Folder" no longer leaks explorer.exe handles~~ | Library Management (Fix) | v1.4.5 |
| ~~Cookie mode no longer breaks downloads for users without Deno~~ | Downloads (Critical Fix) | v1.4.5 |
| ~~Album selection no longer triggers 5 redundant network requests~~ | Downloads (Fix) | v1.4.5 |

## Shipped in v1.4.4

| Feature | Category | Version |
|---------|----------|---------|
| ~~SNES (.spc) advertised as supported chiptune format~~ | Audio & Effects | v1.4.4 |
| ~~Desktop YouTube preview fix (aggressive `%TEMP%` scanning)~~ | Downloads (Fix) | v1.4.4 |
| ~~yt-dlp preview error visibility (removed `--no-warnings --quiet`)~~ | Downloads (Fix) | v1.4.4 |

## Shipped in v1.4.3

| Feature | Category | Version |
|---------|----------|---------|
| ~~NES Music Support (.nsf)~~ | Audio & Effects | v1.4.3 |
| ~~NSF Track Manager (split master NSF → mini-NSFs)~~ | Library Management | v1.4.3 |
| ~~NSF Loop Editor (per-track loop length override via `nsf-loops.json`)~~ | Audio & Effects | v1.4.3 |
| ~~`Play Music State` Dropdown Clarity (Desktop Only / Fullscreen Only labels)~~ | Settings / UX | v1.4.3 |
| ~~yt-dlp Python DLL Failure Diagnostic~~ | Downloads | v1.4.3 |
| ~~GME `starting_song` Honored + Short-Track Auto-Advance via `gme_track_ended`~~ | Audio & Effects (Fix) | v1.4.3 |

## Shipped in v1.4.2

| Feature | Category | Version |
|---------|----------|---------|
| ~~Fullscreen Quick Settings Menu~~ | Controller Mode / UX | v1.4.2 |
| ~~Fullscreen Volume Boost Slider~~ | Audio & Effects | v1.4.2 |
| ~~Stay Paused After External Audio (Desktop)~~ | Playback / Pauses | v1.4.2 |
| ~~Persistent Default Music Backdrop (Keep-Same-Track on Game Switch default ON)~~ | Playback | v1.4.2 |
| ~~Auto-advance Default Music on Song End (toggle)~~ | Playback | v1.4.2 |
| ~~Fullscreen Exit Stray Music Fix~~ | Playback (Fix) | v1.4.2 |

## Shipped in v1.4.1

| Feature | Category | Version |
|---------|----------|---------|
| ~~Fanfare on "Beaten" Status (in addition to Completed)~~ | Gamification | v1.4.1 |
| ~~Abandoned Status Jingle + Toast (with 10 bundled failure jingles)~~ | Gamification | v1.4.1 |
| ~~GME Pause/Resume Fixes (UI freeze, silent jingle returns, race conditions)~~ | Audio & Effects (Fix) | v1.4.1 |
| ~~Song-End Fade Geometry (matches configured duration exactly)~~ | Audio & Effects (Fix) | v1.4.1 |
| ~~Single-Track Loop Silent Fix (FadeOutBeforeSongEnd + one-song folders)~~ | Audio & Effects (Fix) | v1.4.1 |
| ~~`Play Only On Game Select` Randomization Fixes~~ | Playback (Fix) | v1.4.1 |
| ~~JingleService Extraction (internal refactor)~~ | Architecture | v1.4.1 |
| ~~About Tab: Supported Audio Formats Section~~ | Visual & UI | v1.4.1 |

## Shipped in v1.4.0

| Feature | Category | Version |
|---------|----------|---------|
| ~~Retro Chiptune Music Support (.vgm / .vgz via GME)~~ | Audio & Effects | v1.4.0 |
| ~~Faster YouTube Previews (40s section downloads)~~ | Downloads | v1.4.0 |
| ~~Browser Cookie Support (Chrome / Edge / Brave / Opera alongside Firefox)~~ | Downloads | v1.4.0 |
| ~~Cookie Mode Quality + Args Consistency~~ | Downloads (Fix) | v1.4.0 |

## Shipped in v1.3.11 / v1.3.12

| Feature | Category | Version |
|---------|----------|---------|
| ~~Active Theme Music (play Fullscreen theme's `background.mp3` through UPS)~~ | Playback | v1.3.11 |
| ~~Add Music File (Desktop right-click menu + controller-friendly browser)~~ | Library Management | v1.3.11 |
| ~~Skip First Selection Fix (music plays on first game select)~~ | Playback (Fix) | v1.3.11 |
| ~~Normalization Codec Auto-detection (MP3/OGG/FLAC/WAV)~~ | Library Management (Fix) | v1.3.12 |
| ~~External Audio Pause Detection (direct WASAPI)~~ | Playback (Fix) | v1.3.12 |
| ~~Radio Mode + Theme Overlay Respect~~ | Playback (Fix) | v1.3.12 |
| ~~Silence Trim: OGG Codec Support~~ | Library Management (Fix) | v1.3.12 |
| ~~Tool Path Validation (yt-dlp/FFmpeg Found/Not Found status)~~ | Settings / UX | v1.3.12 |

## Shipped in v1.3.6 – v1.3.10

| Feature | Category | Version |
|---------|----------|---------|
| ~~Music Library Dashboard (Experimental)~~ | Dashboard & Statistics | v1.3.6 |
| ~~Dashboard Player Decoupling~~ | Architecture | v1.3.6 |
| ~~Icon Glow (Dynamic game-art driven)~~ | Visual & UI | v1.3.6 |
| ~~Hover Glow~~ | Visual & UI | v1.3.6 |
| ~~Taskbar Color Driven by Game Cover~~ | Visual & UI | v1.3.6 |
| ~~External Control API (localhost REST-ish for StreamDeck / scripts)~~ | Integration & Streaming | v1.3.7–v1.3.10 |
| ~~Bulk Audio Conversion~~ | Library Management | v1.3.7–v1.3.10 |
| ~~Play-On-Select Event-Driven Refactor~~ | Playback (Perf) | v1.3.7–v1.3.10 |

## Shipped in v1.3.5

| Feature | Category | Version |
|---------|----------|---------|
| ~~Open Music Folder — Create Prompt~~ | Library Management | v1.3.5 |
| ~~Create Music Folders for All Games (Bulk Action)~~ | Library Management | v1.3.5 |
| ~~Game Folder Breadcrumbs~~ | Library Management | v1.3.5 |
| ~~Game Index File~~ | Library Management | v1.3.5 |
| ~~Localization Infrastructure (Foundation)~~ | Social & Community | v1.3.5 |

## Shipped in v1.3.4

| Feature | Category | Version |
|---------|----------|---------|
| ~~Nostalgia Mode~~ | Playnite-Aware Playback | v1.3.4 |
| ~~Nostalgia Playlist Mode (By Completion Status Default Music)~~ | Playback | v1.3.4 |
| ~~Game Property Filter (Platform / Genre / Source)~~ | Playback | v1.3.4 |
| ~~Filter Mode (Play only when Playnite filter is active)~~ | Playnite-Aware Playback | v1.3.4 |
| ~~Radio Station Mode~~ | Playback | v1.3.4 |

## Shipped in v1.3.3

| Feature | Category | Version |
|---------|----------|---------|
| ~~Configurable Fade Curves~~ | Audio & Effects (Experimental) | v1.3.3 |

## Shipped in v1.3.2

| Feature | Category | Version |
|---------|----------|---------|
| ~~Global Media Key Control~~ | Integration & Streaming (Experimental) | v1.3.2 |
| ~~Taskbar Thumbnail Media Controls~~ | Visual & UI (Graduated) | v1.3.2 |
| ~~Auto-Cleanup Empty Folders~~ | Library Management | v1.3.2 |
| ~~Playlist Export (M3U)~~ | Library Management | v1.3.2 |
| ~~Extended Default Music Sources~~ | Playback | v1.3.2 |
| ~~PS2 Menu Ambience (Bundled Preset)~~ | Playback | v1.3.2 |
| ~~Custom Cookies File~~ | Downloads | v1.3.2 |
| ~~Per-Tab Reset Buttons~~ | Library Management | v1.3.2 |
| ~~Graduated 6 Experimental Features~~ | Settings / UI | v1.3.2 |
| ~~Improved Default Settings~~ | Settings / UI | v1.3.2 |
| ~~Settings Reorganization (Pauses tab)~~ | Settings / UI | v1.3.2 |
| ~~Style Preset Tuning~~ | Audio & Effects | v1.3.2 |
| ~~Global Settings Reset Rewrite~~ | Library Management | v1.3.2 |

## Shipped in v1.3.1

| Feature | Category | Version |
|---------|----------|---------|
| ~~Install-Aware Auto-Download~~ | Playnite-Aware Playback | v1.3.1 |
| ~~Auto-Pause on External Audio~~ | Playback (Experimental) | v1.3.1 |
| ~~Auto-Pause on Idle / AFK~~ | Playback (Experimental) | v1.3.1 |
| ~~Stay Paused on Focus Restore (#69)~~ | Playback | v1.3.1 |
| ~~Ignore Brief Focus Loss (Alt-Tab)~~ | Playback | v1.3.1 |
| ~~Enhanced Library Statistics~~ | Dashboard & Statistics (Experimental) | v1.3.1 |
| ~~Settings UI Reorganization~~ | Visual & UI | v1.3.1 |

## Shipped in v1.3.0

| Feature | Category | Version |
|---------|----------|---------|
| ~~Completion Celebration~~ | Gamification | v1.3.0 |
| ~~Download Complete Sound~~ | Gamification | v1.3.0 |
| ~~Song Count Badge in Game Menu~~ | Visual & UI | v1.3.0 |
| ~~Music Folder Size in Game Menu~~ | Visual & UI | v1.3.0 |
| ~~Default Music Indicator~~ | Visual & UI | v1.3.0 |
| ~~Celebration Toast Notification~~ | Visual & UI (Experimental) | v1.3.0 |
| ~~Auto-Pause on System Lock~~ | Playback (Experimental) | v1.3.0 |
| ~~Song Progress Indicator~~ | Playback (Experimental) | v1.3.0 |
| ~~Library Statistics Page~~ | Dashboard & Statistics (Experimental) | v1.3.0 |

---

## Dashboard & Statistics

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| ~~**Music Dashboard**~~ ✅ | ~~Central hub showing library overview: total songs, soundtracks, storage used, most-played games, recently played. Tabbed interface (Games, Tracks, Artists, Genres, Stats). Game card grid, game detail view, Radio Mode, expanded Now Playing, audio-reactive cards. Own decoupled `DashboardPlaybackService`.~~ **Shipped v1.3.6** (Experimental) | Medium | High |
| **Personal Top Charts** | "Top 10 Most Played Games", "Top 25 Songs", filterable by week/month/all-time. Requires play count tracking. Surface in dashboard. | Low | Medium |
| ~~**Library Statistics Page**~~ ✅ | ~~Detailed breakdown: file format distribution (MP3 vs FLAC %), bitrate stats, total duration, storage analysis. "50 GB of FLAC could be 12 GB as 320kbps MP3." Like foobar2000's aggregate Properties.~~ **Shipped v1.3.0** (**Enhanced v1.3.1:** avg song length, total playtime, ID3 tag count, bitrate distribution card, reducible track size card) | Low | Medium |
| **Listening Trends Graph** | Line/bar chart showing listening hours per day/week/month over time. Visual analytics similar to Last.fm or Spotify Wrapped year-round. | Medium | Medium |

---

## Playback & Listening Experience

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Gapless Playback** | Eliminate silence between tracks for continuous-mix soundtracks. Pre-buffer next track via `PreLoad()` infrastructure that already exists. Industry standard in 2026. | Low | High |
| ~~**Crossfade Between Games**~~ ✅ | ~~Overlap fade-out/fade-in when switching games instead of silence gap. PS Store and Spotify do this. Existing MusicFader infrastructure supports this.~~ **Shipped v1.4.3 — `EnableTrueCrossfade` setting + dedicated `CrossfadeCoordinator` service in `src/Services/CrossfadeCoordinator.cs` handle smooth overlap transitions between songs/games via the NAudio backend.** | Medium | High |
| **Sleep Timer** | Auto-stop music after configurable minutes. Common in Spotify/podcasts. Simple countdown calling `Stop()`. | Low | Medium |
| **Playback Queue / Up Next** | Queue specific songs across games. "Add to queue" from right-click or dashboard. Persists across game selection. | Medium | High |
| **Song Bookmarking / Favorites** | Star songs across your library. Saved as a flat JSON manifest `favorites.json` (cross-game, filename-keyed). "Favorites" appears as a new pool-based default music source alongside Custom Folder / Random Game. Users build their personal greatest-hits playlist without leaving UPS. Context menu on any song row. Like FFXIV's jukebox. | Low | High |
| **Playback History / Recently Played** | Track songs played and when. "Recently Played" list in dashboard. | Low | Medium |
| **Play Count Tracking** | Track per-song play counts. Surface "Most Played" stats. Standard in every music player. | Low | Medium |
| ~~**Radio Station Mode**~~ | ~~Continuous shuffle across entire library regardless of game selection. Like GTA radio for your collection. Transforms UPS from per-game to library-wide player.~~ | ~~Medium~~ | ~~High~~ |
| ~~**Song Progress Indicator**~~ ✅ | ~~Thin progress bar showing position within current song. Users currently have no position feedback.~~ **Shipped v1.3.0** | Low | Medium |
| **Resume From Last Position** | When returning to a game, resume song where it left off. Like podcast resume. Store per-game song + position. | Low | Medium |
| **Volume Per Game** | Different volume levels per game. Some soundtracks are naturally louder. Stored per game ID. | Low | Medium |
| **Fade Duration Per Game** | Per-game fade override. Atmospheric RPGs get long fades, action games get snappy transitions. | Low | Low |
| **Quick Mute Toggle** | One-click mute that remembers previous volume. Music keeps playing silently. Like TV mute. | Low | Low |
| **Don't Play This Song Skip List** | Blacklist songs without deleting them. "I like 9 of 10 tracks." Per-game skip list. | Low | Medium |
| **Startup Delay Option** | Configurable delay before music starts after Playnite launch. | Low | Low |
| **Non-Destructive Trim Presets** | Save start/end points per song without modifying the file. Playback respects trim points via `LoadAndPlayFileFrom()`. | Low | Medium |
| **Auto-Skip Short Files** | Skip files under a configurable duration (e.g., <5s). Catches jingles/sound effects that accidentally got downloaded. Check duration after metadata read. | Low | Medium |
| ~~**Default Music Randomization**~~ ✅ | ~~When using bundled presets, randomly pick which preset plays instead of always the same one.~~ **Shipped v1.5.0 as "Randomize bundled track every startup" — once-per-session pick (consistent across game switches), labeled checkbox in Settings → Playback → Bundled Ambient subsection.** | Low | Low |
| **Last Played Song Indicator** | When selecting a game, show which song played last time. Simple per-game dictionary in settings. Subtle text display. | Low | Low |
| **Configurable Now Playing Format** | Let users choose display format: "Title - Artist", "Artist: Title", "Title (Game)", "Title only". Dropdown in settings. | Low | Low |
| **A-B Repeat / Loop Section** | Set loop points within a song to repeat a specific section. Useful for enjoying specific parts of long tracks. Common in foobar2000, Winamp. | Low | Low |
| **Replay Current Song** | One-click "play again from start" without navigating menus. Back-skip-to-restart behavior standard in every player. Button in top panel controls. | Low | Low |
| ~~**Fade to Pause**~~ ✅ | ~~Instead of instant pause, fade out over ~0.5s then pause. Fade back in on resume. Feels polished vs jarring stop. Existing `MusicFader` handles this.~~ **Shipped — `MusicFader.Pause()` ramps volume to 0 over `FadeOutDuration`, then triggers the underlying player's `Pause()`. Fade-back-in on Resume is symmetric. Behavior has been default since pre-v1.3.x.** | Low | Medium |
| **Song Intro Skip** | Auto-skip the first N seconds of songs (configurable). Many game OST rips have long silent intros or logo jingles. Check waveform on load. | Low | Low |
| **Audio Ducking During Game Selection** | Briefly lower music volume when actively scrolling/browsing games (rapid `OnGameSelected` fires), restore when user stops on a game. Prevents audio chaos during fast browsing. Uses existing volume multiplier. | Low | Medium |
| **Playback Memory Across Sessions** | Remember which song was playing (and optionally position) when Playnite closed. Resume exact state on next launch. Store in settings: last game ID + song path + position. Like Spotify session restore. | Low | Medium |
| ~~**Auto-Pause on System Lock**~~ ✅ | ~~Pause music when user locks PC (Win+L) or screensaver activates. Resume on unlock. `Microsoft.Win32.SystemEvents.SessionSwitch` event. Add as new pause source in `_activePauseSources`.~~ **Shipped v1.3.0** | Low | Medium |
| ~~**Auto-Pause on Idle / AFK**~~ ✅ | ~~Pause music after no mouse/keyboard input for a configurable duration (e.g., 5-30 minutes). User walked away without locking. Resume on any input. Win32 `GetLastInputInfo()` P/Invoke on a polling timer — identical pattern to external audio detection. Zero new dependencies. Limitation: doesn't detect gamepad input (XInputWrapper exists for future enhancement). Covers the biggest gap in current pause options.~~ **Shipped v1.3.1** | Low | High |
| ~~**Auto-Pause on Another Audio Source**~~ ✅ | ~~Pause music when another application starts producing audio (YouTube, Discord call, Spotify). NAudio CoreAudioApi session enumeration with debounce, instant pause toggle, app exclusion list. OBS excluded by default (mirrors system audio). See issue #19.~~ **Shipped v1.3.1** | Low-Medium | High |
| **Auto-Pause on Screen Off / Display Sleep** | Pause music when monitor powers off due to inactivity (user walked away but PC not locked). Different from system lock — catches users with display timeout but no auto-lock configured. Requires WndProc hook via `HwndSource.AddHook` for `WM_POWERBROADCAST` + `GUID_MONITOR_POWER_ON`. No existing WndProc hooks in codebase. Must distinguish display-off from system sleep, handle multi-monitor edge cases. | Medium | Medium |
| ~~**Stay Paused on Focus Restore (#69)**~~ ✅ | ~~When Playnite regains focus after alt-tab, keep music paused instead of auto-resuming. User must manually press play to resume. Atomic `ConvertPauseSource(FocusLoss → Manual)` avoids audible resume blip. Sub-option under "Pause on focus loss". See [issue #69](https://github.com/aHuddini/UniPlaySong/issues/69).~~ **Shipped v1.3.1** | Low | Medium |
| ~~**Ignore Brief Focus Loss (Alt-Tab)**~~ ✅ | ~~Detects the alt-tab overlay and only pauses if you actually switch apps. Win32 `GetForegroundWindow()` + `GetClassName()` identifies `ForegroundStaging` (Windows 11 task switcher), polls until resolved. Aborted alt-tabs ignored, completed switches pause normally. Skips P/Invoke when already paused. Sub-option under "Pause on focus loss".~~ **Shipped v1.3.1** | Medium | Medium |
| **Auto-Pause on Game Audio Detected** | Pause UPS music specifically when the running game produces its own audio output. Narrower than "another audio source" — only reacts to the game process. Extends #2's NAudio session enumeration with PID matching against active game process. Challenge: game PID ≠ audio session PID for Steam/Proton/Epic launcher games. High false-positive risk across launchers. | Medium | Medium |
| **Auto-Pause on Do Not Disturb / Focus Assist** | Pause music when Windows Focus Assist (DND mode) is active. Respects the user's "leave me alone" signal for audio too. Detectable via WNF state notifications or registry query. | Low | Low |
| **Auto-Pause on Battery Saver** | Pause music when laptop switches to battery saver mode. Conserve CPU/audio resources. `System.Windows.Forms.SystemInformation.PowerStatus` or `Windows.System.Power.PowerManager`. Niche (laptop users only) but trivial. | Low | Low |
| **"Surprise Me" Button** | Pick a random game from library that has music and start playing it. Discovery mechanism — "what music do I even have?" Top panel button or menu item. Uses existing `GetAvailableSongs()` + game iteration. | Low | Medium |
| **Listening Session Recap** | After a browsing session (30+ minutes), show a small summary: "You listened to 12 songs from 5 games." Ephemeral notification using `_api.Notifications.Add`. Track via `OnSongChanged` counter. | Low | Low |

---

## Discovery & Smart Features

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Smart Shuffle** | Weight shuffle by play count (prefer less-played), recency, or rating instead of pure random. Existing shuffle infrastructure. | Low | Medium |
| **Genre/Mood Tagging** | Tag songs with moods (epic, chill, melancholic). Offer "Play all chill game music" cross-library. Like Spotify mood playlists. | Medium | Medium |
| **Context-Aware Playlists from Game Metadata** | Auto-generate playlists from Playnite game data: "90s RPG Soundtracks", "All Metroidvania music", "Games I haven't finished." Zero user effort. | Medium | High |
| **Tag-Based Auto-Playlists** | Generate playlists from existing Playnite tags ("cozy", "story-rich"). Reads existing tags, no setup needed. | Medium | Medium |
| **Soundtrack Matching for Games Without Music** | Instead of default music, play tracks from a similar game (matched by genre/platform/era). Eliminates dead zones intelligently. | Medium | High |
| **Similar Game Music Recommendations** | "If you like Game A's music, try Game B." Based on genre, composer, or audio similarity. | Medium | Medium |
| **Composer Database** | Local database mapping games to composers. "Composed by Nobuo Uematsu." Auto-populated from metadata or community JSON. Discover you have 15 games by the same composer. | Medium | Medium |
| **Mood Detection via Audio Analysis** | Analyze tempo, key, energy, spectral centroid to auto-tag moods. NAudio FFT provides raw data. Local Spotify-like audio features. | High | Medium |
| **Intelligent Auto-Download Priority** | Bulk download prioritizes most-played games (Playnite play time data), recently added, or favorited. Smart ordering. | Low | Medium |
| ~~**Multi-Source Fallback Downloads**~~ | ~~If YouTube fails, auto-try KHInsider, then Zophar. Cascading fallback with configurable priority.~~ ❌ N/A — KHInsider and Zophar removed in v1.3.4 | ~~Medium~~ | ~~Medium~~ |
| ~~**No Music Tag Auto-Apply**~~ ✅ | ~~After a failed download attempt (all sources return nothing), automatically tag the game with "No Music" tag. Tagging infrastructure already exists.~~ **Shipped — `GameMusicTagService.UpdateGameMusicTag` continuously applies `Has Music` / `No Music` tags based on whether a game's folder actually contains music files. Better than the original spec (state-driven rather than failure-event-driven).** | Low | Low |
| **Auto-Reverb by Genre** | Auto-apply reverb/effects presets based on Playnite game genre. Horror → Cathedral, Racing → Clean/Bright, RPG → Dreamy/Warm. Uses existing 18 reverb presets + `game.Genres` metadata. | Low | Medium |
| ~~**Completion-Status Music Filter**~~ | ~~Only play music from games matching a completion status: "Unfinished games only" or "Completed games only." Nostalgia mode for beaten games, motivation mode for backlog. `game.CompletionStatusId` already accessed.~~ **Shipped v1.3.4 as Nostalgia Mode + Nostalgia Playlist Mode** | Low | Medium |
| **Playtime-Weighted Shuffle** | In Radio Station mode, weight shuffle toward games with more playtime (games you care about). `game.Playtime` available but unused. Simple weighted random. | Low | Medium |
| **Platform-Specific Search Hints** | Auto-adjust download search terms by platform. N64/SNES → "OST", PS4/PS5 → "Soundtrack", Arcade → "BGM". Improves auto-download hit rate. | Low | Low |
| **Series/Franchise Playlist** | "Play all music from this franchise." Uses `game.Series` metadata. Select Zelda → plays music from all Zelda games with music. | Low | Medium |
| **Era-Based Playlists** | "Play all 90s game music." Filter by `game.ReleaseYear`. Nostalgia-by-decade mode. | Low | Medium |
| **Auto-Download for Series** | When downloading music for one game, offer to download for all games in the same series. Uses `game.Series` + batch download infrastructure. | Low | Medium |
| **Recently Added Games Priority** | In Radio mode or shuffle, weight toward recently added games (`game.Added`). "Discover your new additions." | Low | Low |

---

## Playnite-Aware Playback

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Per-Game Effects Presets** | Save live effects chain (reverb, bitcrusher, lo-fi) per game ID. Auto-apply when selecting that game. JSON dictionary in settings. "Dark Souls always gets Cathedral reverb." | Low | High |
| **Per-Game Visualizer Theme** | Save spectrum visualizer color theme per game ID. Auto-switch on game selection. Uses existing 22 color themes + Dynamic. | Low | Medium |
| **Mood Transition Bridge** | When switching from intense game (Dark Souls) to calm game (Stardew Valley), play a brief neutral transition sound to bridge the mood shift. Detect mood from genre metadata. | Medium | Low |
| **Game Launch Countdown Hype** | Instead of immediate pause on game start, build a brief crescendo/hype fade (3-5s) before pausing. Like a DJ build-up to game launch. Uses `MusicFader` + volume ramp. | Low | Low |
| **Soundtrack Score Card** | Per-game overlay showing: song count, total duration, storage, format quality, avg bitrate. Right-click menu "Music Info" option. Quick data from existing file service. | Low | Low |
| ~~**Install-Aware Auto-Download**~~ ✅ | ~~Automatically download music when a game changes to Installed status. `OnGameInstalled` event available but unused. Zero-effort library building.~~ **Shipped v1.3.1** | Low | Medium |
| **Uninstall Cleanup Prompt** | When a game is uninstalled, offer to delete its music files too. `OnGameUninstalled` event. Optional — some users keep music for uninstalled games. | Low | Low |
| **Developer/Publisher Playlist** | "Play all music from games by FromSoftware." Uses `game.Developers`/`game.Publishers`. Discover studio music patterns. | Low | Medium |
| **Category-Based Default Music** | Instead of one global default, set different default music per Playnite category. "Retro" category → chiptune default, "AAA" → orchestral. `game.Categories` metadata. | Medium | Medium |
| **Score-Based Priority** | In Radio mode, weight shuffle toward higher-rated games (`game.UserScore`/`game.CriticScore`). "Play music from my best-rated games." | Low | Low |

---

## Audio & Effects

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Equalizer** | 5-10 band EQ via NAudio BiQuadFilter. Presets: Bass Boost, Vocal, Flat. Alongside existing reverb presets. | Medium | Medium |
| **Playback Speed Control** | 0.5x-2.0x speed. "Slowed + reverb" trend. NAudio supports sample rate manipulation. | Medium | Low |
| **Cross-Game Volume Normalization** | ReplayGain-style real-time gain adjustment. Pre-scan loudness per file, adjust on playback. No volume jumps between games. Lighter than full EBU R128 normalization. | Medium | High |
| **Auto-Volume Duck on System Sounds** | Duck music volume when Windows plays notifications. Uses Windows "communications activity" audio API. | Medium | Low |
| **Ambient Sound Layers** | Layer environmental sounds (rain, fireplace, space) on top of music. NAudio stream mixing. Like Noisli meets game music. | High | Medium |
| **BPM Detection & Tempo Matching** | Detect BPM, smooth transitions between tempo-matched songs. DJ-like flow via FFT analysis. | High | Low |
| **Adaptive Bitrate Conversion** | Auto-convert FLAC/WAV to lower bitrates for storage savings. Quality preference setting. FFmpeg handles conversion. | Medium | Medium |
| ~~**"Calm Down" Mode**~~ ✅ | ~~One-click toggle that applies low-pass filter + volume reduction + slow fade.~~ **Shipped v1.5.0** — post-mixer `CalmDownProcessor : ISampleProvider` with 1500 Hz low-pass + 0.5× volume + 1.5s S-curve fade. Fullscreen toggle + `{PluginSettings}` theme binding. | Low | Medium |
| **Peak Meter / VU Display** | Real-time audio level meter alongside or instead of spectrum visualizer. Classic VU look. NAudio sample data already available via `VisualizationDataProvider`. Simpler alternative to FFT spectrum. | Low | Low |
| **Tempo-Aware Shuffle** | When shuffling, avoid jarring tempo jumps by preferring songs with similar BPM to current one. Requires one-time BPM scan stored per file. Smooth listening flow. | Medium | Medium |
| **vgmstream Game Audio Support** | Play video game audio formats (.adx, .brstm, .hca, .wem, .fsb, .vag, .at3, etc.) with loop point support via [vgmstream](https://vgmstream.org/). Complements the GME-backed chiptune support (v1.4.0: VGM/VGZ; v1.4.3: NSF). See detailed roadmap below. | High | High |
| **GME Expansion: GBS / SPC / HES / KSS / SAP / AY Track Managers** | NSF Track Manager (v1.4.3) pattern extended to sibling formats with multiple tracks per file. `.gbs` (Game Boy), `.spc` (SNES), `.hes` (PC Engine / TurboGrafx), `.kss` (MSX), `.sap` (Atari), `.ay` (ZX Spectrum). Shared `MultiTrackChiptuneManagerViewModel` with per-format header patchers. User drops a single master file, splits into mini-files, overrides loop lengths via same JSON manifest as NSF. 80% of the code is already in place — this is largely a matter of writing small per-format header patchers (each ~50 lines). | Medium | Medium |
| **Per-File Loop Override for Non-NSF Chiptune Formats** | Extend `nsf-loops.json` pattern to `.vgm`, `.vgz`, `.spc`, `.gbs`, etc. Rename manifest to `chiptune-loops.json`, keyed by filename. `GmeReader` already checks a manifest for `.nsf` — generalize the check to any GME-supported extension. Lets users trim any looping chiptune track, not just NSF. | Low | Medium |

---

## vgmstream Integration Roadmap

Support for 200+ video game audio formats via [vgmstream](https://github.com/vgmstream/vgmstream). Enables users to drop raw game audio rips directly into music folders.

### Why

Many game soundtracks are only available as raw game audio rips (.adx, .brstm, .hca, .wem, etc.) that standard players can't handle. These formats often contain embedded loop points — metadata that tells the player where to seek back for seamless looping, which is essential for game music that's designed to loop indefinitely.

### Integration Tiers

**Tier 1 — CLI Pre-conversion (Easiest)**

Use `vgmstream-cli.exe` as an external tool (like FFmpeg/yt-dlp) to decode game formats to WAV on first play, then cache the result. User provides the path in settings.

| Aspect | Details |
|--------|---------|
| Command | `vgmstream-cli -o output.wav input.adx` |
| Loop support | Bake N loops into WAV via `-l 3.0 -f 5.0` (3 loops, 5s fade) |
| NAudio | Plays cached WAV through existing pipeline — zero changes needed |
| SDL2 | Same — plays cached WAV via `Mix_LoadMUS()` |
| Effort | ~200-300 lines (new `VgmstreamConversionService`, settings, extensions list) |
| Trade-off | Requires disk space for cached WAVs; no real-time loop control |

**Tier 2 — P/Invoke to libvgmstream (Better)**

Build vgmstream as a DLL and use the C API directly for real-time decoding. Create a custom `VgmstreamReader : WaveStream` for NAudio.

| Aspect | Details |
|--------|---------|
| API | `libvgmstream_init()` → `open_stream()` → `render()` → loop via `seek()` → `free()` |
| Key structs | `libvgmstream_format_t` exposes `channels`, `sample_rate`, `loop_start`, `loop_end`, `loop_flag` |
| NAudio | Custom `WaveStream` feeds PCM samples from `libvgmstream_render()` into the effects chain |
| SDL2 | Still needs pre-conversion (SDL2_mixer can't consume raw PCM from a pipe) |
| Loop points | Read `loop_start`/`loop_end` from format struct, `libvgmstream_seek()` back at loop boundary |
| Effort | ~500-800 lines + building/bundling vgmstream DLL |
| Trade-off | Must build and ship the DLL; more complex but enables real-time loop control and effects on game audio |

**Tier 3 — Full Loop Integration (Advanced)**

Build on Tier 2 with intelligent loop handling in the NAudio pipeline.

| Aspect | Details |
|--------|---------|
| Behavior | Detect loop end in `VgmstreamReader.Read()`, auto-seek to `loop_start` |
| Loop count | Configurable: loop N times then fade to silence, or loop forever until game switch |
| Integration | Works with existing `MusicFader`, `SmoothVolumeSampleProvider`, and visualizer |
| Effort | ~300 lines on top of Tier 2 |

### SDL2 Considerations

SDL2_mixer requires file paths (`Mix_LoadMUS()`) and doesn't accept raw PCM streams. For SDL2 mode:
- **Tier 1** works natively — cached WAV files play as-is
- **Tier 2/3** would need a pre-conversion fallback for SDL2 users, or SDL2 mode could auto-convert on first play and cache (same as Tier 1 but triggered automatically)
- Alternatively, `Mix_LoadMUS_RW()` accepts `SDL_RWops` memory streams — could feed decoded PCM via a memory buffer, but adds complexity

### Common Game Audio Formats

| Platform | Formats | Notes |
|----------|---------|-------|
| Nintendo | .brstm, .bcwav, .dsp, .ast, .swav | Wii, 3DS, GameCube |
| PlayStation | .vag, .at3 | PS1, PSP, PS3 |
| Xbox | .xwb | Xbox 360 |
| CRI Middleware | .adx, .ahx, .aax, .hca | Capcom, FromSoftware, many others |
| FMOD | .fsb | Many modern games |
| Wwise | .wem | Many modern games |

### Implementation Steps (Tier 1)

1. Add `VgmstreamPath` setting (like `FFmpegPath` / `YtDlpPath`) with validation
2. Add game audio extensions to `Constants.SupportedAudioExtensions`
3. Create `VgmstreamConversionService` — decode on first play, cache WAV alongside original
4. Hook into `PlayGameMusic` / `LoadAndPlayFile` — detect game format, convert if needed
5. Settings UI: path browser, loop count, fade duration

---

## Integration & Streaming

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **OBS Text File Export** | Write `now_playing.txt` on each song change for OBS stream overlays. ~10 lines of code. | Low | Medium |
| **Discord Rich Presence** | Show "Listening to: Battle Theme - Final Fantasy VII" in Discord status. Simple named pipe RPC. | Low | Medium |
| **Windows SMTC (System Media Transport Controls)** | Integrate with Windows media overlay (Win+G, volume flyout, Bluetooth controls). ~~Media keys work when Playnite unfocused.~~ Media keys addressed via RegisterHotKey in v1.3.2; SMTC would add Win+G overlay integration and Bluetooth headphone controls. | Medium | Medium |
| ~~**Hotkey/StreamDeck Local API**~~ ✅ | ~~Expose controls via localhost REST API. StreamDeck, Touch Portal, AutoHotkey can query/control UPS. See [docs/EXTERNAL_CONTROL.md](../EXTERNAL_CONTROL.md).~~ **Shipped v1.3.7–v1.3.10** as External Control Service | Medium | High |
| **Twitch Chat Integration** | `!song` shows current track, `!skip` votes to skip, `!request GameName` queues music. Twitch IRC connection. | Medium | Medium |
| **DMCA-Safe Mode** | Flag/skip DMCA-problematic songs. Tag as "stream-safe" or "DMCA risk." Only play safe tracks in this mode. | Low | High |
| **Scrobbling (Last.fm / ListenBrainz)** | Submit played tracks to a scrobbling service. Two destinations to consider: **Last.fm** — broadest user base, OAuth handshake, but game-music tracks often aren't in their catalog and get rejected. **ListenBrainz** — open-source MIT-friendly alternative, no OAuth complications, simple `POST /1/submit-listens`, accepts any track metadata. ListenBrainz is the more practical target for game music; Last.fm for users who already use it. | Low | Medium |
| **Music-Reactive Desktop Wallpaper** | Pipe FFT data to Wallpaper Engine / Lively Wallpaper. Audio-reactive desktop backgrounds. Uses existing `VisualizationDataProvider`. Could also write a dynamic image (current game's cover + subtle visualizer overlay) that wallpaper engines watch via file-watch, avoiding any injection/interop. | Medium | Medium |
| **Network Streaming Output** | Stream audio via HTTP/Icecast. Listen on phone while PC plays Playnite on TV. NAudio output duplication. | High | Low |
| **Headphone Detection Auto-Pause** | Auto-pause on headphone unplug, resume on plug. Like smartphones. `MMDeviceEnumerator` device change events. | Medium | Medium |
| **Audio Output Device Selection** | Choose audio output device independent of Windows default. Game music through speakers, game through headphones. NAudio `WaveOutEvent(deviceNumber)`. | Medium | Medium |
| ~~**Keyboard Shortcuts**~~ ✅ | ~~Global hotkeys: play/pause, skip, volume, mute. Playnite keyboard hook system.~~ **Partially shipped v1.3.2** as Global Media Key Control (media keys only; custom hotkeys not yet supported) | Low | Medium |
| **Copy Song Info to Clipboard** | Button or menu: copies "Now Playing: Battle Theme - Final Fantasy VII" to clipboard. `Clipboard.SetText()`. Share in Discord/chat. | Low | Low |
| **Playnite URI Handler for Music** | Register `playnite://uniplay/` URI scheme. Deep links: `playnite://uniplay/play?game=HollowKnight`, `playnite://uniplay/skip`. External app/script control without REST server. `_api.UriHandler` available but unused. | Low | Medium |
| **Mode Switch Stinger** | Play a short audio stinger when switching Desktop ↔ Fullscreen modes. Reinforces mode transition like console boot sounds. Playnite mode switch events available. | Low | Low |

---

## Visual & UI

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Album Art Display** | Extract/display album art from audio metadata in Now Playing or dashboard. TagLib# reads embedded art. Game cover as fallback via Playnite API. | Low | Medium |
| **Mini Player Mode** | Small floating window with play/pause, skip, song info, visualizer. Like Spotify mini player. WPF `Topmost=true` window. | Medium | Medium |
| **Waveform Progress Bar** | SoundCloud-style waveform as progress indicator. NAudio waveform data generation. | Medium | Medium |
| **Fullscreen Now Playing Overlay** | Brief toast showing what song started in Fullscreen mode. Like Xbox achievement notifications. Auto-dismissing timed overlay. | Medium | Medium |
| ~~**Reactive Visualizer Auto-Theme**~~ ✅ | ~~Visualizer auto-switches color palette based on game cover art. DynamicColor extraction exists — just needs auto-triggering on game change.~~ **Shipped — Spectrum Visualizer's "Dynamic (Game Art)" color theme pulls colors from the current game's cover via `GameColorExtractor` + `DynamicColorCache`. Listed as one of the 22 visualizer themes.** | Low | Medium |
| ~~**Song Count Badge in Game Menu**~~ | ~~Right-click menu shows "UniPlaySong (3 songs)" instead of just "UniPlaySong". Data already available via `GetAvailableSongs`.~~ | ~~Low~~ | ~~Low~~ | **Shipped v1.3.0** |
| ~~**Music Folder Size in Game Menu**~~ | ~~Show folder size next to song count: "UniPlaySong (3 songs, 12.4 MB)". Quick `FileInfo.Length` sum.~~ | ~~Low~~ | ~~Low~~ | **Shipped v1.3.0** |
| **Volume Percentage in Tooltip** | Show "Volume: 72%" in the top panel play/pause button tooltip. Already updating `_playPauseItem.Title` dynamically. | Low | Low |
| **Shuffle Indicator in Now Playing** | Show shuffle icon/text in Now Playing panel when shuffle mode is active. `RandomizeOnX` setting already exists. | Low | Low |
| ~~**Playing Default Music Indicator**~~ | ~~Subtle visual indicator when default/fallback music is playing vs game-specific. `IsPlayingDefaultMusic` property exists.~~ | ~~Low~~ | ~~Low~~ | **Shipped v1.3.0** |
| **Audio Reactive Game Cover Art** | Game cover image pulses/glows to the beat. Subtle animation via PluginUserControl. "Living library" feel. | High | Low |
| **Song Lyrics Display** | Fetch/display lyrics in dashboard. Sources: embedded USLT tags (TagLib#), web APIs. Best for vocal soundtracks (Persona, NieR). | Medium | Low |
| ~~**What's Playing Toast Notification**~~ | ~~Windows toast notification on new song. Title, artist, game name.~~ | ~~Low~~ | ~~Low~~ | **Shipped v1.3.0** (as Celebration Toast) |
| **Music Calendar / Listening Heatmap** | GitHub-contribution-style green grid showing listening days/duration. Stored as daily aggregates. Dashboard display. | Medium | Low |
| **Music Map Visualization** | Interactive graph: games as nodes, sized by songs, colored by genre, connected by composer. Like "Every Noise at Once" for your library. | High | Low |
| **Song Preview on Hover** | In a song list/dashboard context, hovering over a song plays a 10-second preview clip. Like Spotify's track preview. Uses existing `PlayPreview()` with short duration. | Low | Medium |
| **Now Playing Game Cover** | Show the currently-playing game's cover art thumbnail next to the Now Playing ticker in top panel. `_api.Database.GetFullFilePath(game.CoverImage)`. Instant visual context. | Low | Medium |
| **Fullscreen Visualizer Mode** | Full-window spectrum visualizer for fullscreen mode. Currently desktop-only. Reuse existing `SpectrumVisualizerControl` in fullscreen overlay. Optional "screensaver mode": after N minutes of no game selection in Fullscreen, switch to the full-screen visualizer automatically; return to normal on any input. Turns idle Playnite into a music-visualizer piece. | Medium | Medium |

---

## Library Management

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Breadcrumb Song Count** | Add `Songs: N` line to each game's breadcrumb `.txt`, updated when files are added/removed. Gives an at-a-glance count without opening the folder. | Low | Low |
| **Breadcrumb Refresh Action** | "Refresh Breadcrumbs" bulk action in Settings → Editing. Updates all `.txt` files to reflect current game names (handles renames in Playnite). | Low | Low |
| **Breadcrumb Platform Field** | Add `Platform: PC (Windows)` to breadcrumb content. Useful when the same game title exists on multiple platforms with separate library entries. | Low | Low |
| **Orphan Cleanup — Named Games** | When cleanup finds orphaned GUID folders, read the breadcrumb `.txt` to show the game name (`"The Witcher 3 (no longer in library)"`) instead of a raw GUID path. | Low | Medium |
| **Open Game Index Button** | Button in Settings → Editing to open `_game-index.txt` in the default text editor. One-liner `Process.Start`. | Low | Low |
| **Index with Song Counts** | Include song count per game in `_game-index.txt` (e.g. `The Witcher 3 \| a1b2c3... \| 5 songs`). More useful than name+ID alone. | Low | Low |
| **Index CSV Export** | Export `_game-index.txt` as CSV for spreadsheet analysis of your music library. | Low | Low |
| **Breadcrumb as Playlist Seed** | Dashboard reads breadcrumb files to build filtered playlists (e.g. "all games on PC with music") without full folder scans at startup. Foundation for tag-based filtering. | Medium | High |
| **Bulk Metadata Editor** | Edit song titles/artists across library without external tools. Clean up "Track 01" entries. | Medium | Medium |
| **Duplicate Song Detection** | Scan for duplicate audio files by hash. Surface duplicates for cleanup. | Low | Medium |
| **Audio Fingerprint Deduplication** | Find duplicates even across different bitrates/sources using Chromaprint/AcoustID fingerprinting. | High | Low |
| **Music Health Report** | Scan and report: corrupted files, extremely short (<5s), very large, missing metadata, unsupported formats. Diagnostic companion to audio repair. | Medium | Medium |
| ~~**Auto-Cleanup Empty Folders**~~ | After deleting songs, clean up empty game music directories. **Shipped v1.3.2** | Low | Low |
| ~~**Playlist Export (M3U/PLS)**~~ | Export per-game or whole-library playlists for external players (Winamp, VLC, foobar2000). Simple text generation. **Shipped v1.3.2** | Low | Low |
| **Playlist Import (M3U/PLS)** | Import playlists from external players. Parse M3U/PLS, match files to games, create UPS playlists. Bidirectional workflow. | Low | Low |
| **Backup/Restore Music Library** | One-click backup of entire music library to ZIP. Restore on new machine. | Low | Medium |
| **Soundtrack Completionist Tracking** | Show OST completion: "Hollow Knight: 12/26 tracks." Track counts from KHInsider (scraping exists). | Medium | Medium |
| **Export Song List to Text** | Right-click game -> "Export Song List." Writes text file with all song names. `File.WriteAllLines()` + `GetAvailableSongs()`. | Low | Low |
| ~~**Open on KHInsider Context Menu**~~ | ~~Right-click game -> "Search on KHInsider." Opens browser to KHInsider search with game name.~~ ❌ N/A — KHInsider removed in v1.3.4 | ~~Low~~ | ~~Low~~ |
| ~~**Settings Quick Reset per Section**~~ ✅ | ~~"Reset to Defaults" button per settings tab. Tool paths (yt-dlp, FFmpeg) preserved on reset.~~ **Shipped v1.3.2** | Low | Medium |

---

## Social & Community

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Localization / Translation Support** | Allow community contributors to translate the extension into other languages. Implementation: WPF `ResourceDictionary` XAML files per locale (e.g. `Localization/en_US.xaml`, `fr_FR.xaml`). Strings referenced via `{DynamicResource LOC_KeyName}` in XAML and a `ResourceProvider.GetString()` helper in C#. Locale auto-detected at startup, falls back to English. Scope is essential strings only: dialog messages, button labels, settings headers, option names, and key descriptions — not fine-print tooltips or debug strings (~250-350 strings total, roughly half the codebase). Contributors add a single XAML file per language. Best tackled before a v2.0 major release rather than as an incremental patch. | High | Medium |
| **Shareable Music Profiles** | Export stats as shareable card/image: "200 soundtracks, 1500 songs, most played: Persona 5." Like Spotify Wrapped for game music. | Medium | Medium |
| **Community Download Lists** | Export/import curated download lists (game name -> search terms) as JSON. Share on forums. Builds on auto-search database. | Low | Medium |
| **Soundtrack Ratings** | Rate soundtracks 1-5 stars. Aggregate "My top-rated." Feeds into smart shuffle. | Low | Medium |
| **Collaborative Playlists via GitHub Gists** | Store playlists as Gists. Share links, others import. Games matched by name (fuzzy). No backend needed. | Medium | Low |
| **Soundtrack Credits Export** | Generate credits list of all soundtracks played during a session. Text or image export. Attribution for streamers. | Low | Medium |

---

## Gamification & Engagement

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| ~~**Completion Celebration**~~ | ~~Play victory fanfare when game marked "Completed." 11 bundled jingle presets, custom file support, NAudio live effects chain.~~ | ~~Low~~ | ~~Medium~~ | **Shipped v1.3.0** |
| **Music Collection Achievements** | Unlock badges: "100 soundtracks", "1000 songs listened", "Music for every RPG." Displayed in dashboard. | Medium | Medium |
| **Listening Streaks** | Track consecutive days of listening. "15-day streak." Subtle engagement hook. | Low | Low |
| **Music Discovery Challenge** | Weekly suggestion: "Listen to music from a game you haven't played in 6+ months." Re-engage with forgotten library. | Low | Low |
| **Total Listening Time Tracker** | Increment counter on `OnSongEnded`. Display "Total listening time: 42 hours" in settings or dashboard. One TimeSpan field. | Low | Low |
| ~~**Notification Sound on Download Complete**~~ | ~~Play system sound when batch downloads finish. `SystemSounds.Asterisk.Play()`.~~ | ~~Low~~ | ~~Low~~ | **Shipped v1.3.0** |

---

## v1.4+ Opportunities Identified

New ideas surfaced during v1.4.x development or flagged by users. Most are Low effort / High fit because they extend infrastructure we just built.

### Chiptune & Retro Follow-Ups (v1.4.3-native)

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| ~~**Session Auto-Play Lock (Desktop)**~~ ✅ | ~~When Play Music State = Fullscreen Only, Desktop is fully silent. User request: start music manually once via top panel, and THEN have game switches auto-play for the rest of the session (until Playnite closes). New setting: `AutoPlayOnFirstLaunchDesktop` (default ON). When OFF, `_userHasManuallyStartedThisSession` flag gates `ShouldPlayMusic` in Desktop mode.~~ **Shipped — `AutoPlayOnFirstLaunchDesktop` setting in `UniPlaySongSettings.cs:384` + `UserHasManuallyStartedThisSession` flag on the playback service gate `ShouldPlayMusic` in Desktop mode exactly as designed. Sticky-on after first manual press; resets on Playnite restart.** | Low | Medium |
| **Multi-Track Manager Generalization** | NSF Track Manager (`src/Views/NsfTrackManagerDialog.xaml` + VM) is a near-perfect template for any multi-track chiptune format. Generalize to `ChiptuneTrackManagerDialog` accepting per-format header patcher delegates; register `.gbs`/`.spc`/`.hes`/`.kss` etc. patchers. Same dialog, different menu labels per format. | Medium | Medium |
| **Combined Chiptune Loop Manifest** | Instead of `nsf-loops.json`, use `chiptune-loops.json` so all GME-playable formats share the override mechanism. Trivial extension of the existing `NsfLoopManifest` lookup in `GmeReader`. | Low | Low |
| **Diagnose Corrupt Chiptune Files on Load** | When `GmeReader` fails to open a file, surface a clear log line distinguishing: file corruption vs. unsupported chip (already documented for VGM/VGZ via `VgmHeaderSniffer`) vs. missing GME native DLL. Mirrors the yt-dlp DLL-diagnostic pattern. | Low | Low |

### UX Polish

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **`Play Music State` Auto-Hint for New Users** | First-launch banner or tooltip on the Play Music State dropdown: *"New to UPS? Check this first to choose when music auto-plays."* Dismissible. Reduces the exact discoverability gap that prompted the v1.4.3 label fix. | Low | Low |
| **Top Panel Tooltip Shows Current Auto-Play Mode** | Hover the Play button → tooltip includes the active MusicState ("Auto-play: Fullscreen Only"). Helps users remember what's configured without opening settings. | Low | Low |
| **Settings Search Box** | With 10 tabs and ~150 settings, a search box at the top of the Settings dialog (filters visible settings across all tabs as you type) would be a huge UX win. No storage change — purely visibility-filtering. | Medium | High |
| **Onboarding Welcome Tour** | First run of v1.5+: brief multi-step popover tour through core settings. Linked to existing settings sections. Dismissible forever. | Medium | Medium |
| **"What's New" Popup on First Launch After Update** | Parse the release notes for the current version and show a modal with highlights. Keeps users informed without requiring README reading. | Medium | Medium |

### Integration Deepening

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Extension Script Action API** | Register UPS commands as Playnite script actions so other extensions / user scripts can `playbackService.Play()`, `Skip()`, etc. from their own code. `IPlayniteAPI` supports this via `AddPluginSettings`. Complements External Control REST API (shipped) with in-process hooks. | Low | Medium |
| **Game State Push Notifications** | Optional POST-webhook when song changes, game switches, etc. For Discord bots, home automation, smart lights. URL configured in settings; POST payload is JSON (game, song, timestamps). | Low | Low |
| **MQTT Output** | For home-automation folks: UPS publishes song/game state to an MQTT broker. Trigger Hue lights, stream deck, etc. Uses `MQTTnet` NuGet. | Medium | Low |

### Settings Architecture

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| ~~**Settings Import/Export (JSON)**~~ ✅ | ~~Export all UPS settings (excluding tool paths which are machine-specific) to a shareable JSON file. Import on new machine or backup. Uses existing `JsonConvert.SerializeObject` from global-reset path. Users keep asking for this when migrating Playnite installs.~~ **Shipped v1.5.0 as Settings Backup tab — includes both JSON export/import AND a Markdown human-readable snapshot for sharing in support requests. Path-sanitized for safe sharing.** | Low | High |
| **Settings Profiles** | Save named settings snapshots, switch between them. "Streaming", "Casual browsing", "Late night". One-click profile swap. Built on export/import. | Medium | Medium |
| **Per-Settings Help Tooltips** | Tooltip `?` icon next to each setting showing a richer description than the inline one-liner. For settings with subtle effects (RandomizeOnMusicEnd, DefaultMusicContinueSameSong). | Low | Low |

### Performance / Architecture

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Split `UniPlaySong.cs` (5088 lines)** | Plugin entry point has grown past the "reason about in context" threshold. Extract menu construction (`GetGameMenuItems` is ~500 lines alone), Fullscreen quick-settings handlers, event subscriptions into dedicated files. Follows the JingleService extraction pattern from v1.4.1. | Medium | Medium |
| **Split `UniPlaySongSettings.cs` (3628 lines) by Tab** | Partial classes keyed by settings tab (`.General.cs`, `.Playback.cs`, `.Pauses.cs`, etc.). Single logical class at runtime, but each file is browsable. Reset handlers follow the same split in `UniPlaySongSettingsView.xaml.cs`. | Medium | Medium |
| **Split `UniPlaySongSettingsView.xaml` (4059 lines)** | Use `<ContentControl>` with per-tab UserControls (`GeneralTab.xaml`, `PlaybackTab.xaml`, etc.) instead of inlining everything in one file. Improves designer load times and makes each tab self-contained. | Medium | Medium |
| **MusicPlaybackService Split** | At 2048 lines it's mixing pause sources, game-selection, preview timer, song-end fade, radio, default music. Candidates: extract `DefaultMusicScheduler`, `RadioModeService`, `PauseSourceRegistry`. | High | Medium |
| **Reduce Startup Scan Cost for Large Libraries** | Users with 500+ games hit a visible pause at Playnite start when UPS scans music folders. Already have breadcrumb files and `_game-index.txt` — expand them to skip per-folder enumeration when the index is fresh. Check mtime of each folder against last-index-build. Falls back to a scan if dirty. | Medium | Medium |
| **Lazy-Load Library Dashboard Data** | Dashboard loads all games + songs + metadata up front. For 500+ game libraries, virtualize the Game Card grid and load song metadata on-demand when a card scrolls into view. | Medium | Medium |
| **Warm Up NAudio Mixer At Startup** | First-song load has a ~400ms `EnsurePersistentLayer` cost (observed in logs). Initialize the persistent layer in a background task immediately after plugin load so first game-select is instant. | Low | Medium |

### Creative / Niche

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Cross-Game Music Symlinks / Shared Folder** | For game series where the same soundtrack fits multiple entries (e.g. Dark Souls 1/2/3 trilogy). User designates a shared folder; multiple games reference it instead of duplicating MP3s. UPS presents the folder as if it were each game's music. Storage win + consistency. | Medium | Medium |
| **Playlist-as-Game Mode** | Let users create virtual "games" in UPS (not in Playnite) that are just playlists. Shows up in Library Dashboard. Doesn't touch Playnite's game database. | Medium | Low |
| **Now-Playing Badge on Game Card** | When a song is playing, the game card for that game (in Playnite's own library grid) shows a small pulsing "♪" indicator. Uses `IGameDetailsPlugin` or `PluginUserControl` overlay via theme integration. | Medium | Medium |
| **"Music Only" Game Filter Button** | One-click Playnite filter: "games that have UPS music downloaded". Uses the existing tagging system — auto-tag games with `.ups:has-music` on first song download, filter via Playnite's native filter UI. | Low | Medium |
| **Loop Boundary Detection** | Auto-detect natural loop points in audio files using autocorrelation over the waveform. For chiptune files specifically, reduce the need for manual loop editing. Could also apply to MP3/FLAC. | High | Medium |
| **"Tracklist" Export to Markdown / PDF** | Per-game markdown file listing all songs with their durations + any custom loop overrides. Useful for sharing setups or documenting your collection. Distinct from the plain-text "Export Song List" idea — structured output with metadata. | Low | Low |

---

## v1.5+ Opportunities Identified

New ideas brainstormed during the v1.4.6 cycle, audited against the live codebase (May 2026) to confirm none duplicate existing functionality. Categorized by flavor: creative / fun, basic quality-of-life, and novel functional integrations.

### Creative / Fun

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Boot Stinger Per Console / Decade** | Generation-appropriate "console boot" sound when selecting a game from a specific platform (Sega Genesis cry, SNES Mode 7 swoosh, PSX diamond chime, etc.). Plays before the game's music fades in. Toggle off by default; sub-menu to opt platforms in. Uses existing JingleService framework + `game.Platforms` metadata + the same "play short audio then transition" path as the Mode Switch Stinger idea. | Low | Medium |
| **Now Playing Marquee Themed by Game Era** | Now Playing ticker visual style adapts to currently-playing game's era. 80s arcade → CRT scanlines + neon glow. 90s console → pixel font + cyan-on-black. Modern AAA → clean Helvetica. PS1 → wobbly low-poly text. Driven by `game.ReleaseYear`. Ships as small set of WPF resource dictionaries; user picks "Auto by Era" or fixed style. | Medium | Low |
| **Game Music DJ Mix Export** | After a Playnite session, export a continuous MP3/M4A "DJ mix" of every song UPS played, with crossfades, in chronological order. Saved to a `Mixes/` folder. Built on existing FFmpeg integration (concat + crossfade filter) and `OnSongChanged` event tracking. Auto-trim to last N mixes or session-end "Save this mix?" prompt to avoid storage bloat. | Medium | Medium |

### Basic / Quality-of-Life

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Right-click song → "Lock as #1 in Custom Rotation"** | Discoverability fix for the existing Custom Rotation default-music source. Currently adding games to it requires diving into Settings → Playback. New right-click action on any song row (dashboard or game music list) one-clicks adding the song's parent game to the Custom Rotation pool, with optional "pin to top". Pairs with Drag-Drop Reorder (next row). | Low | Low |
| **Drag-Drop Reorder for Custom Rotation Pool** | Custom Rotation already exists as a `List<Guid>` setting. WPF `ListBox` drag-drop attached properties + ↑/↓ row buttons. Lets users pin favorites to the top of the rotation cycle. Pure UI tweak, no backend change. | Low | Low |
| **Per-Song Skip Memory** | When user manually skips a song mid-playback (Next), remember it in a session-scoped HashSet. UPS won't re-pick that song for the rest of the session even if shuffle would normally include it. Cleared on Playnite restart. **Opt-in setting** — off by default, user enables in Settings → Playback as `Remember Manual Skips This Session`. **Visible state** — small `(N skipped this session)` counter in top panel tooltip or Now Playing area; click to see list and one-click un-skip. **Hard time bound** — auto-clears at restart; manual "Clear skip memory" button in Settings. Debug log lines surface skip-block events for transparency. Distinct from the existing permanent-per-game "Don't Play This Song Skip List" idea. | Low | Medium |
| **"Queue Next Game's Music" in Radio Mode** | Single-slot queue for Radio Mode only. While radio is playing, right-click a game → "Queue this game next in radio." When current radio song ends (or user hits Next), UPS plays from the queued game's pool for one cycle, then resumes radio. Single nullable `_queuedGameForRadio` field; `OnSongEnded` checks it before pulling next radio song. Standard playback uses game-selection itself as the queue, so this only makes sense in radio mode where the user has stepped outside per-game playback. | Low | Low |
| **Restore Last Volume on Restart** | Confirm and close the gap where live volume slider changes (during playback) don't always persist back to settings on shutdown. `MusicVolume` is a setting; `OnApplicationStopped` exists; just need to write current player volume back at shutdown, or every N seconds while idle. Small but high-frequency annoyance fix. | Low | Medium |

### Novel / Functional

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Now Playing as a Browser Window / Web Endpoint** | UPS optionally serves a tiny HTML page on `localhost:<port>/now-playing` showing current song, game cover, and a CSS-only visualizer, auto-refreshed via server-sent events. Drop the URL into OBS as a Browser Source for stream overlays (no text-file polling). Open in any browser tab as an "always visible" playback view alongside Playnite. Open from a phone/tablet on the same network for second-screen now-playing. Subsumes the OBS Text File Export idea — strictly better because it's auto-styled, no polling, and works in browsers natively. Builds on the External Control Service (shipped v1.3.7-v1.3.10) — adds an HTML route alongside the existing JSON API. | Medium | High |
| **MIDI Controller Input for Playback** | Map a MIDI controller's transport buttons (play/pause/skip/prev) and rotary encoder (volume, scrub) to UPS playback via MIDI input. Any cheap USB MIDI controller (Korg nanoKONTROL, Akai LPD8) becomes a tactile UPS remote. Twist a knob to scrub, hit a pad to skip, slide a fader for volume. NAudio.Midi (already a dependency) handles the input enumeration; ~50 lines to listen for control-change/note-on events and dispatch to existing playback service methods. Niche audience (streamers, music producers with MIDI on their desk) but no other Playnite plugin offers this. | Medium | Low |
| **Auto-Generate `.m3u` Playlists per Custom Filter** | When user creates a Playnite filter ("Genre = RPG, Era = 90s"), UPS detects it and writes a corresponding `.m3u` to a configurable folder. Contains every song from games matching the filter. Re-writes as the filter changes. Outputs are usable in foobar2000, VLC, Winamp, etc. Combines existing Filter Mode detection + existing M3U Export (shipped v1.3.2). User never opens UPS to manage these — they use Playnite normally and a "RPG_90s.m3u" appears on disk. Pairs naturally with the "Breadcrumb as Playlist Seed" idea. | Low | Medium |
| **System Tray Mini-Mode** | Optionally minimize Playnite to the tray and run UPS as a tray-only audio process. Small icon in system tray; right-click menu has play/pause/skip/volume/now-playing-song. Useful for users who want UPS as their everyday music player but only open Playnite occasionally. Built on already-shipped Music Library Dashboard (which makes UPS viable as a standalone player) + WPF `H.NotifyIcon` NuGet (used by reference codebase DesktopClock per Architecture Ideas section). Existing top-panel media controls already implement most of the menu logic. Must coexist gracefully with Playnite's own minimize-to-tray setting. | Medium | Medium |
| **Game-Aware Hue / Razer Chroma Lighting Sync** | Map UPS's `VisualizationDataProvider` FFT output to peripheral RGB lighting via Philips Hue Sync API or Razer Chroma SDK. Bass hits = red flash on keyboard. Treble shimmer = blue ripple on mousepad. Game cover dominant color (`GameColorExtractor`, already exists for Dynamic visualizer) sets base hue when no audio plays. Both target HTTP APIs (no DLL loading). Hue requires bridge IP discovery (one-time setup); Chroma requires Razer Synapse running. Targets users who already have those ecosystems — UPS becomes another input alongside their game's native RGB. | High | Medium |

---

## Atmosphere & Immersion

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Time-of-Day Awareness** | Adjust mood/volume by time. Evening -> quieter/ambient. Daytime -> normal. Auto-select bundled presets by time. | Low | Medium |
| **Dynamic Volume on Idle** | Lower volume when no game selection changes for X minutes. Browsing rapidly keeps energy up. Presence-aware audio. | Low | Low |
| **Game Launch Countdown** | Instead of immediate pause on Play, build a brief crescendo/hype sequence (3-10s) as game launches. Like a DJ build-up. | Medium | Low |
| **Sound Effects Pack System** | Downloadable UI sound packs for Playnite navigation. Click/hover/transition sounds. Like PS5/Switch UI sounds. Layered on top of music. | High | Medium |
| **Custom Intro/Outro per Game** | Jingle before main music: "Now playing: Hollow Knight." Custom audio clips or TTS. | Medium | Low |
| **Multi-Track Layering Per Game** | Support multiple simultaneous tracks per game (e.g., ambient layer + melody layer). User assigns tracks to layers with independent volume. Like how actual games layer music. NAudio mixer. | High | Medium |
| **Controller Vibration on Beat** | Pulse Xbox controller on detected beats. Subtle tactile music feedback. Uses existing XInput wrapper + FFT beat detection from `VisualizationDataProvider`. | Medium | Low |

---

## Experimental / Labs

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Stem Separation** | AI-powered (Demucs via Python) vocal/instrument separation. Create instrumental versions on the fly. | High | Low |
| **Karaoke Mode** | Stem separation + lyrics display. Remove vocals, show lyrics. Party feature for vocal soundtracks. | High | Low |
| **Voice Commands** | "Skip song", "Play Zelda music." Windows Speech Recognition API (`System.Speech.Recognition`). Built-in .NET, no cloud. | Medium | Low |
| **Game Music Radio with DJ TTS** | Auto-generate announcements between songs using Windows SAPI TTS. "That was Battle Theme from FF7." | Medium | Low |
| **Podcast Generator** | Auto-generate podcast RSS from library. Each episode is a game soundtrack with TTS intro/outro. Subscribe in podcast apps. | High | Low |
| **Crossfade DJ Mode** | Beat-matched crossfades between songs. Detect BPM, align beats for seamless transitions. | High | Low |
| **Spatial Audio / Surround** | Position music in surround field. Windows Sonic / Dolby Atmos passthrough via WASAPI. | High | Low |
| **Plugin API for Theme Developers** | Expose rich music state properties for custom theme widgets, visualizers, music-reactive backgrounds. | Medium | Medium |
| **Scriptable Rules Engine** | JSON-based rules: "When Souls game selected, enable Cathedral reverb." Automated per-game settings. | High | Medium |
| **Music-Aware Game Recommendations** | "You have music for 200 games but haven't played 50 of them." Surface games with music you've never launched. Playnite `game.PlayCount` + music directory existence. Discovery through your own collection. | Low | Medium |
| **Soundtrack Similarity Engine** | Compare audio fingerprints across library to find "similar sounding" games. "Games that sound like Hollow Knight." Chromaprint + vector similarity. | High | Low |
| **AI Playlist Generation** | Describe desired mood in natural language ("epic boss fight music for a rainy evening"), generate playlist from library using local LLM or metadata matching. | High | Low |
| **Music-Reactive Taskbar Color** | Taskbar color shifts with the music — calm songs = cool blue, intense = red/orange, ambient = purple. Uses `SetWindowCompositionAttribute` Win32 call on `Shell_TrayWnd` HWND to apply accent color. Audio energy from existing `VisualizationDataProvider` FFT data drives color selection. No DLL injection needed — safer than RainbowTaskbar's approach. | High | Low |

---

## Architecture Ideas

Technical improvements and library integrations identified through research. These are not features in the user-facing sense — they are implementation strategies, dependency upgrades, and reference codebases that improve quality, reduce code, or unlock future features.

**Tiers:** `Tier 1` = Low effort, high impact (do these) | `Tier 2` = Medium effort, plan these | `Tier 3` = High effort, file away

### Libraries to Add

| Library | Tier | What It Does | Concrete Benefit |
|---------|------|--------------|-----------------|
| **XamlAnimatedGif** | 1 | Animated GIF display in WPF `Image` controls via one attached property. Memory-efficient (JIT frame decode). Supersedes WpfAnimatedGif. | Enables animated cover art panel (`IGameDetailsPlugin`). Users drop `cover_animated.gif` in a game's ExtraMetadata folder, panel displays it automatically. |
| **XamlFlair** | 1 | Attached-property animations in pure XAML — fade, translate, scale, blur, color, compound in one line. WPF `Storyboard` under the hood. Pre-built presets: `FadeIn`, `FadeOut`, `Blur`, `Grow`, `Shrink`, `SlideInFromLeft/Right`. Rx.NET dependency — verify .NET 4.6.2 compat before adopting. | Toast slide-in from right: `Kind='FadeFrom,TranslateXFrom'` animates opacity + position simultaneously. Settings panel fade-in, download dialog transitions. Keep `NowPlayingPanel` render loop as-is. |
| **SharpVectors** | 1 | Render `.svg` files directly in WPF. `<svgc:SvgViewbox Source="icon.svg" />` and done. | SVG icons for settings tabs, desktop bar, menu items. Perfect DPI scaling at any resolution — no more multi-resolution PNG assets. |
| **CommunityToolkit.Mvvm** | 1 | Source generators for `[ObservableProperty]`, `[RelayCommand]`, `INotifyPropertyChanged`. Official MVVM successor to MvvmLight. .NET 4.6.2 compatible. | Collapses `UniPlaySongSettings.cs` and ViewModels — dozens of manual `OnPropertyChanged()` calls replaced with decorated fields. Incremental per-class migration. |
| **LottieSharp** | 1 | Render Lottie JSON animations (Adobe After Effects exports) in WPF. Vector, scalable, tiny file size. | Animated UI elements sourced from LottieFiles.com: loading spinners in download dialog, animated checkmarks, subtle pulse on now-playing indicator. NOT for animated cover art (wrong source format). Requires .NET 4.7 — verify compat. |
| **LoadingIndicators.WPF** | 1 | Collection of animated spinners (ring, wave, dots, etc.) for WPF. | Download dialog and any async operation. Currently no visual feedback while yt-dlp runs. |
| **LiveCharts2** | 2 | Open-source interactive animated charts for WPF. | Music Dashboard: most-played games bar chart, play time trends, song frequency histogram. File away until dashboard work starts. |
| **XamlBehaviors (Microsoft)** | 2 | Official XAML behaviors library — wire interactions in XAML without code-behind (`EventTrigger`, `InvokeCommandAction`). | Cleaner settings UI interactions on new UI work. Not worth retrofitting existing views — use opportunistically. |
| **Lambda Converters** | 1 | Define WPF `IValueConverter`/`IMultiValueConverter` as static lambdas — no separate class per converter. Referenced via `x:Static` in XAML. | Eliminates per-converter boilerplate in settings UI bindings. bool→Visibility, enum display, null checks — all inline. |
| **ValueConverters.NET** | 1 | Pre-built common WPF converters: datetime formatting, enum localization, culture-aware conversion. Drop-in NuGet. | Covers converters you'd otherwise write manually. Pairs with Lambda Converters for custom one-offs. |
| **AutoGrid** | 2 | Drop-in WPF Grid replacement that auto-arranges children without manual row/column assignments. | Reduces settings tab XAML boilerplate on any new settings tabs or dialog layouts. |
| **Flyleaf** | 3 | FFmpeg + DirectX media player with WPF control. Hardware-accelerated, MVVM-friendly, HDR-to-SDR. | Potential third player backend beyond SDL2/NAudio. Revisit only if SDL2 causes hard-to-fix bugs or format gaps. |

### Reference Codebases (Study, No Library to Add)

| Codebase | Relevance | What to Study |
|----------|-----------|---------------|
| **Vividl** | High | WPF GUI wrapper for yt-dlp. Closest public reference to UniPlaySong's download dialog. Study: yt-dlp process lifecycle, download queue UI with per-item status, format selection, error surfacing from stderr. |
| **EarTrumpet** | High | Per-app volume control with system tray and audio peaking visualization. Reference for tray-based audio UIs and how to expose per-app audio controls on Windows. |
| **Sucrose** | High | Open-source Wallpaper Engine alternative. Study: fullscreen detection + auto-pause state machine (analogous to `ShouldPlayMusic()`), multi-monitor overlay handling, "pause when game is running" logic. |
| **OpenNetMeter** | High | Compact always-on-top overlay widget above taskbar. Architecturally identical to UniPlaySong's desktop bar. Key technique: sets `Shell_TrayWnd` as window owner + 200ms Z-order fix timer using `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` to recover if OS resets Z-order (e.g. after Explorer restart). XAML: `WindowStyle="None" AllowsTransparency="True" Topmost="True" ShowInTaskbar="False"`. |
| **DiffusionToolkit** | Medium | Thumbnail grid + metadata panel + folder tree for large media collections. Reference UI pattern for a song browser in the Music Dashboard. |
| **NeeView** | Medium | Polished WPF media browser with gestures, plugin system, book-style navigation. Reference if Music Dashboard includes an album art / song browser panel. |
| **CompactGUI** | Low | Simple "pick folder, show progress, show results" WPF workflow. Reference if bulk normalization gets a dedicated UI dialog. |
| **WindowsEdgeLight** | High | WPF click-through overlay (`WS_EX_TRANSPARENT` + `WS_EX_LAYERED`), always-on-top, high-DPI aware, screen-capture excludable. Study: overlay positioning, DPI handling patterns for desktop bar and future fullscreen Now Playing overlay. |
| **MediaFlyout** | Medium | WPF taskbar flyout for media controls — Fluent Design, acrylic, auto-hides tray icon when no media plays, middle-click pause-all. Study: tray icon + flyout popup design for a UniPlaySong tray flyout. |
| **VoicemeeterFancyOSD** | Medium | Topmost overlay over fullscreen apps using private WinAPI (no graphics API hooks). Uses `ApplicationFrameHost.exe` rename trick for true topmost without Microsoft cert. Study: technique for fullscreen Now Playing overlay that works over games. |
| **DesktopClock** | High | Lightweight always-on-top WPF overlay. Key finding: contains a `FullscreenHideManager` class — exactly the auto-hide-on-fullscreen behavior UniPlaySong's desktop bar needs when a game launches. Also uses CommunityToolkit.Mvvm + H.NotifyIcon + WpfWindowPlacement (save/restore window position across sessions). Study `FullscreenHideManager` specifically. |

| **RainbowTaskbar** | Low | Taskbar color/blur/transparency effects via native C++ DLL hooking Windows composition API (`VisualTreeWatch`, `TAP` — Taskbar Appearance Provider). Too invasive for a Playnite plugin directly, but seeds a "music-reactive taskbar color" feature idea. The safer path: `SetWindowCompositionAttribute` Win32 call on the taskbar HWND without DLL injection. Reference for what's technically possible. |

### Patterns (No Library Required)

| Pattern | Tier | What It Solves |
|---------|------|----------------|
| **Non-Blocking UI (Loaded + async Initialize)** | 1 | Subscribe to `Loaded` event, run async `Initialize()` after UI renders, set `IsBusy` during load. Prevents settings window freeze when scanning library or loading stats. No dependency — just async/await pattern. |

### ControlUp-Specific

| Library / Idea | Tier | Notes |
|----------------|------|-------|
| **WpfAppBar** | 3 | Dock a WPF window to a screen edge (taskbar-style). Could allow ControlUp popup to optionally dock to an edge instead of floating. CC0 licensed. Low priority — current popup works fine. |
| **iNKORE UI.WPF.Modern** | 3 | Fluent 2 design with Mica/acrylic. ControlUp already has custom acrylic. Reference for how to implement proper Windows 11 Mica material if redesigning the popup. |
| **Ookii Dialogs WPF** | 3 | Task dialogs with native Windows styling. Could give ControlUp's popup a more native feel. Not worth the refactor unless redesigning anyway. |

---

## Priority Recommendations

### Quick Wins (Low effort, ship fast) — Top Picks Post-v1.4.6

1. ~~**Settings Import/Export (JSON)**~~ ✅ — **Shipped v1.5.0** as Settings Backup tab (JSON + Markdown snapshot)
2. **Song Bookmarking / Favorites** — cross-game starring via flat JSON manifest, pairs with existing default-music-source picker
3. **Per-File Loop Override for non-NSF chiptune formats** — trivial `GmeReader` extension of existing manifest
4. **Top Panel Tooltip Shows Current Auto-Play Mode** — 2-line change, big discoverability win
5. **Scrobbling (Last.fm / ListenBrainz)** — simple HTTP POST, ListenBrainz is MIT-friendly and accepts game music metadata
6. **Restore Last Volume on Restart** — close the gap where live volume slider changes don't always persist (v1.5 idea, see "v1.5+ Opportunities" below)
7. **Right-click song → "Lock as #1 in Custom Rotation"** — surfaces an existing under-discovered feature (v1.5 idea, see below)
8. **"Queue Next Game's Music" in Radio Mode** — single-slot queue for radio users (v1.5 idea, see below)
9. Gapless playback
10. Per-game effects presets
11. OBS text file export
12. Sleep timer
13. Don't Play This Song skip list
14. "Surprise Me" button
15. Playback memory across sessions
16. Quick mute toggle
17. Copy song info to clipboard
18. Total listening time tracker
19. Personal top charts (most-played games/songs)
20. Auto-skip short files
21. ~~"Calm Down" mode~~ ✅ — **Shipped v1.5.0**
22. Audio ducking during game selection
23. Per-game visualizer theme
24. Now playing game cover
25. Song preview on hover
26. Series/franchise playlist
27. Era-based playlists
28. Auto-reverb by genre
29. Playtime-weighted shuffle
30. Playnite URI handler
31. Default music randomization
32. Shuffle indicator in Now Playing
33. Volume percentage in tooltip
34. Replay current song
35. Song intro skip
36. Listening session recap
37. Auto-pause on battery saver
38. "Music Only" game filter button
39. Diagnose corrupt chiptune files on load
40. Drag-Drop Reorder for Custom Rotation Pool (v1.5 idea, see below)
41. Per-Song Skip Memory (opt-in, with visible counter — v1.5 idea, see below)
42. Auto-Generate `.m3u` Playlists per Custom Filter (v1.5 idea, see below)

### High-Value Features (Medium effort, big impact)

1. **Settings Search Box** — with 10 tabs, this is the biggest UX win remaining
2. **GME Expansion: GBS / SPC / HES / KSS / SAP / AY Track Managers** — leverage NSF Track Manager infrastructure
3. **Multi-Track Manager Generalization** — refactor NsfTrackManager → ChiptuneTrackManager for format-agnostic reuse
4. Windows SMTC (Win+G overlay + Bluetooth — media keys already shipped v1.3.2)
5. Per-game effects presets (Low effort, High impact)
6. Crossfade between games
7. Context-aware playlists from game metadata
8. DMCA-safe mode
9. Discord Rich Presence
10. Cross-game volume normalization
11. Category-based default music
12. Developer/publisher playlist
13. Auto-pause on game audio detected
14. Auto-pause on screen off/display sleep
15. Reduce startup scan cost for large libraries
16. Warm up NAudio mixer at startup
17. Onboarding Welcome Tour
18. "What's New" popup on first launch after update

### Architecture / Technical Debt

1. **Split `UniPlaySong.cs`** (5088 lines) — extract menu construction, event handlers, quick-settings into dedicated files
2. **Split `UniPlaySongSettings.cs`** (3628 lines) — partial classes per tab
3. **Split `UniPlaySongSettingsView.xaml`** (4059 lines) — per-tab UserControls
4. **MusicPlaybackService split** (2048 lines) — extract DefaultMusicScheduler, RadioModeService, PauseSourceRegistry
5. **Adopt CommunityToolkit.Mvvm** — source generators, incremental migration
6. **vgmstream integration** — enables 200+ game audio formats with loop point support

### Ambitious Differentiators (High effort, unique positioning)

1. Soundtrack matching for games without music
2. Multi-track layering per game
3. Ambient sound layers
4. Music-reactive desktop wallpaper
5. Sound effects pack system
6. Mood detection via audio analysis
7. AI playlist generation
8. Loop boundary detection (autocorrelation)
9. Visualizer as Fullscreen screensaver
10. Game cover reactive wallpaper

### Recently Shipped (v1.4 series — see full Shipped tables above)

- **v1.4.6:** PC Engine (.hes) chiptune support, "Split HES Tracks" menu action, two new Bundled Ambient tracks from Mike Aniki (Hub OST, Login OST), `{PluginSettings}` quick-options framework for theme integration (validated against Aniki ReMake), LGPL §6 paperwork for bundled GME, `Enable Game Music` + `Enable Default Music` toggles in Fullscreen Extensions menu
- **v1.4.5:** YouTube download performance overhaul (~30-50% faster), cookie-mode + Deno = ~2x faster downloads, yt-dlp version display in Settings, Fullscreen search-variant buttons (OST/Soundtrack/Music/Theme), FINISH button in download dialog, several download-dialog reliability fixes
- **v1.4.4:** SNES (.spc) advertised as supported, Desktop YouTube preview fix on aggressive %TEMP% scanning, yt-dlp preview error visibility
- **v1.4.3:** NES music support, NSF Track Manager (split + loop editor), MusicState label clarity, yt-dlp DLL diagnostic
- **v1.4.2:** Fullscreen quick settings menu, Fullscreen volume boost, Stay-paused-after-external-audio, Persistent default music backdrop
- **v1.4.1:** Beaten fanfare, Abandoned jingle+toast, GME pause/resume reliability, song-end fade geometry, JingleService extraction
- **v1.4.0:** Retro chiptune music (VGM/VGZ via GME), faster YouTube previews, browser cookie support
- **v1.3.11 / v1.3.12:** Active theme music, Add Music File, normalization codec fix, external audio pause rewrite
- **v1.3.6–v1.3.10:** Music Library Dashboard, External Control REST API, Bulk audio conversion, Icon/Hover Glow, Taskbar color
