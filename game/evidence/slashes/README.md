# The trailer's slashes (ps-w9p.3)

The free mode's playlist feedback (epic ps-w9p, item 3): the swings didn't seem to use the trailer's sword slash
effects, just a generic white circle. The design is in
[unity-game.md, Weapon and ability effects](../../../docs/design/unity-game.md#weapon-and-ability-effects)
(Shipped, ps-w9p.3).

## What was wrong

The slash was Hovl's Sword Slash 2 through an ice-to-white heat ramp, spawned at the strike's reach. Free mode's
full reach is 7.04 m, so it drew at twice its stock size: a white ellipse most of the way round the hero, larger
than the view's middle at the close framing. Every press also rang a 0.7 m ice band out to the reach. Together
they read as one white circle per press ([before.jpg](before.jpg)), with no blade arc and nothing telling a left
cut from a right one.

The trailer's slashes (demo3d's `--trailer`: its cleaves, which play the slash clips, and its spins) are
`spin-trail`, Hovl's Sword Slash 11 in its own gold, at about its stock 2.8 m radius round the hero.

## How it was run

On the Mac (jarvis), the Mono player, fixed time, house-128 (128 BPM), the run from its second minute, no cards,
before (`c22177cd`) and after this change. Standing, Z every half second from 9.3 s; walking west from 9 s, Z
every half second, with F6's forced accent at 10.75 s so the 10.8 s press spins. From the worktree's root:

```sh
SONG="$PWD/public/reactive/house-128/house-128.ogg"
ARGS="--mode free --run --start-minute 2 --no-cards --file $SONG"
Z="z@9.3-9.32,z@9.8-9.82,z@10.3-10.32,z@10.8-10.82,z@11.3-11.32,z@11.8-11.82"
uv run --project demo3d/tools game look after --backend mono --crop 732,250,400,330 --strip 8 --evidence slashes \
  --at 9.60,9.633,9.650,9.667,9.683,9.70,9.717,9.75,10.07,10.10,10.117,10.133,10.15,10.167,10.183,10.217 \
  --player-args "$ARGS --walk $Z"
W="left@9-13,$Z,z@12.3-12.32"
uv run --project demo3d/tools game look after-walking --backend mono --strip 6 --evidence slashes \
  --at 9.667,9.70,10.133,10.167,10.60,10.633,11.067,11.10,11.133,11.55,11.583,12.017 \
  --player-args "$ARGS --walk $W --debug accent@10.75"
uv run --project demo3d/tools game look after-spin --backend mono --crop 800,280,440,360 --strip 8 \
  --at 11.0,11.033,11.05,11.067,11.083,11.10,11.117,11.133,11.5,11.533,11.55,11.567,11.583,11.60,11.617,11.65 \
  --player-args "$ARGS --walk $W --debug accent@10.75"
```

`before.jpg` and `before-walking.jpg` are the first two commands on the build before the change.

The swings, from `--events-out`: standing (aimed north at the golem), SlashL at 9.635 s (reach 4.81 m, an early
press), SlashR at 10.105 s (7.04 m); walking west, SlashL 9.635 and SlashR 10.105 aimed across and behind the walk,
the heavy spin at 11.045 (9.15 m), then SlashR 11.515 and SlashL 11.985 aimed along it.

## What it shows

- **[compare.jpg](compare.jpg)**: the same SlashL frame (9.700 s, four frames after its impact) before and after,
  beside demo3d's trailer (the crystal cut at 8.0 s, from
  [crystal-before-after.jpg](../../../demo3d/evidence/trailer/hero-scale/crystal-before-after.jpg)) and a close
  view of demo3d's run (from [combo-strip-60s.jpg](../../../demo3d/evidence/blade/combo-strip-60s.jpg)). Before,
  white rings round the hero; after, the trailer's gold crescent in front of the knight, near the blade.
- **[before.jpg](before.jpg) / [after.jpg](after.jpg)**, the standing swings close up. Top row, SlashL: the blade
  comes from the knight's right through the front to its left; after, the crescent lies across the front and wipes
  toward the front left, where the blade ends. Bottom row, SlashR: the blade comes back left to right, ending
  behind the right shoulder; the crescent, its mirror image, lies on the right and wipes round to the back right.
  The ordinary strike's ring is a faint line at the reach under it. The SlashL was an early press (reach 4.81 m),
  so its crescent is drawn smaller (2.05 m) than the full SlashR's (3 m).
- **[before-walking.jpg](before-walking.jpg) / [after-walking.jpg](after-walking.jpg)**, the game's own framing:
  after, each light swing is a small gold arc on the knight's side of the cut rather than a white ellipse round it;
  the heavy spin (11.067 to 11.133) keeps its white-hot ring and the cleave wave (11.55, 11.583) that the big
  moments stand out by.
- **[after-spin.jpg](after-spin.jpg)**, close up on the walk: the spin's larger crescent with its heavy ring (top
  row), then SlashR aimed along the walk (bottom row), the crescent staying round the running knight as it moves
  (the slash takes the glide's velocity) and wiping to the front right where the blade ends.

## The clips' arcs

`EffectCues.Arc` holds each clip's sweep, measured once in the editor from the knight's right hand round the hips
(seen from above, 1/30 s steps through each clip's impact; degrees clockwise from the facing):

| Clip | Hand's path (s: degrees) | Swept | Arc's middle |
|---|---|---|---|
| SlashL (LightCombo01A, impact 0.35) | 0.267: 114, 0.367: 38, 0.467: -31 | -163 (right to left) | 45 |
| SlashR (LightCombo01B, impact 0.267) | 0.184: -37, 0.284: 67, 0.384: 131 | +209 (left to right) | 40 |
| Slash3 (LightCombo01C, impact 0.417) | 0.334: 126, 0.434: 12, 0.534: -6 | -149 (right to left) | 60 |
| Spin (HeavyFlourish, impact 1.433) | 1.350: 88, 1.450: -4, 1.550: -77 | -259 (counter-clockwise) | 15 |

"Swept" is over the whole sampled window (0.25 s before the impact to 0.12 s after); the arc's middle is about the
middle of the path through the impact, rounded. The slam (LightLeaping01) chops left to right, but its effect is
the ground shock, which has no way round.

The crescent as it comes wipes clockwise round its back left (middle -135 degrees, read off the stills), so a
swing turns it by the clip's middle less the crescent's, and mirrors it (the effect's x negated in its own frame)
for a counter-clockwise cut. Slash3 (a follow-up, which needs Amount) isn't in these runs; it is drawn as SlashL.

## Checks

`EffectsSoundTests.ASlashIsTheBladesCutTheClipsWayRoundAndMovesWithTheFreeHero`: the cues carry the clips the hero
swings with (SlashL, SlashR, then Slash3 and SlashR for follow-ups), a full swing is drawn at the blade's arc and a
weak press's smaller while the reach still goes to the crystal wave, a slam's shock still covers its reach, the
combo's cuts mirror,
and a slash moves with the glide at its impact (a replaced glide dropped). The GPU mirror itself shows only in the
stills. The slash costs less than before: Sword Slash 11 converts to 5 emitters and about 43 particles a spawn
against Sword Slash 2's 7.

How it reads by hand is on [the operator's list](../operator-pending.md).
