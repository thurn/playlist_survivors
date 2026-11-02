# Edge cut: hero

Written by `champions edgecut review hero` from the recipe [tools/recipes/edgecut/hero.py](../../../tools/recipes/edgecut/hero.py) (role hero).

The hero's edge cut (ps-6y2.20; design.md, The Hero): Synty's knight (Polygon Knights, one skin on a humanoid
rig) with the broadsword it holds and the round shield in its left fist, redrawn in white as long lines over a body of
convex parts.

- Pose: the line choice sees it in its sword idle, not its T-pose, so the outline it picks is the one the game shows.
- Body: one hull per part, corners budgeted by how much of the outline the part makes; the sheath at the hip is
  hidden (the sword is in the hand). The shield is a flat disc.
- Lines: the long outline-making creases (lines.choose), chains shorter than 6% of the height dropped, capped per
  part; three contour strokes along each arm, which the low-poly arms give no long crease for (the legs' scribbled
  and cost black share); the shield an
  empty circle; the blade its two edges meeting at the tip and a bright core, a stroke along its middle drawn twice
  (the lines add, so the core is twice as bright as any other line: the brightest thing on screen); the guard and
  the grip.
- Clips (the hero's rig plays them; design.md, The Hero > Animation, and Dashes): the sword idle; the legs' runs, the
  sprint forward (it strides 3.9 m a 0.6 s cycle: 6.5 m/s, near the hero's 6) and the run backward and to each side
  (for an aim the torso can't twist to); Sword Slash's LightCombo01A and LightCombo01B and HeavyFlourish01 (the spin);
  Blade Rush's HeavyStab01, Leap Slam's LightLeaping01, Shield Charge's forward parry and the plain Dash's forward
  dodge. Impacts and ends are demo3d's measurements (the right hand's peak speed); the rig's bake measures the runs'
  strides.
- Shards: a piece per part, the chest and the pelvis split in two, the shield in two.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 55% | 64.0% (poses 66%, 65%, 69%, 63%, 57%, 65%) | ok |
| line segments <= 220 | 84 | ok |
| body triangles 300-800 | 348 | ok |

22 shard pieces; the bone texture holds 37 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density, three poses to a row (idle, run, slashB, slashA, spin, dodge), each the original then the cut, and a last row of the shatter: the first pose broken by a blow away from the camera, then 0.08, 0.2, 0.4, 0.8 and 1.4 s after (as many as the row holds). Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (57.6 px tall): [far.jpg](far.jpg)
- game (116.1 px tall): [game.jpg](game.jpg)
- near (232.3 px tall): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 47.5% | 48.5% | 703 | 41 |
| far | run | 45.8% | 48.5% | 491 | 30 |
| far | slashB | 51.5% | 52.2% | 707 | 47 |
| far | slashA | 44.6% | 45.8% | 625 | 40 |
| far | spin | 46.9% | 49.3% | 604 | 45 |
| far | dodge | 48.9% | 49.5% | 683 | 45 |
| game | idle | 66.4% | 68.3% | 2511 | 82 |
| game | run | 64.8% | 68.9% | 1803 | 60 |
| game | slashB | 68.7% | 70.1% | 2479 | 96 |
| game | slashA | 63.0% | 65.0% | 2151 | 80 |
| game | spin | 56.7% | 67.5% | 2325 | 89 |
| game | dodge | 64.5% | 68.3% | 2494 | 89 |
| near | idle | 82.0% | 82.6% | 9344 | 163 |
| near | run | 79.6% | 82.3% | 6804 | 120 |
| near | slashB | 82.4% | 82.9% | 9245 | 195 |
| near | slashA | 79.0% | 80.0% | 8035 | 161 |
| near | spin | 77.6% | 81.2% | 9015 | 175 |
| near | dodge | 81.0% | 82.2% | 9374 | 178 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| head | 34 | 12 |
| chest | 40 | 3 |
| belly | 16 | 6 |
| pelvis | 32 | 1 |
| shoulder_l | 20 | 4 |
| shoulder_r | 18 | 4 |
| upperarm_l | 12 | 3 |
| upperarm_r | 16 | 3 |
| forearm_l | 12 | 3 |
| forearm_r | 14 | 4 |
| hand_l | 12 | 2 |
| hand_r | 12 | 0 |
| thigh_l | 20 | 4 |
| thigh_r | 16 | 4 |
| calf_l | 16 | 4 |
| calf_r | 14 | 3 |
| foot_l | 12 | 0 |
| foot_r | 16 | 0 |
| shield | 16 | 16 |
| sword | 0 | 6 |
| blade | 0 | 2 |
