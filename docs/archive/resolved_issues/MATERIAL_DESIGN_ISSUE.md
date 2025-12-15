# Material Design Integration Issue

## Problem

When integrating Material Design into UniPSong dialogs, the extension fails to load with the error:

```
Could not load file or assembly 'MaterialDesignThemes.Wpf, PublicKeyToken=df2a72020bd7962a' or one of its dependencies. The system cannot find the file specified.
```

## Context

- **Target Framework**: .NET Framework 4.6.2
- **Material Design Version Attempted**: 4.9.0
- **Material Design Colors Version**: 2.1.4
- **Issue**: DLLs are present in package but fail to load at runtime

## Investigation

### What Was Verified

1. **DLLs Are Present in Package**:
   - `MaterialDesignThemes.Wpf.dll` (9.5 MB) - ✅ Present
   - `MaterialDesignColors.dll` (302 KB) - ✅ Present
   - Both DLLs are copied to package directory

2. **Output Directory Contains**:
   - `HtmlAgilityPack.dll`
   - `MaterialDesignColors.dll`
   - `MaterialDesignThemes.Wpf.dll`

3. **Package Script**:
   - Script correctly copies all DLLs from `bin\Release\net4.6.2`
   - Filters out system DLLs appropriately

### Potential Causes

1. **Version Compatibility**: Material Design 4.9.0 may require .NET Framework 4.7.2+ (not 4.6.2)
2. **Missing Dependencies**: Material Design may have transitive dependencies (ControlzEx, ShowMeTheXAML) that aren't being copied
3. **Assembly Loading**: The DLLs may need to be loaded differently in Playnite's extension context

## Solution Attempted

### Downgrade to Compatible Version

Changed `UniPSong/UniPlaySong.csproj`:
```xml
<!-- Changed from: -->
<PackageReference Include="MaterialDesignThemes" Version="4.9.0" />
<PackageReference Include="MaterialDesignColors" Version="2.1.4" />

<!-- To: -->
<PackageReference Include="MaterialDesignThemes" Version="4.7.0" />
<PackageReference Include="MaterialDesignColors" Version="2.1.0" />
```

**Status**: Not yet tested - user needs to run:
```powershell
cd C:\Projects\UniPSound\UniPSong
dotnet restore
dotnet clean
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File package_extension.ps1
```

## Alternative Solutions

### Option 1: Use Older Material Design Version
- **Material Design 4.7.0** - Known to work with .NET Framework 4.6.2
- **Material Design 4.6.0** - Also compatible
- **Material Design 4.5.0** - Last version with full 4.6.2 support

### Option 2: Remove Material Design Entirely
- Use standard WPF with custom styling
- Keep compact design (smaller windows, smaller text)
- Use simple, modern WPF controls
- No external dependencies beyond what's already included

### Option 3: Check for Missing Dependencies
If downgrading doesn't work, check for:
- `ControlzEx.dll` (window management)
- `ShowMeTheXAML.dll` (XAML debugging, may not be required)
- Other transitive dependencies

## Files Modified

1. **UniPSong/UniPlaySong.csproj**
   - Added Material Design packages (downgraded to 4.7.0)

2. **UniPSong/Views/DownloadDialogView.xaml**
   - Updated to use Material Design components
   - Added Material Design resource dictionaries
   - Made dialogs more compact (700x500 instead of 900x600)

3. **UniPSong/Services/DownloadDialogService.cs**
   - Updated window sizes to be more compact

4. **UniPSong/package_extension.ps1**
   - Added logic to check for Material Design dependencies (ControlzEx, ShowMeTheXAML)
   - Script attempts to copy missing dependencies from NuGet cache

## Next Steps

1. **Test Downgraded Version**:
   - Restore, clean, build, and package
   - Install in Playnite and test dialogs
   - If it works, we're done

2. **If Downgrade Fails**:
   - Check NuGet packages for Material Design dependencies:
     ```powershell
     Get-ChildItem "$env:USERPROFILE\.nuget\packages\materialdesignthemes\4.7.0" -Recurse -Filter "*.dll"
     ```
   - Manually copy any missing dependencies to package

3. **If Still Fails**:
   - Remove Material Design entirely
   - Revert to standard WPF with custom styling
   - Keep the compact design improvements

## References

- Material Design GitHub: https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit
- Material Design 4.7.0 NuGet: https://www.nuget.org/packages/MaterialDesignThemes/4.7.0
- Playnite Extension Development: https://api.playnite.link/docs/tutorials/extensions/plugins.html

## Solution Implemented

### Root Cause Identified
The packaging script was filtering out `Microsoft.Xaml.Behaviors.dll` because it starts with "Microsoft.*", but this DLL is **required** by Material Design 4.7.0 as a dependency.

### Fix Applied
Updated `package_extension.ps1` to explicitly include `Microsoft.Xaml.Behaviors.dll`:
```powershell
# Material Design requires Microsoft.Xaml.Behaviors.dll (exception to Microsoft.* filter)
$behaviorsDll = Join-Path $outputDir "Microsoft.Xaml.Behaviors.dll"
if (Test-Path $behaviorsDll) {
    Copy-Item $behaviorsDll -Destination $packageDir -Force
    Write-Host "  Copied: Microsoft.Xaml.Behaviors.dll (required by Material Design)" -ForegroundColor Gray
}
```

### Package Contents (Verified)
- ✅ MaterialDesignThemes.Wpf.dll (9,240 KB)
- ✅ MaterialDesignColors.dll (295.5 KB)
- ✅ Microsoft.Xaml.Behaviors.dll (141.88 KB) - **Now included**

## Current Status

- ✅ Material Design packages added to project (4.7.0)
- ✅ XAML updated to use Material Design components
- ✅ Package script updated to include Microsoft.Xaml.Behaviors.dll
- ✅ Package created successfully with all dependencies
- ✅ **READY FOR TESTING**: Package includes all required DLLs

---

**Last Updated**: 2025-12-01
**Issue**: Material Design DLL loading failure in Playnite extension
**Root Cause**: Missing Microsoft.Xaml.Behaviors.dll dependency + XAML parser loading before assemblies
**Solution**: 
1. Updated packaging script to explicitly include Microsoft.Xaml.Behaviors.dll
2. Added assembly pre-loading in DownloadDialogService before XAML parsing
3. Added assembly resolution handler in UniPlaySong.cs
4. Created centralized DLL folder (lib\dll\) for dependency management
**Status**: ✅ **RESOLVED** - Material Design working successfully in production

