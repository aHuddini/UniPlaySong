# UniPlaySong Feature Ideas

Comprehensive collection of potential features, ranging from basic QoL improvements to ambitious experimental concepts. Organized by category with rough effort/impact estimates.

**Legend:** Effort: `Low` (hours) / `Medium` (days) / `High` (week+) | Impact: `Low` / `Medium` / `High` | ~~Strikethrough~~ = Shipped

---

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
| **Music Dashboard** | Central hub showing library overview: total songs, soundtracks, storage used, most-played games, recently played. A single place to explore your music library. Every modern music player has this (Spotify Home, iTunes Library). WPF UserControl in settings or standalone window. | Medium | High |
| **Personal Top Charts** | "Top 10 Most Played Games", "Top 25 Songs", filterable by week/month/all-time. Requires play count tracking. Surface in dashboard. | Low | Medium |
| ~~**Library Statistics Page**~~ ✅ | ~~Detailed breakdown: file format distribution (MP3 vs FLAC %), bitrate stats, total duration, storage analysis. "50 GB of FLAC could be 12 GB as 320kbps MP3." Like foobar2000's aggregate Properties.~~ **Shipped v1.3.0** (**Enhanced v1.3.1:** avg song length, total playtime, ID3 tag count, bitrate distribution card, reducible track size card) | Low | Medium |
| **Listening Trends Graph** | Line/bar chart showing listening hours per day/week/month over time. Visual analytics similar to Last.fm or Spotify Wrapped year-round. | Medium | Medium |

---

## Playback & Listening Experience

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **Gapless Playback** | Eliminate silence between tracks for continuous-mix soundtracks. Pre-buffer next track via `PreLoad()` infrastructure that already exists. Industry standard in 2026. | Low | High |
| **Crossfade Between Games** | Overlap fade-out/fade-in when switching games instead of silence gap. PS Store and Spotify do this. Existing MusicFader infrastructure supports this. | Medium | High |
| **Sleep Timer** | Auto-stop music after configurable minutes. Common in Spotify/podcasts. Simple countdown calling `Stop()`. | Low | Medium |
| **Playback Queue / Up Next** | Queue specific songs across games. "Add to queue" from right-click or dashboard. Persists across game selection. | Medium | High |
| **Song Bookmarking / Favorites** | Star songs across your library. "Favorites" playlist as default music or browsable list. Like FFXIV's jukebox. | Medium | Medium |
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
| **Default Music Randomization** | When using bundled presets, randomly pick which preset plays instead of always the same one. 3 presets + existing random infrastructure. | Low | Low |
| **Last Played Song Indicator** | When selecting a game, show which song played last time. Simple per-game dictionary in settings. Subtle text display. | Low | Low |
| **Configurable Now Playing Format** | Let users choose display format: "Title - Artist", "Artist: Title", "Title (Game)", "Title only". Dropdown in settings. | Low | Low |
| **A-B Repeat / Loop Section** | Set loop points within a song to repeat a specific section. Useful for enjoying specific parts of long tracks. Common in foobar2000, Winamp. | Low | Low |
| **Replay Current Song** | One-click "play again from start" without navigating menus. Back-skip-to-restart behavior standard in every player. Button in top panel controls. | Low | Low |
| **Fade to Pause** | Instead of instant pause, fade out over ~0.5s then pause. Fade back in on resume. Feels polished vs jarring stop. Existing `MusicFader` handles this. | Low | Medium |
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
| **No Music Tag Auto-Apply** | After a failed download attempt (all sources return nothing), automatically tag the game with "No Music" tag. Tagging infrastructure already exists. | Low | Low |
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
| **"Calm Down" Mode** | One-click toggle that applies low-pass filter + volume reduction + slow fade. For when a soundtrack is too intense but you don't want silence. Like "night mode" for audio. Uses existing NAudio filter chain. | Low | Medium |
| **Peak Meter / VU Display** | Real-time audio level meter alongside or instead of spectrum visualizer. Classic VU look. NAudio sample data already available via `VisualizationDataProvider`. Simpler alternative to FFT spectrum. | Low | Low |
| **Tempo-Aware Shuffle** | When shuffling, avoid jarring tempo jumps by preferring songs with similar BPM to current one. Requires one-time BPM scan stored per file. Smooth listening flow. | Medium | Medium |

---

