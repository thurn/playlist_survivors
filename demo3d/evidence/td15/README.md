# TD15: the art pass — evidence

All on Cygnus (RTX 4070 Ti, i7-12700F), the IL2CPP player windowed at 1920×1080, MSAA 4×, rung
E2. The shots, the readability table and `s0-headroom/` are at the authored density **2×** (the
headroom step, after TD14); `s0/` and the first round's numbers below were at 1×.

- `reference/`: the packs' own demo scenes through our pipeline and camera (`ReferenceStills`):
  Elven Realm's `Demo` and Dark Fantasy's `Demo_Graveyard_01`, at 30 and 40 m view widths, from
  the framings on their walkable floor that see the most dressing, under our night (`-night`: the
  arena's moon, ambient, fog and post). The same framings under the scenes' own lights (`-own`) were
  removed from git after the scene milestone's review (screenshots of the vendors' demos, not our
  game); `ReferenceStills` writes them to the gitignored `captures/td15/reference-own/`.
- `shots/`: the six beauty shots (`<shot>-empty.jpg`, `<shot>-horde.jpg`, 960 px from the 1080p
  PNGs in the gitignored `captures/td15/`), from `beauty.py`.
- `readability.md`: the readability measure for each horde shot, and the floor (written by
  `beauty.py`).
- `canopy/`: the canopy fade, off and on, with the hero standing behind a forest-edge tree.
- `s0/`: S0's rows at E2, 1× and 2×, TD15's first round (`rows.jsonl`, `report.md`, `table.md`).
- `s0-headroom/`: S0 on TD14's final rung at 1×, 2× and 4× (`rows.jsonl`, `report.md`,
  `table.md`), and the authored 2× after the review's fixes (`*-2x-final.*`).
- `beauty.py`: the beauty-shot captures, the ID renders and the measure.

## The reference stills

