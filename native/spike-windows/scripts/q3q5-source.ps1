# Q3 (the delay line over 10 minutes) and Q5 (the heard delay) against the jittered click source.
# A: route B (session volume 1e-5) and the delay on the headset, with a guard, while an endpoint
#    loopback of the headset records what it hears.
# B: meanwhile a second delay of the same source renders to Steam Streaming Speakers (another
#    clock, heard by nobody), compensating A's route, with its own endpoint loopback.
param([int]$Seconds = 600)
. "$PSScriptRoot\common.ps1"
$q3 = "$Ev\q3"
New-Item -ItemType Directory -Force $q3 | Out-Null

$proc = Start-Source "$q3\source.jsonl" @('--jitter')
"# $(Get-Date -Format o) q3q5-source.ps1 -Seconds $Seconds, source pid $($proc.Id)" | Out-File -Encoding utf8 -Append "$Ev\commands.txt"
$a = Start-Spike "$q3\delay-headset.jsonl" @('delay', "$($proc.Id)", '3', "$Seconds", '--route', 'vol:1e-5', '--guard', '--wav', "$Wav\q3-cap-headset.wav")
Start-Sleep -Seconds 2
$b = Start-Spike "$q3\delay-steam.jsonl" @('delay', "$($proc.Id)", '3', "$($Seconds - 10)", '--gain', '1e5', '--out', 'Steam Streaming Speakers', '--wav', "$Wav\q3-cap-steam.wav")
Start-Sleep -Seconds 1
# (Both recordings end before either delay does, so no live click after the restore is counted.)
$la = Start-Spike "$q3\endpoint-headset.jsonl" @('loopback', "$($Seconds - 15)", '--wav', "$Wav\q3-loop-headset.wav")
$lb = Start-Spike "$q3\endpoint-steam.jsonl" @('loopback', "$($Seconds - 15)", '--device', 'Steam Streaming Speakers', '--wav', "$Wav\q3-loop-steam.wav")
foreach ($p in $a, $b, $la, $lb) { $p.WaitForExit() }
Stop-Process -Id $proc.Id -Force
Spike "$q3\lag-headset.json" @('analyze', 'lag', "$Wav\q3-cap-headset.wav", "$Wav\q3-loop-headset.wav", '3', '--series') | Out-Null
Spike "$q3\lag-steam.json" @('analyze', 'lag', "$Wav\q3-cap-steam.wav", "$Wav\q3-loop-steam.wav", '3', '--series') | Out-Null
Spike "$q3\apps-after.jsonl" @('apps') | Out-Null
'done'
