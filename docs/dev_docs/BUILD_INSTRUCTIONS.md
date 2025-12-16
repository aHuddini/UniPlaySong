# UniPlaySong - Build Instructions

## Prerequisites

1. **.NET SDK** (with .NET Framework 4.6.2 support)
   - Download from: https://dotnet.microsoft.com/download
   - Install .NET Framework 4.6.2 Developer Pack if needed
   - Verify installation: `dotnet --version`

2. **PowerShell** (for packaging script)
   - Windows PowerShell 5.1+ or PowerShell Core 7+
   - Verify installation: `$PSVersionTable.PSVersion`

3. **Internet connection** for NuGet package restoration
   - NuGet packages will be restored automatically

4. **Required NuGet Packages** (will be restored automatically):
   - PlayniteSDK (6.11.0.0)
   - Microsoft.CSharp (4.7.0)
   - HtmlAgilityPack (1.11.46)
   - System.Net.Http (4.3.4)
   - MaterialDesignThemes (4.7.0)
   - MaterialDesignColors (2.1.0)
   - Newtonsoft.Json (13.0.1)

## Building the Extension

### Recommended: Using dotnet CLI

The dotnet CLI is the recommended method for building the extension.

**Complete build and package workflow:**

```powershell
# Navigate to project directory
cd X:\Projects\UniPlaySong

# Clean previous builds
dotnet clean -c Release

# Restore NuGet packages
dotnet restore

# Build in Release configuration
dotnet build -c Release
```

**One-liner version:**

```powershell
cd X:\Projects\UniPlaySong; dotnet clean -c Release; dotnet restore; dotnet build -c Release
```

**Verify Output:**
- Check that `UniPlaySong.dll` is created in:
  ```
  bin\Release\net4.6.2\UniPlaySong.dll
  ```
- Verify dependencies are copied (HtmlAgilityPack.dll, MaterialDesignThemes.Wpf.dll, etc.)

### Alternative: Using Visual Studio

If you prefer using Visual Studio:

1. **Open the Solution**:
   ```
   Open UniPlaySong/UniPlaySong.sln in Visual Studio
   ```

2. **Restore NuGet Packages**:
   - Right-click solution → "Restore NuGet Packages"
   - Or: Tools → NuGet Package Manager → Package Manager Console
   - Run: `Update-Package -reinstall`

3. **Select Build Configuration**:
   - Configuration: **Release**
   - Platform: **AnyCPU**

4. **Build the Project**:
   - Build → Rebuild Solution (or Ctrl+Shift+B)
   - Verify no errors in Error List

5. **Verify Output**:
   - Check that `UniPlaySong.dll` is created in:
     ```
     bin\Release\net4.6.2\UniPlaySong.dll
     ```
   - Verify dependencies are copied (HtmlAgilityPack.dll, etc.)

### Alternative: Using MSBuild

```powershell
cd C:\Projects\UniPSound\UniPlaySong
msbuild UniPlaySong.csproj /p:Configuration=Release /p:Platform=AnyCPU /t:Rebuild
```

## Verifying the Build

After building, verify:

1. **Main DLL exists**:
   - `bin\Release\net4.6.2\UniPlaySong.dll`

2. **Dependencies are present**:
   - `bin\Release\net4.6.2\HtmlAgilityPack.dll`
   - `bin\Release\net4.6.2\MaterialDesignThemes.Wpf.dll`
   - `bin\Release\net4.6.2\MaterialDesignColors.dll`
   - `bin\Release\net4.6.2\Newtonsoft.Json.dll`
   - Other dependencies as needed
   
3. **SDL2 DLLs** (required for SDL2MusicPlayer):
   - `lib\SDL2.dll` - Must be present for packaging
   - `lib\SDL2_mixer.dll` - Must be present for packaging
   - These are automatically included in the package by `package_extension.ps1`

4. **No build errors**:
   - Check build output for errors or warnings
   - Verify all files are present before packaging

## Common Build Issues

### Issue: "Reference assemblies for .NETFramework,Version=v4.6.2 were not found"

**Solution**: Install .NET Framework 4.6.2 Developer Pack
- Download from: https://dotnet.microsoft.com/download/dotnet-framework/net462
- Install the Developer Pack (not just the runtime)

### Issue: "Unable to find package PlayniteSDK"

**Solution**: Configure NuGet package source
1. Tools → NuGet Package Manager → Package Manager Settings
2. Package Sources → Ensure `nuget.org` is enabled
3. URL: `https://api.nuget.org/v3/index.json`

### Issue: Dependencies not copied to output

**Solution**: Verify project file has:
```xml
<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
```
(This should already be in the .csproj file)

## Packaging the Extension

After building, package the extension using the PowerShell script:

**Recommended method (with execution policy bypass):**

```powershell
# Navigate to project directory
cd X:\Projects\UniPlaySong

# Package the extension (bypasses execution policy)
powershell -ExecutionPolicy Bypass -File .\package_extension.ps1 -Configuration Release
```

**Alternative (if execution policy allows):**

```powershell
cd X:\Projects\UniPlaySong
.\package_extension.ps1 -Configuration Release
```

**Complete workflow (clean, build, and package):**

```powershell
cd C:\Projects\UniPSound\UniPlaySong
dotnet clean -c Release
dotnet restore
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File .\package_extension.ps1 -Configuration Release
```

**Note**: The packaging script automatically:
- Reads version from `version.txt` (single source of truth)
- Updates `AssemblyInfo.cs` with current version
- Updates `extension.yaml` with current version
- Creates `.pext` package with correct version in filename
- Copies all required DLLs and files to the package directory

**Output location:**
The packaged `.pext` file will be created in the project root directory with a name like:
```
UniPlaySong.a1b2c3d4-e5f6-7890-abcd-ef1234567890_1_0_6.pext
```

See `docs/VERSIONING.md` for details on the version management system.

## Next Steps

After successful build and packaging:
1. Verify the `.pext` file was created in the UniPSong directory
2. Install the generated `.pext` file in Playnite
3. Test the extension

## Project Structure

```
UniPlaySong/
├── bin/Release/net4.6.2/    # Build output (after building)
│   ├── UniPlaySong.dll      # Main extension DLL
│   └── [dependencies]        # NuGet package DLLs
├── lib/                      # SDL2 native DLLs (required)
│   ├── SDL2.dll             # SDL2 core library
│   └── SDL2_mixer.dll       # SDL2 audio mixer
├── package/                  # Package output (created by packaging script)
│   ├── UniPlaySong.dll      # Main extension DLL
│   ├── extension.yaml       # Extension manifest
│   ├── icon.png             # Extension icon
│   └── [all dependencies]   # All required DLLs
├── version.txt              # Version number (single source of truth)
├── AssemblyInfo.cs          # Assembly metadata (auto-updated by scripts)
├── extension.yaml           # Extension manifest (auto-updated by scripts)
├── icon.png                 # Extension icon
├── UniPlaySong.csproj       # Project file
├── UniPlaySong.sln          # Solution file
└── package_extension.ps1    # Packaging script (handles version updates)
```

