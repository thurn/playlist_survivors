# Q1 and Q2 against the click source (automated).
. "$PSScriptRoot\common.ps1"
$q1 = "$Ev\q1"; $q2 = "$Ev\q2"
New-Item -ItemType Directory -Force $q1, $q2 | Out-Null

# --- Q1: capture the source by process loopback while we play a 3 kHz tone ourselves; an
# endpoint loopback of the headset meanwhile shows the tone really was playing.
$proc = Start-Source "$q1\source.jsonl"
$loop = Start-Spike "$q1\endpoint-loopback.jsonl" @('loopback', '13', '--wav', "$Wav\q1-endpoint.wav")
Start-Sleep -Milliseconds 500
Spike "$q1\capture-with-tone.jsonl" @('capture', "$($proc.Id)", '10', '--tone', '--wav', "$Wav\q1-capture.wav") | Out-Null
$loop.WaitForExit()
Spike "$q1\endpoint-tone.json" @('analyze', 'tone', "$Wav\q1-endpoint.wav") | Out-Null
Spike "$q1\endpoint-clicks.json" @('analyze', 'clicks', "$Wav\q1-endpoint.wav") | Out-Null

# --- Q2: each route on the source's sessions; process loopback (fidelity) and endpoint
# loopback of the headset (what reaches the speakers), 8 s each, then restore.
$routes = @('none', 'mute', 'vol:1e-3', 'vol:1e-4', 'vol:1e-5', 'none-after')
foreach ($r in $routes) {
    $tag = $r -replace ':', '-'
    if ($r -notlike 'none*') { Spike "$q2\$tag-mute.json" @('mute', "$($proc.Id)", $r) | Out-Null }
    Spike "$q2\$tag-apps.jsonl" @('apps') | Out-Null
    $loop = Start-Spike "$q2\$tag-endpoint.jsonl" @('loopback', '9', '--wav', "$Wav\q2-$tag-endpoint.wav")
    Start-Sleep -Milliseconds 300
    Spike "$q2\$tag-capture.jsonl" @('capture', "$($proc.Id)", '8', '--wav', "$Wav\q2-$tag-capture.wav") | Out-Null
    $loop.WaitForExit()
    if ($r -notlike 'none*') { Spike "$q2\$tag-restore.json" @('restore') | Out-Null }
    Spike "$q2\$tag-capture-clicks.json" @('analyze', 'clicks', "$Wav\q2-$tag-capture.wav") | Out-Null
    Spike "$q2\$tag-endpoint-clicks.json" @('analyze', 'clicks', "$Wav\q2-$tag-endpoint.wav") | Out-Null
    Spike "$q2\$tag-endpoint-level.json" @('analyze', 'level', "$Wav\q2-$tag-endpoint.wav") | Out-Null
}
# The offset between the two timelines (process loopback vs endpoint loopback QPC stamps).
Spike "$q2\tap-to-endpoint-lag.json" @('analyze', 'lag', "$Wav\q2-none-capture.wav", "$Wav\q2-none-endpoint.wav", '0') | Out-Null
Stop-Process -Id $proc.Id -Force

'done'
