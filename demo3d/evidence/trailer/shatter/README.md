# The crystal cut's shattering formations

Every crystal formation break of `--trailer crystal` (ps-b5z.4; [trailer.md, Crystal
formations](../../../../docs/tech-demo-3d/trailer.md#crystal-formations)), one strip each: the
frame 20 frames before the impact (the formation standing), the impact frame, then +3, +10, +25
and +50 frames (60 fps, so the last is 0.83 s on). Each tile is a 1040 x 676 crop of the 1080p
frame round the hero and the formation, labelled with its trailer second.

| Strip | Shot | Environment | Impact (trailer s) | Bar.beat | Blow |
|---|---|---|---|---|---|
| `break-01` | 2 | crypt | 4.803 | 82.3 | spin |
| `break-02` | 2 | crypt | 9.449 | 85.3 | cleave |
| `break-03` | 2 | crypt | 13.320 | 88.1 | cleave |
| `break-04` | 3 | lava | 17.191 | 90.3 | cleave |
| `break-05` | 3 | lava | 21.061 | 93.1 | cleave |
| `break-06` | 3 | lava | 22.610 | 94.1 | cleave |
| `break-07` | 4 | forest | 29.578 | 98.3 | spin |
| `break-08` | 4 | forest | 34.223 | 101.3 | cleave |
| `break-09` | 4 | forest | 38.094 | 104.1 | cleave |
| `break-10` | 5 | neon | 41.191 | 106.1 | cleave |
| `break-11` | 5 | neon | 44.287 | 108.1 | cleave |
| `break-12` | 6 | crypt | 47.384 | 110.1 | cleave |
| `break-13` | 7 | lava | 50.481 | 112.1 | cleave |
| `break-14` | 8 | forest | 54.352 | 114.3 | spin |
| `break-15` | 8 | forest | 56.674 | 116.1 | cleave |

The climax's two (`break-14`, `break-15`) are the cut before ps-b5z.5's boss fight, whose two
breaths now smash the climax's formations instead (see [the fight's evidence](../fight/README.md)).

What they show:

- **Big and unmissable.** Each formation is a cluster of white-gold spires up to 10 m tall, the
  largest and brightest solid thing on screen at the shots' 40-65 m views, among blocks of every
  hue. In the dark environments its glow carries it; on lava's orange cracks and in the crypt's
  pale stone too.
- **The fight's result, on the impact frame.** Each breaks on a hero blow on a strong beat, its
  ring (the gold arc) reaching it: on the impact frame it flashes white, three frames on its
  spires are splitting, ten frames on it is a burst of chunks a metre across, and by +25 to +50
  they rain down round its foot and out over the horde. Nothing cracks or glows before the
  impact frame (the -20 tiles show it whole and quiet).
- **The feel.** The impact frame carries the break's lens hit, centred on the formation, so the
  blur streaks the horde round it and leaves it sharp, the post flash, the camera's shake and
  the hit-stop (the horde holds while the shards fly). `break-11`'s formation stands behind one
  of neon's blue buildings; it reads over its roof.
- **The horde pours through.** From the break the ground where it stood is open: the horde
  that parted round it walks over the debris (`break-02` and `break-10`, +25 to +50).

The checks (`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer
--name crystal --checks`), its new `breaks` column the moments on screen:

```
shot  environment  on screen / target  swings/s  kills/swing (mean, peak)  hero acted  kite  liveness  blocks  lane  breaks
   1  neon                 - / 3           1.21          1.0, 1                 100 %     -         0       -   103       0
   2  crypt              511 / 520         1.05         54.5, 184               100 %   2.4         0    79 %    85       3
   3  lava               418 / 440         1.29         21.4, 70                100 %   2.4         0    79 %    99       3
   4  forest             764 / 760         1.05         66.7, 189               100 %   2.3         0    74 %   148       3
   5  neon               376 / 400         1.29         41.9, 90                100 %   2.5         0    82 %   100       2
   6  crypt              442 / 450         1.29         36.8, 84                100 %   2.4         0    68 %   105       1
   7  lava               534 / 550         1.29         29.8, 32                100 %   2.3         0    82 %    80       1
   8  forest            1191 / 1300        1.00        273.4, 1627              100 %   2.3         0    83 %    89       2
trailer checks: ok
```

Made with (a built player; `stills.py` lists the frames round each break from the shots' dumps,
`sheets.py` crops and labels them, projecting each formation with the still's `view-*.json`):

```
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --name crystal --out-dir captures/crystal-dump
"demo3d/Builds/mac/mono/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal --trailer crystal --fixed-time --capture-stills captures/shatter \
  --stills-at $(python3 demo3d/evidence/trailer/shatter/stills.py captures/crystal-dump -20,0,3,10,25,50) --seconds 58.5
uv run --project demo3d/tools python demo3d/evidence/trailer/shatter/sheets.py \
  captures/crystal-dump captures/shatter demo3d/evidence/trailer/shatter -20,0,3,10,25,50
```

Tuning notes:

- **Size.** A first pass at 1.8 m by 7 m read, but small at 45-52 m views; 2.2 m by 10 m (the
  defaults now) reads as towering without crowding the streams.
- **Framing.** The camera looks down at 55 degrees and lags the hero, so a formation ahead of a
  hero running up the screen had its top cut off by the frame (first pass, shot 2). The check now
  wants the top's projection in view too (`ViewGeometry.Raised`), and each formation stands 20
  to 80 degrees off the hero's line, where the frame holds it whole, rather than on it, where
  the hero's path also bent round it and could miss its blow.
- **Placement.** Each is placed just inside one blow's ring (the reach plus the radius, less 0.8
  m) and outside every earlier blow's, so it falls to the blow it was placed for; a slam on a
  cut reaches 7.2 m, so shot 8's first formation waits for 114.3's spin. Shot 5 aims at 400
  monsters, not 420, as its streams squeeze between the formations and neon's blocks.
- **Both runtimes.** The checks must pass under Unity's runtime too (ps-vr7). With shot 3's
  later formations on 93.3 and 95.3 its last bar thinned to 370 against 440 under Unity (384 under
  `dotnet`); on 93.1 and 94.1 it holds 395 under `dotnet` and passes both. Shot 8's first
  formation stood 70 degrees off the hero's line, and under Unity alone a goblin at the view's
  far side marked time for the cut's beat; at 50 degrees both pass.
- **The lens hit.** On the blow's own hit (centred on the hero) the radial blur smeared the
  formation; a break's hit now replaces the blow's, centred on the strike.
