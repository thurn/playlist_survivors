# SF10 — bounded long-run state and F review

SF10 bounds Survivors storage and snapshot copying while preserving monotonic
recording IDs, deterministic playback and causal use of arrived stream data.
The measured source `561b18be33a7e08608e5ac2bb25f881d3a04514d` includes SH8/SH9 and the cold-preparation repair
`16f2e75f7f2aafb90c8345d45e23888c7a8d4172`; native bead notes record its final
source and integrated SHA. `milestone-f-commits.json` preserves the F commits
and native notes/comments used by the cold review. `review.md` records its
three confirmed findings, corrections and regression checks. There is no
confirmation review of the fixes.

## Implementation

Live pools retain dense objects behind monotonic public IDs, preserving ID
order and RNG identity. Cleanup keeps sampling/set-piece roots and queued or
deferred effects. Retired recoil and missile targets are rejected; the latter
was caught by SH5's real-XP/projectile rollback tests during final integration.
A missile then uses its existing retarget path. Legacy pools remain unchanged.

Immutable 256-entry shared pages hold growing beat/tick/grid/stride histories.
Snapshot clones share those pages and their directory; lazy contiguous read
caches are not cloned. Song indices are immutable shared data. Spatial bins
and a 64-entry pure separation memo live outside rollback state. A digest is
only a hint: all input bits and the bin origin must match before reuse.

The view caches surviving displayed journal prefixes, invalidating rolled-back
prefixes. Tests compare every cached view and emitted moment with the former
full-overlay/linear-dedup path at nondefault lead and delayed keys. Replay
keeps only the requested settled-hash prefix and consumes transient moments;
it retains no settled record log. It lives in `session/replay.rs`, preserving
the prerequisite repair's analyzer cache isolation.

The review fixed finite gem timing on zero/one-beat input, victory waiting
forever without a future committed downbeat, and retained replay moments.
Survivors now uses a finite free pulse when the committed grid is exhausted;
healthy grid timing stays unchanged. Finale damage clears even synthetic
high-HP foes. Tests compare full/truncated and poisoned uncommitted futures,
plus frame cuts and full-session replay. One obsolete indefinite-victory-wait
expectation was rewritten for the fixed ending; this is not TS-oracle parity.

## Equal-density measurement

Command from `native/`:

```sh
/usr/bin/time -l cargo run --release -p cli --example survivors_scale -- /tmp/sf10-normal-session equal
```

The decompressed input is SF9's kept reverse-order Rust stream, SHA256
`1e056fad9ac630251e4c8260b92405fd8f1007730ca1164c196392d5e25deaa3`,
18,760,947 bytes. No analyzer or stitched synthetic stream replaces that input.
The synthetic schedule uses minute 25's XLFlower roster at constant 300
population, effectively unkillable HP, damage exemption and 400 zero-XP gems.
Before the first snapshot, it maxes every base weapon implemented in that measured source
(Sword, Garlic, Magic Wand) and all six passives, applying their real stat deltas. The setup test asserts Amount 2, Cooldown 0.6, Might 1.5, Recovery 1, Duration 1.5 and max HP 298.5984, including after rollback. This fixture is separate from
play sessions and does not change their knobs, offers or progression. Future
implemented base weapons automatically join it; SP5 measures final effects.

Checkpoints precede victory at 59.9, 899.9 and 1799.9 run seconds. Thirty-one
clones measure actual allocator-requested heap bytes and clone CPU time;
inline snapshot bytes are added separately. These are clone costs, not total
run memory: shared stream history remains one retained copy, and the reported
history index bytes are a conservative upper sum of possibly shared indices.
The bounded scratch memo is also reported separately.

| Run minute | Heap bytes | Total with 5,440 inline bytes | Allocations | Clone CPU median / p99, µs |
|---|---:|---:|---:|---:|
| 1 | 311,252 | 316,692 | 461 | 43.959 / 58.584 |
| 15 | 257,102 | 262,542 | 78 | 12.083 / 16.916 |
| 30 | 260,044 | 265,484 | 94 | 12.708 / 16.125 |

Minute 30 / minute 1 is **0.838303×**, below 2×. Active effect state follows
the music phase; these are equal foe/gem counts, not identical projectile
phases. The simulation loop takes 485.624 s wall at background QoS (build/input setup is separate in the raw timing),
including contention while the utility matrix runs. It retains no settled
record log. Final cleanup leaves zero foes and 400 original zero-XP gems.
The pure separation memo is bounded at 1,228,800 payload bytes.

The minute-25 window covers 7,699 frames over 60 run seconds, 642 injected
25 ms late key transitions, 2,231 rollback frames and 459.364 re-simulated
song seconds. Background engine CPU p50/p99 is 0.450/7.814 ms;
rollback-frame CPU p99 is 8.874 ms, 9.647 s total. These deliberately lower
priority CPU values do not certify the production frame bar below.


## Offscreen frame bar

