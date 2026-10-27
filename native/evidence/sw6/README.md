# SW6 — timed swarms and closing flower walls

Base `c5162345fb14a5e2834836e7e98c18338a384446`. Necessary source-curation
prerequisite `hv-n7fn.43` repaired the two event-only combat rows omitted by
SF1. Its separate fresh worktree/source commit is staged at that base; SW6
reads values only from `docs/survivors/`. No raw game data/source/assets.
Native bead notes record final source/integrated SHAs and gate certificate.

## Rules and implementation choices

- Schedule rows remain Mad Forest's 21 entries. A minute arms at its run
  boundary. Its first jump of at least 6 dB must be known after that boundary
  and have a timestamp inside that run minute; older or sub-threshold jumps
  cannot unlock it. Neither a future entry nor a future timestamp is used
  early. Without a qualifying jump, the unlock is at run minute +50 s.
- The demo moves the **first attempt** to that drop/fallback, then retains
  the source delay between extra attempts. `extraRepeats` means additional
  firings. Zero delay produces one attempt, even if repeats are nonzero.
  This resolves the source timer's initial delay against the fixed design's
  on-drop first firing. Unlocked repeat trains may cross the next minute;
  they are neither compressed nor silently discarded at a minute boundary.
- Each positive chance is tested independently per attempt, with
  `random*100 <= chance/Luck`; absent or explicit zero is unconditional.
  The demo's Luck is 1. Keys include seed, origin minute, source row and
  attempt ordinal, so frame cuts, snapshots and replay consume no shared RNG
  state. Admitted attempts snap once to the next known eighth; no grid means
  their raw deadline. A menu invalidates a provisional song admission and
  reanchors it from the held run deadline after resume.
- Duration expiry also owns run time; menus/music holds cannot spend it.
  Survivors disappear as recycled removals, with no kill, XP or chicken.
  The 300-alive cap is unchanged. Far regular actors may leave to make room;
  boss holders/chests and other active event members are preserved. An
  attempt is truncated to available space, and an empty admission creates
  no phantom group. Boss admission runs before event admission at a stop.
- Source BATSWARM uses HP0.1/power1/XP1; GHOSTSWARM HP1/power5/XP1.5;
  both speed700, fixed direction and no level scaling. Existing bat/bee art
  and eighth/sixteenth gaits are reused. Spawn clusters follow the source
  viewport ellipse (0.8×0.9 of width/height, π/500 angle increments); all
  members share one crossing direction sampled toward the hero at entry.
  They keep that heading, rather than chasing the hero after entry. Recoil
  still uses source resistance. Event identities prevent far recycling.
- Flower walls use the source FLOWER role, count100, level-scaled HP,
  hero-centred viewport ellipse and evenly spaced full-ring angles. They
  reuse `pieces.rs`'s pinned `RingPath` geometry toward its existing inner
  radius100. The source converted enemy speed, rather than duration, sets
  travel distance; movement updates on native two-beat bars (finite free
  pulses without a usable grid). Plants retain sway/bob presentation.
  Source duration removes surviving members. No legacy ring vacuum or
  other non-VS set piece is added.
- All pending attempts, groups, expiry, heading and wall progress are in
  SimState snapshots. New integration into the loop is additive; Engine's
  rollback algorithm is untouched. Unknown event kinds/missing event roles
  fail content validation. No offer/easy-knob retune or new art bake.

## Checks and measurements

`tests-events.txt`: six focused tests pass: minute/knownAt/6dB boundary,
repeat counts, last-ten-second fallback, poisoned hidden jump, expiry,
pause/snapshot equivalence, source fixed direction, boss-preserving cap,
explicit-zero chance, wall HP/source-speed bar movement, and finite zero/one
beat behavior including zero-delay single-fire semantics. The wall fixture
checks actual level7 HP21 and actual path travel against converted source
speed. Snapshot output retains the recorder prefix; fixture data is not
retroactively removed after initialization.

`tests-source.txt`: ten source/schedule contracts, including both new combat
rows against the curated appendix and rejection of an unknown event kind.
`tests-sim-lib.txt`: all 94 final sim unit tests pass. `tests-live-suite.txt`: all
20 live integration tests. Focused clippy is clean. The first aggregate found a remaining content
contract expecting 20 roles; it now expects the 20 roster/boss roles plus
two event-only roles. `tests-content.txt` keeps the focused check, including
the unchanged frozen legacy subset hash. The initial aggregate failure is
separately kept; it was this bead's contract update, not a tooling failure.

The actual new-event Engine test (`tests-live-event.txt`) keeps source
count/delay/duration and moves minute2's first bat event to minute0 for a
short rollback smoke. Enemy HP1e6/XP0 is configured as data **before** Engine's
initial snapshot. Hero damage and weapon stats are normal. All **96,846
records**, hash `8ad5d382c9e26476`, match at lead0 and lead0.23/25ms-late keys,
with six key rollbacks reaching 0.254s back. Three lifetimes expire, peak100.
The initial after-construction setup experiment is separately named
`tests-live-event-initial-after-construction-setup.txt`; it is not final
fixture evidence because its check mutations were outside the initial
Engine snapshot. No runtime constructor/rollback changes were needed.

The normal-damage positioning seed1/cached original-order **five-run-minute**
prefix (`normal-five.json`) reaches run300s, level10, 850 kills, minHP
92.0833%, peak300, with 2,249,717 settled records/hash `839f3125fcc09399`.
This exercises real minute2–4 swarms and song gaps, without scripted damage
exemption or altered game data. Level/kill pace is report-only; this single
prefix is not a full-run survival/balance or 41-case matrix claim. SW7
remeasures the final game with evolutions and enables only survival gates.

## Offscreen evidence and normal app smoke

`capture/` has four inspected 1280×720 stills and three silent24fps clips:
bat/ghost96 frames each (4s), flower192 frames (8s montage of entry/closing).
The actual Engine admits 50/20/100 members on the known eighth after a +9dB
jump known at3s (sampled at3.05s). The source first attempt is isolated at
minute0 with no repeats; HP1e6/XP0 makes actors durable and suppresses offers,
hero damage is normal, and a presentation-only camera scale0.55 exposes the
full source ellipse. Synthetic128BPM and silent rendering are labelled.
`events.jsonl` and media hashes keep admissions, peaks and exact output scope.
These are presentation/geometry fixtures, not a real-music/feel/survival test.
No app window or audio output is opened for them.

`headless-play*.{txt,json}` keeps the normal30-app-second File/Delayed
playlist smoke. The final production build exits 0 in 34.864s wall,
session 1793065087, with a real Garlic pick/resume, 106 sound starts and zero
late skips/drops. Its printed
`Stopped,24s` is song elapsed, not the pause-subtracted run clock. Operator
File/System feel and drop/wall readability are recorded pending in
`operator-pending.md`, with no approval or playtest pause.

## Commands

From `native/`:

```sh
cargo test -p sim --lib survivor_events::tests -- --nocapture
cargo test -p sim --test survivor_spawning
cargo test -p eval --test live
cargo clippy -p sim -p eval -p app --all-targets -- -D warnings
cargo build --release -p app -p cli
target/release/playlist-survivors --swarm-check ../native/evidence/sw6/capture
target/release/ps survive --minutes 5 --bot positioning --seed 1
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions target/release/playlist-survivors --playlist playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 30
```

Final `scripts/check-fast` runs once on the final source, followed immediately
by a Conventional Commit. Candidate from the worktree; `tg approve --wait`
from main delivers to staging. No CI/shared test infrastructure/analyzer
change requires local full CI. SW7 owns W's cold review; no per-SW6 review.
