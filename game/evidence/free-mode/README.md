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
uv run --project demo3d/tools game look free-mode --backend mono --at 7.6 --frames 18 --strip 6 --evidence free-mode \
  --player-args "--mode free --file $PWD/public/reactive/house-128/house-128.ogg --walk $WALK --view-width 10"
```

## What it shows

- **The walk**: 11 HeroGlide events and no HeroStep. The drawn hero moves 0.0752 m a frame on each
  axis on the diagonal (6.4 m/s, a 1.5 m tile per 0.234 s stride step), at off-grid points such as
  (8.036, -1.347), with the run clip weighted 1 and no hop.
- **The swings** (since ps-w9p.1: react now, land on the grid), as `events.csv` has them: each attack's wind-up
  start, impact and reach (full reach is 7.037 m), against the beat. The tracker's beats here are 0.47 s apart
  (6.12, 6.59, 7.06, 7.53, 8.00, 8.47, 8.93, 9.40), so the eighths fall on them and halfway between:

  | Press (s) | Wind-up from (s) | First eighth 0.12 s on | Impact (s) | Grid point | Reach (m) | Grade | Note |
  |---|---|---|---|---|---|---|---|
  | 6.50 | 6.50 | 6.825 | 6.825 | half beat | 5.56 | 0.79 | off the beat |
  | 6.60 | | 6.825 | | | | | lands on the eighth the swing before has: dropped mid-wind-up |
  | 7.00 | 7.00 | 7.295 | 7.295 | half beat | 7.04 | 1 | |
  | 7.25 | 7.295 | 7.53 | 7.53 | beat | 7.04 | 1 | winds up from the swing before's impact, which it keeps |
  | 7.60 | 7.60 | 7.765 | 7.765 | half beat | 9.15 | 1 | full on the accented beat 7.53: a heavy spin; its cleave's bands from 8.00 a sixteenth (0.1175 s) apart |
  | 8.00 | 8.00 | 8.235 | 8.235 | half beat | 7.04 | 1 | |
  | 8.35 | 8.35 | 8.47 | 8.47 | beat | 3.70 | 0.53 | close to a quarter beat off |
  | 8.90 | 8.90 | 9.165 | 9.165 | half beat | 7.04 | 1 | |

  Every impact is on an eighth, and so are the deaths and structure breaks: the spin's at 7.765, its cleave's at
  8.00, 8.1175, 8.235 and on. The drawn hero (`hero.csv`) is in the attack on each key-down frame: at 6.500 the
  SlashL clip at 0.000 s with the attack weighted 0.17, its wind-up running 0.325 s to the impact frame (6.833, the
  clip past its 0.35 s impact, the blade's flare up).
- **[free-mode.jpg](free-mode.jpg)**, 7.60 to 7.88 s a frame apart: the knight beside a crystal wall winds up from
  the heavy press's key-down frame (+0) and spins on the half beat at 7.765 s (+10), breaking the wall and sending
  out its cleave wave on the impact frame.

How it feels by hand (the pointer, the timing window, the eighth's wait from press to impact) is on
[the operator's list](../operator-pending.md).
