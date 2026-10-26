# SH3 — sword spin

The published sword shot drives a procedural 360-degree pink sweep, three fading
arc afterimages, a white core and tip glow. Accents use a wider gold sweep and
expanding shock ring. `normal.png`, `accent.png` and the silent
[4-second capture](spin-and-accent.mp4) come from `--sword-check`: real Engine
shots over synthetic 128 BPM committed beats/one forecast, with enemies disabled
in memory. This shows presentation, not encounter pacing. `live-still.png` is a
normal-damage File-mode run. No new asset bake or source clip was needed.

## TTK and radius

`ttk-and-radius.txt` is the kept output of
`cargo test -p sim survivor_sword_ttk_and_personal_space_measurements -- --nocapture`.
It reads the real bat/skeleton HP and applies actual sword Fates. Targets are
stationary, with knockback and XP disabled to isolate hit count. First shot is
at zero; full-cycle TTK adds one attack period to death minus first hit, modeling
entry just after the preceding attack. The Whip column is the analytical source
reference `ceil(HP / 1) * 1.35 s`, not an execution of Vampire Survivors.

| BPM | Bat HP 0.5: hits / full-cycle s | Skeleton HP 1.5: hits / full-cycle s | Whip reference bat / skeleton s |
|---|---|---|---|
| 70 | 1 / 0.857 | 3 / 2.571 | 1.350 / 2.700 |
| 128 | 2 / 0.938 | 5 / 2.344 | 1.350 / 2.700 |
| 174 | 2 / 0.690 | 6 / 2.069 | 1.350 / 2.700 |

All six full-cycle results beat that rank-one Whip reference. Damage, cooldown,
amount and accent damage are unchanged. The base radius changes from 80 to 48 px.
The same test compares four radii, two real chasing roles, three BPMs and four
spawn phases (96 cases), normal hero damage, starting 150 px away. Each radius
has 24 cases. Minimum/median kill distances and total bites are:

| Radius px | Minimum / median kill distance px | Hero bites |
|---|---|---|
| 48 | 12.000 / 45.253 | 8 |
| 56 | 24.946 / 50.995 | 1 |
| 64 | 24.946 / 56.979 | 0 |
| 80 | 48.608 / 75.206 | 0 |

48 px gives the short personal-space weapon a closer kill boundary and leaves
real contact exposure. These isolated single-target cases do not establish
whole-run survival, crowd balance or feel. SH11/SW7 own their sweeps; operator
feel remains pending. `radius-before.txt` is the initial phase-zero probe before
full-cycle reporting, retained as an intermediate output. Historical SF5's
80 px evidence is unchanged. `radius-summary.json` is a compact summary of the
96 rows; `radius-boundary-test.txt` tests actual inside/outside damage.

## Sound and rollback interface

Survivors fire cues (`sword-fire`, `sword-accent-fire`) keep the published shot
onset, including non-sixteenth subdivisions. Impact cues (`sword-hit`,
`sword-accent-hit`) are projected only from existing grid-aligned EnemyImpact
journal records. They use the SH2 scheduler, rollback cancellation and category
caps. No new simulation record/hash bytes are introduced. One weapon/grid cue
coalesces simultaneous targets. Legacy sounds keep their previous path.
`Cause::WeaponImpact { s, what, accent }` carries a typed weapon identity for
future evolutions; SW5 must add its palette/visual routing when Bloody Tear
exists. `Sprite::arc` is reusable by SH4 and later presentation work.

## Validation and limits

- `live-tests.txt`: all 16 eval live tests pass, including an impact projection
  tied to real EnemyImpact records; `session-tests.txt`: three session tests pass.
- `sound-tests.txt`: actual decoded palette scheduling/caps/cancellation and the
  exact subdivision-onset regression pass. No listening verdict is claimed.
- `clock-fixture-tests.txt`: all17 clock/freeze/finale tests pass after their
  local fixture disables unrelated combat spawns. The initial fast check caught
  two tests entering XP offers after the reach change (`check-fast-initial.txt`);
  gameplay behavior is retained and the timing fixture now isolates its subject.
- `arc-test.txt` and `render-tests.txt` (all10 tests): GPU arc has a hollow centre, directional tail and no pixels
  outside its span. Stills were visually inspected.
- `headless.txt` / `replay.txt`: fresh 20-second normal-damage File run, 43 sound
  starts, zero late skips/drops; 11,279 live settled records match replay.
- `card-capture-regression.txt`: shared temporary clip helper still completes
  SH7's open/pick/resume fixture. Raw temporary frames are removed on drop.
- `benchmark-contexts.json` and matching logs compare unchanged staging
  340787afc28a7963aad9385b4a83e967f26eeaa5 with this source. Normal gate scheduling:
  baseline p99 1.18 ms, candidate 1.15 ms, both below 4 ms. Background scheduling:
  baseline 4.77 ms and candidate 4.66 ms, both fail. `bench.txt` is the original
  background invocation, retained honestly; the gate uses normal scheduling.
  This CPU frame-work sample is not a GPU time or minute-25 density verdict.

Run from `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 20
cargo run --release -p app -- --headless --sword-check /tmp/sword-check
```

Cold runner review and final fast-check results are recorded alongside delivery
in the bead notes. Source commit SHA is recorded there after commit; it cannot
be embedded into its own tree.
