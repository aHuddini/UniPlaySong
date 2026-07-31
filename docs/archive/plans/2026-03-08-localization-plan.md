# Localization Support — Implementation Plan

**Created:** 2026-03-08
**Status:** Infrastructure complete. String extraction deferred until a translator is available.

---

## Background

UniPlaySong has ~500-600 hardcoded user-facing strings across C# and XAML. As of v1.3.5, the localization infrastructure has been established — the dictionary loading system, English resource file, and string helper are in place. No strings have been extracted yet.

**Infrastructure shipped in v1.3.5:**
- `src/Localization/en_US.xaml` — English ResourceDictionary (canonical reference for all keys)
- `src/Common/ResourceProvider.cs` — `GetString(key)` helper with safe fallback to key name
- `UniPlaySong.cs` `LoadLocalization()` — locale detection at startup, merges correct dictionary into `Application.Current.Resources`

---

## How It Works

At startup, `LoadLocalization()` detects `CultureInfo.CurrentUICulture` (e.g. `fr-FR`), normalizes to `fr_FR`, then tries to load:
1. `pack://application:,,,/UniPlaySong;component/Localization/fr_FR.xaml`
2. `pack://application:,,,/UniPlaySong;component/Localization/fr.xaml`
3. Falls back to `en_US.xaml`

**In C#:**
```csharp
// Before:
playniteApi.Dialogs.ShowMessage($"No music folder exists yet for {game.Name}.", "UniPlaySong");

// After:
playniteApi.Dialogs.ShowMessage(
    string.Format(ResourceProvider.GetString("LOC_OpenMusicFolder_CreatePrompt"), game.Name),
    ResourceProvider.GetString("LOC_PluginName"));
```

**In XAML:**
```xml
<!-- Before: -->
<Button Content="Open Music Folder" .../>

<!-- After: -->
<Button Content="{DynamicResource LOC_OpenMusicFolder}" .../>
```

---

## What To Extract (Scope)

**Extract — essential, high-value:**
- All `ShowMessage` / `ShowErrorMessage` / `ShowQuestion` dialog text
- Settings tab headers (`General`, `Playback`, `Pauses`, etc.)
- Section headers within tabs
- Button labels
- Checkbox and option labels
- Key descriptions (the first sentence explaining a setting)

**Skip — not worth extracting:**
- Fine-print tooltip descriptions (gray sub-text under settings)
- Log/debug messages
- File extension strings (`.mp3`, `.flac`, etc.)
- Tag strings (`[UPS-HasMusic]`)
- Anything in `Constants.cs`
- FFmpeg/yt-dlp path labels and technical references

**Estimated scope after filtering:** ~250-350 strings (roughly half the codebase).

---

## Extraction Phases

### Phase 1 — C# dialogs (highest user impact, easiest to verify)

Do one file per commit. Build and test after each.

| File | Dialog calls | Notes |
|------|-------------|-------|
| `Menus/GameMenuHandler.cs` | ~69 | Largest concentration |
| `UniPlaySong.cs` | ~23 | Plugin-level messages |
| `Services/DownloadDialogService.cs` | ~19 | Download prompts |
| `Handlers/NormalizationDialogHandler.cs` | ~15 | Normalization messages |
| `Handlers/TrimDialogHandler.cs` | ~12 | Trim messages |
| `Handlers/AmplifyDialogHandler.cs` | ~10 | Amplify messages |
| Remaining services | ~90 | Lower priority |

For each string:
1. Add key + English value to `en_US.xaml` under the appropriate `<!-- Section -->` comment
2. Replace hardcoded string in C# with `ResourceProvider.GetString("LOC_KeyName")`
3. For dynamic strings with game names/counts: use `string.Format(ResourceProvider.GetString("LOC_Key"), value)`

### Phase 2 — Settings XAML (most volume, visual-only risk)

`UniPlaySongSettingsView.xaml` in order:
1. Tab headers (10 strings, high visibility)
2. Section headers within tabs
3. Button labels
4. Checkbox / radio option labels
5. Key descriptions (first sentence only)
6. Fine-print sub-descriptions (optional, lowest priority)

Replace `Text="..."` / `Content="..."` with `Content="{DynamicResource LOC_Key}"`.

### Phase 3 — Dialog XAML files

`BatchManualDownloadDialog.xaml`, `ControllerAmplifyDialog.xaml`, etc.
Same pattern as Phase 2, smaller files.

---

## Key Naming Convention

Format: `LOC_[Area]_[Description]`

| Area | Used for |
|------|---------|
| (none) | Truly global (OK, Cancel, Yes, No) |
| `Tab_` | Settings tab names |
| `Section_` | Settings section headers |
| `MusicFolder_` | Open/create music folder dialogs |
| `GameIndex_` | Game index file dialogs |
| `Normalization_` | Normalization dialogs |
| `Trim_` | Trim dialogs |
| `Amplify_` | Amplify dialogs |
| `Download_` | Download dialogs |
| `Cleanup_` | Cleanup dialogs |
| `Migration_` | Migration dialogs |

**Keys must be descriptive enough** that a translator understands the context without reading C# code. Bad: `LOC_Msg_047`. Good: `LOC_MusicFolder_CreatePrompt`.

---

## How to Add a New Language

1. Copy `src/Localization/en_US.xaml`
2. Rename to your locale code (e.g. `fr_FR.xaml`, `de_DE.xaml`, `ja_JP.xaml`)
3. Translate the string values — **do not change `x:Key` names**
4. Do not translate `{0}`, `{1}` tokens — these are runtime placeholders
5. Place the file in `src/Localization/` and submit a pull request

No C# knowledge required. The plugin will auto-detect and load the file.

---

## When to Execute

**Trigger conditions (either/or):**
- A user requests a specific language and offers to provide the translation
- A large settings refactor is already in progress (files are open anyway)

**Do not** extract strings proactively without a translator ready — it's a week of mechanical work with no immediate user benefit.

---

## Files Modified by Infrastructure (v1.3.5)

| File | Change |
|------|--------|
| `src/Localization/en_US.xaml` | New — English ResourceDictionary with ~30 seed strings |
| `src/Common/ResourceProvider.cs` | New — `GetString()` helper |
| `src/UniPlaySong.cs` | Added `LoadLocalization()` call in `OnApplicationStarted` |
