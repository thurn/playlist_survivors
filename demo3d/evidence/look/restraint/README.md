# The neon look toned down, on the crystal cut (ps-wnj.24, 2026-11-01)

The same change as [game's](../../../../game/evidence/look/restraint/README.md)
on the crystal cut: calm by default, the loudest effects only in a special
moment ([neon-look.md, Restraint and special moments](../../../../docs/archive/design/neon-look.md#restraint-and-special-moments)).
Here the special moments are the formation smashes and structure breaks
(`BreakMoments`, whose rings are special) and the heaviest blows (weight
0.95 or more); flash cuts and lens hits are the trailer's own and unchanged.
The shots are not re-staged yet (ps-wnj.17).

Before is an IL2CPP Mac player of 6610faff, after one of this change:

```sh
APP="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --capture-stills "$PWD/DIR" --stills-format png \
  --capture-mask --stills-at 4,12,20,28,36,44,52,55.95,56.3 --seconds 57 --look edge
uv run --project demo3d/tools demo3d look-metrics DIR \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json --preset edge \
  --impact still-055.950 --impact still-056.300
uv run --project demo3d/tools demo3d filmstrip restraint-after-break-edge --at 9.3 --frames 16 --every 2 --warmup 3.0 \
  --columns 8 --player-args "--trailer crystal --look edge"   # before: --app the saved 6610faff player
```

(The player wants an absolute stills path: a relative one wrote nothing.)

## Stills and strips

- `crystal-012.jpg`, `crystal-036.jpg`, `crystal-055.jpg`: each preset's
  before (left) and after (right). The formations and props come down from
  near white to the gem: the formations still outshine the props
  (`StructureLook.Featured`, faces at 3x the props' and at least a third of
  the gem, were 4x and a half), and the grid recedes. 55.95 s is the
  breath's mass kill, an impact frame: still a burst, a little less white.
- `formation-break-PRESET.jpg`: shot 2's formation smash (9.30-9.80 s, every
  second frame), before above after. Before, the shards held solid white for
  0.3 s and more after the impact while the grid lit up round it. After, the
  smash is a special moment: the formation flashes white on its impact
  frames, the post's flash and the lens hit land as before, and within about
  0.2 s the shards fall to the gem's cream (the tier's envelope falling over
  0.5 s to the calm 30%, and the glow's peak about two thirds of before).

## The grid

Floor L* p99 (the grid's lines, from the ID render and view json) over the
seven steady stills:

| preset | before | after |
| --- | --- | --- |
| edge | 11.6-25.0 | 5.0-10.1 |
| solid | 4.3-19.1 | 5.9-13.0 |
| hologram | 9.3-24.0 | 3.3-10.8 |
| prism | 10.8-29.9 | 10.2-24.3 (the gems' halos onto the floor) |

`floor_p99_max` (20, prism 26) breaks before in edge, hologram and prism and
passes after in every preset.

## Metrics (full tables in metrics/)

- **Restraint rules pass in every preset**: `floor_p99_max`,
  `background_light_max` (0.007-0.053 after), `flash_frames_max` (no steady
  still flashes, before or after: the cut's flashes sit on impact frames).
- **Bright share fell**: edge 0.06-0.18 to 0.04-0.11, hologram 0.04-0.16 to
  0.03-0.08, prism 0.04-0.14 to 0.03-0.08. `bright_share_min` (0.04,
  "something emits") now breaks on hologram's and prism's sparsest shots (4,
  28 and 52 s at 0.026-0.036; prism broke it once before). A first pass
  cut every post option's bloom and halo by 30%; hologram's and prism's thin
  emitters (wires, small gems at trailer distance) are cut by only 10-15%
  now, which moved them little: most of the loss is the calmer beat and ring
  light on the horde. Left for ps-wnj.19, which judges every preset's
  metrics on the cut: lower the floor for those presets, or lift their
  monsters' resting light.
- Prism's 52 s still now breaks `monster_form_min` by a hair (29.8 against
  30; 31.4 before).
- Unchanged: `halo_min`, `hero_margin_min`, `key60_min` and
  `swarm_separation_min` broke before and still do (the post's, the hero
  treatment's, the staging's and the looks'); white share stays at or under
  0.1%.

## Checks

- `trailer --name crystal --checks`: ok (the timeline is unchanged).
- The action cut is unchanged: its post feel's amounts come from
  arena-layout.json, whose flashes have no calm share (1), and it draws its
  own crowd and structures.
