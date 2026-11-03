# Walking sword: current contract and validation

Authorized November 3, 2026 in ps-oli, following operator feedback that walking
and F were fun but the swing felt small, slow and disconnected. This amends the
[restoration snapshot](game-feel-plan.md). [Design](design.md) remains the
current game specification; the [assembly report](../../rhythm_champions/evidence/walking-assembly/README.md)
records actual tested revisions, recipes and limits.

## Delivered contract

- F is the primary active attack while ordinary movement continues. An
  alternating body cut begins immediately, extends the held blade about the
  fist and hits once after 150 ms. Base circular reach is 4.2 m; empowered
  reach 5.94 m. Sword and Landing Blast enhancements still apply. Space keeps
  its 4 m reposition, 3 m base landing circle and travel presentation.
- F/Space share 600 ms cooldown, its final 150 ms buffer, 250 ms shield and
  three-second 2× area aura. Held input does not repeat. Buffered actions keep
  their original grade; a timed buffer wins over an off-beat replacement.
- Air sounds on accepted F. One contact sound follows only real contact,
  stronger for three or more bodies. Contact response, damage, shatter and
  sound use one impact timestamp. Historical F records can change outcome
  because reach and grading changed; the recording format stays version 6.
- Heard-time reward: beats ±min(100 ms, 20% local interval), halves
  ±min(80 ms, 16%), strong onsets ±80 ms. Reliable half beats need no onset.
  Genuine gaps remain; unreliable beat tracking grants eligible actions the
  fallback reward. Cooldown rejection is distinct from off-beat acceptance.
- Default monster/hero shards disappear in flight before downward contact,
  within 0.5 seconds. No resting rubble. Tier limits are 600/1,500/3,000 live
  pieces. Structure debris and the pinned trailer comparison stay separate.
- Ordinary fodder forms eight-body single-type local cohorts under the
  existing per-body budget. No global speed increase. Comparative probes
  supported shorter enemy ages at higher speed, not better visual grouping;
  they found no general frozen-pathing failure.
- Starting XP/healing collection reach is 3.5 m. Attraction adds 20% of that
  baseline per level. Stored drops survive streaming and arrivals.
- Ogre/Conductor/Dragon calls occur at 180/360/540 **horde** seconds. Warning,
  fight, cards and pauses do not advance that clock. Bosses retain their
  telegraphs/moves and finite reinforcement budget (three groups of three).
  Big ork/troll ordinary elites retain bodies/contact damage, with no special
  charge/slam or associated ground marks. Ranged mushroom spores remain.
- Existing target curve: 25–35 normal choices; first around 20–30 s. Actual
  scripted results vary. Do not cancel clearing power with hidden scaling to
  force older fight-duration goals.

## Finding local evidence

`just champions-tool logs` lists isolated engine logs under local cache
`~/Library/Caches/playlist-survivors/champions/player-logs/`. Checkouts have
separate identities and launches have unique directories. Normal CLI, build
launchers, tests, captures and replays route from startup; explicit `-logFile`
is respected. Raw Unity/editor launches outside these entry points can still
use Unity's default rotating log. Do not attribute another worktree's log to
operator play. See [launch isolation](../../rhythm_champions/evidence/player-log-isolation/README.md)
for exact supported entry points and retention.

Each run independently writes detail and `.log.summary` under cache
`champions/run-diagnostics/`; the engine log prints both exact paths. Details
are capped at 512 KiB while totals continue. Keep 100 inactive runs, protecting
open runs. Summary snapshots update every five song seconds and on normal
close; abrupt exit retains flushed rows and `running-or-interrupted` status.
These are local files with no uploads or external telemetry. See
[rhythm evidence](../../rhythm_champions/evidence/rhythm-diagnostics/README.md)
for counter semantics, timing fields and sampled aura-uptime limitations.

## Verification and remaining judgement

Use the sibling evidence for controlled before/after F, actual captured mix,
debris/cohort comparisons, timing fixtures, collection conservation and full
natural-clock victories/death. Assembly adds current-build real music plus
scripted F-primary movement and synchronized application audio/video; automated
scripts are never represented as operator hand-play. Keep screenshots, movies,
sessions and vendor-derived outputs outside public git; commit only text.

The supplied trailer is an aesthetic comparison, not shipped gameplay proof.
Judge the held cut, contact and opening dark pocket at gameplay speed, then
check dense weapon/XP overlap and each boss's clear, warnings and timer.
Operator judgement of fun, rhythm feel and speaker/Bluetooth sync remains
non-blocking. M1-class performance and GPU-only timing remain unmeasured; do
not convert shared-host Mono captures or readback benchmarks into acceptance.
A single final Warden and the run's CI verify delivery, without a human gate.
