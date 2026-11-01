# Monster and hero motion (ps-wnj.14, 2026-11-01)

The look package's motion layers
([neon-look.md, Motion](../../../../docs/design/neon-look.md#motion)): springs (squash and
stretch, lean into travel, overshoot and settle on every step, lunge and hit), swarm individuality
(each life a little off its spot, facing and size, a standing tilt and a slow wobble of its own) and
the `--beat-ripple` flag. Every preset turns springs and swarm on; the ripple is off in all of them.

The amounts are restrained on the operator's direction (2026-11-01, relayed by the vizier: the neon
visuals overwhelmed, so subtle secondary motion by default and flourishes kept for special moments): a
step's squash is 13% of the height and its lean a shear of 0.2, a kick's lean 0.3, the wobble a 0.025
lean and a 1.5% breath. A louder first tuning (a 20% squash, a 0.3 lean) read better in still strips
but is not the default. The swarm's quiet static variance (size, facing, place, tilt) stays as tuned
below, since it carries the separation and never flashes.

Before and after come from one IL2CPP Mac build: `--motion none` switches the layers off, so only
the motion differs. Windowless fixed-time frames on jarvis, the solid preset unless named. The
synthetic feeds are ps-msz.3.8's ([game/evidence/crowd](../../crowd/README.md),
`make_feed.py`), from the worktree's root:

```sh
python3 game/evidence/crowd/make_feed.py
S="$PWD/public/reactive/house-128/house-128.ogg"; C="$PWD/captures/game/crowd"
L="uv run --project demo3d/tools game look"
$L motion-row-after --evidence look/motion/row --at 7.417 --frames 16 --strip 4 --crop 1000,200,720,240 \
  --player-args "--file $S --events $C/row-5.jsonl --look solid"          # add --motion none for before
$L motion-lunge-after --evidence look/motion/lunge --strip 4 --crop 560,140,800,560 \
  --at 1.667,1.733,1.8,1.867,1.917,1.95,2.0,2.05,2.1,2.167,2.233,2.333 --player-args "--file $S --events $C/horde-300.jsonl --look solid"
$L motion-knock-after --evidence look/motion/knock --strip 4 --crop 560,140,800,560 \
  --at 4.2,4.25,4.283,4.317,4.35,4.383,4.417,4.467,4.517,4.583,4.65,4.75 --player-args "--file $S --events $C/horde-300.jsonl --look solid"
$L motion-clumps-solid-after --evidence look/motion/clumps-solid-after --at 3.317,6.133,9.1 \
  --player-args "--file $S --events $C/horde-300.jsonl --look solid"       # and --look edge
uv run --project demo3d/tools game look-metrics captures/game/look/motion-clumps-solid-after --preset solid \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/arcade.json --out game/evidence/look/motion/clumps-solid-after
```

## A block stepping

[row/motion-row-before.jpg](row/motion-row-before.jpg) and
[row/motion-row-after.jpg](row/motion-row-after.jpg): five models stepping 0.6 m east on
house-128's beat at 7.53 s, its move window from 7.436. After: through the move (+2 to +5) each body
stretches and leans into its travel, most visibly the mushroom (magenta) and the goblin; on +6, the
first frame past the beat and the frame the feet plant on, every body drops into a small squash,
widened toward the camera, then rebounds and settles by +12. It is meant to read as weight, not as a
bounce. The feet are where they were before on every
frame: only the body's shape moves, so the step still plants on its beat's frame. The row stands a
little off its line and each body at its own tilt (the swarm).

## A lunge

[lunge/motion-lunge-before.jpg](lunge/motion-lunge-before.jpg) and
[lunge/motion-lunge-after.jpg](lunge/motion-lunge-after.jpg): the feed's lunges from their notice
at 1.68 to their contact at 1.91, round the hero (centre). After: the big orc (amber, above the
hero) crouches and leans back through the first of its wind-up, then reaches into its contact (+4,
1.917) with its whole body, its club landing further toward the hero, and springs back through
2.0-2.2.

## A knockback

