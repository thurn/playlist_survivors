# Native Trailer

> **Archived 2026-11-01.** Superseded by Rhythm Champions: [the design](../../champions/design.md) and [the plan](../../champions/plan.md). Kept for history; not authoritative.

A minute of the native game as a trailer: pure play, staged shot by shot, cut on the beat to
one continuous window of *Action*. It is native's counterpart to the 3D tech demo's trailer
mode ([docs/archive/tech-demo-3d/trailer.md](../tech-demo-3d/trailer.md)).

- **Run it:** `just native-trailer` writes `captures/native-trailer.mp4` (1080p60 H.264, AAC).
- **The spec:** [native/trailers/action.yaml](../../../native/trailers/action.yaml), the shot list.
- **The base level:** [levels/trailer.yaml](../../../levels/trailer.yaml), which every shot runs.
- **The code:** `native/crates/app/src/slice/trailer.rs` (`--trailer <session>`).

## What the viewer sees

- **The music window is demo3d's.** It runs from song 121.40 s (just before bar 79.3, break-2's
  stabs) to the cut to black on 117.3. Then 1.5 s of black while the song fades out.
- **There are eight shots, on demo3d's cuts:** the cold open, then the drop (81.1), the re-entry
  (89.1), drop-2's hottest phrase (97.1) and the final peak (105.1). After that the cuts
  quicken (109.1, 111.1) into the climax (113.1).
- **Each shot has its own place, horde and arsenal:** a zone, an enemy block and evolved
  weapons. Examples: skeletons into Bloody Tear in the green woods, bat swarms under Thunder
  Loop in the ember hollow, orcs into Soul Eater in the gold grove.
- **The climax is a boss in the biggest crowd.** The hero has the whole arsenal, and the
  finale's blast lands on the final slam (117.1). By 117.3 the horde has fallen and the field
  lies dead.
- **There is no interface.** No HUD, level-up cards, gems, damage numbers, boss banners, chests
  or evolution screens are drawn. Nothing levels up or drops.

## How it works

Every frame is the game's own simulation, but each shot is staged for it:

1. **Each shot is its own short run.** It runs `levels/trailer.yaml` with the shot's overrides
   from the spec:
   - `zone`: the run's one zone.
   - `row`: the one schedule row, which sets the crowd. A row without set pieces runs on the
     song clock, so its `minimum` is the crowd *on screen*. A row with set pieces (`events`:
     `BAT_SWARM`, `FLOWER_WALL`) must run on the run clock, so its `minimum` is everyone alive.
     The row's `widen` becomes the camera's zoom (1.2 / widen).
   - `arsenal` and `passives`: given to the hero at the run's first frame. An evolution brings
     its base weapon to max rank and adds its passive.
   - `finale`: the position where the blast should land. The run's deadline is set just before
     it. The blast lands on the first *even* beat of the run at or after the deadline, so
     `warmup` may need a beat more to put the slam on one.
   - `level`: any other level field, merged key by key.
2. **The run starts `warmup` seconds before the shot's cut (6 s by default),** so the crowd is
   in place at the first frame.
3. **The music stream comes from a recorded showcase session.** The run is fed that session's
   analyzer stream, rebased to the run's start. The showcase's virtual player (Positioning,
   seed `player`) plays the hero, with hero damage off. Given the same session, the trailer is
   the same every time.
4. **The sound is the song, continuous from the first frame,** at `music`. Under it are each
   shot's own sound effects, rendered offline through the effects' mixer at `sfx`. The effects
   crossfade over 24 ms at each cut, and everything fades out over the black.

`just native-trailer` records the showcase once, headless, into
`$PS_CACHE/trailer-session/` (about 3.5 minutes; it needs demo2d's baked `action.wav`). After
that it reuses the session. Rendering takes about 2 minutes.

## Editing it

- **The log prints each shot's crowd at its first and last frame:** on screen, alive, and zone.
  For the climax it also prints when the blast lands and when the field is clear. Use these to
  tune `minimum` and `finale`.
- **For a quick contact sheet:**

  ```bash
  ffmpeg -i captures/native-trailer.mp4 -vf "fps=1,scale=480:270,tile=6x5" sheet-%d.png
  ```

- **Pick enemies that contrast with the ground.** Slimes vanish on the green woods, so they play
  in the night glade.