## Integration & Streaming

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
| **OBS Text File Export** | Write `now_playing.txt` on each song change for OBS stream overlays. ~10 lines of code. | Low | Medium |
| **Discord Rich Presence** | Show "Listening to: Battle Theme - Final Fantasy VII" in Discord status. Simple named pipe RPC. | Low | Medium |
| **Windows SMTC (System Media Transport Controls)** | Integrate with Windows media overlay (Win+G, volume flyout, Bluetooth controls). ~~Media keys work when Playnite unfocused.~~ Media keys addressed via RegisterHotKey in v1.3.2; SMTC would add Win+G overlay integration and Bluetooth headphone controls. | Medium | Medium |
| **Hotkey/StreamDeck Local API** | Expose controls via localhost REST API. StreamDeck, Touch Portal, AutoHotkey can query/control UPS. | Medium | High |
| **Twitch Chat Integration** | `!song` shows current track, `!skip` votes to skip, `!request GameName` queues music. Twitch IRC connection. | Medium | Medium |
| **DMCA-Safe Mode** | Flag/skip DMCA-problematic songs. Tag as "stream-safe" or "DMCA risk." Only play safe tracks in this mode. | Low | High |
| **Scrobbling (Last.fm)** | Submit played tracks to Last.fm. Track listening habits. Simple HTTP API. | Low | Medium |
| **Music-Reactive Desktop Wallpaper** | Pipe FFT data to Wallpaper Engine/Lively Wallpaper. Audio-reactive desktop backgrounds. Uses existing VisualizationDataProvider. | Medium | Medium |
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
| **Reactive Visualizer Auto-Theme** | Visualizer auto-switches color palette based on game cover art. DynamicColor extraction exists — just needs auto-triggering on game change. | Low | Medium |
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
| **Fullscreen Visualizer Mode** | Full-window spectrum visualizer for fullscreen mode. Currently desktop-only. Reuse existing `SpectrumVisualizerControl` in fullscreen overlay. Screensaver-like ambient display. | Medium | Medium |

---

## Library Management

| Feature | Description | Effort | Impact |
|---------|-------------|--------|--------|
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

---

## Architecture Ideas

Technical improvements and library integrations identified through research. These are not features in the user-facing sense — they are implementation strategies, dependency upgrades, and reference codebases that improve quality, reduce code, or unlock future features.

**Tiers:** `Tier 1` = Low effort, high impact (do these) | `Tier 2` = Medium effort, plan these | `Tier 3` = High effort, file away

### Libraries to Add

| Library | Tier | What It Does | Concrete Benefit |
|---------|------|--------------|-----------------|
| **XamlAnimatedGif** | 1 | Animated GIF display in WPF `Image` controls via one attached property. Memory-efficient (JIT frame decode). Supersedes WpfAnimatedGif. | Enables animated cover art panel (`IGameDetailsPlugin`). Users drop `cover_animated.gif` in a game's ExtraMetadata folder, panel displays it automatically. |
| **XamlFlair** | 1 | Attached-property animations in pure XAML — fade, translate, scale, blur, color. No code-behind. | Settings panel transitions, download dialog fade-in, toast slide animations. Keep `NowPlayingPanel` render loop as-is; use XamlFlair for everything else. |
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
| **OpenNetMeter** | High | Compact always-on-top overlay widget above taskbar. Architecturally identical to UniPlaySong's desktop bar. Study: window positioning relative to taskbar, minimal-footprint overlay, dark mode support. |
| **DiffusionToolkit** | Medium | Thumbnail grid + metadata panel + folder tree for large media collections. Reference UI pattern for a song browser in the Music Dashboard. |
| **NeeView** | Medium | Polished WPF media browser with gestures, plugin system, book-style navigation. Reference if Music Dashboard includes an album art / song browser panel. |
| **CompactGUI** | Low | Simple "pick folder, show progress, show results" WPF workflow. Reference if bulk normalization gets a dedicated UI dialog. |
| **WindowsEdgeLight** | High | WPF click-through overlay (`WS_EX_TRANSPARENT` + `WS_EX_LAYERED`), always-on-top, high-DPI aware, screen-capture excludable. Study: overlay positioning, DPI handling patterns for desktop bar and future fullscreen Now Playing overlay. |
| **MediaFlyout** | Medium | WPF taskbar flyout for media controls — Fluent Design, acrylic, auto-hides tray icon when no media plays, middle-click pause-all. Study: tray icon + flyout popup design for a UniPlaySong tray flyout. |
| **VoicemeeterFancyOSD** | Medium | Topmost overlay over fullscreen apps using private WinAPI (no graphics API hooks). Uses `ApplicationFrameHost.exe` rename trick for true topmost without Microsoft cert. Study: technique for fullscreen Now Playing overlay that works over games. |
| **DesktopClock** | Low | Lightweight always-on-top WPF overlay with tray, context menu, customization. Simpler reference for persistent desktop overlay with minimal footprint. |

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

