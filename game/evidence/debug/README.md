# The debug surfaces (ps-msz.6.12)

The F1 menu's commands, `--start-minute`, `--level` and the overlay's flow field
([the design](../../../docs/archive/design/unity-game.md#debug-surfaces)). Measured on the Mac (jarvis) on
2026-10-31 with an IL2CPP player built from this bead's tree.

## Tests

- `game/Assets/Game/Tests/Sim/DebugCommandTests.cs` (.NET and EditMode): the clock's jump (only the new
  minute's boss, no burst of the wand's shots jumped over), a role in view and a boss on the next
  downbeat, the grants and an evolution with what it needs, invulnerability, and a run started at 25:00:
  level 27, Bloody Tear and Soul Eater possible, a chest that evolves, minute 25's boss alone with its
  health times the level. The jump's timer shift, the spawner's skip and the hero's level were each
  mutation-checked.
- `SessionTests.ARunStartedAtAMinuteReplaysItsDebugCommandsWithTheirArguments`: a run session at 25:00
  with six argument commands replays to its hash.
- `game/Assets/Game/Tests/EditMode/DebugSurfaceTests.cs`: `--debug`'s names and arguments, the menu's
  letters and Shift, and `--level`'s files.

## A scripted headless run at 25:00, watched to its hash

```sh
"game/Builds/mac/il2cpp/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode \
  -logFile "$PWD/captures/game/debug/start25.log" --file "$PWD/public/reactive/house-128/house-128.ogg" \
  --headless --seconds 42 --start-minute 25 --watch --sessions "$PWD/captures/game/debug/sessions" \
  --events-out "$PWD/captures/game/debug/start25-events.csv" \
  --debug "spawn:werewolf@8,boss:boss-werewolf@9,weapon:magic-wand@10,passive:spinach@10,evolve:holy-wand@11,invulnerable@12,stampede@13,accent@14,change@15,break@16,regrow@20,chest@21,jump:28@24,invulnerable:0@30,finale@34"
```

Exit 0 after 88 s (the run, then its replay). The run settled `40D3D0B97384ADB4` (15868 events) and
the replay of its session (`"version": 2, "level": "run", "startMinute": 25, "commands": 15`)
matched. From the events:

| Command | Seen |
|---|---|
| `--start-minute 25` | RunStart at 3.78 s; minute 25's boss-xlflower lands at 4.72 s (and the signature minotaur, due from 1:30) |
| spawn:werewolf@8 | a werewolf spawns at 8.00 s |
| boss:boss-werewolf@9 | lands as a boss at 9.40 s, the next downbeat |
| weapon, passive, evolve@10-11 | Magic Wand's projectiles and Holy Wand from 10 s (696 projectiles) |
| stampede@13 | a set piece at 13.15 s, the next main beat |
| accent@14 | a heavy strike breaks a structure at 14.56 s |
| change@15 | a zone change at 15.03 s; its regrowth at 15.97 s |
| break@16 | nothing to break: the structures in reach had regrown at 15.97 s and glint while they grow |
| regrow@20 | regrowth on the next downbeat, 20.65 s |
| chest@21 | a tier-3 chest drops two tiles ahead at 21.00 s (a headless hero stands, so it stays) |
| jump:28@24 | the run clock goes on from 28:00: 1690 s at the end |
| finale@34 | the blast at 34.72 s, the next downbeat; the victory card at 37.72 s |

## `--level`

`--file house-128.ogg --headless --seconds 10 --level "$PWD/game/Content"` played the run on the
checkout's content files in place of the bake's (the log names the seven content files and arena.json
it replaced): exit 0, 582 frames, the run playing from 3.78 s.

## The overlay's flow field

[still-016.000.jpg](still-016.000.jpg): `game look debug --evidence debug --at 16 --player-args
"--start-minute 25 --overlay --debug spawn:werewolf@8,invulnerable@9"`. The arrows head down the
field to the hero, cyan near it; red crosses mark the blocked cells of the structures and the arena's
edge; the text reads the run clock at 25:16, level 27 and INVULNERABLE.

The F1 menu needs a window and a key, so it is an operator row in
[operator-pending.md](../operator-pending.md).
