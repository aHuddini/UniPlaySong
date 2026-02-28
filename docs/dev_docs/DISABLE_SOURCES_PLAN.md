# Plan: Disable KHInsider and Zophar Download Sources

## Context

GitHub suspended the account (no notice given, discovered via 404 on profile page). After analysis with the Playnite developer, the first remediation step is disabling KHInsider and Zophar as download sources. These involve direct HTML scraping of third-party websites using HtmlAgilityPack.

YouTube and SoundCloud are left untouched — they delegate entirely to yt-dlp (a user-installed external tool). UniPlaySong never downloads content itself for those sources.

**Approach:** Comment out / dead code. Don't delete the downloader classes — just prevent them from being instantiated or called. Quick and fully reversible.

---

## Files to Modify

### 1. `Downloaders/DownloadManager.cs` — 5 edits

**a) Constructor (line 47-48):** Comment out instantiation, set to null
```csharp
// DISABLED: KHInsider and Zophar sources temporarily removed (GitHub TOS review)
// _khDownloader = new KHInsiderDownloader(httpClient, htmlWeb, errorHandler);
// _zopharDownloader = new ZopharDownloader(httpClient, htmlWeb, errorHandler);
_khDownloader = null;
_zopharDownloader = null;
```

**b) GetAlbumsForGameInternal Priority 1 block (lines 100-123):** Wrap in `if (_khDownloader != null)` guard
```csharp
// === PRIORITY 1: KHInsider — DISABLED ===
if (_khDownloader != null)
{
    // ... existing KHInsider block stays as-is inside guard ...
}
```

**c) GetAlbumsForGameInternal Priority 2 block (lines 125-147):** Same null guard pattern
```csharp
// === PRIORITY 2: Zophar — DISABLED ===
if (_zopharDownloader != null)
{
    // ... existing Zophar block stays as-is inside guard ...
}
```

**d) GetHintAlbums KHInsider hint (lines 1067-1079):** Guard with null check
```csharp
if (_khDownloader != null && !string.IsNullOrWhiteSpace(hint.KHInsiderAlbum))
```

**e) GetDownloaderForSource Source.All fallback (line 1122-1123):** Change from `_khDownloader` to `_ytDownloader`
```csharp
case Source.All:
    return _ytDownloader;  // was _khDownloader
```

**Why these are sufficient:** `GetDownloaderForSource` for `Source.KHInsider` / `Source.Zophar` already returns the field (now null), and all callers handle null with "No downloader available" warnings. `BestAlbumPick` will have empty `khAlbums` / `zopharAlbums` lists — the LINQ `Where` filters produce empty collections harmlessly.

### 2. `Services/DownloadDialogService.cs` — 1 edit

**Source selection dialog (lines 116-124):** Remove KHInsider and Zophar options from user-facing menu
```csharp
var sourceOptions = new List<Playnite.SDK.GenericItemOption>
{
    // DISABLED: KHInsider and Zophar temporarily removed (GitHub TOS review)
    // new Playnite.SDK.GenericItemOption("KHInsider", "Download from KHInsider (Game soundtracks)"),
    // new Playnite.SDK.GenericItemOption("Zophar", "Download from Zophar (Video game music archive)"),
    new Playnite.SDK.GenericItemOption("YouTube",
        youtubeConfigured
            ? "Download from YouTube (Playlists and videos)"
            : "Download from YouTube (Playlists and videos) - yt-dlp/ffmpeg required for downloads")
};
```

### 3. `UniPlaySongSettingsView.xaml` — 1 edit

**Bulk Download section (lines 2599-2613):** Disable the "Download Music for All Games" button and section. Comment out or collapse.
```xml
<!-- DISABLED: Bulk download temporarily removed (GitHub TOS review) -->
<!--
<Separator Margin="0,15"/>

<TextBlock Text="Bulk Download" FontSize="14" FontWeight="SemiBold" Margin="0,10,0,10"/>

<StackPanel Orientation="Horizontal" Margin="0,10">
    <Button Content="Download Music for All Games"
            Width="220"
            Margin="0,0,10,0"
            Command="{Binding DownloadMusicForAllGamesCommand}"/>
    <TextBlock Text="Download music for all games that don't have any yet."
               VerticalAlignment="Center"
               FontSize="11"
               Foreground="Gray"
               TextWrapping="Wrap"/>
</StackPanel>
-->
```

### 4. `UniPlaySongSettingsView.xaml` — Search Hints section (1 edit, applied)

**Search Hints Database UI (lines ~2671-2721):** Comment out GitHub download button, revert-to-bundled button, auto-check checkbox. Keep status display and "Open Database Folder" button.

### 5. `UniPlaySong.cs` — Startup auto-check (1 edit, applied)

**`OnApplicationStarted` (line 529-532):** Comment out the `CheckForHintsUpdatesAsync()` call.

### 6. `UniPlaySongSettingsView.xaml.cs` — Reset handler (1 edit, applied)

**`ResetSearchTab_Click` (line 243):** Comment out `AutoCheckHintsOnStartup` reset line.

### 7. `UniPlaySongSettingsViewModel.cs` — Status text (1 edit, applied)

**`UpdateHintsDatabaseStatus` (line 1030):** Changed "Using bundled database (not downloaded from GitHub)" → "Using bundled database".

### 8. Files that DON'T change

- **KHInsiderDownloader.cs / ZopharDownloader.cs** — files stay in repo, just never instantiated
- **Source.cs enum** — `KHInsider` and `Zophar` values stay (prevents compile errors)
- **UniPlaySongSettings.cs** — no download source settings exist, nothing to change
- **search_hints.json** — KHInsider hints stay in file but guarded by null check; silently skipped at runtime
- **SearchHintsService.cs** — `DownloadHintsFromGitHub()` and `CheckForHintsUpdates()` methods stay but are never called

---

## Verification

1. `dotnet clean -c Release && dotnet build -c Release` — must compile clean
2. `powershell -ExecutionPolicy Bypass -File package_extension.ps1` — must package
3. Grep for `new KHInsiderDownloader` and `new ZopharDownloader` — should only appear in commented-out lines
4. Grep for `_khDownloader =` and `_zopharDownloader =` — should only assign `null`
5. Grep for `CheckForHintsUpdatesAsync` — should only appear in commented-out lines and the method definition

---

## Risk Analysis (from full codebase audit)

### What was flagged as risky
- **KHInsider/Zophar scraping** — direct HTML scraping with HtmlAgilityPack, User-Agent spoofing ("to avoid blocking")
- **YouTubeClient.cs** — reverse-engineered `youtubei/v1/search` and `youtubei/v1/next` internal API (search/metadata only, not downloading)
- **YouTubeDownloader.cs** — passes `--extractor-args` anti-bot flags and `--cookies-from-browser` to yt-dlp
- **Deleted video files in git history** — ~37MB of DEMOClip*.mp4 still in git object store

### What's NOT risky
- No secrets/credentials in codebase
- Licensed DefaultMusic MP3s (Pixabay Content License)
- SDL2/NuGet DLLs (all open-source, properly sourced)
- SoundCloud (hints-only, delegates to yt-dlp)

### Potential future actions (not in this plan)
- Purge DEMOClip*.mp4 from git history via `git filter-repo`
- Remove YouTubeClient.cs reverse-engineered API (replace with official YouTube Data API v3)
- Strip anti-bot flags from yt-dlp command construction
- Remove cookie harvesting support
