# Shared helpers for the experiment scripts (Windows PowerShell 5.1).
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$Spike = "$Root\target\release\spike.exe"
# The click source runs from a copy, so Windows' per-app volume memory for it (keyed by the
# executable's path) never touches the spike's own output.
$Src = "$Root\target\release\clicksource.exe"
$Ev = (Resolve-Path "$Root\..\evidence").Path + '\windows-spike'
# Long WAVs stay out of the repository.
$Wav = "$env:TEMP\spike-wav"
New-Item -ItemType Directory -Force $Wav | Out-Null
if (-not (Test-Path $Src) -or (Get-Item $Src).LastWriteTime -lt (Get-Item $Spike).LastWriteTime) {
    Copy-Item $Spike $Src -Force
}

# Quotes arguments with spaces for Start-Process.
function Join-Args([string[]]$a) { ($a | ForEach-Object { if ($_ -match ' ') { '"' + $_ + '"' } else { $_ } }) -join ' ' }

function Start-Source([string]$log, [string[]]$extra = @()) {
    $p = Start-Process $Src -ArgumentList (Join-Args (@('source', '--seconds', '3600') + $extra)) -PassThru -WindowStyle Hidden -RedirectStandardOutput $log
    Start-Sleep -Milliseconds 1500
    $p
}

function Start-Spike([string]$log, [string[]]$a) {
    Start-Process $Spike -ArgumentList (Join-Args $a) -PassThru -WindowStyle Hidden -RedirectStandardOutput $log
}

# Runs the spike in the foreground, saving its JSON lines.
function Spike([string]$log, [string[]]$a) {
    "# spike $(Join-Args $a)" | Out-File -Encoding utf8 -Append "$Ev\commands.txt"
    & $Spike @a | Tee-Object -FilePath $log | Out-Null
    Get-Content $log
}

function Last-Json([string]$log) { Get-Content $log | Select-Object -Last 1 | ConvertFrom-Json }
