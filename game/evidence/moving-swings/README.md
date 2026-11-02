# Swings on the move (ps-w9p.2)

The free hero swinging while it walks ([the design](../../../docs/archive/design/game-modes.md#swinging-on-the-move)):
the legs keep running along the walk and carry the hips, and the upper body twists to the strike's aim, up to
120 degrees; past that the twist unwinds, the whole body turns and the legs come off the forward run, to a
backpedal for a strike straight behind.

## How it was run

On the Mac (jarvis), the Mono player, fixed time, house-128 (128 BPM), the run from its second minute (a crowd
round the hero), no cards. The hero stands while the crowd gathers, then walks from 9 s to 14 s, pressing Z
every half second from 9.3 s. From the worktree's root:

```sh
Z=",z@9.3-9.32,z@9.8-9.82,z@10.3-10.32,z@10.8-10.82,z@11.3-11.32,z@11.8-11.82,z@12.3-12.32,z@12.8-12.82,z@13.3-13.32,z@13.8-13.82"
EXE="game/Builds/mac/mono/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors"
ARGS="--mode free --run --start-minute 2 --no-cards --file $PWD/public/reactive/house-128/house-128.ogg"
for WALK in "left@9-14$Z" "up@9-14$Z"; do
  "$EXE" -batchmode -force-metal --fixed-time --no-session $ARGS --walk "$WALK" --seconds 15 \
    --events-out captures/game/moving-swings/events.csv --hero-out captures/game/moving-swings/hero.csv
done
uv run --project demo3d/tools game look moving-swings --backend mono --strip 6 --crop 400,120,920,620 \
  --at 9.32,9.45,9.55,9.635,9.85,9.93,10.00,10.105,10.20,10.40,10.575,10.70 --evidence moving-swings \
  --player-args "$ARGS --walk left@9-14$Z --view-width 10"
```

## What it shows

The sword aims itself at the crowd (`Sword.Aim`), so where the monsters stand picks each strike's direction
against the walk. `hero.csv` has the drawn pose on each impact frame: the legs' yaw (the presenter's, eased
over 0.04 s), the upper body's twist from them, and the weights of the forward run, the backpedal and the
walk's share of the hips. Yaws are degrees clockwise from north.

Walking **west** (travel -90):

| Press (s) | Impact (s) | Aim | Off the travel | Legs | Twist | Run | Back | Hips |
|---|---|---|---|---|---|---|---|---|
| 9.30 | 9.635 | 23 | 112 | -90 | 113 | 1.00 | 0.00 | 1.00 |
| 9.80 | 10.105 | 68 | 158 | 22 | 45 | 0.00 | 0.38 | 0.38 |
| 10.30 | 10.575 | 68 | 158 | 22 | 45 | 0.00 | 0.38 | 0.38 |
| 10.80 | 11.045 | -90 | 0 | -90 | 0 | 1.00 | 0.00 | 1.00 |

Walking **north** (travel 0):

| Press (s) | Impact (s) | Aim | Off the travel | Legs | Twist | Run | Back | Hips |
|---|---|---|---|---|---|---|---|---|
| 9.30 | 9.635 | 0 | 0 | 0 | 0 | 1.00 | 0.00 | 1.00 |
| 9.80 | 10.105 | 180 | 180 | -178 | 0 | 0.00 | 1.00 | 1.00 |
| 10.30 | 10.575 | 180 | 180 | -180 | 0 | 0.00 | 1.00 | 1.00 |
| 10.80 | 11.045 | 157 | 158 | 113 | 45 | 0.00 | 0.38 | 0.38 |
| 11.30 | 11.515 | 68 | 68 | 0 | 68 | 1.00 | 0.00 | 1.00 |
| 11.80 | 11.985 | 23 | 22 | 0 | 23 | 1.00 | 0.00 | 1.00 |

- Within 120 degrees (the 112, 68 and 22 degree strikes) the legs stay on the travel, running forward and
  carrying the hips, and the twist alone reaches the aim.
- Past it (158 degrees) the twist has unwound to 45 and the legs have turned 113 off the travel, between a
  strafe and a backpedal: the backpedal (the run clip played backward) has 0.38 of them and the attack's own
  footwork the rest. Straight behind (180), there is no twist: the knight faces the strike and backpedals.
- Two strikes in a row the same way (9.8 then 10.3) hold the pose between them rather than unwinding and
  winding again.
- The swing's twist is on the aim before its impact (it eases in over the first 60% of the wind-up) and
  unwinds over 0.25 s after the follow-through: between 9.83 and 10.1 the north walk's knight swings round
  from twisting toward the strike to backpedalling, with no jump frame to frame.

**[moving-swings.jpg](moving-swings.jpg)**, the west walk: the top row from 9.32 s, the knight running west
with its chest, arms and blade turned to the north-north-east strike (+0 to +3, its impact at 9.635); then
from 9.85 the next strike, behind it to the east-north-east, turning its whole body (+4 to +8, the impact at
10.105, the blade pointing east); the bottom row's last frames the third strike landing at 10.575 the same
way. The north walk is not in the strip: the monsters behind it stand between the camera and the knight.

How it feels by hand (the twist's size, the turn round for a strike behind, legs square to the walk) is on
[the operator's list](../operator-pending.md).
