# Q4 (crash safety) against the click source, for each route that changes state (A, B):
#   1. silence the source with `delay --route`, taskkill /F the spike: is the source audible again?
#   2. the same with `--guard`: does the guard restore it, and how fast?
#   3. silence, kill the source too, relaunch it: what level does Windows give the new session?
. "$PSScriptRoot\common.ps1"
$q4 = "$Ev\q4"
New-Item -ItemType Directory -Force $q4 | Out-Null
$guardLog = "$env:LOCALAPPDATA\spike-windows\guard.jsonl"
$stateFile = "$env:LOCALAPPDATA\spike-windows\state.json"
if (Test-Path $guardLog) { Remove-Item $guardLog }

# The current source's session: "volume/mute".
function Source-State([string]$log) {
    Spike $log @('apps') | Out-Null
    $s = Get-Content $log | ConvertFrom-Json | Where-Object { $_.pid -eq $proc.Id } | Select-Object -First 1
    if ($s) { "$($s.volume)/$($s.mute)" } else { 'no session' }
}

# The peak reaching the headset over 2 s (the source's clicks are at -20 dBFS when audible).
function Heard([string]$tag) {
    Spike "$q4\$tag-endpoint.jsonl" @('loopback', '2', '--wav', "$Wav\q4-$tag-endpoint.wav") | Out-Null
    Spike "$q4\$tag-endpoint-level.json" @('analyze', 'level', "$Wav\q4-$tag-endpoint.wav") | Out-Null
    (Get-Content "$q4\$tag-endpoint-level.json" | ConvertFrom-Json).peak_dbfs
}

$summary = @()
foreach ($r in 'mute', 'vol:1e-5') {
    $t = $r -replace ':', '-'
    $proc = Start-Source "$q4\$t-source.jsonl"

    # 1. Killed, no guard.
    $d = Start-Spike "$q4\$t-1-delay.jsonl" @('delay', "$($proc.Id)", '3', '60', '--route', $r)
    Start-Sleep -Seconds 4
    $before = Source-State "$q4\$t-1-apps-before-kill.jsonl"
    taskkill /F /PID $d.Id | Out-Null
    Start-Sleep -Seconds 1
    $after = Source-State "$q4\$t-1-apps-after-kill.jsonl"
    $heard = Heard "$t-1-after-kill"
    $left = Test-Path $stateFile
    Spike "$q4\$t-1-restore.json" @('restore') | Out-Null
    $restored = Source-State "$q4\$t-1-apps-after-restore.jsonl"
    $summary += [pscustomobject]@{ route = $r; step = '1 spike killed, no guard'; before_kill = $before; after_kill = $after; heard_peak_dbfs = $heard; state_file_left = $left; after_manual_restore = $restored }

    # 2. Killed, with the guard.
    $d = Start-Spike "$q4\$t-2-delay.jsonl" @('delay', "$($proc.Id)", '3', '60', '--route', $r, '--guard')
    Start-Sleep -Seconds 4
    $before = Source-State "$q4\$t-2-apps-before-kill.jsonl"
    taskkill /F /PID $d.Id | Out-Null
    Start-Sleep -Milliseconds 1500
    $after = Source-State "$q4\$t-2-apps-after-kill.jsonl"
    $heard = Heard "$t-2-after-kill"
    $g = Get-Content $guardLog -ErrorAction SilentlyContinue | Select-Object -Last 1
    $g | Out-File -Encoding utf8 "$q4\$t-2-guard.json"
    $ms = if ($g) { ($g | ConvertFrom-Json).restore_ms } else { 'no guard record' }
    $summary += [pscustomobject]@{ route = $r; step = '2 spike killed, guard'; before_kill = $before; after_kill = $after; heard_peak_dbfs = $heard; guard_restore_ms = $ms; state_file_left = (Test-Path $stateFile) }
    if (Test-Path $stateFile) { Spike "$q4\$t-2-restore.json" @('restore') | Out-Null }

    # 3. Silenced, then the source killed and relaunched.
    Spike "$q4\$t-3-mute.json" @('mute', "$($proc.Id)", $r) | Out-Null
    taskkill /F /PID $proc.Id | Out-Null
    Start-Sleep -Seconds 1
    $proc = Start-Source "$q4\$t-3-source-relaunched.jsonl"
    $relaunched = Source-State "$q4\$t-3-apps-relaunched.jsonl"
    $heard = Heard "$t-3-relaunched"
    Spike "$q4\$t-3-restore.json" @('restore') | Out-Null
    $restored = Source-State "$q4\$t-3-apps-after-restore.jsonl"
    taskkill /F /PID $proc.Id | Out-Null
    Start-Sleep -Seconds 1
    $proc = Start-Source "$q4\$t-3-source-relaunched-again.jsonl"
    $again = Source-State "$q4\$t-3-apps-relaunched-again.jsonl"
    taskkill /F /PID $proc.Id | Out-Null
    $summary += [pscustomobject]@{ route = $r; step = '3 source killed while silenced, relaunched'; after_relaunch = $relaunched; heard_peak_dbfs = $heard; after_manual_restore = $restored; relaunched_after_restore = $again }
}
$summary | ConvertTo-Json | Out-File -Encoding utf8 "$q4\summary.json"
$summary | Format-List
