# G5: the live engine's cost

Measured 2026-10-24 on the development Mac (Apple silicon), host load average 11–13 throughout
(other sessions' builds and tests): the numbers are under load, not on an idle machine.

## Engine per frame and rollback re-simulation (`ps runner-bench`)

House-128's stream replayed as it would arrive, frames at 120 Hz with a 50 ms stall 1% of the
time, a press and release on each committed beat reaching the runner 5–35 ms late, the simulation
0.2 s ahead; 90 s.

```
cd native && cargo run --release -p cli -- runner-bench
```

| Run | Engine per frame median / p95 / p99 / max (ms) | Re-simulation per second median / p95 (ms) | Rollbacks, song replayed |
|---|---|---|---|
| 1 | 0.025 / 0.530 / 0.743 / 1.27 | 5.6 / 17.2 | 2043, 428.7 s (4.8× real time) |
| 2 | 0.026 / 0.637 / 0.874 / 1.67 | 5.4 / 20.4 | same |
| 3 | 0.026 / 0.551 / 0.794 / 1.61 | 5.6 / 18.0 | same |
| 4 | 0.025 / 0.553 / 0.800 / 1.55 | 5.6 / 19.8 | same |

Re-simulation p95 is about 20 ms of CPU per second against the 250 ms target: no fallback
(docs/design/native-app.md, "Arrivals roll back like late keys") is needed. Gates, the worst p95
plus half: engine per frame p95 ≤ 1.0 ms, re-simulation p95 ≤ 31 ms per second.

## The frame's work: sim, evaluate, encode (`playlist-survivors --bench`)

The same stream and presses for 60 s at 120 Hz, each frame's scene and HUD built and encoded (post
passes included) into a 1920 × 1080 offscreen target; the drawable's wait is not part of it.

```
cd native && cargo run --release -p app -- --bench
```

| Run | Median / p95 / p99 / max (ms) |
|---|---|
| 1 | 0.27 / 0.92 / 1.19 / 6.39 |
| 2 | 0.24 / 0.85 / 1.13 / 5.72 |
| 3 | 0.23 / 0.80 / 1.04 / 5.87 |

p99 about 1.2 ms against the 4 ms target. Gate: p95 ≤ 1.4 ms (0.92 plus half). The max (about
6 ms, once per run) is the first frames' pipeline and texture setup.

What this does not show: the GPU's own time (the encode is the CPU's share), a visible window's
present, AirPods' longer lead (rollbacks grow with it), or an idle machine.

## Live, muted (`--file … --mute`)

House-128 in File mode for 30 s, no presses: 862 rollbacks re-simulating 160.8 s of song (5.4×
real time), every sound on time (0 skipped late). The app's own frame timing there is not usable:
with the window hidden the drawable is refused and frames return at once (about 23 000 a second),
filed as its own bead.
