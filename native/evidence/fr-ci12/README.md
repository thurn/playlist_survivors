# Dense-combat CI follow-up

The FR2 release run `01a17e0d-031c-7ff3-93dd-b03f9d4bedb5` failed
`ci-full-native`: `--bench-density --check` reported frame p99 4.00 ms over
its 4 ms gate (prior-release.txt). Local full CI had passed on the subsequent
CI-policy commit. We preserve the benchmark's scene, assertions and threshold.

The restored step motion repeatedly recalculated separation during a beat's
short travel window even after every destination was fixed. A guard now skips
that work when no foe can create a new step. Integration still happens first.
A late spawn, recoil expiry or landing still triggers separation and a causal
shortened step. No gameplay balance, collision distance, travel or feel knob
changes. Scratch separation vectors are only consumed when creating a new step.

`focused.txt`: all ten main-beat tests pass, including a regression proving no
new separation/cache work between admissions and preserved late spawn, recoil,
landing and stationary holds. Existing full determinism/causality tests run in
check-fast. Cold read-only warden review: no actionable findings. Its limitation
was no explicit recoil/landing test; that coverage was added before delivery.

Same local release binary/scene at 1080p, 300 foes, 400 gems, 7,200 frames:

| Observation | Frame p99 | Engine p99 | Max frame |
| --- | ---: | ---: | ---: |
| Before | 2.56 ms | 2.167 ms | 3.34 ms |
| After | 2.68 ms | 2.260 ms | 3.48 ms |

Both pass. These runs do **not** establish a frame-time improvement or reproduce
the prior release failure; host variation dominates this small change. The
regression establishes removal of redundant work, not universal performance.
No gate was weakened. Exact outputs are before.txt/after.txt. Final aggregate:
check-fast.txt. Release remains asynchronous; delivery waits only for gate.

Playable from native/: `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed`.
