# UniPlaySong Backup Script
# Creates a timestamped backup of the UniPSong extension
# Excludes build artifacts, packages, and other generated files

param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  UniPlaySong Backup Script" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = $scriptDir
$parentDir = Split-Path -Parent $scriptDir

# Read version from version.txt if not provided
if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionFile = Join-Path $scriptDir "version.txt"
    if (Test-Path $versionFile) {
        $Version = (Get-Content $versionFile -Raw).Trim()
    } else {
        $Version = "unknown"
    }
}

Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host ""

# Generate backup name with timestamp
$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$backupName = "backup_UniPSong_v$Version`_$timestamp"
$backupPath = Join-Path $parentDir $backupName

Write-Host "Source: $sourceDir" -ForegroundColor Gray
Write-Host "Backup: $backupPath" -ForegroundColor Gray
Write-Host ""

# Check if source exists
if (-not (Test-Path $sourceDir)) {
    Write-Host "ERROR: Source directory not found: $sourceDir" -ForegroundColor Red
    exit 1
}

# Create backup directory
Write-Host "Creating backup directory..." -ForegroundColor Yellow
if (Test-Path $backupPath) {
    Write-Host "  WARNING: Backup directory already exists, removing..." -ForegroundColor Yellow
    Remove-Item -Path $backupPath -Recurse -Force
}
New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
Write-Host "  Created: $backupName" -ForegroundColor Green
Write-Host ""

# Files and directories to exclude
$excludePatterns = @(
    "bin",
    "obj",
    "package",
    "*.pext",
    "*.zip",
    "backup_*",
    "__pycache__",
    ".vs",
    ".git",
    "*.log",
    "*.cache"
)

Write-Host "Copying files (excluding build artifacts)..." -ForegroundColor Yellow

# Function to check if path should be excluded
function Should-Exclude {
    param([string]$Path, [string]$SourceDir)
    
    $item = Get-Item $Path -ErrorAction SilentlyContinue
    if (-not $item) { return $true }
    
    $relativePath = $Path.Replace($SourceDir, "").TrimStart("\")
    
    foreach ($pattern in $excludePatterns) {
        if ($pattern -like "*.pext" -or $pattern -like "*.zip" -or $pattern -like "*.log" -or $pattern -like "*.cache") {
            $ext = $pattern.Substring(1) # Remove *
            if ($item.Extension -eq $ext) { return $true }
        }
        elseif ($pattern -like "backup_*") {
            if ($item.Name -like "backup_*") { return $true }
        }
        elseif ($item.Name -eq $pattern) {
            return $true
        }
        elseif ($relativePath -like "*\$pattern\*" -or $relativePath -like "$pattern\*") {
            return $true
        }
    }
    
    return $false
}

# Copy files recursively
$fileCount = 0
$dirCount = 0

Get-ChildItem -Path $sourceDir -Recurse -File | ForEach-Object {
    if (-not (Should-Exclude $_.FullName $sourceDir)) {
        $relativePath = $_.FullName.Replace($sourceDir, "").TrimStart("\")
        $destPath = Join-Path $backupPath $relativePath
        $destDir = Split-Path $destPath -Parent
        
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            $dirCount++
        }
        
        Copy-Item $_.FullName -Destination $destPath -Force
        $fileCount++
        
        if ($fileCount % 50 -eq 0) {
            Write-Host "  Copied $fileCount files..." -ForegroundColor Gray
        }
    }
}

# Calculate backup size
$backupSize = (Get-ChildItem -Path $backupPath -Recurse -File | 
    Measure-Object -Property Length -Sum).Sum

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  BACKUP COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Backup Details:" -ForegroundColor Yellow
Write-Host "  Name: $backupName" -ForegroundColor White
Write-Host "  Location: $backupPath" -ForegroundColor White
Write-Host "  Files: $fileCount" -ForegroundColor White
Write-Host "  Directories: $dirCount" -ForegroundColor White
Write-Host "  Size: $([math]::Round($backupSize/1MB, 2)) MB" -ForegroundColor White
Write-Host ""