[knock/motion-knock-before.jpg](knock/motion-knock-before.jpg) and
[knock/motion-knock-after.jpg](knock/motion-knock-after.jpg): the hero's blows at 4.24 hit and knock
back six monsters over 0.12 s. After: each struck body is thrown back along its push on the hit's
own frame (+1) and squashed, overshoots forward and settles by about 4.6.

## Swarm separation on dense clumps

The horde feed's 300 monsters in same-type clumps (3.3, 6.1 and 9.1 s), before and after, in two
presets ([clumps-solid-before](clumps-solid-before/metrics.md),
[clumps-solid-after](clumps-solid-after/metrics.md),
[clumps-edge-before](clumps-edge-before/metrics.md),
[clumps-edge-after](clumps-edge-after/metrics.md)):

| swarm separation (>= 13) | 3.317 | 6.133 | 9.100 | mean |
| --- | ---: | ---: | ---: | ---: |
| solid, before | 13.8 | 12.7 (fail) | 13.6 | 13.4 |
| solid, after | 14.1 | 13.1 | 13.8 | 13.7 |
| edge, before | 9.5 | 7.6 | 9.9 | 9.0 |
| edge, after | 8.2 | 8.2 | 10.6 | 9.0 |

The skeletons' and demons' blocks stop reading as one stamped lattice: neighbours differ in size
(within 10%), facing (0.12 rad), place (up to 0.2 m at cast scale 1) and tilt, so their seams vary.
Solid's clumps all pass now; edge's mean is unchanged (its worst still is better, its best worse):
the edge look's separation comes from its lit outlines, which tilting bodies neither add nor take
away, and the pose each still catches moves its number by a point or two (with the louder springs
the same swarm measured 9.4/10.5/11.6). A first, gentler swarm (6% size, 0.1 rad, 0.15 m, a 0.05
tilt) moved separation only within noise (solid 13.8/13.1/13.8, edge 7.7/9.3/10.3), so the swarm
was raised to these values. The edge look's remaining failures here are its own (ps-wnj.10 measured 16.5-17 on the peak scenario's
stills); the other rules these stills fail (the key, bright share, halo) are the looks' and the
post's, unchanged by motion. Readability holds (dL* +51 to +53 before and after).

## The beat audit passes with the ripple off

`game beat peak` (game/evidence/beat/README.md's peak command, `--evidence look/motion`):
2399 frames, 14317 moments, 0 off their frame ([peak.json](peak.json)); 5627 footfalls checked.
With `--beat-ripple` ([peak-ripple.json](peak-ripple.json)) the audit still finds every footfall on
the frame of its own (delayed) plant; against the flag-off run the same footfalls land 46-135 ms
late, p50 115 ms, by their distance from the hero (a sixteenth note at the song's tempo is about
135 ms, reached at 24 m).

## Perf at the peak holds

`game perf --only peak,break,finale --player-args "--look solid" --evidence look/motion/perf`
([summary](perf/summary.json)):

| Scenario | probe p50 / p99 (ms) | main p99 / max (ms) | first frames max (ms) |
| --- | ---: | ---: | ---: |
| peak | 3.51 / 6.02 | 1.62 / 3.90 | 29.9 |
| break | 5.52 / 6.19 | 2.93 / 3.03 | 42.1 |
| finale | 4.07 / 8.68 | 2.25 / 5.19 | 38.7 |

Taken with the louder first tuning; the restrained one changes only constants, so the cost is the
same. The solid look without the layers measured 5.7 / 9.2 ms at the peak (ps-wnj.11, another session's
load): the springs are a few ALU ops and one 48-byte read per vertex, and the clocks one more
mapped buffer per type. Nothing comes near 33 ms after each scenario's first frames (shader
warm-up, as before).

## The hero

The knight takes the same step spring on its hops and dashes (`HeroMotion`'s stretch and lean,
`HeroRig`): it stretches and leans into a hop and squashes on its touchdown. It is a few percent
of a small figure at the game camera's framing, so it has no strip of its own here; the EditMode
tests check the spring's shape (`MotionTests`).
