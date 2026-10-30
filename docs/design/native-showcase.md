# Native Showcase

`--showcase` is the native game playing *Action*, demo2d's song, start to end
with nobody at the keys, at demo2d's crowd and framing. It exists for one
comparison: the native game against the
[2D pixel demo](../tech-demo-2d/README.md), the same song and the same crowd
curve, so the operator can judge which feels better. It was built by epic
`ps-8sb`; the evidence is in `native/evidence/showcase/`.

## Running it

From `native/`, after demo2d's setup has baked the song
(`uv run --project demo3d/tools demo2d setup` writes
`demo2d/Assets/Baked/Audio/action.wav`, never committed):

```sh
# Watch it, with sound: the app quits a few seconds after the finale.
cargo run --release -p app -- --showcase

# Agents: no window, muted. It records logs/sessions/<id>.
cargo run --release -p app -- --showcase --headless

# Measurements of a recorded session, offscreen and as fast as it can:
cargo run --release -p app -- --showcase-stills logs/sessions/<id>    # three stills of the run
cargo run --release -p app -- --showcase-density logs/sessions/<id>   # crowd per section, frame time
cargo run --release -p app -- --showcase-arsenal logs/sessions/<id>   # level-ups, chests, arsenal per section
```

- `--file <path>` plays another file; `PS_ACTION_WAV` points at the song when
  the worktree has no bake (a worktree shares nothing with the main checkout).
- `--showcase --headless --seconds 200` stops just before the finale: the
  song is 199 s, the finale waits for 3 s of quiet, and Delayed listening
  hears everything 3 s late. Without `--seconds` it quits on its own.
- `--showcase-density` and `--showcase-arsenal` replay the session's music on
  the showcase level as it is now (`--level` for another), with the virtual
  player at the keys and five player seeds. They write
  `<session>/density/` and `<session>/final/`.

## What it is

- **The game, as it is.** Native's hero, roster, weapons, animations,
  effects, HUD, post and sound. Nothing is restyled toward demo2d.
- **The song.** demo2d's bake of *Action* (3:19), heard in Delayed listening
  (3 s late, beats from hindsight); native's own analysis finds the beats.
  The run starts by native's rule, 2 s of clear music (song 7.0 s), and ends
  with native's finale on the song's confirmed end.
- **The player.** `players::SurvivorBot` (positioning) moves the hero and picks
  every card; chests resolve on their own. The hero never falls
  (`heroStands`: hits land, but its health stops at a tenth).
- **The level.** `levels/action-showcase.yaml`: one schedule row per section
  of `songs/action/songmap.json`, keyed to the song clock (`songS`), so card
  freezes never shift the crowd.

## How it differs from demo2d

| | demo2d | `--showcase` |
|---|---|---|
| Engine | Unity, a precomputed timeline | native's sim, live on the audio |
| Beats | the song map | native's analysis of the audio |
| Crowd | `onScreen` x 400 per section | the same targets, counted in the view; the spawner steers a crowd round the view toward them |
| Camera | `viewWidthM` against a 30 m base | the same widths as a widening of native's camera, eased over 2 s |
| Monsters | demo2d's pixel roster | native kinds of a similar role (casters become `vs-xlbat`: native has no ranged survivors kind) |
| Hero | a knight with combos and spells on the grid | native's survivors hero and arsenal, levelling as it plays |
| Interruptions | none | level-up cards and chests freeze the run (the music plays on) |
| HUD | none | native's HUD |
| The end | the horde cleared on accents | native's finale blast, about 3 s after the song |

Crowds fall slowly after a peak in native: it takes only out-of-sight enemies
off the field, so a crowd on screen thins only as fast as the hero kills it.
Where the late hero kills faster than the spawner refills (the breakdown, the
outro), the level asks a little more than demo2d's count; the level file notes
each.

## Growth in 3:19

Native's survivors run is 30 minutes long; the showcase compresses it so the
final peak shows a late-run arsenal (evidence:
`native/evidence/showcase/final/`):

- **`xpScale` per row**: the share of each gem's experience the hero takes
  while the row plays. At demo2d's crowd the hero would level every 3 to 4 s
  (39 level-ups, 66 s of the song frozen on cards). The showcase banks levels
  in the calm sections (the breakdown, the build and break-2), takes a few in
  the grooves and the drops, and none from the final peak on: the climax
  never stops for a card.
- **Three tier-3 chests** (five items each) ride on bats at the starts of the
  groove, the breakdown and groove-2. `evolves` lets the last two evolve a
  weapon (otherwise only chests of the Mad Forest's minutes 10 to 25 may).
  The hero takes a chest only when clear of the crowd, so each lands a
  section or so after its bat appears.
- **The result**: about 25 level-ups and 46 s frozen, none in the final peak
  or after it; the final peak plays Bloody Tear, Holy Wand and Soul Eater
  beside King Bible and Lightning Ring.

## Pending on the operator

Watch `--showcase` with the speakers on beside demo2d's build and judge the
feel: the row is in `native/evidence/operator-pending.md`.
