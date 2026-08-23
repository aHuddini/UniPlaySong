# Two-tier settings navigation

Working plan for restructuring UniPlaySong's settings from 16 flat tabs into a
category → page hierarchy, based on the view Mike Aniki proposed.

Reference file: `aniki-two-tier-settings-view.xaml.reference` (his file verbatim,
kept for diffing — not built).

## What he built

A `TabControl` of 5 categories across the top, each containing a nested
`TabControl` with `TabStripPlacement="Left"` for its pages. Both levels are
`TabItem`s with fully retemplated styles, so it reads as a nav rail rather than
as stock WPF tabs.

| Category | Pages |
|---|---|
| **Home** | Quick Start, Setup, About |
| **General** | General, Playback, Pauses |
| **Features** | Gamification, Downloads, Editing, Theme Support |
| **Audio** | Live Effects |
| **Advanced** | Music Library, Experimental, Migration, Backup, Cleanup, ~~Toast Notifications~~ (hidden) |

Two reusable styles carry the whole look:

- `SettingsCategoryTabItemStyle` — the top row
- `SettingsPageTabItemStyle` — the left rail

Both set `Template`, so they own the entire visual; `IsSelected`, `IsMouseOver`
and `IsEnabled` triggers do the state work.

## What's genuinely new, beyond rearranging

- **A Music Library page.** Library statistics (total songs, total size, games
  with music, average per game) pulled out of Experimental into their own page.
  Real content, already bound to existing `Stats*` properties.
- **An Audio category** holding only Live Effects today — room for the audio
  pages to grow into.
- **Toast Notifications hidden** rather than deleted (`Visibility="Collapsed"`).

## Structural questions to settle before building

These are the decisions worth making deliberately; the rest is mechanical.

### 1. Does the category row hold state, or is it pure navigation?

A nested `TabControl` remembers which page was last open per category. Opening
Settings therefore lands wherever the user last was — which is either helpful
(returns you to your work) or disorienting (never the same place twice). Worth
choosing rather than inheriting from whatever WPF does by default.

### 2. Where does Quick Start live?

It is currently the third tab and pitched as "start here". Under **Home** it
keeps that framing. The tension: Home also holds About, which is reference
material nobody needs on first run. A category whose three pages are
"do this first", "configure tools", and "release notes" is coherent only if
Home means "starting points" rather than "important things".

### 3. Is one-page Audio a category or a page?

**Audio → Live Effects** is a category containing a single page, so the left
rail renders with one entry. Either it is a placeholder for pages that do not
exist yet, or Live Effects belongs under Features and the category should wait
until there is a second page to justify it.

### 4. What happens to the 1.7.1 collapsible sections?

1.7.1 just added expandable sections inside long tabs (Bulk Actions, Library
Automations, Random Game Picker, and so on). With pages now shorter, some of
those groupings may be redundant — a page short enough to read at a glance does
not need its content hidden behind a disclosure triangle. Needs a pass per page,
not a blanket decision.

### 5. Does the nav survive Playnite's themes?

The proposal hardcodes 78 distinct colors and uses `DynamicResource` twice.
**This matches the current view exactly** (0 `DynamicResource`, same 78 colors),
so it is not a regression — but the two nav styles are the one part of the UI
where theme mismatch would be most visible, since they frame everything else.
Worth deciding whether the rail specifically should follow `TextBrush` and
friends even if page bodies stay hardcoded.

## Verified compatibility

Checked before building, so these are settled:

- Same `x:Class` (`UniPlaySong.UniPlaySongSettingsView`) and `xmlns:local`.
- All **18** event handlers it references exist in the code-behind (which has
  22). No code-behind changes needed to make it compile.
- Builds clean against current `main` and packages without error.

## Not yet verified

- Whether every binding resolves at runtime. XAML binding failures are silent —
  a control renders empty rather than erroring — so this needs a real install,
  not a build.
- Whether his file was based on the post-1.7.1 layout. It is 143 lines longer
  than current, but a longer file does not prove it carries every 1.7.1 change;
  a page-by-page diff is the only way to be sure nothing regressed.

## Build note

The preview `.pext` was built by temporarily swapping the file in, then
restoring:

```
pext/preview/UniPlaySong_1_7_1_ANIKI-UI-PREVIEW.pext
```

The reference copy in this folder carries a `.reference` extension so it is
never picked up as a source file by the build.
