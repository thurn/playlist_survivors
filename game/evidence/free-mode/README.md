# The free mode (ps-y21)

`--mode free` ([the design](../../../docs/design/game-modes.md)): the hero walks freely and the
sword strikes on Z, its reach and damage graded by how close to a beat or half beat the press was.

## How it was run

On the Mac (jarvis), the Mono player, fixed time, house-128 (128 BPM), with scripted keys: Right
from 6 s, Up from 7 s (a diagonal), Left from 8.5 s, and Z eight times at 6.5, 6.6, 7.0, 7.25, 7.6,
8.0, 8.35 and 8.9 s. From the worktree's root:

```sh
WALK="right@6-7.5,up@7-8.5,left@8.5-9.5,z@6.5-6.52,z@6.6-6.62,z@7.0-7.02,z@7.25-7.27,z@7.6-7.62,z@8.0-8.02,z@8.35-8.37,z@8.9-8.92"
EXE="game/Builds/mac/mono/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors"
"$EXE" -batchmode -force-metal --fixed-time --no-session --mode free --file "$PWD/public/reactive/house-128/house-128.ogg" \
  --walk "$WALK" --seconds 11 --events-out captures/game/free-mode/events.csv --hero-out captures/game/free-mode/hero.csv
uv run --project demo3d/tools game look free-mode --backend mono --at 7.45 --frames 18 --strip 6 --evidence free-mode \
  --player-args "--mode free --file $PWD/public/reactive/house-128/house-128.ogg --walk $WALK --view-width 10"
```

## What it shows

- **The walk**: 11 HeroGlide events and no HeroStep. The drawn hero moves 0.0752 m a frame on each
  axis on the diagonal (6.4 m/s, a 1.5 m tile per 0.234 s stride step), at off-grid points such as
  (8.036, -1.347), with the run clip weighted 1 and no hop.
- **The swings**, as `events.csv` has them (press, impact, reach; full reach is 7.037 m):

  | Press (s) | Impact (s) | Reach (m) | Grade | Note |
  |---|---|---|---|---|
  | 6.50 | 6.58 | 5.56 | 0.79 | off the beat |
  | 6.60 | 6.78 | 7.04 | 1 | mid-swing: waited for the swing, 0.2 s after the one before |
  | 7.00 | 7.08 | 7.04 | 1 | |
  | 7.25 | 7.33 | 7.04 | 1 | |
  | 7.60 | 7.68 | 9.15 | 1 | on an accented beat: a heavy spin (x1.3 reach) |
  | 8.00 | 8.08 | 7.04 | 1 | |
  | 8.35 | 8.43 | 3.70 | 0.53 | close to a quarter beat off |
  | 8.90 | 8.98 | 7.04 | 1 | |

- **[free-mode.jpg](free-mode.jpg)**, 7.45 to 7.73 s a frame apart: the knight walks up beside a
  crystal wall swinging, then the heavy press at 7.68 s spins with its cleave wave and breaks the
  wall on the impact frame.

How it feels by hand (the pointer, the timing window, the 80 ms from press to impact) is on
[the operator's list](../operator-pending.md).
