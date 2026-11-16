# Renders one settings page to a PNG, headless, so label/description/layout changes can be checked without
# installing the .pext. Build first (dotnet build -c Release). Run in an STA PowerShell, ONE page per process
# (a second run in the same process registers AssemblyResolve twice and crashes):
#
#   powershell -STA -NoProfile -ExecutionPolicy Bypass -File scripts/render_settings_page.ps1 `
#       -Page DefaultMusicPage -Out C:\path\page.png -Height 900 -ScrollTo end `
#       -Set 'MusicState=Fullscreen;EnableDefaultMusic=true'
#
# -Page      settings page class in UniPlaySong.Controls.Settings (e.g. StartupPage, DefaultMusicPage)
# -Set       settings to show, 'Name=Value;Name=Value' (a string: -File can't pass a hashtable); values are converted
#            to the property's type, enums by name
# -ScrollTo  'end', or a pixel offset, for sections below the fold (the window can't be taller than the screen)
# View-model-only bindings (live status text, detected theme names) render empty; that is expected.
param(
    [Parameter(Mandatory = $true)][string]$Page,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$Width = 760,
    [int]$Height = 900,
    [string]$ScrollTo = '',
    [string]$Set = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml

$bin = Join-Path $PSScriptRoot '..\src\bin\Release\net4.6.2'
$bin = (Resolve-Path $bin).Path
[AppDomain]::CurrentDomain.add_AssemblyResolve({ param($sender, $e)
    $p = Join-Path $bin (($e.Name -split ',')[0] + '.dll')
    if (Test-Path $p) { return [Reflection.Assembly]::LoadFrom($p) }
    return $null })
$asm = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'UniPlaySong.dll'))
if (-not ('UpsRenderSettingsHolder' -as [type])) {
    Add-Type -TypeDefinition 'public class UpsRenderSettingsHolder { public object Settings { get; set; } }'
}
$null = New-Object System.Windows.Application  # makes pack:// URIs resolve

$settings = $asm.CreateInstance('UniPlaySong.UniPlaySongSettings')
foreach ($pair in ($Set -split ';' | ? { $_.Trim() })) {
    $name, $raw = $pair -split '=', 2
    $name = $name.Trim(); $raw = $raw.Trim()
    $prop = $settings.GetType().GetProperty($name)
    if (-not $prop) { throw "No setting named $name" }
    $value = if ($prop.PropertyType.IsEnum) { [Enum]::Parse($prop.PropertyType, [string]$raw) }
             else { [Convert]::ChangeType($raw, $prop.PropertyType, [Globalization.CultureInfo]::InvariantCulture) }
    $prop.SetValue($settings, $value)
}

$view = $asm.CreateInstance("UniPlaySong.Controls.Settings.$Page")
if (-not $view) { throw "No settings page named $Page" }
$holder = New-Object UpsRenderSettingsHolder
$holder.Settings = $settings
$view.DataContext = $holder

$win = New-Object System.Windows.Window
$win.Width = $Width; $win.Height = $Height; $win.Content = $view
$win.WindowStyle = 'None'; $win.ShowInTaskbar = $false; $win.Left = -5000
$win.Show()
$win.Dispatcher.Invoke([Action]{}, 'ApplicationIdle')

if ($ScrollTo) {
    $sv = $view.Content
    if ($sv -is [System.Windows.Controls.ScrollViewer]) {
        if ($ScrollTo -eq 'end') { $sv.ScrollToEnd() } else { $sv.ScrollToVerticalOffset([double]$ScrollTo) }
        $win.Dispatcher.Invoke([Action]{}, 'ApplicationIdle')
    }
}

$rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $Width, $Height, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
$rtb.Render($win)
$enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
$fs = [IO.File]::Create($Out)
try { $enc.Save($fs) } finally { $fs.Close() }
$win.Close()
"saved $Out"
