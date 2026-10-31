# The crystal cut

Windowless stills of `--trailer crystal` (ps-7i4; [trailer.md, The crystal
cut](../../../../docs/tech-demo-3d/trailer.md#the-crystal-cut)), at trailer
seconds, scaled to 1280 wide:

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal --trailer crystal --fixed-time --capture-stills captures/crystal \
  --stills-at 1.2,3.3,3.45,10,20,33,41.3,45.1,47,50,52.85,53.0,57.5 --seconds 58
```

| Still | Shot | What it shows |
|---|---|---|
| `still-001.200` | 1, neon | The duel: three glowing elites close on the hero |
| `still-003.300`, `still-003.450` | 2, crypt | The courtyard's east-south run shatters at 3.25 s; its sapphire shards fly beside the hero as the drop's charge breaks on them |
| `still-010.000` | 2, crypt | Blocks of one type each (cyan, green, red, violet) streaming after the hero |
| `still-020.000` | 3, lava | Converging streams on black basalt |
| `still-033.000` | 4, forest | The vortex, spiralling in |
| `still-041.300` | 5, neon | The west-north run just broken (41.19 s), the horde coming through |
| `still-045.100` | 5, neon | The north-east run shattering (45.06 s) |
| `still-047.000` | 6, crypt | The short vortex |
| `still-050.000` | 7, lava | |
| `still-052.850`, `still-053.000` | 8, forest | Both ruins-gate runs blow out together (52.80 s) as the climax's streams arrive |
| `still-057.500` | 8, forest | The laser over the packed horde, the boss above |

The checks (`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c
Release -- trailer --name crystal --checks`):

```
shot  environment  on screen / target  swings/s  kills/swing (mean, peak)  hero acted  kite  liveness  blocks  lane
   1  neon                 - / 3           1.21          1.0, 1                 100 %     -         0       -   103
   2  crypt              504 / 520         1.05         55.8, 195               100 %   2.4         0    81 %    88
   3  lava               420 / 440         1.29         23.3, 69                100 %   2.4         0    79 %    83
   4  forest             769 / 760         1.05         71.6, 201               100 %   2.3         0    75 %   147
   5  neon               394 / 420         1.29         45.8, 97                100 %   2.5         0    87 %    91
   6  crypt              436 / 450         1.29         42.5, 91                100 %   2.4         0    68 %   107
   7  lava               535 / 550         1.29         27.5, 33                100 %   2.3         0    80 %    73
   8  forest            1195 / 1300        1.00        275.9, 1641              100 %   2.3         0    82 %    86
trailer checks: ok
```

Tuning notes:

- A first look (glow 1.2, structures gain 0.22 and glow 0.25, crypt grade
  −0.15) flattened the gems toward neon silhouettes, left the crypt's pale
  stone light grey, and its wall shards dark and hard to see. Glow 1.0, the
  crypt at −0.45 and the structures at gain 0.3 and glow 0.6 fixed all three.
- Placing the breaks: lava's kit leaves every wall run too narrow an opening
  to break, and neon's most courtyard runs. A charge along the ruins wall's
  south side starved its first bar: the glade there is too small for the
  wave. So the drop's breaks moved to the crypt's courtyard, on the action
  cut's open-field side.
- Shot 3 (lava) runs three streams, not four (ps-vr7): with four, its last
  bar thinned to 372 against 440 under Unity's runtime (the EditMode test)
  while `dotnet test` passed. `still-020.000` is retaken with three; the
  first capture of the cut (captures/trailer-crystal-cut.mp4, 11:54 on
  2026-10-31) predates it.
