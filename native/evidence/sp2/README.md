# SP2 — boss entrances, deaths and treasure

The normal game now announces scheduled bosses two native two-beat bars ahead
under steady committed tempo. A short original synthesized brass-like horn
plays on the warning bar. The authoritative camera eases to 84% of its director
zoom, then returns over 1.4 active world seconds after all announced bosses
have landed. Both rendering and visibility/spawn bounds use this camera.

Bosses keep their seeded approach ray but land visibly at the camera edge,
inside their sprite extents plus 12 px. Admission is on the next native
bar/downbeat; source minutes, HP/level scaling, speed, damage, contact attacks,
chest probability and reward selection remain the same. There is no invisible
hittable descent. A population-cap hold retains the generic notice and camera
until the last held boss lands. Freeze holds camera progression and reanchors
admission after resume. With no known grid, arrival retains the raw deadline;
warning lead uses the configured free pulse. Tempo changes, freezes and cap
holds can extend the warning; this does not predict unobserved music.

`BossCue { kind, pending, song, run, id, x, y }` is snapshot-owned and journaled.
The live view keeps at most 32 recent cues plus one pending warning. Death
cues follow the actual grid-aligned lethal reaction (which can remain held
through an immediately opened chest freeze), not recycling. The existing
sound scheduler cancels speculative voices after retraction, respects UI
budgets, does not duck music and suppresses late cues. No bake or vendor art
was added. Existing licensed bomb/stone cues accompany landing/death.

Chest ground beacons and rising motes lead to the physical chest. The existing
actual two-bar/eighth-note roulette gets a settling icon motion, expanding
reward rays, a narrowing gold beam and a brief icon punch. It still reads the
simulation's tier, ticks and rewards; presentation never chooses items.

## Kept evidence

`sequence.jsonl` and the seven PNGs plus `boss-chest.mp4` come from:

```
cargo run --release -p app -- --boss-check evidence/sp2
```

Run from `native/`. This labelled, silent 1280×720 offscreen fixture uses a
synthetic 128 BPM stream, an otherwise empty field and the real minute-1 boss
with XP suppressed, a scripted tier-3 chest and normal hero damage. Actual
movement picks up the dropped chest; five sequential Sword upgrades are
verified. It establishes presentation/timing through a real kill/drop/touch,
not natural acquisition, dense combat, full-run balance or human audio feel.
`capture.txt` and `sequence.jsonl` retain the measured times and frame count.

`replay.txt` exercises the real Engine at lead 0.237, 25 ms late pause keys and
17 ms frame cuts: the complete settled records match the 120 Hz/on-time run.
`live-tests.txt` also covers the actual XP-offer camera sample regression and
a real boss kill/chest freeze/8 ticks/reward replay. `sim-tests.txt` includes
unknown future-tail, steady two-bar, visible landing, held-camera and partial
cap admission checks. `sound-tests.txt` checks actual decoded palette voices,
exact onsets and pre-playback retraction; horn PCM bounds at 8/44.1/48 kHz.
`review-fixes.txt` also covers irregular committed bars and a real killed boss whose held impact survives actual pool retirement. One cold warden review found these two confirmed P2s; both are fixed as detailed in `cold-review.md`. `clippy.txt` retains the focused lint result. `headless.txt`/`normal-replay.txt`
are the ordinary File/Delayed smoke and replay, with no window or speakers.

SP1 owns evolution art/transforms and its serialized bake. SP3 may compose
its loudness camera with this authoritative multiplier. SP5 owns the dense
evolved-effects budget and final readability/performance sweep. Actual
music/horn fit and human readability remain in operator-pending.
