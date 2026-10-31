# Trailer: the 60 s re-cut (ps-7mx.3)

The trailer re-cut to about a minute ([trailer.md, The Shot List](../../../../docs/tech-demo-3d/trailer.md#the-shot-list)):
eight shots from break-2's duel (bar 79) through drop-2 into final-peak, to
a final slam on 117.1 (58.22 s), two beats of aftermath, the cut to black on
117.3 and the 1.5 s fade: 60.5 s in all.

- **Room to breathe.** After the 2.5 s duel, three 8-bar shots (12.4 s
  each) cut on the drop and the re-entries at 89 and 97: the desert charge,
  the lava converge and the blossom vortex. Final-peak then quickens: 4 bars
  of neon, 2 of snow, 2 of the crypt, and an unbroken 4-bar climax in the
  forest. No shot is under 2 bars.
- **All seven environments,** the three newer ones carrying the long middle
  (lava, blossom) and final-peak's re-entry (neon).
- **The choreography pass's horde and hero** (ps-7mx.1): coloured blocks of
  like monsters, blows on the strong beats at a natural rate, the hero
  never surrounded.

## Checks

`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --checks`:

```
shot  environment  on screen / target  swings/s  kills/swing (mean, peak)  hero acted  kite  liveness  blocks  lane
   1  crypt                - / 3           1.21          1.0, 1                 100 %     -         0       -   103
   2  desert             545 / 560         1.05         67.5, 238               100 %   2.4         0    83 %    86
   3  lava               431 / 440         1.29         29.5, 77                100 %   2.4         0    81 %    87
   4  blossom            768 / 760         1.05         73.9, 215               100 %   2.3         0    78 %   143
   5  neon               410 / 420         1.29         62.5, 213               100 %   2.5         0    81 %    88
   6  snow               450 / 450         1.29         41.5, 82                100 %   2.4         0    78 %   105
   7  crypt              526 / 550         1.29         31.0, 38                100 %   2.3         0    77 %    73
   8  forest            1266 / 1300        1.00        238.7, 1427              100 %   2.3         0    83 %    99
trailer checks: ok
```

- Every check passes for `--roster forest` and `--roster heroic` too.
- Seeds 3, 5, 7, 9, 11, 13 and 21 pass every check; seed 42 misses two
  bars' density by a few points (shot 2's bar 87 at 468 of 560, shot 4's
  bar 97 at 628 of 760). The seed sweep copies the trailer file with another
  `seed` as `trailer-sN.json` (not committed) and runs
  `trailer --checks --name sN`.
- Pinned (`--environment`) to desert, snow, forest, lava or blossom every
  check passes; pinned to crypt or neon, shot 3's bar 96 holds 371 and 366
  of 440, just under the 15% line.
- The choreography tests pass (`dotnet test demo3d/dotnet/Demo3D.Choreography.Tests`,
  252, and demo2d's, 41); the normal timelines keep their pinned hashes.

## Top-down sheets

`../choreography/topdown.py` draws each horde shot's timeline from above, a
frame per chosen beat, monsters coloured by type (white skeleton, green
goblin, cyan slime, purple bat, orange mushroom, red demon, brown Big Ork,
blue Troll, magenta the boss), the hero yellow with the open-lane check's
6 m circle, turned by the shot's heading so up is the camera's forward:

| file | shot | what to look for |
|---|---|---|
| `topdown-shot2-desert.jpg` | 2, desert charge, 81.1–88.3 | the crater on the cut, the wave turning to chase, purple bat blocks streaming in after the second turn |
| `topdown-shot3-lava.jpg` | 3, lava converge, 89.1–96.3 | streams of one colour each, the Big Orks' brown masses among them, the lane open behind the hero |
| `topdown-shot4-blossom.jpg` | 4, blossom vortex, 97.1–104.3 | the spiral of green, purple and white bands closing behind the hero round the loop |
| `topdown-shot5-neon.jpg` | 5, neon charge, 105.1–108.3 | the wave split by the slam, the hero running on down the street |
| `topdown-shot6-snow.jpg` | 6, snow vortex, every beat of 109–110 | the short vortex: blocks spiralling in, the hero circling |
| `topdown-shot7-crypt.jpg` | 7, crypt converge, every beat of 111–112 | three streams meeting the hero |
| `topdown-shot8-forest.jpg` | 8, the climax, 113.1–116.4 | a patchwork flood, the boss (magenta) on the hero's flank after the path turns at 116.1 |

## Player stills

From the IL2CPP player on the Mac (Metal), windowless at fixed time with the
trailer's full look, built from this bead's tree, one run for every still
(`../choreography/stills.py` makes the sheets). The player logs eight shots,
flash cuts at 2.481, 14.868, 27.255, 39.642 and 52.029 s, 338 sound cues and
48 lens hits, and builds its eight shot simulations in 0.68 s.

| file | what |
|---|---|
| [first-frames.jpg](first-frames.jpg) | every shot's first frame: the duel, then the five flash cuts' white (2.481, 14.868 over the lava's orange, 27.255, 39.642, 52.029) and the hard cuts' opening blows (45.836 snow, 48.933 crypt) |
| [mid-shots.jpg](mid-shots.jpg) | a frame inside each shot, two in each long one: 1.2 (the duel), 6.0 and 10.5 (desert), 18.0 and 23.0 (lava), 30.5 and 36.0 (blossom), 42.5 (neon), 47.3 (snow), 50.4 (crypt), 55.5 and 57.8 (the climax) |
| [cut-81.jpg](cut-81.jpg) | the drop: the third brute falling (2.42, 2.465), the white (2.481, 2.50), the crater coming out of it (2.533-2.70), the hero running into the wall (3.0) |
| [cut-105.jpg](cut-105.jpg) | final-peak's re-entry: the blossom vortex's end (39.60), the flash over neon's opening slam (39.642-39.80), the street (40.10) |
| [cut-113.jpg](cut-113.jpg) | the climax's cut: the crypt streams' end (51.95), the flash over the forest's slam (52.029-52.10), the flood (52.30, 55.5) |
| [final-slam.jpg](final-slam.jpg) | the climax before the slam (58.10, 58.21), the slam's frame and its lens hit (58.223, 58.24), the screen burning (58.30-58.65), the dead field (58.85, 58.98), black (59.00, 59.40) |
| [blow-lava.jpg](blow-lava.jpg) | a cleave in the lava shot (91.1, 17.965 s) from 18.0 s, every 0.1 s, cropped round the hero: the ring landing, the follow-through, the hero running on between blows |
| [timing.md](timing.md) | the cut-frame GPU probes: see "Cut-frame timing" below |

What they show:

- **Coloured blocks in every horde.** Red demon blocks against skeleton
  and goblin fields in the desert, lava and crypt; the blossom's spiral in
  bands of red, white and goblin green; neon's chartreuse mushroom masses;
  the climax's patchwork round the lasers. As ps-7mx.1 found, under the
  default palette skeletons, goblins and bats share tan tones, so the
  demons' red and neon's mushrooms read strongest.
- **Room to breathe.** The long shots hold one place each for 12.4 s; the
  two mid-shot stills in each show the horde still flowing in its second
  half (desert 10.5, lava 23.0, blossom 36.0).
- **The new environments carry the middle.** Lava's glowing cracks, the
  blossom's pale stone and pink trees, neon's lit street, each in its own
  grade.
- **The end** matches the first cut's: the slam clears the screen, the dead
  are down by 58.98, black on 59.00.

## The mix

`uv run --project demo3d/tools demo3d trailer --stand-in --out captures/trailer-standin.mp4`
runs every capture step but the window: 3,631 frames from song 121.400 to
181.897 s, a 60.5 s video, 338 cues and 16 clips with none missing, the
limiter's peak at its 0.95 ceiling, nothing from before any of the seven
cuts sounding 60 ms after it, and the end fade from 180.397 s falling to
silence. The windowed capture (`just trailer`) is the operator's.

## Cut-frame timing

[timing.md](timing.md): a cut frame runs up to about 14 ms over the frames
after it, but drawing the same frames with no cut (`--shot N
--from-preroll`) costs as much or more: the cost is each shot's opening
blow landing on its cut frame, not the switch, which costs 0.10-0.12 ms of
CPU. The live run's smoothness at the cuts stays the operator's check.

## The runs

```
P="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
C=$PWD/captures/trailer/recut
"$P" -batchmode -force-metal -logFile $C/all/player.log --trailer --fixed-time --capture-stills $C/all \
  --stills-at 0,1.2,2.42,2.465,2.481,2.50,2.533,2.60,2.70,3.00,6.0,10.5,14.80,14.868,14.917,15.10,18.0,18.10,18.20,18.30,18.40,18.50,18.60,18.70,23.0,27.20,27.255,27.30,27.50,30.5,36.0,39.60,39.642,39.66,39.70,39.80,40.10,42.5,45.836,47.3,48.933,50.4,51.95,52.029,52.05,52.10,52.30,55.5,57.8,58.10,58.21,58.223,58.24,58.30,58.45,58.65,58.85,58.98,59.00,59.40 \
  --seconds 59.5
# copy each sheet's stills into its own folder, then:
uv run --project demo3d/tools python -I demo3d/evidence/trailer/choreography/stills.py first-frames.jpg recut-first-frames DIR
uv run --project demo3d/tools python -I demo3d/evidence/trailer/choreography/stills.py blow-lava.jpg recut-blow-lava DIR 640
# top-down sheets:
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --out-dir $C/shots
uv run --project demo3d/tools python -I demo3d/evidence/trailer/choreography/topdown.py $C/shots/shot-N.json OUT.jpg LABEL YAW T...
```
