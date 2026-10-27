# SP5 — final evolved density and milestone P

The actual hero, HP bar and gem cores stay above additive effects. Nearby
per-hit recipes merge in48px cells over50ms, with separate24-cell budgets for
hits/deaths. A150ms hit count reduces particles, alpha and size; dense body
reactions keep their original colors instead of turning an entire horde
white. Ordinary per-hit decals fade within1s; their other source layers retain
their configured lifetime. Boss death showpieces bypass this budget. All enemy
flashes/falls, logical damage and per-enemy/per-beat sums remain; labels that
occupy the same42×22px screen cell share that space. Sparse effects, weapon
showpieces, bosses, chest/evolution overlays and legacy rendering remain.

## Actual workload and limits

New explicit fixtures install all five actual evolutions and six applied max
passives before the first rollback snapshot. The original base-loadout release
fixture remains separate. `evolved_density_engine` replaces each minute with
minute25's XLFlower roster at300 actors, HP1e12 and400 zero-XP gems, with the
hero damage-exempt. This is performance/presentation evidence, never balance
or natural acquisition evidence. The moving case uses real directional keys,
attraction poses and pickup events, so its gem count falls from400. The death
case uses HP40/XP0 and actual lethal fates at the same source roster/minimum.

`density_check.rs` keeps nine offscreen stills and `density.jsonl`, from the
real house-128 stream, real engine records and actual final weapon visuals.
`captures.txt` confirms all three cases; all nine stills were inspected.
The final capture run uses the same final source as the production benchmarks.
No screenshot is a natural minute25 survival claim. Operator eye/ear checks
remain in operator-pending.

## Frame work

Existing 120Hz / 60s / 1920×1080 workload, lead 0.2s and 5–35ms late keys,
including 1,776 rollbacks. CPU time counts engine, scene and GPU encoding; it is not a
GPU-completion or maximum-frame bar. At the app's normal scheduling priority:

| Final loadout | Frame p99 / max ms | Engine / scene / encode p99 ms |
|---|---:|---:|
|Five evolved, 300 foes / 400 stationary gems|3.18 / 4.37|2.728 / 0.212 / 0.403|
|Five evolved, moving, 300 foes, 306–400 gems, 36 moving poses, 24 pickup moments|3.21 / 4.44|2.799 / 0.179 / 0.340|

Raw `bench-*-final.txt` files keep final source stats and counts. Earlier
production-priority runs remain as `bench-*-production.txt`. Final runs pass the
unchanged4ms p99 bar. The same workload under background QoS is slower
(`bench-evolved.txt`/`bench-moving.txt`,10.17/10.55ms p99 after optimization;
15.78/16.39ms before). Background QoS pins work to efficiency cores and these
are not production-priority frame certifications. Long capture/age jobs stay
at background priority. The normal-priority benchmarks use the existing
app benchmark's scheduling, not a raised thread priority.

## Exact contact optimization

Sampling (`profile-before.txt`) identified repeated full-foe scans by Bible
and Wand collision tests. An ephemeral exact-time spatial broad phase indexes
positions once per weapon pass, keeps source contact predicates and original
alive-list order, and rechecks live eligibility after earlier hits. Damage
queues recoil for later dispatch, so positions do not move inside the pass.
No collision shape, cooldown, damage or target tie changes; no cache enters
rollback state. Large queries fall back to the ordered point list.

`contacts-test.txt` compares indexed contacts/order against the real
`aim::catches` predicate at cell boundaries, zero-length and long sweeps,
negative coordinates and varied body radii. `contact-equivalence.txt` keeps
four actual before/after cases (stationary/moving × durable/killable): every
settled hash/count matches exactly, including845 rollbacks at lead0.237 and
25ms late keys. The cases include actual kills/recoil and moving pickups.
The baseline uses preoptimization Bible/Missile code from77602ca9 with these
same final-loadout fixtures; `contact-optimization.patch` records the exact
runtime delta. `source-sha256.txt` records measured source inputs.

