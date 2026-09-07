# Publishes the packaged .pext to the current version's GitHub beta pre-release.
#
# Deliberately NOT wired into package_extension.ps1: packaging happens on every local build, and
# publishing is public and hard to take back. Run this when a build is actually ready for testers.
#
#   powershell -ExecutionPolicy Bypass -File scripts/publish_beta.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/publish_beta.ps1 -DryRun
#
# Matches the convention already in use: tag "{version}-beta", pre-release, assets accumulating as
# UniPlaySong-1_8_7-beta.pext, -beta2.pext, -beta3.pext. The next number is read off the release
# itself, so it stays correct across machines and after a manual upload.

param(
    [switch]$DryRun,      # show what would happen, upload nothing
    [string]$Notes = ""   # release body, used ONLY when creating the release (never overwrites)
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Fail($msg) {
    Write-Host ""
    Write-Host "  ERROR: $msg" -ForegroundColor Red
    Write-Host ""
    exit 1
}

# --- Preconditions ---------------------------------------------------------

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Fail "GitHub CLI (gh) is not installed or not on PATH."
}

& gh auth status *> $null
if ($LASTEXITCODE -ne 0) { Fail "GitHub CLI is not authenticated. Run: gh auth login" }

$versionFile = Join-Path $repoRoot "version.txt"
if (-not (Test-Path $versionFile)) { Fail "version.txt not found at $versionFile" }

$version = (Get-Content $versionFile -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "version.txt does not look like a version: '$version'" }

$underscored = $version -replace '\.', '_'
$sourcePext = Join-Path $repoRoot "pext\UniPlaySong-$underscored.pext"
if (-not (Test-Path $sourcePext)) {
    Fail "No packaged .pext for $version. Run the build and package steps first.`n         Expected: $sourcePext"
}

$tag = "$version-beta"
$title = "UniPlaySong v$version-beta"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Publish beta - v$version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- Does the pre-release exist yet? ---------------------------------------

$existingAssets = @()
& gh release view $tag *> $null
$releaseExists = ($LASTEXITCODE -eq 0)

if ($releaseExists) {
    $json = & gh release view $tag --json assets,isPrerelease | ConvertFrom-Json
    $existingAssets = @($json.assets | ForEach-Object { $_.name })

    if (-not $json.isPrerelease) {
        Fail "Release '$tag' exists but is NOT marked as a pre-release. Refusing to touch it."
    }
    Write-Host "  Release $tag exists ($($existingAssets.Count) asset(s))" -ForegroundColor Gray
} else {
    Write-Host "  Release $tag does not exist yet - will create it" -ForegroundColor Gray
}

# --- Work out the next beta number -----------------------------------------
#
# First upload is plain "-beta", subsequent ones "-beta2", "-beta3". Read from the release rather
# than a local counter so it stays right if a build was uploaded from elsewhere or by hand.

$used = @()
foreach ($name in $existingAssets) {
    if ($name -match "UniPlaySong-$underscored-beta(\d*)\.pext$") {
        $used += ,([int]($matches[1] -replace '^$', '1'))
    }
}

$next = 1
while ($used -contains $next) { $next++ }
$suffix = if ($next -eq 1) { "beta" } else { "beta$next" }
$assetName = "UniPlaySong-$underscored-$suffix.pext"

$stagedPext = Join-Path $repoRoot "pext\$assetName"

Write-Host "  Source : $(Split-Path -Leaf $sourcePext)" -ForegroundColor Gray
Write-Host "  Upload : $assetName" -ForegroundColor White
Write-Host "  Size   : $([math]::Round((Get-Item $sourcePext).Length / 1MB, 1)) MB" -ForegroundColor Gray
Write-Host ""

if ($DryRun) {
    Write-Host "  DRY RUN - nothing was published." -ForegroundColor Yellow
    Write-Host ""
    exit 0
}

# --- Publish ---------------------------------------------------------------

Copy-Item $sourcePext $stagedPext -Force

try {
    if (-not $releaseExists) {
        # Notes only on creation. The body is curated by hand afterwards and must never be
        # overwritten by a later build.
        $body = if ($Notes) { $Notes } else { "Beta build of v$version. See CHANGELOG.md for details." }
        & gh release create $tag $stagedPext --prerelease --title $title --notes $body
        if ($LASTEXITCODE -ne 0) { Fail "gh release create failed." }
    } else {
        & gh release upload $tag $stagedPext
        if ($LASTEXITCODE -ne 0) { Fail "gh release upload failed." }
    }

    $url = (& gh release view $tag --json url | ConvertFrom-Json).url

    Write-Host ""
    Write-Host "  PUBLISHED" -ForegroundColor Green
    Write-Host "  $assetName -> $tag" -ForegroundColor White
    Write-Host "  $url" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  The manifest is untouched - a pre-release is not offered to users by Playnite." -ForegroundColor Gray
    Write-Host ""
} finally {
    # The staged copy stays in pext/ (gitignored) as a local record of what each tester got.
}
