# Per-minute medians and spreads of Q3's delay-line fill and of the heard delay (Q5).
. "$PSScriptRoot\common.ps1"
$q3 = "$Ev\q3"

function Median($v) { $s = $v | Sort-Object; $s[[int][math]::Floor(($s.Count - 1) / 2)] }

$out = @()
foreach ($run in 'headset', 'steam') {
    $lines = Get-Content "$q3\delay-$run.jsonl" | ConvertFrom-Json | Where-Object { $_.t -ne $null -and $_.t -gt 4 -and $_.fill_ms -ne $null }
    $lag = Get-Content "$q3\lag-$run.json" | ConvertFrom-Json
    foreach ($g in ($lines | Group-Object { [math]::Floor($_.t / 60) })) {
        $f = $g.Group | ForEach-Object { [double]$_.fill_ms }
        $m = [int]$g.Name
        # (The lag series' times start at the first delayed click, about 4 s into the run.)
        $d = $lag.series | Where-Object { [math]::Floor(([double]$_[0] + 4) / 60) -eq $m } | ForEach-Object { [double]$_[1] }
        $out += [pscustomobject]@{
            run = $run; minute = $m
            fill_median_ms = [math]::Round((Median $f), 3)
            fill_spread_ms = [math]::Round(($f | Measure-Object -Maximum).Maximum - ($f | Measure-Object -Minimum).Minimum, 3)
            heard_raw_median_ms = if ($d) { [math]::Round((Median $d), 3) } else { $null }
            heard_raw_spread_ms = if ($d) { [math]::Round(($d | Measure-Object -Maximum).Maximum - ($d | Measure-Object -Minimum).Minimum, 3) } else { $null }
            underrun_frames = ($g.Group | Select-Object -Last 1).underrun_frames
            cap_dropped = ($g.Group | Select-Object -Last 1).cap_dropped
        }
    }
}
$out | ConvertTo-Json | Out-File -Encoding utf8 "$q3\per-minute.json"
$out | Format-Table -AutoSize | Out-String -Width 200
