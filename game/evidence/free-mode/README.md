# The free mode (ps-y21)

`--mode free` ([the design](../../../docs/archive/design/game-modes.md)): the hero walks freely and the
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
- **The swings** (since ps-w9p.8: hit at once; the beat grades the payoff, never the timing), as `events.csv`
  has them: each attack's wind-up start, impact, reach (full reach is 7.037 m) and grade (`d5`, the attack event's
  `Grade`), against the beat. The tracker's beats here are 0.47 s apart (6.12, 6.59, 7.06, 7.53, 8.00, 8.47, 8.93,
  9.40). Scripted keys are heard as the sim takes them, so each impact is `Sword.PressImpactS` (1/30 s) on:

  | Press (s) | Wind-up from (s) | Impact (s) | Press to impact (s) | Reach (m) | Grade | Note |
  |---|---|---|---|---|---|---|
  | 6.50 | 6.50 | 6.533 | 0.033 | 5.56 | 0.79 | off the beat: smaller, its light, flare and swing at 0.79 |
  | 6.60 | 6.60 | 6.683 | 0.083 | 7.04 | 1 | in the swing before's follow-through: waits for its 0.15 s swing, winding up from the key |
  | 7.00 | 7.00 | 7.033 | 0.033 | 7.04 | 1 | on the beat: 1.75x light, 1.25x flare, the heavy whoosh under the swing |
  | 7.25 | 7.25 | 7.283 | 0.033 | 7.04 | 1 | a half beat |
  | 7.60 | 7.60 | 7.633 | 0.033 | 9.15 | 1 | full on the accented beat 7.53: a heavy spin; its cleave's bands from 7.708 (75 ms on), 37.5 ms apart |
  | 8.00 | 8.00 | 8.033 | 0.033 | 7.04 | 1 | |
  | 8.35 | 8.35 | 8.383 | 0.033 | 3.70 | 0.53 | close to a quarter beat off: half the reach and damage, dim and quiet |
  | 8.90 | 8.90 | 8.933 | 0.033 | 7.04 | 1 | |

  Every main impact is 1/30 s after its press but the one that waited for the swing before (ps-w9p.1 landed these
  0.12-0.33 s on, on the eighths). The deaths and structure breaks follow at once: the spin breaks the wall at
  7.633 and its cleave's bands kill and break from 7.708 every 37.5 ms; the 8.90 press's blow kills at 8.933. A live
  key reaches the sim about the lead (~90 ms) after it was heard, past its impact; since ps-w9p.10 the press then
  resolves in heard time, so live too its impact is 1/30 s on ([press-latency](../press-latency/README.md)). The drawn hero (`hero.csv`) is in the strike on each key-down frame: at
  6.500 the SlashL clip at 0.267 s, 0.083 s short of its 0.35 s impact frame, the attack weighted 0.5 (1 a frame
  later), at its impact frame on 6.533 with the flare at 0.79 (the grade), then held (0.360, 0.371 a frame apart:
  the hit-stop at 0.25x); at 7.600 the spin at 1.350 of its 1.433 s impact, on it at 7.633 with the heavy's 1.5 flare.
- **[free-mode.jpg](free-mode.jpg)**, 7.60 to 7.88 s a frame apart, from the heavy press's key-down frame (+0): the
  knight beside a crystal wall is in the spin at once, and two frames later (+2, 7.633 s) the blow lands, the gold
  crescent sweeps and the wall shatters; the cleave wave rolls out from +6 and breaks the rest by +11.

How it feels by hand (the pointer, the timing window, the strike's sliver of wind-up, the hit-stop and the on-beat payoff) is on
[the operator's list](../operator-pending.md).
