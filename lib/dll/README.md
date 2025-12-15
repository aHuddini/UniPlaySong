# Extension DLLs - One-Stop-Shop

This directory contains all DLL dependencies required for the UniPlaySong extension.

## Purpose

This folder serves as a centralized location for all extension DLLs, making it easy to:
- Verify all dependencies are present
- Update DLLs when needed
- Ensure consistent packaging

## Required DLLs

### Material Design (UI Framework)
- **MaterialDesignThemes.Wpf.dll** - Material Design theme library (required)
- **MaterialDesignColors.dll** - Material Design color palette (required)
- **Microsoft.Xaml.Behaviors.dll** - XAML behaviors library (required by Material Design)

### Other Dependencies
- **HtmlAgilityPack.dll** - HTML parsing for downloaders

## SDL2 DLLs

SDL2 native DLLs are stored in the parent `lib\` directory:
- `lib\SDL2.dll` - SDL2 core library
- `lib\SDL2_mixer.dll` - SDL2 audio mixer

## Maintenance

When updating dependencies:
1. Build the project: `dotnet build -c Release`
2. Copy updated DLLs from `bin\Release\net4.6.2\` to this directory
3. Verify all required DLLs are present
4. Rebuild package: `.\package_extension.ps1`

## Packaging

The `package_extension.ps1` script uses this directory as the **primary source** for DLLs, falling back to build output if not found here.

## Version Information

- Material Design: 4.7.0 (compatible with .NET Framework 4.6.2)
- Material Design Colors: 2.1.0
- Microsoft.Xaml.Behaviors: 1.1.39 (required by Material Design 4.7.0)
- HtmlAgilityPack: 1.11.46

