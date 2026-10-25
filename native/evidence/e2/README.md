# E2: Delayed listening in the app

Run 2026-10-24/25 on the developer's Mac, headless. An unbundled run has no System Audio
Recording grant, so its taps capture silence: everything audible is pending the operator
(`../operator-pending.md`).

## QA items 14–19

| # | Item | Result |
|---|---|---|
| 14 | Delayed on Music: silent `M`, then late; other apps live | Pending (grant, speakers). The tap is created and runs: `playlist-survivors --delayed --tap-child <file> --headless --seconds 25` taps a child of ours (silent until tapped, touching no other app); the delay line's fill held at 2930 ms with 0 underruns over 20 s (timing log `delay`) |
| 15 | Delayed run: committed beats tighter than Direct | The Galop live in File mode, 75 s each: Delayed 79.0% of committed beats within 45 ms of the reference, Direct 51.6%; every Delayed beat known at least 0.70 s before it falls (`qa15-galop.txt`, `score.py`) |
| 16 | `kill -9`: the app audible at once; relaunch mutes nothing | Pending by ear. A killed run holding public taps (`PS_PUBLIC=1`) left 2 objects (tap and aggregate), destroyed at the next launch; private taps go with their process (ES) |
| 17 | A stall: the watchdog unmutes within about 2 s, the run ends, the log says why | `--stall-at 6` (3 s stall): tripped 2.03 s in, fell back to Direct, logged `watchdog` |
| 18 | A browser: the video notice; back to Direct ends the run, heard live | The notice renders (`listening-card.png`, every line shown at once); live check pending |
| 19 | AirPods: in sync by ear; the measured delay holds `M` | Pending (device, loopback) |

## What it doesn't show

- That muting works, or that the heard delay is `M`: both need the grant and a loopback.
- Two devices' drift: the host-time mapping should follow it, unmeasured.
