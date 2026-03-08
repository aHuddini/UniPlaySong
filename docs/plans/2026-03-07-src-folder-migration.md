# src/ Folder Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Move all C# source code and project files into a `src/` subfolder to reduce root directory clutter from 37 entries to ~15.

**Architecture:** Git `mv` preserves history. The `.sln` file stays at the root (standard .NET convention) with its project reference updated to point to `src/UniPlaySong.csproj`. The packaging script uses `$projectRoot` for all paths — only `bin\` and `AssemblyInfo.cs` paths need updating to `src\bin\` and `src\AssemblyInfo.cs`.

**Tech Stack:** .NET 4.6.2 / MSBuild, PowerShell packaging script, git mv

---

## What moves into `src/`

These items move from root → `src/`:

**Folders:**
- `Audio/`
- `Common/`
- `Controls/`
- `DeskMediaControl/`
- `Downloaders/`
- `Handlers/`
- `Menus/`
- `Models/`
- `Monitors/`
- `Players/`
- `Services/`
- `ViewModels/`
- `Views/`

**Files:**
- `AssemblyInfo.cs`
- `UniPlaySong.cs`
- `UniPlaySong.csproj`
- `UniPlaySongSettings.cs`
- `UniPlaySongSettingsView.xaml`
- `UniPlaySongSettingsView.xaml.cs`
- `UniPlaySongSettingsViewModel.cs`

**Stays at root (not moved):**
- `UniPlaySong.sln` — stays at root (standard .NET convention)
- `extension.yaml`, `icon.png`, `LICENSE`, `version.txt` — packaging assets
- `AutoSearchDatabase/`, `DefaultMusic/`, `Jingles/` — runtime assets
- `Manifest/`, `scripts/`, `docs/`, `lib/` — tooling/docs
- `CHANGELOG.md`, `README.md` — repo docs
- `bin/`, `obj/`, `package/`, `pext/`, `backups/`, `build/` — gitignored outputs

---

## Task 1: Move source files into src/ using git mv

**Files:**
- Move: 13 source folders + 7 source files → `src/`

**Step 1: Create the src/ directory**

```bash
mkdir src
```

**Step 2: Move all source folders with git mv**

```bash
git mv Audio src/Audio
git mv Common src/Common
git mv Controls src/Controls
git mv DeskMediaControl src/DeskMediaControl
git mv Downloaders src/Downloaders
git mv Handlers src/Handlers
git mv Menus src/Menus
git mv Models src/Models
git mv Monitors src/Monitors
git mv Players src/Players
git mv Services src/Services
git mv ViewModels src/ViewModels
git mv Views src/Views
```

**Step 3: Move source files with git mv**

```bash
git mv AssemblyInfo.cs src/AssemblyInfo.cs
git mv UniPlaySong.cs src/UniPlaySong.cs
git mv UniPlaySong.csproj src/UniPlaySong.csproj
git mv UniPlaySongSettings.cs src/UniPlaySongSettings.cs
git mv UniPlaySongSettingsView.xaml src/UniPlaySongSettingsView.xaml
git mv UniPlaySongSettingsView.xaml.cs src/UniPlaySongSettingsView.xaml.cs
git mv UniPlaySongSettingsViewModel.cs src/UniPlaySongSettingsViewModel.cs
```

**Step 4: Verify git status shows all renames (no deletions/additions)**

```bash
git status
```

Expected: All lines show `renamed:` — no plain `deleted:` or `new file:` entries.

---

## Task 2: Update UniPlaySong.sln to point to src/

**Files:**
- Modify: `UniPlaySong.sln:6`

The `.sln` currently references `"UniPlaySong.csproj"`. Change it to `"src/UniPlaySong.csproj"`.

**Step 1: Edit the project reference line**

Find this line in `UniPlaySong.sln`:
```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "UniPlaySong", "UniPlaySong.csproj", "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
```

Replace with:
```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "UniPlaySong", "src/UniPlaySong.csproj", "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
```

---

## Task 3: Update src/UniPlaySong.csproj — fix asset paths

**Files:**
- Modify: `src/UniPlaySong.csproj`

The `.csproj` references `extension.yaml`, `icon.png`, `LICENSE`, and `AutoSearchDatabase\search_hints.json` as relative paths. These files stay at root, so from `src/` they need `../` prefix.

**Step 1: Update the `<None Include>` paths**

Find the `<ItemGroup>` with `<None Include=...>` entries and update to:

```xml
<ItemGroup>
    <None Include="..\extension.yaml">
        <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
    <None Include="..\icon.png">
        <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
    <None Include="..\LICENSE">
        <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
    <None Include="..\AutoSearchDatabase\search_hints.json">
        <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
