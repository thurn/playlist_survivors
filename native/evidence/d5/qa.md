# D5: the QA checklist, as far as an agent can

Run 2026-10-24 on the developer's Mac, headless (`--headless`: no window, muted), in File mode. The
output at the time was the operator's AirPods Pro (Bluetooth, reported latency 160 ms), so these
runs exercise the long-latency path. System mode needs the capture grant's one click, and trying
it would put the prompt on the operator's screen: those items are pending (rows in
`../operator-pending.md`), as are items needing hardware changes, keys or an ear.

Raw outputs: `house-run.txt`, `house-report.txt` (`ps report`, `ps log --no-table`, `ps replay`),
`galop-speech.txt`.

| # | Item | Result |
|---|---|---|
| 1 | Capture prompt once | Pending (System mode, the operator's click) |
| 2 | Steady track: a run starts by itself, strikes on the beat | File mode, house-128 with `--press-beats`: a run started at 2.6 s and ended on the confirmed end at 67.6 s. But with the AirPods' 160 ms, sounds were late: 35 voices skipped, 171 of 509 scheduled after their start time. Filed (the speculation lead must cover the output latency and a sample's lead) |
| 3 | Browser audio; the game's sounds don't fool the tracker | Pending (System mode) |
| 4 | Free prelude: unjudged and silent below the gate, then locks on | orpheus-galop: no run through the prelude, a run starts at 50.8 s once the evidence holds the gate |
| 5 | Speech: no run starts | A `say` recording, 30 s: no run started. Start (Enter) for a beatless run: pending (keys) |
| 6 | Music stops: confirmed end about 3 s later; a new run when it resumes | The file ending: the run ended as THE SONG ENDED about 3-4 s after the music stopped (house-128 67.6 s). Resuming: pending (System mode) |
| 7 | Pause the game | Pending (Esc or focus: keys) |
| 8 | Gapless playlist | Pending (System mode) |
| 9 | Speakers to headphones mid-run | Pending (hardware); the device poll and THE OUTPUT CHANGED path are in G6 |
| 10 | AirPods: notice, sync after calibration | The notice shows (log: "Bluetooth: sync is best-effort"); sync by ear pending |
| 11 | Sessions saved; `ps replay` reproduces the run | house-128: 421 507 settled records, the replay the same (`house-report.txt`) |
| 12 | Watch my run replays the run with its music | Visuals only: the replay has no music. Filed |
| 13 | Calibrate changes judging and the flash | Pending (keys; row in operator-pending) |
| 14-19 | Delayed listening | Not built yet (E2) |

`ps report` on the house-128 session (AirPods): ticks arrive 9 ms before their heard time
(committed ahead; capture to arrival met), the frame's work p99 1.3 ms (met), rollbacks
re-simulated p95 9.7 ms a second, 1.6 times the song (met); key delivery not measured headless
(the stand-in player calls the engine directly).