`native/target/release/playlist-survivors --bench-density --check` runs at
production priority, offscreen at 1920×1080/120 Hz with 300 foes, 400 actual
simulation gems, max current loadout, lead 0.2 and 5–35 ms late scripted Space
keys. The kept log prints the real inventory and finalized stats. The native release step enforces
the existing p99 <4 ms bar. No window or Dock icon opens.

The full native component passes on the measured source: frame work p99
**3.02 ms**, max 4.85 ms over 7,200 frames (7,080 at verified density).
Engine/scene/encode CPU p99 are 2.219/0.687/0.356 ms; 1,776 rollbacks
re-simulate 2.160 s wall. See `frame-density-final.txt`. The bar is p99,
not a maximum-frame threshold.

The first post-SH9 run failed at 4.64 ms. A fixture audit also found that
passive ranks had been maxed without applying their stat deltas. That setup
error is corrected through the production `apply_survivor_stats` path; its
old scale runs are diagnostic only, with scope corrections in `pre-sh8/`
and `pre-applied-passive-stats/`. The normal matrix/replay never uses that
synthetic setup. The corrected focused profile and full native check both
pass; no threshold changed and no effects were disabled.

Background/utility experiments, the old Sword-only baseline, pre-SH3 outputs
and pre-review runs remain explicitly historical evidence. Lower-priority CPU
measurements do not certify the production-priority frame bar. The early BAT1
overload and forced legacy-weapon experiment are not acceptance evidence; the
latter hit an unsupported long-gap fire-trail assertion. No legacy gameplay
change was made to accommodate it.

## Normal-damage matrix and replay

`scripts/ci-full survivors` passes all 41 reports in **320.403 s**, reusing
valid source/audio-keyed Rust analyses. All 20 positioning cases pass the
survival criteria. Seven AFK cases survive too long; they are reports for SW7.
Pacing is never asserted. Exact commands, case reports and cache-hit stderr
are in `matrix-final/`.

The normal-damage original-order positioning seed-1 run finishes 1,800 run
seconds above **84.583% minimum HP**, level 17 with 623 kills. The release
replay component settles all **31,399,652 records** exactly, hash
`1fb4266a6a8eae06`, diagnostic=False, in 26.715 s. Its outer measured maximum
resident set is **76,349,440 bytes** (not the wrapper's own physical footprint).
Metadata/keys and raw-input provenance are in `recorded-normal-final/`.
The 19,063,791-byte original-order input is byte-identical to the kept gzip
under `pre-sh8/recorded-normal-current/`, avoiding a duplicate 3.5 MB blob.
Use the matching validated source to reproduce a recording; later deliberate
weapon/offer changes are not treated as an oracle for old runs.

The same-gauge historical comparison is separate: SF9's original direct
replay had a 5,014,343,640-byte physical peak; pre-SH8 SF10's direct replay is
53,068,160 bytes. Their source and commands are retained, and the current
normal replay above is independently verified after pickup integration.


No easy knobs or offers change. Only SW7 tunes them; AFK/pacing observations
are handed off as reports. The first cold continuous matrix timeout is kept
in `continuous-preparation-timeout.txt` with its profile; prerequisite `.41`
repaired it at cause. Its independent cold 41-case matrix took 670.8 s within
the unchanged 1200 s budget. SF10 preserves that analysis cache and uses its
own sim-dependent matrix key.

## Playability and validation

```sh
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions native/target/release/playlist-survivors --playlist native/playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 30
```

Session `1793048812` is the final real-time check. The final 30-second headless File/Delayed check stops normally after
24 run seconds: 106 sounds, zero skipped late/dropped, two speculative
cancellations. It selects Garlic from a real level-up and resumes on the grid.
No eye/ear acceptance is inferred from a muted headless check.

The corrected source's `scripts/check-fast` passes in 35 s (native 34 s),
followed immediately by its Conventional Commit. The full native component
passes in 262.27 s, including the corrected frame bar and all ignored tests.
Its unchanged corpus-parity inputs reuse the earlier eight-track pass.
Only evidence/documentation changes follow; the final source-equivalence
check is recorded in the native notes. No repeat was run just to obtain a
faster warm check.


Focused checks cover shared-page boundaries/branching, ID retirement and
snapshot restore, queued/deferred roots, separation-cache input collisions,
cached-vs-uncached physics, exact view/moment reference behavior, real upgrade
picks/projectiles across late-key rollback, and replay prefix boundaries
(zero, one, middle, exact and too long). Native ignored tests and corpus parity
run in the updated full native component. Earlier full baker/Python-analysis
components remain valid because their inputs did not change. All long CLI
jobs enter background QoS; release matrices/parity retain utility QoS.

Later milestone commits may land while these long measurements run. Every
measurement remains tied to its source; Tollgate's gate validates combined
integration, and later release checks automatically include newly implemented
weapons. SP5 owns the final-effects remeasurement. Source and integrated
commit SHAs are recorded separately in the native bead notes.

The milestone File/System eye/ear/feel checks are recorded in
`native/evidence/operator-pending.md`, including capture permission and speakers.
They do not block delivery. Old recovery checkpoints, rebases and their fast
checks are retained as history; only the final outcomes above certify this
source.
