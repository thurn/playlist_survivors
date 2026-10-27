# SP3 — zones, music-driven light and build aura

The level's zone fallback is now 240 active seconds, with existing detected
sections still taking precedence. The same planner uses the configured free
pulse when no committed future beat is available. Freeze preserves the
remaining time. The first victory blast starts an infernal-grove wave; queued
non-infernal transitions cannot displace it during the ending.

Existing `director.camera` zoom/lead stays authoritative and composes SP2's
boss multiplier. Ambient/beat pulses remain; bloom now varies with music
energy and beat, and prop lights follow energy/accent strength. A short
actual-known positive >=6dB jump gives a bounded drop pulse, with at most64
motion trails on actual bat/ghost swarm actors. No new gameplay event or
swarm is invented by the renderer. The hero's ground glow and aura read
0–5 owned evolutions from the real Inventory, independent of pinned tier.
Base-only builds retain a small locating glow and no tall aura. The fifth
evolution reaches alpha0.345, below the former tier3 alpha0.48.

The lane now learns only known onsets and excludes unknown ticks from
loudness/evidence/key choices. Exact-sample camera blending also excludes a
late tick until knownAt. Source stats, attacks, offers and SW7's easy knobs
are unchanged. No assets baked, no new workers, no analyzer changes and no
rollback-runner/live-recorder edits. By the Survivors review policy, the
SP5 milestone review covers this bead; no per-bead cold review required.

## Checks and evidence

- `clock-zones.txt`: real source configuration, all19 clock/freeze/finale
  tests. On a steady grid the first four fallbacks occur at240/480/720/960;
  without beats they occur at240.1/480.2/720.3/960.4 on the source0.35s free
  pulse. After a fifth cycle away, victory returns to infernal. Long-pause
  cases keep the four-minute remainder. These empty-field clock tests do
  not establish survival or natural progression.
- `camera-test.txt`: a late old loud tick cannot change zoom before knownAt.
- `lane-tests.txt`: actual evidence/accents/key behavior, including poisoned
  unknown tick/jump/onset tails; `aura-tests.txt`: actual Inventory slots,
  no base-rank/tier halo and increasing strength across all five evolutions.
- `clippy.txt`: focused lint. `check-fast.txt`: final diff-aware check.
- `headless.txt` / `normal-replay.txt`: ordinary muted File/Delayed smoke and
  replay, separate from presentation overrides below.

Run from `native/`:

```
cargo run --release -p app -- --zone-check evidence/sp3
cargo run --release -p app -- --aura-check evidence/sp3
cargo run --release -p app -- --swarm-check evidence/sp3/swarms
```

`zones.jsonl` records actual wave/camera/grade state; five themed1280×720
stills and108 silent24fps frames in `four-minute-sweep.mp4` use real four-minute fallbacks on a
synthetic120BPM empty field with the victory shortened to16:02. Six separate
`aura-N.png` poses explicitly override only displayed inventory to compare
the aura; they do not claim natural evolution grants or weapon behavior.
The swarm captures reuse SW6's labelled durable XP-free source-event
fixtures, with a presentation-only wide camera; they are silent and do not
establish dense real-run performance, human mix or balance. All captures run
at the required background priority, below gate/release work.

SP1 retains evolution-specific art/transformation and serialized bake
ownership. SP4 owns recap/end cards. SP5 owns dense evolved readability,
effects budget, scale measurements and the final milestone review. Human
feel, music fit and System capture remain in operator-pending.

The ordinary35s File/Delayed smoke starts218 sounds with zero late skips or output drops; its replay matches35,625 settled records (37,220 with the longer replay tail),87 kills and HP0.97. All21 actual Engine replay tests also pass. The capture trace samples quiet/medium/loud zoom near1.30/1.20/1.05 and bloom0.475/0.58/0.70. These are controlled samples, not a density benchmark.
