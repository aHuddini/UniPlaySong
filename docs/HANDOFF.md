# Handoff — settings revamp

**Temporary.** Written 2026-08-16 as a continuity note because the `.remember`
rollup is producing no history files. Delete this once the revamp merges or the
history is back.

Branch: `feature/settings-rail` — pushed to `origin` at `488cb9f`, 46 commits
ahead of `main`. `main` and `dev` are untouched. Working tree clean.

---

## What this branch is

The settings window was a flat strip of ~15 tabs, several of them 700-1400 lines.
It is now a **two-level rail**: a vertical left rail of 10 groups, and a
horizontal top strip of pages within each group. Every page is its own file under
`src/Controls/Settings/`, restyled against one design system.

### Rail groups and their pages

| Group | Pages |
|---|---|
| About | Overview · Credits · Links · Donate |
| Quick Start | Profiles |
| Setup | Tools · Downloads · Automations |
| General | Media Controls · Tagging · Performance |
| Playback | Startup · Trigger Rules · Default Music · Randomization · Global Overrides |
| Pauses | Common Events · External Audio |
| Live Effects | Volume · Fade Transitions · Live Effects · Visualizers |
| Gamification | Library Events · PlayniteAchievements · ControlUp |
| Library | Statistics · Audio Editing · Audio Management |
| Advanced | Theme Support · Toast Notifications · Experimental · Migration · Backup · Cleanup · Debug |

Shell: `src/UniPlaySongSettingsView.xaml` decides which groups exist and which
pages sit in each.

---

## Rules that are easy to break

**Reset is per rail group, and no code writes defaults down.**
`SettingsResetService` copies values from a pristine `UniPlaySongSettings`, so
changing a default means editing the backing field only. Every setting must be
filed in `SettingsGroups.Map` or `NeverReset` — `SettingsResetCoverageTests`
fails by name if you add one and forget.

**Moving a setting's UI means moving its map entry.** The map decides which
group's Reset button owns a setting. Move a toggle to another page without
moving its entry and the wrong group's Reset silently clears it. This bit us
once already (`AutoDeleteMusicOnGameRemoval`, Advanced → Setup).

**A section bar labels content, never a bare run of expanders.** `UpsSectionBar`
looks identical to a collapsible section header but has nothing to collapse, so
a bar sitting on nothing but expanders reads as a broken control. Four of these
shipped and had to be removed.

**Never `DisplayMemberPath` on a restyled ComboBox.** It templates the dropdown
items only; the closed box falls back to `ToString()` and shows a type name. Use
`ItemTemplate` — `UpsDisplayNameTemplate` or `UpsLabelTemplate`, chosen by the
property the items expose. The dropdown list still looks right, which is what
makes this easy to miss.

**Don't restructure page content when asked to split or restyle it.** Reordering
sections, regrouping controls under invented headings, or splitting one expander
into several is a separate decision from moving a page. This was reverted once.

Full reasoning: [dev_docs/SETTINGS_DESIGN.md](dev_docs/SETTINGS_DESIGN.md).
Shareable version: [dev_docs/SETTINGS_STYLE_KIT.md](dev_docs/SETTINGS_STYLE_KIT.md).

---

## Verifying a change

Build alone proves very little here. `ControlTemplate` and `StaticResource`
faults are **runtime**, not compile-time — a page can build clean and render
blank, black-on-black, or with a control that silently does nothing.

1. `dotnet clean -c Release`
2. `dotnet build -c Release`
3. `dotnet test tests\UniPlaySong.Tests.csproj -c Release` — 243 tests
4. `powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1`
5. **Render the page headlessly** and look at it. The scratchpad holds
   `render-live.ps1 -PageType <PageName> -Height <n>`, which loads the built DLL,
   instantiates the control and writes a PNG. This caught, among others: a toggle
   whose knob never moved, section headings that rendered with no text, a page
   that opened completely blank, and `UPS_BackgroundAudio` losing its underscore
   to an access-key mnemonic.

Run all of 1-4 after any code change. Never report done without them.

---

## Copy editing pipeline

Settings copy is edited in bulk, not string by string.

- **Extract**: `extract-copy.ps1` pulls every author-written string out of the
  35 pages into `copy.json` (~1007 rows, each with file, line, kind, text).
- **Edit**: a browser bench, published as an artifact, renders every string in an
  editable textarea grouped by page, with per-row checkboxes for selective
  export. It emits JSON carrying `file`, `line`, `old`, `new`.
- **Apply**: `apply-copy.ps1 -Json <file> -Apply` locates each edit **by line
  number and exact match**, never a global search-and-replace — several strings
  repeat across the window.

Both scripts live in this session's scratchpad, not the repo.

### The encoding trap, twice

`Get-Content` reads a BOM-less UTF-8 file as **ANSI** in PowerShell 5.1, so every
em dash and arrow arrives as mojibake and stops matching the XAML. A first apply
pass matched only 119/155 edits for exactly this reason. Read data files with
`[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($p))`. Keep `.ps1`
sources ASCII-only — the same fault breaks the parser when a script contains an
em dash.

Related: `Set-Location` moves PowerShell's location but **not** the .NET process
working directory. `[IO.File]` uses the process CWD while `Resolve-Path` uses the
PS location; that split truncated two files here. Use absolute paths.

Also: `2>&1` on a native exe in PS 5.1 wraps stderr in ErrorRecords and reports
failure on exit 0. A passing `node` run looked like a crash.

---

## Open items

- Automations subtitle still says yt-dlp/FFmpeg are required — no longer true of
  the auto-delete toggle that moved onto that page.
- Cleanup's orphaned-music button lost its "(Manual)" suffix when the automatic
  counterpart moved away. Restore or leave.
- Links page says Twitter / `twitter.com` rather than X.
- `src/DesignReworkIdea/` still has a csproj exclusion ItemGroup; delete both when
  the revamp ships.
- Element-content strings (a `TextBlock` with inline `Hyperlink` or mixed `Run`s)
  are **not** reachable from the copy bench — the extractor reads attributes only.
  Overview, Credits and Donate have several. Hand-edit those.

---

## Standing constraints

- **Never push to `archive`.** It is a frozen pre-1.3.4 preservation repo pinned
  at `4362cda`, not a mirror. Release pushes go to `origin` + `gitea` only.
- Commit when asked; **ask before pushing**, before touching `main` or `dev`, and
  before anything outward-facing.
- This branch has never been merged anywhere. Pushing it to origin is authorised;
  merging it is not.