### Quick Wins (Low effort, ship fast)
1. Gapless playback
2. Per-game effects presets
3. OBS text file export
4. Sleep timer
5. Fade to pause
6. Don't Play This Song skip list
7. "Surprise Me" button
8. Playback memory across sessions
9. Community download lists (JSON export/import)
10. Quick mute toggle
11. Copy song info to clipboard
12. ~~Open on KHInsider context menu~~ ❌ N/A — KHInsider source removed in v1.3.4
13. Total listening time tracker
14. Personal top charts (most-played games/songs)
15. Auto-skip short files
16. "Calm Down" mode
17. Audio ducking during game selection
18. Per-game visualizer theme
19. Now playing game cover
20. Song preview on hover
21. ~~Completion-status music filter~~ ✅ Shipped v1.3.4 as Nostalgia Mode + Nostalgia Playlist Mode
22. Series/franchise playlist
23. Era-based playlists
24. Auto-reverb by genre
25. Playtime-weighted shuffle
26. Playnite URI handler
27. Default music randomization
28. Shuffle indicator in Now Playing
29. Volume percentage in tooltip
30. No Music tag auto-apply after failed downloads
31. Replay current song
32. Song intro skip
33. Listening session recap
34. Auto-pause on battery saver

### Shipped Quick Wins (v1.3.4)
- ~~Radio Station mode (Full Library, Custom Folder, Custom Game Rotation, Completion Status Pool)~~
- ~~Game Property Filter (Platform / Genre / Source)~~
- ~~Filter Mode (play game music only when Playnite filter active)~~
- ~~Nostalgia Mode (Completion-status music filter)~~
- ~~Nostalgia Playlist Mode (Completion Status Pool default music source)~~

### Shipped in v1.3.3
- ~~Configurable fade curves (5 styles: Linear, Quadratic, Cubic, S-Curve, Logarithmic)~~

### Shipped Quick Wins (v1.3.2)
- ~~Global media key control~~
- ~~Taskbar thumbnail media controls (graduated)~~
- ~~Auto-cleanup empty folders~~
- ~~Playlist export (M3U)~~
- ~~Extended default music sources (Custom Folder, Random Game, Custom Game Rotation)~~
- ~~PS2 Menu Ambience bundled preset~~
- ~~Custom cookies file for yt-dlp~~
- ~~Per-tab reset buttons (settings quick reset per section)~~
- ~~Graduated 6 experimental features to stable~~
- ~~Improved default settings~~
- ~~Settings reorganization (dedicated Pauses tab)~~
- ~~Style preset tuning~~
- ~~Global settings reset rewrite~~

### Shipped Quick Wins (v1.3.1)
- ~~Install-aware auto-download~~
- ~~Auto-pause on idle/AFK~~
- ~~Auto-pause on another audio source~~
- ~~Stay paused on focus restore (#69)~~
- ~~Ignore brief focus loss (alt-tab)~~
- ~~Enhanced library statistics~~
- ~~Settings UI reorganization~~

### Shipped Quick Wins (v1.3.0)
- ~~Completion celebration~~
- ~~Song count badge in game menu~~
- ~~Notification sound on download complete~~
- ~~Default music indicator~~
- ~~Music folder size in game menu~~
- ~~Celebration toast notification~~
- ~~Auto-pause on system lock~~
- ~~Song progress indicator~~
- ~~Library statistics page~~

### High-Value Features (Medium effort, big impact)
1. Music Dashboard (central stats/library hub)
2. Windows SMTC (Win+G overlay + Bluetooth — media keys already shipped v1.3.2)
3. Per-game effects presets (Low effort but High impact)
4. Crossfade between games
5. ~~Radio Station mode~~ ✅ Shipped v1.3.4
6. Context-aware playlists from game metadata
7. DMCA-safe mode
8. Hotkey/StreamDeck local API
9. Discord Rich Presence
10. Cross-game volume normalization
11. Category-based default music
12. Developer/publisher playlist
13. Auto-pause on game audio detected
14. Auto-pause on screen off/display sleep

### Ambitious Differentiators (High effort, unique positioning)
1. Soundtrack matching for games without music
2. Multi-track layering per game
3. Ambient sound layers
4. Music-reactive desktop wallpaper
5. Sound effects pack system
6. Mood detection via audio analysis
7. AI playlist generation
