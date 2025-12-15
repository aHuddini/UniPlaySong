# UniPlaySong - Build Instructions

## Prerequisites

1. **Visual Studio 2019 or later** (with .NET Framework 4.6.2 support)
   - OR .NET SDK with MSBuild
   - Install .NET Framework 4.6.2 Developer Pack if needed

2. **Internet connection** for NuGet package restoration
   - Ensure `https://api.nuget.org/v3/index.json` is enabled in Visual Studio's NuGet package sources

3. **Required NuGet Packages** (will be restored automatically):
   - PlayniteSDK (6.11.0.0)
   - Microsoft.CSharp (4.7.0)
   - HtmlAgilityPack (1.11.46)
   - System.Net.Http (4.3.4)

## Building the Extension

### Option 1: Using Visual Studio (Recommended)

1. **Open the Solution**:
   ```
   Open UniPSong/UniPlaySong.sln in Visual Studio
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

### Option 2: Using Command Line (MSBuild)

```powershell
cd UniPSong
msbuild UniPlaySong.csproj /p:Configuration=Release /p:Platform=AnyCPU /t:Rebuild
```

### Option 3: Using dotnet CLI

```powershell
cd UniPSong
dotnet build UniPlaySong.csproj -c Release
```

## Verifying the Build

After building, verify:

1. **Main DLL exists**:
   - `bin\Release\net4.6.2\UniPlaySong.dll`

2. **Dependencies are present**:
   - `bin\Release\net4.6.2\HtmlAgilityPack.dll`
   - Other dependencies as needed
   
3. **SDL2 DLLs** (required for SDL2MusicPlayer):
   - `lib\SDL2.dll` - Must be present for packaging
   - `lib\SDL2_mixer.dll` - Must be present for packaging
   - These are automatically included in the package by `package_extension.ps1`

3. **No build errors**:
   - Check Visual Studio Error List
   - Check Output window for warnings

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

After building, package the extension:

```powershell
cd UniPSong
.\package_extension.ps1
```

**Note**: The packaging script automatically:
- Reads version from `version.txt` (single source of truth)
- Updates `AssemblyInfo.cs` with current version
- Updates `extension.yaml` with current version
- Creates `.pext` package with correct version in filename

See `docs/VERSIONING.md` for details on the version management system.

## Next Steps

After successful build and packaging:
1. Verify the `.pext` file was created in the UniPSong directory
2. Install the generated `.pext` file in Playnite
3. Test the extension

## Project Structure

```
UniPSong/
├── bin/Release/net4.6.2/    # Build output (after building)
│   ├── UniPlaySong.dll      # Main extension DLL
│   └── [dependencies]        # NuGet package DLLs
├── lib/                      # SDL2 native DLLs (required)
│   ├── SDL2.dll             # SDL2 core library
│   └── SDL2_mixer.dll       # SDL2 audio mixer
├── version.txt              # Version number (single source of truth)
├── AssemblyInfo.cs          # Assembly metadata (auto-updated by scripts)
├── extension.yaml           # Extension manifest (auto-updated by scripts)
├── UniPlaySong.csproj       # Project file
├── UniPlaySong.sln          # Solution file
└── package_extension.ps1    # Packaging script (handles version updates)
```

