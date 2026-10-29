# TD13: the demo

The full song's runs. Each `song/*.json` is one `--song-check` verdict, the song rule as
performance.md states it since TD13: every 20 s window from 5 s into the run, every 5 s, plus one
ending at the run's last frame, at p99 GPU 4.2 ms and main and render 10.4 ms, with the
effect-light drop share's p99 at most 2% and at least half the window's frames carrying a GPU time;
no frame after the warm-up over twice its line; no rejected spawn; the whole song covered. Each
verdict also carries the run's context: the harness's machine samples (`machineQuiet`,
`machineReasons`, `gpuTempMaxC`, `otherCpuPctMax`), the build and the command line. Window times
are song seconds (each frame at its own Update's song time). All runs: Cygnus, the IL2CPP release
player, 1920×1080 windowed, uncapped, 6 job workers, the finished arena (E2 at 2×), TD11's default
cuts, MSAA 4×, the climax's spells at the demo load plus the laser.

**After the answer milestone's review** (frames collected with the harness's own policy,
`FrameTimingCollector`, and placed by song time):

| file | envelope | worst window (GPU p99) | the climax's worst (155 to 170 s) | verdict |
|---|---|---|---|---|
| `song/final-1.json` | 425 (the level script) | 4.022 ms (178.97 s) | 3.791 ms | **pass** (other processes reached 10.6% CPU: noisy by the harness's rule) |
| `song/final-2.json` | 425 (the level script) | 4.026 ms (178.97 s) | 3.784 ms | **pass**, quiet |
| `song/envelope-450-1.json` | 450 (`--envelope`) | 4.298 ms (178.97 s) | 4.158 ms | fail (1 window), and `wholeSong` false from a coverage bug since fixed |
| `song/envelope-450-2.json` | 450 (`--envelope`) | 4.266 ms (178.97 s) | 4.120 ms | fail (1 window), same |

`song/final-1-stats.csv` is final-1's `--stats` (twice a second: on-screen alive and dying against
the target, particles, spawns, light cues, lights used, drop share). In it the final peak holds 413
on screen on average and 428 at most.

**Before the review** (`song/before-review/`, the earlier measurement: a looser frame-commit rule,
frames on their own clock, windows labelled up to about 1.5 s early, and the run's last seconds
judged only by chance): 500 failed (worst 4.539 ms, 6 windows), 475 failed (4.502 ms, 5 windows),
450 passed once (4.176 ms) and failed once (4.208 ms, 2 windows), 425 passed twice (3.90 and 3.86
ms), and the first two runs of the baked 425 build passed (4.027 and 4.035 ms). These led to the
envelope; the runs above confirm it.

What they show: at the envelope the GPU's p99 binds; main thread p99 stays under 3.6 ms and render
under 1.3 ms; no window drops light (p99 0; frames that drop light are under 1% of any window), no
spawn is rejected. The worst window is always the last (the climax's last 7 s, the outro and the
ending's clear). 7 to 8% of frames get no GPU time from FrameTimingManager even with the harness's
policy, so every run is "noisy" by the 0.5% rule and a GPU p99 may read slightly low.

What they don't show: a real laptop (every target number is inferred, TD12's report); what makes
the climax cost more than S3 at the same count (the boss, the laser, the arena around the
courtyard and TD13's light clustering all differ, and no run isolates them); the live audio's
timing (the operator's listening rows); and the look (the video and the operator's viewing row).

`choreography/song-onscreen.txt` is the choreography CLI's output at 425 (`song --hero --onscreen`
from `demo3d/`: the section table, the hero's position every 5 s and the on-screen count every
second): the climax holds 426 on average and 452 at most, alive and dying, with the hero at about
(9.5, 16), against the courtyard's east wall.

Reproduce (from `demo3d/`, after `demo3d unity BakeAll` and `demo3d build --backend il2cpp`):

    Builds\il2cpp\PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -job-worker-count 6 --song-check <out.json> --stats <out.csv> [--envelope N]

The video (gitignored, `D:\wt\demo3d\captures\td13\`): `demo.mp4` from
`--capture-video ..\captures\td13\demo.mp4 --dump-sounds ..\captures\td13\sounds.json`
(fixed-time 1080p60, 11,940 frames, 199 s), and `demo-mixed.mp4` from
`uv run --project demo3d/tools demo3d mix --events captures/td13/sounds.json --video captures/td13/demo.mp4 --out captures/td13/demo-mixed.mp4`
(the song plus all 2,770 sound events; the NCS song and vendor sounds stay out of git).
