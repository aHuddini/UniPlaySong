# Material Design Dependency Analysis

## Current Status: DLLs Present But Still Failing to Load

### Verified Facts

1. **All Required DLLs Are Present in Package:**
   - ✅ MaterialDesignThemes.Wpf.dll (9,240 KB)
   - ✅ MaterialDesignColors.dll (295.5 KB)
   - ✅ Microsoft.Xaml.Behaviors.dll (141.88 KB)
   - ✅ HtmlAgilityPack.dll (165.5 KB)

2. **DLLs Load Successfully in PowerShell:**
   - MaterialDesignThemes.Wpf.dll loads correctly
   - MaterialDesignColors.dll loads correctly
   - Microsoft.Xaml.Behaviors.dll loads correctly

3. **Assembly References Verified:**
   - MaterialDesignThemes.Wpf references:
     - Microsoft.Xaml.Behaviors 1.1.0.0 (PublicKeyToken: B03F5F7F11D50A3A)
     - MaterialDesignColors 1.0.1.0 (PublicKeyToken: DF2A72020BD7962A)
   - We have Microsoft.Xaml.Behaviors 1.1.39 (should be compatible with 1.1.0.0)

### Root Cause Hypothesis

The error occurs in **Playnite's extension loading context**, not in standard .NET loading. Possible causes:

1. **AppDomain Isolation**: Playnite may load extensions in a separate AppDomain with different assembly resolution
2. **Probing Path**: Playnite might not probe the extension directory for dependencies
3. **Binding Redirects**: Version mismatches might require explicit binding redirects
4. **Resource Loading**: Material Design resources (XAML) might not be accessible in Playnite's context

### Solutions to Try

#### Option 1: Add Assembly Resolution Handler

Add custom assembly resolution in `UniPlaySong.cs` constructor:

```csharp
AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
{
    string assemblyName = new AssemblyName(args.Name).Name;
    string extensionPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    string dllPath = Path.Combine(extensionPath, $"{assemblyName}.dll");
    
    if (File.Exists(dllPath))
    {
        return Assembly.LoadFrom(dllPath);
    }
    return null;
};
```

#### Option 2: Verify DLL Locations

Ensure all DLLs are in the same directory as `UniPlaySong.dll` in the installed extension.

#### Option 3: Check Playnite Logs

Check `%APPDATA%\Playnite\playnite.log` for detailed error messages about which dependency is missing.

#### Option 4: Try Older Material Design Version

If 4.7.0 still fails, try 4.6.0 or 4.5.0 which have fewer dependencies.

#### Option 5: Remove Material Design

As a last resort, revert to standard WPF with custom styling (see `DIALOG_ALTERNATIVES.md`).

### DLL Management Structure

We've created `lib\dll\` as a one-stop-shop for all extension DLLs:

```
UniPSong/
├── lib/
│   ├── dll/                    # All extension DLLs (one-stop-shop)
│   │   ├── HtmlAgilityPack.dll
│   │   ├── MaterialDesignColors.dll
│   │   ├── MaterialDesignThemes.Wpf.dll
│   │   ├── Microsoft.Xaml.Behaviors.dll
│   │   └── README.md
│   ├── SDL2.dll                # SDL2 native DLLs
│   └── SDL2_mixer.dll
```

The packaging script (`package_extension.ps1`) now:
1. Uses `lib\dll\` as the **primary source** for DLLs
2. Falls back to build output if not found in `lib\dll\`
3. Explicitly lists all required DLLs to ensure nothing is missed

### Next Steps

1. **Add Assembly Resolution Handler** - Try Option 1 above
2. **Check Playnite Logs** - Get detailed error information
3. **Verify Installed Package** - Check if DLLs are in correct location after installation
4. **Test with Assembly Resolution** - See if custom resolution fixes the issue

### Files Modified

- `package_extension.ps1` - Updated to use `lib\dll\` as primary source
- `lib\dll\README.md` - Documentation for DLL management
- `lib\dll\*.dll` - All required extension DLLs

