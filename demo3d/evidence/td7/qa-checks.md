# TD7: Sweeps and the machine lock (qa.md), on Cygnus, 2026-10-27/28

Each check names its command (from `D:\wt\td7`, with `demo3d` meaning
`uv run --project demo3d/tools demo3d`) and quotes the player's bench.log. The sweep folders
themselves stayed in the worktree's gitignored `demo3d/Logs/bench/`; the small sweep's and the
frontier's rows are kept here in [small/](small/) and [frontier/](frontier/).

Two things on Cygnus shaped these checks:

- From 23:13 on 2026-10-27, a Windows Firewall prompt for a development build of the player
  (raised by TD7's own attempt at Unity GPU markers) held the foreground. Answering it changes a
  security setting, so it waits for the operator, and every sweep after it refused at the
  foreground check, which comes after the backbuffer, worker, API, power, desktop and display
  checks. The frontier was then measured with `--no-foreground` (focus loss recorded, not noise).
- Another session (TD5) ran Unity imports, IL2CPP builds and its own crowd bench beside these
  sweeps, which made most runs noisy, and its tooling killed TD7's player twice (exit code 1, the
  log cut mid-line) right before taking the machine lock, apparently by image name. TD7's last
  frontier ran from a renamed copy of the player (`PlaylistSurvivors3D-TD7.exe`).

| Check | Result |
|---|---|
| Small sweep, one scene, two points | **Pass.** `demo3d sweep demo3d/sweeps/synthetic-small.json`: 21 rows, each with timings, counters, machine state and `pass`; frontier 30 m 1,593 (capacity-bound, 3 probes + 1 full run), 40 m 2,230 (GPU). See [small/](small/). |
| A heavy build beside a sweep | **Pass**, with TD5's real builds as the load: the small sweep printed `waiting for a quiet machine: other processes 25.2% CPU (31 s)` and `machine still busy after 61 s (other processes 14.7% CPU); running anyway` before repeats, marked 12 of 21 runs noisy with their reasons (`other processes used 16.2% CPU over 5 s`), repeated each twice, and its report counts them ("Noisy runs: 12 of 21", "Frontier points resting on noisy runs: 1 of 2"). The frontier waited 200 s to start and marked all 51 runs noisy, including `other processes used 22.7% of the GPU's 3D engine (busiest: pid 38608 ...)` from TD5's stills player and `the player's window was occluded by UnityWndClass "Playlist Survivors 3D Demo"`. `sweeps/qa-noisy.json` stays for a staged check. |
| A second sweep waits for the lock | **Pass.** Sweep A took the lock at 23:21:05; B, started at 23:21:08, printed `waiting for the machine lock ... (pid 34864 since 2026-10-27 23:21:05: demo3d sweep synthetic-small.json)` and started its player at 23:21:20, after A's exited at 23:21:17. TD5's crowd bench also waited for TD7's lock and took it as each TD7 sweep ended. |
| A sweep killed with `taskkill /F` | **Pass.** `taskkill /F /PID <orchestrator>` at 23:21:44.5: the Job object took its player down with it (`tasklist` found no player a second later), and a new sweep started at 23:21:45.9 printed `took the machine lock` with no wait. pytest `test_a_killed_holder_frees_the_lock_at_once` repeats it with `TerminateProcess`. |
| Balanced power plan | **Pass.** `powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e` needs no administrator rights; the sweep exited 3 with `REFUSED (powerPlan): the power plan is Balanced (381b4222-f694-41f0-9685-ff5bb260df2e); sweeps need High performance or Ultimate Performance`; High performance was restored at once (`powercfg /getactivescheme`). |
| A locked desktop | **Pending on the operator**: an agent can't unlock the desktop again. The check is `OpenInputDesktop` + `SwitchDesktop`, refusal `desktopLocked`. |
| The display off | **Partly.** The monitor was turned off (`SC_MONITORPOWER`, the same state as the idle timeout) and a sweep started 8 s later: `display was off, now on after 0.6 s ... held on for the sweep`. The run then refused at the foreground check (the firewall prompt), so "runs with its window composited" waits for the prompt's dismissal. |
| Three sweeps back to back | **Partly**: not run as three dedicated sweeps (`sweeps/qa-thermal.json`), because TD5's crowd bench held the machine lock from 01:23 for longer than TD7 could wait. Throttle reasons are recorded in every row (`gpuThrottle`), thermal ones are noise, and the report lists every reason seen; the frontier's 51 consecutive runs over 45 minutes stayed at 67 to 75 °C with `power_cap` only. TD12's sweeps cover the back-to-back case. |
| A different window size | **Pass.** `demo3d sweep ... --screen 1600x900`: `REFUSED (backbuffer): the backbuffer is 1600x900 (rendering 1600x900); the sweep needs 1920x1080 (-screen-width 1920 -screen-height 1080 -screen-fullscreen 0)`. Also `--workers 4`: `REFUSED (jobWorkers): JobsUtility.JobWorkerCount is 4; the sweep needs 6 (-job-worker-count 6)`. |
| `jobWorkers: 6` in every row | **Pass.** Every row in [small/](small/) and [frontier/](frontier/) has `"jobWorkers": 6` (the report's "Job workers in every row: 6"). |
