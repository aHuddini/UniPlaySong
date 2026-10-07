# SPIKE A runner: each configuration gets a fresh producer (1280x720 @ 60 fps) and an 8-second host run.
param([int]$Seconds = 8, [int]$Width = 1280, [int]$Height = 720, [string]$Pacing = "ondemand")
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$obj = Join-Path $here 'obj'
$out = Join-Path $obj 'results'
New-Item -ItemType Directory -Force $out | Out-Null
foreach ($cfg in @(@('gpu','hw'), @('gpu','soft'), @('cpu','hw'), @('cpu','soft'))) {
    $name = "$($cfg[0])-$($cfg[1])-$Pacing"
    $producer = Start-Process -FilePath (Join-Path $obj 'vt_producer.exe') -ArgumentList "$Width $Height 60 $($Seconds + 10) $Pacing" `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $out "$name-producer.txt")
    Start-Sleep -Milliseconds 500
    $pstart = $producer.TotalProcessorTime
    $host_ = Start-Process -FilePath (Join-Path $obj 'host\VtHost.exe') `
        -ArgumentList "$($cfg[0]) $($cfg[1]) $Seconds `"$(Join-Path $out "$name.txt")`" `"$(Join-Path $out "$name.png")`"" -PassThru -Wait
    $producer.WaitForExit(15000) | Out-Null
    "=== $name"
    Get-Content (Join-Path $out "$name.txt")
}
