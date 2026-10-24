# B4: the simulation core against the oracle

Measured 2026-10-24 on arm64 macOS (rustc 1.98.1), hv-t8o8.16.

## Event logs

`crates/sim/tests/oracle_prefix.rs` replays each full core run's keys
(`native/oracle/fixtures/<track>/<profile>/keys.jsonl.gz`) in the Rust simulation, fed the
track's `stream.json` as Replay with `StreamEnd`, and compares the simulation's own categories of
`events.jsonl.gz` line for line: `beat`, `input`, `settle`, `swing`, `cleave`, the `groove`,
`tier` and `health` series, `enemy` (its fates, steps and death as separate timed lines), `hit`,
`bite`, `heal`, `upgrade`, `chicken`, `vacuum`, `weapon`, `chain` (its points as timed lines),
`hud.xp`, `hud.fire` and `hud.levelUp`. The emitted categories (`effect`, `sound`, `ui`, `gem`,
`hud.kill`, `hud.perfect`) are the recording's (B7); set pieces, archers, the boss, zone waves and
landmarks are B6's.

- **To T** (B4's acceptance): every line before the first judging-dependent moment (the first
  judged press, or the first groove change after song time 0).
- **To the first set piece:** every line before B6's first event (a set piece, an archer, a zone
  wave, a landmark, a slam or a vacuum). Judging and the settled groove are ported with the core,
  so this already covers most of B5's ground.

| Run | Lines to T | Lines to the first set piece |
|---|---|---|
| `accelerando-100-140/reactive-moderate` | 73 | 359 |
| `causality-mix/mash4` | 88 | 1147 |
| `causality-mix/reactive-moderate` | 87 | 1225 |
| `cc-hairy-larry-and-the-flying-hungarians-ride-me-to-heaven/reactive-moderate` | 88 | 1232 |
| `cc-kevin-macleod-black-vortex/reactive-moderate` | 140 | 5757 |
| `cc-kimiko-ishizaka-aria/reactive-moderate` | 148 | 807 |
| `dnb-174/reactive-moderate` | 80 | 486 |
| `hiphop-92-swing/reactive-moderate` | 73 | 325 |
| `house-128/reactive-moderate` | 79 | 395 |
| `notjam-breakbeat-chips/reactive-moderate` | 75 | 1212 |
| `notjam-chill-menu/reactive-moderate` | 76 | 1024 |
| `notjam-waltz-of-the-lich-king/reactive-moderate` | 196 | 355 |
| `orpheus-galop/mash4` | 158 | 2595 |
| `orpheus-galop/reactive-moderate` | 158 | 2595 |
| `pop-arrangement/reactive-moderate` | 154 | 1048 |

Every line matches on all 15 full runs in both checks (2.7 s for both in the test profile). Not
shown: the chain runs (the other profiles and tracks: their hash chains include the emitted
categories, so they wait for B7), anything at or after B6's first event, and the frame hashes
(B7).

The TS simulation read some beat times past the stream known at the time; the streaming
simulation reproduces them exactly without reading ahead:

- **Deferred beat times.** A downbeat (a level-up after the director's wait, an overdue level-up),
  an orb's apply time, a cleave's first band and its bands, a chain's next jump and an enemy's
  entrance on a bar are queued, at the place in push order TS gave them, once the beat their time
  needs arrives. Such a time falls at or after the beat before it, and every shipped beat is known
  by its predecessor's time (checked over 14,468 beats; the simulation asserts it).
- **A stomp's cancelled held strides** run to a stride step up to one grid step ahead. It is kept
  as an index and read when a held stride asks: a step not yet known lies past every step a held
  stride is queued at.

The oracle runs reach few of these (one level-up, one orb, no deferred downbeat), so
`engine::tests` checks a downbeat whose beat arrives late, and the oracle test file checks that
FORMAT.md's frame procedure records exactly what a single run does and that a restored snapshot
runs on the same.

serde_json parses decimals only approximately unless its `float_roundtrip` feature is on: a
fixture's `0.23333333333333334` read as `0.23333333333333336`. `stream` and `sim` now enable it, so
`stream.json` reads exactly as `JSON.parse` reads it.

## The gate

`scripts/ci-gate` on this change (its native section: rustfmt, purity, clippy and the tests of
`sim` and `stream`), in a fresh `CARGO_TARGET_DIR`:

| Run | Time | Host load (1 min) |
|---|---|---|
| Cold (dependencies built) | 21 s | 5.17 |
| Warm | 5 s | 7.78 |

Within the gate's 60 s budget. Tollgate's persistent gate directory keeps the dependencies built,
so a candidate touching the sim pays about the warm figure plus the crate's own rebuild.
