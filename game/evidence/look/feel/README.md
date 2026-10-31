# The night look's beat feel and the camera (ps-msz.3.7)

The night post stack moving with the live stream, the fast lane and the game camera
([the design](../../../../docs/design/unity-game.md), Post-processing and Camera). The code is
game/Assets/Game/Client/Look/: `FastLane` (native's lane.rs on the sim's revealed stream),
`FeelMoments` (accents, heavy strikes, breaks and drops), `PostFeel` (the offsets on the night
volume), `CameraRig`, `BeatGlobals` and `LookPresenter`, which the frame loop subscribes. The loop
now plays in the baked arena (`--no-arena` for the placeholder plane), with the placeholder
capsules standing in for the hero and the horde.

## How it was run

On the Mac (jarvis), the Mono player, fixed time (frame n is song time n/60), house-128 through
the plugin, from the worktree's root:

```sh
S="$PWD/public/reactive/house-128/house-128.ogg"; C="$PWD/captures/game"
uv run --project demo3d/tools game look accent --backend mono --at 7.49 --frames 12 --strip 6 --evidence look/feel --player-args "--file $S --camera-out $C/accent-camera.csv"
uv run --project demo3d/tools game look beat --backend mono --at 10.30 --frames 12 --strip 6 --evidence look/feel --player-args "--file $S --camera-out $C/beat-camera.csv"
uv run --project demo3d/tools game look drop --backend mono --at 4.4,5.0,5.5,5.9,6.0,6.1 --strip 6 --evidence look/feel --player-args "--file $S"
uv run --project demo3d/tools game look camera-path --backend mono --at 2,2.5,3,3.25,3.5,4,4.5,5 --strip 4 --evidence look/feel --player-args "--file $S --hero-path"
"game/Builds/mac/mono/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode -force-metal -logFile $C/camera-path.log --fixed-time --file $S --hero-path --seconds 40 --camera-out $C/camera-path.csv
```

`--camera-out` writes each frame's song time, focus, width, punch, shake, level, pulse, flash and
post offsets; the CSVs and full-size frames stay in the gitignored captures/game/. Each frame's
mean luma below is measured from the full-size stills.

## Results

- **The pulses land on the beat frame.** house-128's beats at 10.34 s and 7.53 s fall between
  frames, so the first frame after each is the one that must carry the pulse.
  - [beat.jpg](beat.jpg), the beat at 10.34 s: frames 10.300 to 10.333 sit at luma 21.4 (pulse
    0.000 to 0.004); frame 10.350 jumps to 22.6 (pulse 0.575, +0.069 stops), then decays over
    the beat (22.5, 22.4, ... 21.9 by 10.483). The beat's evidence (about 0.49) is under the
    gate, so it pulses at the soft 0.6.
  - [accent.jpg](accent.jpg), a strong onset on the beat at 7.53 s: 21.3 at 7.500 and 7.517,
    then 31.3 at 7.533: flash 0.98, +0.76 stops, aberration 0.49 and the punch starting (the
    view 2% narrower a sixteenth later), all decaying within the beat (24.7 at 7.683, flash 0.31).
    Aberration is exactly 0 on every frame without a flash.
  - [drop.jpg](drop.jpg): the build-up before the 6.6 dB loudness jump at 6.0 s rises from
    4.4 s (rise 0.006 at 4.5, 0.17 at 5.0, 0.42 at 5.5, 0.55 at 5.983); on the 6.000 frame it
    gives way to the drop's flash (0.55, +0.39 stops) and an 8 cm shake. house-128's jump is
    small, so the build is gentle.
- **The camera follows smoothly at a fixed 60 fps.** With `--hero-path` the camera follows a
  synthetic hero walking a 12-tile square in 1.5 m strides (one every 0.25 s, each taking 0.11 s:
  13.6 m/s while striding, 6 m/s on average). Over 40 s (2401 frames, every step exactly 1/60 s)
  the focus moved at most 8.75 m/s (6.88 on average) and accelerated at most 28.7 m/s²: it
  glides between strides and rounds the corners, with no frame-to-frame jumps.
  [camera-path.jpg](camera-path.jpg) shows the hero's light pool staying centred as the arena
  slides past heading east, then north after the corner at 3 s.
- **The view width eases over two bars.** The music's level went loud at 2.217 s and the width
  eased from 30 to 34 m by 4.100 s: 1.883 s, four beats of 0.47 s. (In the quiet intro it dipped
  toward 28 m and came back as the level settled.)
- The EditMode tests (`LookTests`) check the same on synthetic streams: the pulse is 1 on the beat
  and 0.25 half a beat later, the kick's anticipation is (1 - lead/90 ms)², the drop builds over
  1.6 s, nothing shows before it is known, the accent's flash and aberration, the zone grade's
  two-bar ease, the strikes' bounded punch and shake, the camera's smoothness on the square walk,
  the two-bar width ease with its hysteresis, the framing's width and pitch, and the footprint.

## Decisions

- The fast lane reads the sim's revealed entries (known by the frontier, which leads frameSong by
  the lead) and shows each at `max(t, known_at)`. In practice every entry is known about 2.45 s
  before it sounds, so the 90 ms anticipation and the 1.6 s build-up have room, and fixed-time
  frames reproduce.
- Accents are strong onsets at most one per 0.5 s (native's flashes); heavy strikes are the
  deaths sharing an impact time, weighted 0 at 2 kills to 1 at 24 (demo3d's BlowFeel, scaled to
  the slice's horde); breaks flash, punch and shake by their force, past the hits' 0.25 m shake
  cap up to 0.4 m.
- The camera's lead carries the hero's smoothed velocity times 2τ besides 1.5 m along its
  heading: a 0.4 s critically damped spring alone trails a striding hero by about 5 m. The
  published footprint (`CameraRig.Footprint`, for `HordeConfig.CameraFootprint`) is the loud
  view's trapezoid grown 4 m each side for the lead and the spring's transients (3.5 m at a
  square corner).
- Zone grades are keyed by the arena's palettes (neon is the base); `PostFeel.SetZone` is the
  hook M3's zones call.
