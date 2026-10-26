# SW3 — final base-roster art and gaits

The eight base roles now use the design's final mappings. Boss-prefixed roles,
source combat stats, encounter roster/schedule and easy knobs are unchanged.
SF11 already baked the licensed skeleton boss and four thorn props, so no
binary atlas or vendor source changed here. `npm ci` preceded this work;
`assets-check.txt` passes lint, types and the existing 14 sprites / 26 props /
66 effects / 245 sounds (4.7 MB).

| Roles | Art / presentation | Gait / walk cycle |
|---|---|---|
| MUDMAN1 / MUDMAN2 | orc / orc-elite | heavy steps on 1 and 3; 2 beats |
| WEREWOLF | orc-ember | offbeat eighths; 1 beat |
| XLBAT | bat at 2×, dark violet tint | beat steps / slow wing cycle; 1 beat |
| XLMANTIS / XLMUMMY | SF11 skeleton boss, cool / warm tint | downbeat step and dust ring; 4 beats |
| FLOWER / XLFLOWER | summer / autumn thorn props | downbeat closure, bar sway; 4 beats |

These use SH6's existing actual-position lunge, causal gait planner and
freeze behavior. No attack scheduler or rollback algorithm changed. Brute
walk art is 128 px instead of the old 64 px humanoid; presentation halves its
configured scale to preserve the old overall footprint. Thorn props use 0.6×
the configured scale (55 px wide becomes about 33 px, replacing a 32 px slime).
Sim radii and offscreen spawn/recycle extents keep their previous values.
`content-audit.json` compares all 20 roles with source commit
`8e5320736e06e954f10435b74a68c9cccb8327e5`: only atlas, group, gait,
walkCycleBeats and hop change. Full source stats, health, contact damage,
radius, scale, hover, behavior and death recipes are identical.

Plants shear about their base and close slightly on downbeats. Their hit
flash uses a renderer alpha-mask flag: the GPU test checks both white RGB and
preserved transparency, with an unchanged colored neighbor. Brutes emit a
short brown downbeat ring; alpha is divided by the alive brute count in each
96 px cell to restrain crowd brightness. Existing death bursts/fades remain.
The tint is suppressed during character hit flashes so those stay white.

## Captures and limits

Each `vs-*.png` is a 1280×720 still taken during approach, before the hero
obscures the role. Each matching silent `vs-*.mp4` captures actual gait and
contact lunge across a downbeat, 52 frames / 24 fps = 2.166667 s at 640×360.
`captures.txt` records exact song intervals; `clip-metadata.json` verifies
all eight video streams and absence of audio. Stills and sampled video frames
were inspected for asset choice, anchor and motion.

The fixture uses a synthetic 128 BPM stream, lead zero, one normally spawned
role, HP 1,000,000 / XP 0, hero damage exemption, Might 0 / Area 0 and scenery
props suppressed. Zero power alone still allows sword recoil; Area 0 keeps
that diagnostic sword out of reach, allowing the slow flower to close
naturally. Weapon-fire presentation is hidden. Positions, gait and lunges
come from the actual simulation, without manual pose placement. This is art
and gait evidence, not survival, natural encounter timing, balance, listening
or dense-scene GPU performance evidence.

`roster-test.txt` checks each mapping/cycle/pattern against the actual baked
assets: four facings and white-flash twins for walk/hurt, or anchored plant
props. `source-contracts.txt` retains seven appendix/schedule/admission checks.
`lunge-tests.txt` passes four existing contact, freeze, free-clock generation
and uncommitted-tail tests. `render-tests.txt` passes all 12 GPU/render tests,
including `silhouette-test.txt`'s new alpha-mask check. `clippy.txt` passes app,
render and sim with all targets and warnings denied.

`headless-35s.txt` is a normal-damage File check with no automated card pick:
it reaches the real level-up freeze at run 7.403 s and remains there, with
36 sounds, no late skips/drops, 10 kills and full HP. `replay.txt` matches all
12,571 live settled records (12,701 with replay tail). This confirms normal
startup/menu playability; the eight role captures cover later art directly.
Operator feel remains in operator-pending, without a pause.

From `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 35
cargo run --release -p app -- --headless --roster-check /tmp/roster-check
cargo test -p app roster_uses_baked
cargo test -p sim --test survivor_spawning
cargo test -p sim survivor_lunge
cargo test -p render
cargo clippy -p app -p render -p sim --all-targets -- -D warnings
```

SW4 can reuse `brute` and the app's automatic larger-frame correction/tints;
its boss identities, charges and slams remain its work. SW6 can reuse the
thorn prop roles for flower walls without a bake. SP1 has the next serialized
bake. SW7 owns the W milestone review and any measured balance adjustment.