Neither demo scene is a flat glade: Elven Realm's is a floating island of cliffs and trees (the
framing picks its walkable top), and the graveyard is a fenced 30 m plot. What they set is the
density: in a 30 m view the graveyard has a tomb or headstone every 4–5 m, dead trees on its
corners and a fence along its sides; Elven Realm's top packs trees, rocks and plants with no bare
patch wider than a few metres. Our edge bands and landmarks now match that; our fighting ground
stays open by design (environment.md, Layout), dressed with flat pieces only. Under our night the
packs' pale stone and lilac atlases are by far the brightest things in frame, which is why TD15
darkens the kit (`kitValue`, below). Materials the sync lacks render as dark neutral Lit
(Demo_Graveyard_01 names three ground materials its package doesn't contain); the packs' own
shader graphs are drawn as plain URP Lit from their albedo.

## The beauty shots and the checklist

Each shot is a tracked definition in `arena-layout.json` (`shots`): the game camera's ground focus
(`at`, which is also where the hero holds), the view width, the stress time `t` (20 s) and the
horde `n` (2,000 requested, the level script's placeholder envelope, capped by each view's
packing). The camera is the game's (55° pitch, 30° vertical field of view, north-facing). With the
horde: S3 at demo load, the hero holding `at`. Empty: the same frame with `--beauty-empty` (no
horde, no combat effects; the hero and the ambient fireflies and wisps stay).

| shot | landmark or edge in view | canopy over fighting ground | effects against the set | notes |
|---|---|---|---|---|
| opening (44, 40), 40 m | the rune circle, a ring of candles and glowing flowers, ground dressing | none | warm bursts and orange rings stand out; TD8's blue-white spell rings are close in hue to the (violet) rune | the stress scenes' hold |
| path (7, −30), 36 m | stepping stones, the forest edge, the tombstone ring and its rune, the glowing tree | none | as above | |
| glowing-tree (−6, −40), 32 m | the glowing tree whole, its light pool, the flowers at its base | none | as above | moved from (−5, −46): its canopy was cut off |
| graveyard (24, −13), 34 m | tombs, statue, candles' warm pools, fence, the ruin wall | none | as above | |
| courtyard (0, 16), 40 m | the walls, the rune circle, the braziers' glow | none | as above | |
| forest-edge (−12, −48), 32 m | the camp's west edge band and forest, the glowing tree | canopies cover the edge band only | as above | |

Nothing floats or clips in the stills; the scatter shows no obvious repeat. The heroes in the
editor's iteration stills are unanimated (T-pose); the player's are animated.

## The readability measure and its floor

`beauty.py` runs each shot three times: the beauty shot (the horde with effects), the same frame
without effects (`--no-effects --capture-mask`: the measured still, then the same frame again as
the ID render) and the empty shot. The ID render draws the crowd flat white, and the set and hero
black with their depth, so a monster behind a canopy or a wall is not a monster pixel; the
ground, mist, runes, pools and particles are hidden in it (the scene milestone's review, item 1:
the first measure instead dropped pixels where the horde still matched the empty still, which also
dropped dark monsters on dark ground, and let effects over monsters raise their L\*). Monster
pixels are the thresholded ID render; their ground is a ring 3–10 px outside every monster.
**dL\*** is the median monster CIE L\* less the median ring L\*; dL\*bg compares with the same
pixels of the empty still; dC\* is the chroma difference. `readability.md` has the table.

At the authored 2×, after the review:

| shot | dL\* | dL\*bg | dC\* |
|---|---|---|---|
| opening | 20.6 | 15.2 | 12.8 |
| path | 21.0 | 15.1 | 13.4 |
| glowing-tree | 18.8 | 14.3 | 12.3 |
| graveyard | 20.2 | 14.7 | 13.8 |
| courtyard | 19.4 | 15.2 | 12.6 |
| forest-edge | 19.5 | 12.6 | 13.4 |

**The floor is dL\* ≥ 17 in every shot**, re-derived after the review (the lowest shot, the
glowing tree's 18.8, less a margin for capture noise). Without the effects' light the ring is darker
(L\* 4–6 against 7–12 before) and the shots closer together; the forest edge, which the first
measure put at 13.1, reads like the rest once dark monsters stop being dropped.

The first round (1×, the first measure, kept for its history): opening 20.2, path 17.4, glowing
tree 18.5, graveyard 20.1, courtyard 21.2, forest edge 13.1, floor 12. The lever was the horde's
light, not the set: with the crowd's fill and rim off (TD9's crowd lighting) the opening read 6.1
and the graveyard 5.7; trials (`--crowd-fill`, `--crowd-rim`, captures under
`captures/td15/t0..t2/`) gave opening 6.1 / 14.5 / 17.6 and graveyard 5.7 / 15.0 / 18.2 at fill
0 / 1.0 / 2.0 (rim 0 / 0.3–0.5 / 0.5–0.8); the final 2.5 with rim (0.6, 0.7, 0.95) is in use.

What it does not show: one moment per shot (t = 20 s) at one horde size; the ring is ground
*between* monsters, so at 30–60 % monster coverage it is a thin sample; the measure leaves effects
out, so it reads the horde against the set alone, not the fight's look; L\* ignores hue, so dC\* is
reported beside it. The ID renders kept here's runs include a few dozen pixels of the stand-in
projectile spheres `--no-effects` draws (left out of the ID render since).

## S0, the first round (1×, before TD14)

S0 (the empty arena, its lighting and post, now with the art pass and the ambient fireflies and
wisps on the effect runtime), rung E2, MSAA 4×, the camera touring the six shots; medians of three
attempts, GPU / main / render p99 in ms (`s0/table.md` lists each run's last attempt):

| density | kit renderers | 40 m | 55 m | TD9's E2 (40 m / 55 m) |
|---|---|---|---|---|
| 1× (authored) | 1,903 | 0.46 / 0.89 / 0.59 | 0.50 / 0.99 / 0.64 | 0.39 / 0.85 / 0.52, 0.44 / 0.96 / 0.59 |
| 2× | 2,598 | 0.47 / 0.93 / 0.60 | 0.55 / 1.03 / 0.67 | 0.40 / 0.89 / 0.53, 0.45 / 0.92 / 0.56 |

Far inside the ceiling (1.5 / 1.7 / 1.7 ms). The art pass costs about 0.06–0.07 ms of GPU p99 at
1×; draw calls went from 186–195 to about 261 at 40 m, and set-pass calls from about 20 to 55
(the canopy twins, the mist, runes and pools, and the effect runtime's passes). As in TD9, S0 runs
CPU-bound near 1,000 fps and every run is marked noisy for its frames without GPU time; attempts
agree within 0.01 ms of GPU p99. An earlier attempt, beside another worktree's shader compile at
99% CPU, measured main thread 4–5 ms and was discarded.

## S0 at the authored density 2× (the headroom step)

On TD14's final rung (E2 with the triplanar rock), medians of three attempts, GPU / main / render
p99 in ms:

| density | kit renderers | 40 m | 55 m |
|---|---|---|---|
| 1× | 1,903 | 0.46 / 1.01 / 0.62 | 0.51 / 1.01 / 0.64 |
| **2× (authored)** | 2,598 | 0.47 / 0.92 / 0.61 | 0.55 / 1.06 / 0.70 |
| 4× | 3,271 | 0.49 / 1.00 / 0.63 | 0.60 / 1.10 / 0.70 |
| **2×, after the review's fixes** | | **0.49 / 0.91 / 0.60** | **0.56 / 1.03 / 0.67** |

All inside 1.5 / 1.7 / 1.7 ms, so the look chose 2× (TD15's entry). The kit renderer counts are the
bake before the review's dressing fix, which drops fighting-ground dressing that sat on fixed
pieces.
