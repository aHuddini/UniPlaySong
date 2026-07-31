# Play Only on Game Select [Fullscreen Mode] — Design

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** In Fullscreen mode, game-specific music only plays when the user explicitly selects a game (A button). While browsing with D-pad, default/ambient music plays instead.

**Architecture:** Intercept A/B button presses in the existing `OnControllerButtonStateChanged` SDK override. Track which game was explicitly selected. Gate `HandleGameSelected` to use default music when browsing vs game-specific music when selected.

**Tech Stack:** C# / .NET 4.6.2 / WPF / Playnite SDK 6.15

---

## Behavior

- Setting enabled + browsing with D-pad → default music plays (or continues playing)
- Setting enabled + A button pressed → switches to the selected game's specific music
- Setting enabled + B button (back to list) → switches back to default music
- Setting disabled → current behavior (music changes on every game hover)
- Only affects Fullscreen mode. Desktop mode behaves normally regardless of setting.
- No impact on Radio Mode, Filter Mode, or other play states — this is a separate toggle

## Setting

- **Property:** `PlayOnlyOnGameSelect` (bool, default false)
- **Label:** "Play Only on Game Select [Fullscreen Mode]"
- **Location:** Playback tab, near existing play method options
- **Reset:** Included in ResetPlaybackTab_Click

## Implementation

1. New setting property + backing field in `UniPlaySongSettings.cs`
2. Checkbox in `UniPlaySongSettingsView.xaml` (Playback tab)
3. Reset handler update in `UniPlaySongSettingsView.xaml.cs`
4. A/B detection in `UniPlaySong.cs` `OnControllerButtonStateChanged` — set/clear explicit select state
5. `MusicPlaybackCoordinator.HandleGameSelected` — when setting enabled in Fullscreen, play default music on hover, game-specific music only for explicitly selected game

## Files to Modify

| File | Change |
|------|--------|
| `UniPlaySongSettings.cs` | New `PlayOnlyOnGameSelect` property + backing field |
| `UniPlaySongSettingsView.xaml` | Checkbox in Playback tab |
| `UniPlaySongSettingsView.xaml.cs` | Reset handler |
| `UniPlaySong.cs` | A/B button handling in controller override |
| `MusicPlaybackCoordinator.cs` | `HandleGameSelected` gating logic |
| `MusicPlaybackService.cs` | May need method to trigger default music explicitly |