</ItemGroup>
```

---

## Task 4: Update scripts/package_extension.ps1 — fix two paths

**Files:**
- Modify: `scripts/package_extension.ps1`

Two paths hardcode the project root as the base for build output and AssemblyInfo:

1. **Line 38** — `$assemblyInfoPath` currently resolves to `$projectRoot\AssemblyInfo.cs`. After the move it must be `$projectRoot\src\AssemblyInfo.cs`.

2. **Line 65** — `$outputDir` is `"bin\$Configuration\net4.6.2"`. After the move it must be `"src\bin\$Configuration\net4.6.2"`.

**Step 1: Update $assemblyInfoPath**

Find:
```powershell
$assemblyInfoPath = Join-Path $projectRoot "AssemblyInfo.cs"
```

Replace with:
```powershell
$assemblyInfoPath = Join-Path $projectRoot "src\AssemblyInfo.cs"
```

**Step 2: Update $outputDir**

Find:
```powershell
$outputDir = "bin\$Configuration\net4.6.2"
```

Replace with:
```powershell
$outputDir = "src\bin\$Configuration\net4.6.2"
```

---

## Task 5: Update .gitignore — fix bin/ and obj/ paths

**Files:**
- Modify: `.gitignore`

Currently `.gitignore` has `bin/` and `obj/` at the top. These will now live under `src/bin/` and `src/obj/`. The bare `bin/` entry will still match `src/bin/` because gitignore patterns match any path component by default — so no change needed here.

**Step 1: Verify gitignore still works**

```bash
git status
```

Expected: `src/bin/` and `src/obj/` do NOT appear as untracked (they're gitignored via the `bin/` and `obj/` patterns).

If they DO appear, add:
```
src/bin/
src/obj/
```

to `.gitignore`.

---

## Task 6: Update CLAUDE.md — fix build commands

**Files:**
- Modify: `CLAUDE.md`

The build commands in the `## Build` section currently are:
```bash
dotnet clean -c Release
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

These run from the project root. `dotnet clean` and `dotnet build` with no path argument will look for a `.sln` or `.csproj` in the current directory. After the move, `UniPlaySong.sln` remains at root, so these commands still work as-is.

**Step 1: Verify this is actually the case**

Run from project root:
```bash
dotnet build -c Release
```

Expected: Finds `UniPlaySong.sln` → finds `src/UniPlaySong.csproj` → builds successfully. Output DLL lands in `src/bin/Release/net4.6.2/UniPlaySong.dll`.

No CLAUDE.md changes needed if build succeeds. If the build fails with "no project found", update build commands to:
```bash
dotnet clean src/ -c Release
dotnet build src/ -c Release
```

---

## Task 7: Full build + package verification

**Step 1: Clean build**

```bash
dotnet clean -c Release && dotnet build -c Release
```

Expected: `Build succeeded` with 0 errors, 0 warnings. Output at `src/bin/Release/net4.6.2/UniPlaySong.dll`.

**Step 2: Package**

```bash
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1 -Configuration Release
```

Expected: `PACKAGE CREATED SUCCESSFULLY!` with `.pext` file in `pext/`.

**Step 3: Verify root is clean**

```bash
ls
```

Expected: Root has ~15 entries. No source folders or `.cs`/`.csproj` files at root level.

---

## Task 8: Update docs/dev_docs/BUILD_INSTRUCTIONS.md

**Files:**
- Modify: `docs/dev_docs/BUILD_INSTRUCTIONS.md`

This file has hardcoded paths that reference the pre-migration layout. After moving source to `src/`, these need updating.

**Step 1: Update all `bin\Release\net4.6.2` references → `src\bin\Release\net4.6.2`**

There are 5 occurrences across the "Verifying the Build" and "Alternative: Using Visual Studio" sections. Replace all instances:

Find: `bin\Release\net4.6.2\UniPlaySong.dll`
Replace: `src\bin\Release\net4.6.2\UniPlaySong.dll`

Find: `bin\Release\net4.6.2\HtmlAgilityPack.dll`
Replace: `src\bin\Release\net4.6.2\HtmlAgilityPack.dll`

And so on for all `bin\Release\net4.6.2\*` paths (use replace_all on the prefix).

**Step 2: Update the MSBuild command**

Find:
```
msbuild UniPlaySong.csproj /p:Configuration=Release /p:Platform=AnyCPU /t:Rebuild
```

Replace with:
```
msbuild src\UniPlaySong.csproj /p:Configuration=Release /p:Platform=AnyCPU /t:Rebuild
```

**Step 3: Update the Project Structure tree**

Find the tree block starting with `├── bin/Release/net4.6.2/` and replace with:
```
├── src/                          # All C# source code
│   ├── bin/Release/net4.6.2/    # Build output (after building)
│   │   ├── UniPlaySong.dll      # Main extension DLL
│   │   └── [dependencies]       # NuGet package DLLs
│   ├── UniPlaySong.csproj       # Project file
│   ├── AssemblyInfo.cs          # Assembly metadata (auto-updated by scripts)
│   └── [source folders]         # Audio, Common, Controls, etc.
```

Remove the standalone `AssemblyInfo.cs`, `UniPlaySong.csproj` lines from the root level.

**Step 4: Update the "Open solution" reference**

Find:
```
Open UniPlaySong/UniPlaySong.sln in Visual Studio
```

This is fine as-is — `.sln` stays at root.

---

## Task 9: Update memory and commit

**Step 1: Update memory file**

In `C:\Users\asad2\.claude\projects\c--Projects-UniPSound-UniPlaySong\memory\MEMORY.md`, update the build workflow note to clarify that source lives in `src/` and output lands in `src/bin/`. Also note that `msbuild` invocations should use `src\UniPlaySong.csproj`.

**Step 2: Commit**

```bash
git add -A
git commit -m "refactor: move source code into src/ subfolder"
```

**Step 3: Push to dev and merge to main**

```bash
git push origin dev
git checkout main
git merge dev
git push origin main
git checkout dev
```

---

## Rollback

If anything breaks irreversibly:

```bash
git reset --hard HEAD
```

This restores all files to their pre-move state since nothing has been committed yet until Task 8.
