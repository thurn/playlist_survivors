# SF9 — causal bots and survival reports

Initial base `d5e988ec7cdd1e5ba10787f124065f212ead9cff`; refreshed onto SH1's
`fd8ee17c06c53a3e53048c1dfbd1ee9bb5397d54` before final checks. Delivery SHA and
candidate are in `hv-n7fn.9`'s native notes. No game stats or offers change.

## Players and choices

`players::SurvivorBot` implements the existing observation-only `Player`
interface. It sees the current hero, visible enemy/gem positions and the
published offer, never the stream. `positioning` is the CLI default;
`afk`, `random-pick` and the older diagnostic `held` are explicit choices.

The positioning bot keeps a direction for a stride, then chooses another from
the current state. It flees the distance-weighted crowd centre within 240 px
(three starting sword radii). Within 90 px (sword radius plus hero radius), it
prioritizes escape; otherwise it pulls toward the nearest gem, with a 0.35
crowd bend. Those are bot geometry choices, not adjustments to VS or game
numbers. Eight-direction movement holds at most two adjacent arrows; it never
presses Space or opposite arrows. With no nearby crowd or gem it keeps its
seeded initial heading. AFK sends no movement presses outside menus.

Every bot releases its movement keys on a freeze. Card1 is selected for
positioning/AFK; random-pick hashes the full seed string and level, so a
reopened screen doesn't consume another random draw. They wait one heard beat
(the screen's final reveal interval) after card3, then send one highlight and
Confirm press, releasing both on the next poll. This uses the screen's fixed
known timings without predicting an unknown stream entry. On resume the
positioning bots freshly press movement, respecting the held-key guard.

`ps survive --bot positioning|afk|random-pick --seed N --order
original|reverse|rotate` preserves the same gap schedule in each real track
order. A prepared fixture rejects an alternative order instead of merely
relabeling the same input. Reports retain both clocks, HP history/minimum,
levels by minute, weapon ranks at chests, evolutions, peak alive and settled
hash. Chests/evolutions remain empty until their gameplay beads land.

## Gate and focused checks

The committed Rust fixture is 180 **song** seconds. Choices hold the run clock:
the initial smoke reaches 169.121 run seconds, level 3, full HP and peak 300,
with 1,459,699 settled records in 1.16–1.41 wall seconds. Only surviving that
fixture prefix is asserted. The user's only-survival-gates instruction takes
precedence over the old level-at-time assertion: level is printed as an XP
sanity report. XP/offer mechanics have their own regression tests.

- `players-tests.log`: movement, menu releases/repicks, AFK, full seed bits.
- `fixture-smoke.log`: all three bots replay real keys and ignore entries
  beyond a driven prefix, plus the three-minute survival smoke.
- `cli-tests.log`, `cache-tests.log`: actual track orders preserve gaps;
  invalid bot/order combinations fail; continuous cache keys include audio,
  model/analyzer version, order and gaps while ignoring checkout path identity.
- `sweep-unit-tests.log`: strict survival/HP boundaries, report-only pacing,
  missing minute data, bounded workers at jobs 1/2, joined worker failures,
  and continuous preparation at jobs=1 without overlapping warmup.

Initial real-audio checks on the pre-SH1 base:

- `parallel-smoke.json` and `serial-smoke.json`: positioning and AFK both
  reach 180 run seconds with full HP; jobs 4 and jobs 1 produce identical
  per-case hashes (`3e6000cdeab8e28a`, `e3a8c4f734a1e45e`). These short runs
  have no full-run balance verdict. Cold cache preparation plus the parallel
  smoke takes 357.516 s at background QoS.
- `random-pick-fixture.json` / `random-pick-replay.log`: seed 7, level 2,
  171.557 run seconds, all 1,514,388 settled records replay exactly,
  hash `7e4b433cce695348`. Full HP; no balance claim.
- `continuous-{cold,warm}.json` / `continuous-replay.log`: the first two actual
  tracks plus a 2 s gap are 126.99794 s of continuously analysed PCM. Cold
  preparation 110.918 s, warm 2.884 s at background QoS; identical hash
  `85cb947ceca25114`. The 60-run-second normal-damage check replays all 174,117
  settled records exactly. This checks a boundary/cache, not the full playlist.

## Release matrix and budget

`ci-full-survivors` is a separate release step, budget 20 minutes, with its own
Cargo target directory. `scripts/ci-full survivors` runs it; the default full
local CI includes it too. It uses utility QoS (`PS_NATIVE_BATCH=1` keeps its
children in that class); manual sweep jobs default to background QoS. Six
bounded workers are chosen for this 18-CPU, 64 GiB host, leaving capacity for
higher-priority work. Workers join on success/error and re-raise failures.

The complete matrix has 41 normal-damage reports:

- Five seeds × original/reverse/rotated orders × positioning/AFK (30).
- Five seeds × positioning/AFK on the continuously analysed original source
  (10). Its first analysis starts alongside track-cache preparation; the
  remaining continuous cases reuse the atomically written full-source cache.
- One random-pick original-order report, evidence only.

The source/audio/model/order/gap keyed continuous cache avoids analysing the
same 38-minute source ten times. A clean input-tree stamp skips the matrix
while the sim, players, engine, analyzer, content, levels, playlist, audio and
runner code stay unchanged. Dirty inputs do not create a pass stamp. Missing
report artifacts force a new run. Assertions and report-only runs have
different keys. Each command/stdout/stderr is kept, including failures.

Until SW7, failed balance criteria are **reported**. SW7 adds
`--assert-survival` to the survivor section of `scripts/ci-full`:
positioning must finish 30:00 above 25% minimum HP; AFK must die strictly before
10:00. Random-pick and damage-exempt diagnostics have no survival verdict.
Level at 5/30, weapon rank at chest and evolutions are always reports;
unreached minutes are absent rather than invented. The first normal matrix
case retains a session. The full 30-minute replay has a separate `ci-full-survivors-replay` release
step and Cargo target directory, also budgeted at 20 minutes. It reuses a
completed normal session from the exact current-input matrix, or records a
completed matrix case; if none is available, it explicitly labels an exempt
runner diagnostic. It never turns diagnostic survival into a balance verdict.
Metadata hash/count/damage mode must agree before `ps replay` compares the
whole settled prefix. `--replay-case` can choose a normal case explicitly.

Commands (from repository root):

```sh
uv run --no-project python native/scripts/survivors_sweep.py --only 2 --minutes 3 --jobs 4 --out /tmp/sf9-sweep-smoke1
uv run --no-project python native/scripts/survivors_sweep.py --only 2 --minutes 3 --jobs 1 --out /tmp/sf9-sweep-smoke-serial1
scripts/check-fast
scripts/ci-full
```

Final matrix reports, full session replay, headless playable command and
validation results are kept here and summarized in the bead's native notes.
No per-SF9 cold review: SF10 reviews milestone F. No rollback Engine algorithm
was changed.

## Full matrix measurements

The source `0f774d2` matrix completed all 41 reports in **1,126.907 s**
(18m 47s), including cold continuous analysis: within its 20-minute budget.
Seventeen of twenty positioning runs reached 30:00; thirteen also kept minimum
HP strictly above 25%. Seven balance criteria therefore failed and remain
report-only. All twenty AFK runs died before 10:00. The random-pick run finished
30:00 at minimum HP 0.483; it has no balance verdict. See `matrix/cases.csv`
and the per-case JSON/command/stderr files for every result. The complete
continuous stream retains analyzer state across the actual gaps; three cached
orders test order sensitivity. No knobs or offers were retuned.

| Input / order | Positioning passes | Positioning finished | Lowest HP range | AFK death seconds | Level at 5 (report) | Level at 30 (report) |
|---|---:|---:|---:|---:|---:|---:|
| cached / original | 3/5 | 3/5 | 0–0.85 | 323.413–329.48 | 3–9 | 7–13 |
| cached / reverse | 4/5 | 5/5 | 0.236111–0.903328 | 317.673–323.867 | 7–12 | 8–19 |
| cached / rotate | 3/5 | 5/5 | 0.1875–0.765162 | 320.629–329.529 | 3–7 | 9–13 |
| continuous / original | 3/5 | 4/5 | 0–0.8125 | 319.866–328.292 | 4–8 | 8–14 |


Level-at-minute values are sampled at the clock boundary; `lastLevel` is
separate and can rise during the victory burst. Empty chest/evolution arrays
mean those gameplay beads have not landed, rather than zero weapon ranks.

`recorded-original-fall/` retains the first normal case's exact input, metadata
and keys, with a fixed-timestamp gzip stream. Decompress as `stream.jsonl` in a
new folder alongside meta/keys to replay. This case fell at 1,394.526 run
seconds; it is not the full 30-minute replay proof.

The initial full local `scripts/ci-full` passed baker, analysis, native and the
complete matrix (`ci-full-initial.log`). The plan-table audit then added the
separate automated replay component. Its changes are Python/CI routing only:
the matrix function, compiled game, analyzer and data inputs did not change.
Their completed full-check results are reused; final fast checks and the new
replay component run on the final source. A new replay-specific stamp keeps
that proof distinct from the report-only matrix stamp.

## Full recorded-session proofs and memory baseline

The final separate replay component passes in 59.879 s. Because the matrix
stamp came from the pre-replay-wrapper source, the default path correctly did
not claim an exact current-input matrix cache. It created an explicitly
**damage-exempt** 30-minute diagnostic: all 30,500,286 settled records replay
exactly, hash `f8eb3bbe025b1b6f`; it has no survival verdict. Its kept report and
replay are in `ci-replay-diagnostic/`.

A separate **normal-damage** positioning seed-1 run in reverse order reaches
30:00 at minimum HP 75%, 1,391 kills and final level 17. Its 1,891.458 song
seconds include choice freezes. Driving takes 76.762 wall seconds at background
QoS; every one of **29,528,212** settled records replays exactly, hash
`b9aee49487a2df1d`. `recorded-normal-30/` keeps meta/keys and the exact stream as
a fixed-timestamp gzip; replay after decompressing as `stream.jsonl` alongside
meta/keys. See `normal-30*.{json,log}` for raw results.

The same `/usr/bin/time -l` outputs measure peak memory footprint 269,222,512
bytes for driving (~256.8 MiB), versus 5,014,343,640 bytes for replay (~4.67 GiB).
This is a before-SF10 baseline, not evidence that long-session state is bounded.
SF10 has these commands and numbers in its notes.

```sh
/usr/bin/time -l native/target/release/ps survive --bot positioning --seed 1 --order reverse --minutes 30 --record /tmp/sf9-normal-30-session1
/usr/bin/time -l native/target/release/ps replay /tmp/sf9-normal-30-session1
scripts/ci-full survivors-replay
```

After the replay/CI routing additions, final `scripts/check-fast` passes in
27 s (native 26 s). No Rust/game/analyzer or matrix-function source changed
after the complete initial full check; the newly added replay section and its
12 Python tests were checked on the final source. The initial 100 s aggregate
was cold across the SH1 integration, above the nominal local budget; the warm
final aggregate is within it.

## Playable app check

```sh
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions native/target/release/playlist-survivors --playlist native/playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 30
```

`headless-final.log` is the normal-priority real-time check; it stops normally,
with no app window. The earlier background-priority check (`headless-background.log`)
skipped five effects, so it is kept as a QoS comparison rather than a successful
audio-timing claim. Real-time File checks use normal app priority; accelerated
survive/replay batches use the background/utility policy above.
