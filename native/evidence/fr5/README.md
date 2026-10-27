# FR5 — Restored weapon choices

Chain Lightning and Fire Trail are real six-slot Survivors inventory choices,
with eight ranks, captured shots, cards/icons, sample palettes, snapshots,
recorded attacks and actual damage/held-time recap. Seven bases compete for
six slots; five existing evolution pairs remain. Magnet gives +30% pickup
radius per rank and Armour +1 flat contact armour, both five ranks. The first
three choices are unchanged; alternative chain/fire/passive offers start at
levels3–6. No second legacy loadout or new evolution was added.

Chain starts at260px to reach beyond the deliberately enlarged190px sword;
its140px hops reuse the old chain delivery and committed quarter grid (weapon
free pulse in beatless music). Fire samples actual past hero poses, deduplicates
a target across overlapping circles, and grows radius/lifetime with real rank
and stats. Visual fire uses the captured shot and held world time, so menus do
not grow or age the trail. Shared shape hits now use the current admitted enemy
pose; a fast-bat regression catches the formerly stale root miss.

## Evidence and limits

- legal-paths.txt: bounded legal-choice search reaches all seven max-rank bases,
  all five evolutions while owning both restored weapons and both new passives,
  and a six-slot restored build which excludes the seventh weapon. These are
  reachable choice paths, not a promise that every random path gets every item.
- passive-ranks.txt: forty actual card resolutions verify all eight passives.
- focused-all.txt: initial complete sim/eval/app suites; final ci-full.txt and
  check-fast.txt include review fixes. Chain AoE dedup, three real hops,
  trail movement/rank growth, modal pending-chain snapshot replay, beatless
  weapons, Magnet/Armour and current fast-step hit position are covered.
- projection-tests.txt: ten seconds of real House128 timestamped music in an
  explicit durable-enemy six-slot fixture. Chain947.7 and trail4.5 damage, each
  held9.9945s;21/33 actual shots each produce exactly one projected fire cue. Synthetic known
  quarter-grid forecasts exercise captured accents through the real sound path. This is not
  a natural progression or balance measurement.
- cards/: labelled synthetic1280x720 inventory/card states, visually inspected;
  distinct CC-BY3 Game-icons glyphs have authors/licences in the asset ledger.
- combat/: restored six-slot injected fixture,300 foes/up to400 gems, scripted
  HP, actual recorded hit/pickup activity. Not a natural run. Foreground health
  and hero remain visible; broad overlapping white attacks need FR9's density
  hierarchy. No claim that these stills establish subjective feel.
- bench-restored.txt: unchanged1080p density gate,7200frames, p992.46ms, max2.90ms
  (<4ms p99). This is one retained local run, not a general performance guarantee.
- baker-check.txt: lint, typecheck and all generated asset checks pass.
- decoded-sounds.txt and real-mix.txt: actual decoded fire/hit/accent cues,
  cancellation and four real-music PCM mixes at deep music attenuation; effects
  RMS .008249, peak .054599. Short listening WAVs live in ignored main-checkout
  captures/feel-fr5; actual speaker/System audition remains operator-pending.

Cold review found three confirmed issues: beatless paused chains could strand
(and lose pooled state), real restored audio skipped typed accent/impact routing,
and displayed fire history could retain discarded motion after late-key replay.
All are fixed with actual held-state, live-cause and25ms-late-key regressions.
The authoritative fixed-sample hero path now rewinds with the view. No confirmation
review.

The injected dense fixture is shared test infrastructure, so full local CI is
required as well as final check-fast; results are retained separately.
The native section passed all tests (including release-only cases), purity,
unused dependencies, eight-track analysis parity, runner/frame benchmarks and
the existing density benchmark (p992.89ms). A separately injected restored build
uses the dedicated benchmark above.

Playable from native/: `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed` (add `--legacy` for judging).

The full41-case matrix passed with zero survival failures in728.06s; all20
positioning cases completed30minutes, minimum HP across them .933. AFK and
random-choice results remain diagnostic. matrix-summary.json and compressed
matrix-raw.jsonl.gz retain every case and exact command. This run is on the
FR5 tree before the independently landed finale-cancellation repair is rebased;
its exact formerly crashing case already passed under fr-ci13. Final fast
checks after integration cover both changes.

Full scripts/ci-full passed, including the30-minute replay:21,324,540 records,
hash2f8f3aaf9ac13e44,208.55s. No release-only failures remain in this local run.