## Scale, smoke and review

The background30-minute age/step/rollback remeasurement uses SF10's kept
normal arrived stream (`/tmp/sf10-normal-session`, reconstructed from its kept
recording) and final evolved fixture. This keeps the recorded SF9 level as a
synthetic stress configuration, replacing all spawn rows with the minute25
roster. The house-stream frame/capture checks above use the current level.
Input stream SHA256 is1e056fad9ac630251e4c8260b92405fd8f1007730ca1164c196392d5e25deaa3
(18,760,947bytes), restored from native/evidence/sf9/recorded-normal-30/.
`scale-age-before.txt` is the original scan implementation; `scale-age.txt` is
the indexed implementation. `scale-equivalence.json` checks the same
37,770,095 settled records/hash bbf44af1350f9943 over the whole run.

|Active minute|Snapshot copied bytes (heap+inline)|Clone CPU median/p99 µs|
|---|---:|---:|
|1|300,533 (294,749+5,784)|18.667 /20.167|
|15|286,140 (280,356+5,784)|13.875 /14.541|
|30|286,104 (280,320+5,784)|27.459 /29.541|

Minute30/minute1 is0.951989× (under2×), with300 foes/400 gems at every
checkpoint. These measure31 actual snapshot clones, not total retained run
memory. Peak process footprint45,187,456B; maxRSS54,362,112B. The indexed
background minute25 window has7,699frames/642late keys/2,231rollback frames;
step CPU p50/p990.577/9.569ms, rollback CPU p9910.332ms and14,869.894ms total
re-simulation CPU. These background CPU numbers are scoped separately from
the production frame bar. Final storage has0foes/400gems and no retained
settled log. Inner measurement wall517.091s excludes startup; external time
831.14s includes background-priority startup parsing/waits. The baseline
inner wall1,121.323s includes different host contention and is not a speedup
claim.

`headless.txt` keeps the normal File/Delayed30s app check:163 sounds,0 late or
dropped; `normal-replay.txt` keeps its replay. `live-tests.txt` keeps21 actual
view-cache/rollback tests; source/evolution and budget checks are retained.
`app-tests-final.txt` keeps71 final app tests including source boss-layer
lifetimes, actual live/retired boss Scene reactions in a full budget, sparse
critical label order/color/size and dense label/value preservation. The first
local full-CI sparse-label failure is retained separately; the final app
suite/lint was rerun after that bounded fix.
No analyzer/assets/baker/CI/policy changes. Because the shared fixture was
refactored, local scripts/ci-full ran and passed after review fixes. The full
suite includes all survival gates and a diagnostic29,042,724-record replay,
hash0a2cc6b87ae71bad (not a normal-damage survival claim). `ci-summary.json`
records its exit status and source fingerprint. The separate normal-damage
app smoke/replay remains identified above. `check-fast.txt` keeps the final
aggregate run after all source fixes.

`milestone-p-commits.json` identifies SP1–SP4's exact landed commits/native
notes, alongside SP5's final diff. One fresh cold read-only warden review
covered this cohort; its confirmed boss-effect finding and fixes are recorded
in `review.md` and native bead notes. No confirmation review ran.

Run from `native/`:

```
target/release/playlist-survivors --bench-evolved --check
target/release/playlist-survivors --bench-evolved-moving --check
target/release/playlist-survivors --density-check evidence/sp5
mkdir -p /tmp/sp5-scale-input
cp evidence/sf9/recorded-normal-30/meta.json evidence/sf9/recorded-normal-30/keys.jsonl /tmp/sp5-scale-input/
gzip -dc evidence/sf9/recorded-normal-30/stream.jsonl.gz > /tmp/sp5-scale-input/stream.jsonl
cargo run --release -p cli --example survivors_scale -- /tmp/sp5-scale-input evolved
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
```
