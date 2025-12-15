# Version Management System

## Overview

UniPlaySong now uses a centralized versioning system. **`version.txt`** is the single source of truth for the version number.

## How It Works

### Single Source of Truth
- **`version.txt`** - Contains the version number (e.g., `1.0.3.2`)
- All scripts and code read from this file automatically

### Automatic Updates

When you update `version.txt`, the following are automatically updated:

1. **AssemblyInfo.cs** - Updated by `package_extension.ps1` before building
   - `AssemblyVersion`
   - `AssemblyFileVersion`
   - `AssemblyInformationalVersion`

2. **extension.yaml** - Updated by `package_extension.ps1` before packaging
   - `Version` field

3. **C# Code** - Reads version from `Assembly.GetExecutingAssembly().GetName().Version`
   - Log messages automatically use the correct version
   - No hardcoded version strings in code

4. **Package Filename** - Generated from version (e.g., `UniPlaySong.*_1_0_3_2.pext`)

5. **Backup Script** - Reads version from `version.txt` automatically

## Usage

### Updating the Version

1. **Edit `version.txt`** with the new version number:
   ```
   1.0.3.3
   ```

2. **Build and package**:
   ```powershell
   dotnet build -c Release
   .\package_extension.ps1
   ```

   The package script will:
   - Update `AssemblyInfo.cs` with the new version
   - Update `extension.yaml` with the new version
   - Create package with correct version in filename

### Creating a Backup

```powershell
.\create_backup.ps1
```

The backup script automatically reads the version from `version.txt`. You can also override it:
```powershell
.\create_backup.ps1 -Version 1.0.3.3
```

## Files That Use Version

| File | How It Gets Version |
|------|---------------------|
| `version.txt` | **Source of truth** - manually edited |
| `AssemblyInfo.cs` | Updated by `package_extension.ps1` |
| `extension.yaml` | Updated by `package_extension.ps1` |
| `UniPlaySong.cs` | Reads from `Assembly.GetExecutingAssembly().GetName().Version` |
| `package_extension.ps1` | Reads from `version.txt` |
| `create_backup.ps1` | Reads from `version.txt` |

## Benefits

1. **Single Update Point** - Change version in one place (`version.txt`)
2. **No Hardcoding** - Version strings are generated automatically
3. **Consistency** - All files always have the same version
4. **Less Error-Prone** - No risk of forgetting to update a file
5. **Automated** - Scripts handle all updates during build/package

## Version Format

Use semantic versioning: `MAJOR.MINOR.PATCH.BUILD`

Examples:
- `1.0.3.2` - Standard version
- `2.0.0.0` - Major version bump
- `1.0.3.3` - Patch version bump

## Migration Notes

If you're upgrading from the old system:
1. Create `version.txt` with current version
2. Remove any hardcoded version strings from scripts
3. The package script will update `AssemblyInfo.cs` and `extension.yaml` automatically

