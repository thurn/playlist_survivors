# Q2 route D (automated, against the click source).
. "$PSScriptRoot\common.ps1"
$q2 = "$Ev\q2"

# --- Q2 route D (the app's own output device): the source renders to Steam Streaming Speakers,
# which nothing hears. Does process loopback still capture it? Is the headset silent?
$proc = Start-Source "$q2\D-source.jsonl" @('--device', 'Steam Streaming Speakers')
$loop = Start-Spike "$q2\D-endpoint.jsonl" @('loopback', '9', '--wav', "$Wav\q2-D-endpoint.wav")
$loop2 = Start-Spike "$q2\D-steam-endpoint.jsonl" @('loopback', '9', '--device', 'Steam Streaming Speakers', '--wav', "$Wav\q2-D-steam-endpoint.wav")
Start-Sleep -Milliseconds 300
Spike "$q2\D-capture.jsonl" @('capture', "$($proc.Id)", '8', '--wav', "$Wav\q2-D-capture.wav") | Out-Null
$loop.WaitForExit(); $loop2.WaitForExit()
Stop-Process -Id $proc.Id -Force
Spike "$q2\D-capture-clicks.json" @('analyze', 'clicks', "$Wav\q2-D-capture.wav") | Out-Null
Spike "$q2\D-endpoint-level.json" @('analyze', 'level', "$Wav\q2-D-endpoint.wav") | Out-Null
Spike "$q2\D-endpoint-clicks.json" @('analyze', 'clicks', "$Wav\q2-D-endpoint.wav") | Out-Null
Spike "$q2\D-steam-endpoint-clicks.json" @('analyze', 'clicks', "$Wav\q2-D-steam-endpoint.wav") | Out-Null
'done'
