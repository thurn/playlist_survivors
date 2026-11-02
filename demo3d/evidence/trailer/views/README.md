# The closer views

Both cuts after the run's final review (ps-b5z.6; [trailer.md, Overdrive](../../../../docs/archive/tech-demo-3d/trailer.md#overdrive-the-starting-point)).
The operator's review: "no yellow glow around the hero please", then "maybe a closer camera
angle? hero honestly looked fine before". So the hero's glowing outline (ps-b5z.1) is gone, the
hero stays at true size, and most shots frame closer, each on-screen target scaled down with the
view's area so the crowd per square metre holds:

| Shot | Default cut | Crystal cut |
|---|---|---|
| 2 | 45 m (kept) | 40.5 m (was 45) |
| 3 | 42→47 m (was 45→50) | 45→50 m (kept) |
| 4 | 40 m (was 50) | 40 m (was 50) |
| 5 | 34 m (was 40) | 36 m (was 40) |
| 6 | 32 m (was 40) | 32 m (was 40) |
| 7 | 36 m (was 45) | 36 m (was 45) |
| 8, the fight | 34 m (was 36→44) | 32 m (was 36→44) |

The two kept shots, the default cut's desert charge and the crystal cut's lava streams, fell
outside the density check's band in their first or last bar at every closer view tried.

- `action.jpg`, `crystal.jpg`: one row per moment (trailer 8, 20, 33, 43, 47.5, 50.5 and 54.5 s,
  shots 2 to 8): the frame at half size, and a 480 x 270 crop round the hero doubled.

What they show:

- **At 32-36 m** (43 s and on) the hero reads in the crops and can be found in the full frames:
  about 35 px tall, a textured figure among dyed (default) or glowing (crystal) monsters.
- **At 40-45 m** (8 and 20 s, and the crystal cut's 33 s) the hero is still hard to find in the
  full frame, most of all in the default cut's bright desert and lava, where the blows' bloom and
  the hero's warm light pool swallow them. Those shots' views are bound by their density bands.

Made with (a built player):

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal --trailer [crystal] --fixed-time --capture-stills captures/views-CUT \
  --stills-at 8,20,33,43,47.5,50.5,54.5 --seconds 55
uv run --project demo3d/tools python demo3d/evidence/trailer/views/sheet.py captures/views-CUT \
  demo3d/evidence/trailer/views/CUT.jpg CUT
```
