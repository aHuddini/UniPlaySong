# UniPlaySong Extension Packaging Script
# Creates a .pext package for Playnite installation
# 
# Usage: .\package_extension.ps1 [-Configuration Release|Debug]
# 
# Note: This script packages an already-built project. Build first with:
#   dotnet build -c Release

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  UniPlaySong Extension Packaging" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

# Read version from version.txt (single source of truth)
$versionFile = Join-Path $scriptDir "version.txt"
if (-not (Test-Path $versionFile)) {
    Write-Host "ERROR: version.txt not found. Please create it with the version number (e.g., 1.0.3.2)" -ForegroundColor Red
    exit 1
}
$versionFull = (Get-Content $versionFile -Raw).Trim()
# Convert version format: 1.0.3.2 -> 1_0_3_2 for filename
$version = $versionFull -replace '\.', '_'

# Update AssemblyInfo.cs with version from version.txt
$assemblyInfoPath = Join-Path $scriptDir "AssemblyInfo.cs"
if (Test-Path $assemblyInfoPath) {
    $assemblyInfoContent = Get-Content $assemblyInfoPath -Raw
    # Update version attributes if they exist
    if ($assemblyInfoContent -match '\[assembly:\s*AssemblyVersion\("[\d\.]+"\)\]') {
        $assemblyInfoContent = $assemblyInfoContent -replace '\[assembly:\s*AssemblyVersion\("[\d\.]+"\)\]', "[assembly: AssemblyVersion(`"$versionFull`")]"
    } else {
        # Add version attributes if they don't exist
        if ($assemblyInfoContent -notmatch 'AssemblyVersion') {
            $assemblyInfoContent += "`n`n// Version information - updated automatically by scripts from version.txt`n"
            $assemblyInfoContent += "[assembly: AssemblyVersion(`"$versionFull`")]`n"
            $assemblyInfoContent += "[assembly: AssemblyFileVersion(`"$versionFull`")]`n"
            $assemblyInfoContent += "[assembly: AssemblyInformationalVersion(`"$versionFull`")]"
        }
    }
    if ($assemblyInfoContent -match '\[assembly:\s*AssemblyFileVersion\("[\d\.]+"\)\]') {
        $assemblyInfoContent = $assemblyInfoContent -replace '\[assembly:\s*AssemblyFileVersion\("[\d\.]+"\)\]', "[assembly: AssemblyFileVersion(`"$versionFull`")]"
    }
    if ($assemblyInfoContent -match '\[assembly:\s*AssemblyInformationalVersion\("[\d\.]+"\)\]') {
        $assemblyInfoContent = $assemblyInfoContent -replace '\[assembly:\s*AssemblyInformationalVersion\("[\d\.]+"\)\]', "[assembly: AssemblyInformationalVersion(`"$versionFull`")]"
    }
    Set-Content -Path $assemblyInfoPath -Value $assemblyInfoContent -NoNewline
    Write-Host "Updated AssemblyInfo.cs with version $versionFull" -ForegroundColor Gray
    Write-Host ""
}

# Build paths
$outputDir = "bin\$Configuration\net4.6.2"
$packageDir = "package"
$extensionName = "UniPlaySong"
$extensionId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890"

# Verify DLL exists and show details
$dllPath = Join-Path $outputDir "UniPlaySong.dll"
if (-not (Test-Path $dllPath)) {
    Write-Host "ERROR: UniPlaySong.dll not found in $outputDir" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please build the project first:" -ForegroundColor Yellow
    Write-Host "  dotnet build -c $Configuration" -ForegroundColor White
    Write-Host ""
    exit 1
}

# Show DLL info to verify it's fresh
$dllInfo = Get-Item $dllPath
Write-Host "Found DLL: $($dllInfo.Name)" -ForegroundColor Green
Write-Host "  Size: $([math]::Round($dllInfo.Length/1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  Modified: $($dllInfo.LastWriteTime)" -ForegroundColor Gray
Write-Host ""

# Clean previous package
Write-Host "Preparing package directory..." -ForegroundColor Yellow
if (Test-Path $packageDir) {
    Remove-Item -Path $packageDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  Cleaned existing package directory" -ForegroundColor Gray
}
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

Write-Host "Copying extension files..." -ForegroundColor Yellow

# Update extension.yaml with current version before copying
$extensionYamlPath = Join-Path $scriptDir "extension.yaml"
if (Test-Path $extensionYamlPath) {
    $yamlContent = Get-Content $extensionYamlPath -Raw
    # Update version line if it exists
    if ($yamlContent -match "Version:\s*[\d\.]+") {
        $yamlContent = $yamlContent -replace "Version:\s*[\d\.]+", "Version: $versionFull"
        Set-Content -Path $extensionYamlPath -Value $yamlContent -NoNewline
        Write-Host "  Updated extension.yaml with version $versionFull" -ForegroundColor Gray
    }
}

# Copy core files
$coreFiles = @(
    "extension.yaml",
    "icon.png",
    "LICENSE",
    "search_hints.json"
)

foreach ($file in $coreFiles) {
    if (Test-Path $file) {
        Copy-Item $file -Destination $packageDir -Force
        Write-Host "  Copied file: $file" -ForegroundColor Gray
    } else {
        Write-Host "  WARNING: $file not found (optional)" -ForegroundColor Yellow
    }
}

# Copy main DLL
Copy-Item $dllPath -Destination $packageDir -Force
Write-Host "  Copied: UniPlaySong.dll" -ForegroundColor Gray

# Copy SDL2 native DLLs (required for SDL2MusicPlayer)
Write-Host "Copying SDL2 native DLLs..." -ForegroundColor Yellow
$sdl2Dlls = @("SDL2.dll", "SDL2_mixer.dll")
$sdl2Found = $false

# Try multiple locations
$searchPaths = @(
    # PlayniteSound output directory
    (Join-Path (Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) "src\PlayniteSound") "bin\Release\net4.6.2"),
    # PlayniteSound installed extension
    (Get-ChildItem -Path "$env:APPDATA\Playnite\Extensions" -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*Sound*" -or $_.Name -like "*9c960604*" } | Select-Object -First 1 | ForEach-Object { $_.FullName }),
    # Local lib directory
    (Join-Path $scriptDir "lib")
)

foreach ($dll in $sdl2Dlls) {
    $copied = $false
    foreach ($searchPath in $searchPaths) {
        if ($searchPath -and (Test-Path $searchPath)) {
            $sourcePath = Join-Path $searchPath $dll
            if (Test-Path $sourcePath) {
                Copy-Item $sourcePath -Destination $packageDir -Force
                Write-Host "  Copied: $dll from $sourcePath" -ForegroundColor Gray
                $sdl2Found = $true
                $copied = $true
                break
            }
        }
    }
    
    if (-not $copied) {
        Write-Host "  WARNING: $dll not found in any search location" -ForegroundColor Yellow
    }
}

if ($sdl2Found) {
    Write-Host "  SDL2 DLLs copied successfully" -ForegroundColor Green
} else {
    Write-Host "  ERROR: SDL2 DLLs not found. SDL2MusicPlayer will fail to initialize." -ForegroundColor Red
    Write-Host "    Please copy SDL2.dll and SDL2_mixer.dll to lib\ directory" -ForegroundColor Yellow
}

# Copy dependencies - Use lib\dll as primary source, fallback to build output
Write-Host "Copying dependencies..." -ForegroundColor Yellow
$dllLibDir = Join-Path $scriptDir "lib\dll"

# Required DLLs for the extension (explicit list to ensure nothing is missed)
$requiredDlls = @(
    "HtmlAgilityPack.dll",
    "MaterialDesignColors.dll",
    "MaterialDesignThemes.Wpf.dll",
    "Microsoft.Xaml.Behaviors.dll"
)

# System DLLs that Playnite provides (don't include)
$excludedDlls = @(
    "UniPlaySong.dll",
    "Playnite.SDK.dll",
    "System.Net.Http.dll",  # Playnite may provide this
    "System.Security.Cryptography.*"  # System DLLs
)

foreach ($dllName in $requiredDlls) {
    $copied = $false
    
    # Try lib\dll first (one-stop-shop for all DLLs)
    $libDllPath = Join-Path $dllLibDir $dllName
    if (Test-Path $libDllPath) {
        Copy-Item $libDllPath -Destination $packageDir -Force
        Write-Host "  Copied: $dllName from lib\dll" -ForegroundColor Gray
        $copied = $true
    } else {
        # Fallback to build output
        $outputDllPath = Join-Path $outputDir $dllName
        if (Test-Path $outputDllPath) {
            Copy-Item $outputDllPath -Destination $packageDir -Force
            Write-Host "  Copied: $dllName from build output" -ForegroundColor Gray
            $copied = $true
        }
    }
    
    if (-not $copied) {
        Write-Host "  WARNING: $dllName not found in lib\dll or build output" -ForegroundColor Yellow
    }
}

# Copy any other DLLs from build output that aren't in our required list or excluded
Write-Host "Copying additional dependencies from build output..." -ForegroundColor Yellow
$additionalDlls = Get-ChildItem -Path $outputDir -Filter "*.dll" | Where-Object { 
    $_.Name -ne "UniPlaySong.dll" -and 
    $_.Name -ne "Playnite.SDK.dll" -and
    $_.Name -notin $requiredDlls -and
    $_.Name -notlike "System.*" -and 
    $_.Name -notlike "WindowsBase.dll" -and
    $_.Name -notlike "PresentationCore.dll" -and
    $_.Name -notlike "PresentationFramework.dll"
}

if ($additionalDlls) {
    foreach ($dll in $additionalDlls) {
        $destPath = Join-Path $packageDir $dll.Name
        if (-not (Test-Path $destPath)) {
            Copy-Item $dll.FullName -Destination $destPath -Force
            Write-Host "  Copied: $($dll.Name)" -ForegroundColor Gray
        }
    }
}

# Material Design dependencies that might not be in output directory
Write-Host "Checking for Material Design dependencies..." -ForegroundColor Yellow
$materialDesignDeps = @("ControlzEx.dll", "ShowMeTheXAML.dll")
$nugetPaths = @(
    "$env:USERPROFILE\.nuget\packages",
    "$env:ProgramFiles\NuGet\Packages",
    "$env:ProgramFiles(x86)\NuGet\Packages"
)

foreach ($depName in $materialDesignDeps) {
    $found = $false
    foreach ($nugetPath in $nugetPaths) {
        if (Test-Path $nugetPath) {
            $depFile = Get-ChildItem -Path $nugetPath -Recurse -Filter $depName -ErrorAction SilentlyContinue | 
                Where-Object { $_.DirectoryName -like "*\lib\net*\*" -or $_.DirectoryName -like "*\lib\net4*\*" } | 
                Select-Object -First 1
            
            if ($depFile) {
                $destPath = Join-Path $packageDir $depName
                if (-not (Test-Path $destPath)) {
                    Copy-Item $depFile.FullName -Destination $destPath -Force
                    Write-Host "  Copied: $depName from NuGet" -ForegroundColor Gray
                    $found = $true
                    break
                }
            }
        }
    }
    
    if (-not $found) {
        Write-Host "  WARNING: Could not find $depName (may not be required)" -ForegroundColor Yellow
    }
}

# Also check for HtmlAgilityPack if not already copied
if (-not (Test-Path (Join-Path $packageDir "HtmlAgilityPack.dll"))) {
    Write-Host "  Attempting to copy HtmlAgilityPack from NuGet..." -ForegroundColor Yellow
    $found = $false
    foreach ($nugetPath in $nugetPaths) {
        if (Test-Path $nugetPath) {
            $htmlAgilityPack = Get-ChildItem -Path $nugetPath -Recurse -Filter "HtmlAgilityPack.dll" -ErrorAction SilentlyContinue | 
                Where-Object { $_.DirectoryName -like "*\lib\net*\*" } | 
                Select-Object -First 1
            
            if ($htmlAgilityPack) {
                Copy-Item $htmlAgilityPack.FullName -Destination $packageDir -Force
                Write-Host "  Copied: HtmlAgilityPack.dll from NuGet" -ForegroundColor Gray
                $found = $true
                break
            }
        }
    }
    
    if (-not $found) {
        Write-Host "  WARNING: Could not find HtmlAgilityPack.dll" -ForegroundColor Yellow
        Write-Host "  Extension may fail to install if dependencies are missing." -ForegroundColor Yellow
    }
}

# Create .pext file (ZIP with different extension)
Write-Host "Creating .pext package..." -ForegroundColor Yellow

# Create pext output folder if it doesn't exist
$pextOutputDir = Join-Path $scriptDir "pext"
if (-not (Test-Path $pextOutputDir)) {
    New-Item -ItemType Directory -Path $pextOutputDir -Force | Out-Null
    Write-Host "  Created pext output folder" -ForegroundColor Gray
}

$pextFileName = "$extensionName.$extensionId`_$version.pext"
$pextFilePath = Join-Path $pextOutputDir $pextFileName
$zipFilePath = Join-Path $pextOutputDir "$extensionName.$extensionId`_$version.zip"

# Remove old package if exists
if (Test-Path $pextFilePath) {
    Remove-Item $pextFilePath -Force -ErrorAction SilentlyContinue
}
if (Test-Path $zipFilePath) {
    Remove-Item $zipFilePath -Force -ErrorAction SilentlyContinue
}

# Verify package contents before creating archive
Write-Host "Verifying package contents..." -ForegroundColor Yellow
$packageFiles = Get-ChildItem -Path $packageDir -File
$requiredFiles = @("UniPlaySong.dll", "extension.yaml", "icon.png")
$missingFiles = @()

foreach ($required in $requiredFiles) {
    if (-not ($packageFiles | Where-Object { $_.Name -eq $required })) {
        $missingFiles += $required
    }
}

if ($missingFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "ERROR: Missing required files in package:" -ForegroundColor Red
    foreach ($file in $missingFiles) {
        Write-Host "  - $file" -ForegroundColor Red
    }
    exit 1
}

Write-Host "  Package contains $($packageFiles.Count) files" -ForegroundColor Gray
Write-Host ""

# Create ZIP first (Compress-Archive limitation)
Write-Host "Creating .pext archive..." -ForegroundColor Yellow
try {
    Compress-Archive -Path "$packageDir\*" -DestinationPath $zipFilePath -Force

    # Rename to .pext
    Rename-Item -Path $zipFilePath -NewName $pextFileName -Force

    $packageInfo = Get-Item $pextFilePath

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  PACKAGE CREATED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Package Details:" -ForegroundColor Cyan
    Write-Host "  File: $($packageInfo.Name)" -ForegroundColor White
    Write-Host "  Size: $([math]::Round($packageInfo.Length/1KB, 2)) KB" -ForegroundColor White
    Write-Host "  Location: $($packageInfo.FullName)" -ForegroundColor White
    Write-Host "  Version: $versionFull" -ForegroundColor White
    Write-Host ""
    Write-Host "Package Contents:" -ForegroundColor Cyan
    foreach ($file in $packageFiles | Sort-Object Name) {
        Write-Host "  - $($file.Name) ($([math]::Round($file.Length/1KB, 2)) KB)" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "To install in Playnite:" -ForegroundColor Cyan
    Write-Host "  1. Open Playnite" -ForegroundColor White
    Write-Host "  2. Go to Add-ons -> Extensions" -ForegroundColor White
    Write-Host "  3. Click 'Add extension' and select the .pext file" -ForegroundColor White
    Write-Host ""
} catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  ERROR: Failed to create package" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Yellow
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host $_.Exception.InnerException.Message -ForegroundColor Red
    }
    Write-Host ""
    exit 1
}
