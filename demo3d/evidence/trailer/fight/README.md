# The dragon fight

Both cuts' climaxes as a boss fight (ps-b5z.5; [trailer.md, The boss
fight](../../../../docs/archive/tech-demo-3d/trailer.md#the-boss-fight)), retaken by the run's final review
(ps-b5z.6) with no hero outline and the closer fight views (34 m, 32 m in the crystal cut). One
strip per attack, from the batch-mode player's fixed-time stills of shot 8, each tile a 1280 x 720
crop of the 1080p frame centred between the hero and the zone's origin, labelled with its trailer
second:

1. the windup plus 0.05 s: the zone lights under the hero;
2. halfway to the dodge: the fill charging outward, the hero strafing inside it (often mid-blow on
   the dragon);
3. the dodge's first frame: the hero still in the zone;
4. 0.12 s into the dodge: the hero dashing out of it;
5. the frame before the impact: the hero clear, the zone's fill at its rim;
6. the impact: the zone white-hot, the lens hit, the dragon's clip striking;
7. 3 frames on: the breath's fire down the cone, or the tail sweep's slash round the dragon;
8. 12 frames on: the dead in the zone, the hero beside it.

| Strip | Cut | Attack | Windup | Impact (trailer s, bar.beat) | Dodge | Killed |
|---|---|---|---|---|---|---|
| `action-1-breath` | action | breath | 113.2 | 53.578 (114.1) | 3.1 m | 79 |
| `action-2-sweep` | action | sweep | 114.4 | 55.900 (115.3) | 5.8 m | 0 |
| `action-3-breath` | action | breath | 116.1 | 57.449 (116.3) | 3.6 m | 78 |
| `crystal-1-sweep` | crystal | sweep | 113.2 | 53.578 (114.1) | 4.0 m | 0 |
| `crystal-2-breath` | crystal | breath | 114.4 | 55.900 (115.3) | 2.9 m | 67, and crystal-1 |
| `crystal-3-breath` | crystal | breath | 116.1 | 57.449 (116.3) | 3.5 m | 64, and crystal-2 |

What they show:

- **A fight in an arena.** The dragon holds the middle of a clearing (the action cut's is the
  forest's fairy ring, its candles and rune circle round it), the horde pressing at its edge 10 m
  out, the hero circling it. Every hero blow that reaches it lands (five on screen in each cut: the
  slam on the cut, 113.3, the spin on 114.3, 115.1 and 116.1), a spark and a flinch.
- **Telegraphed, then dodged.** Each zone glows from its windup beat: the breath's cone in red, the
  sweep's disc red inside a magenta rim, both drawn premultiplied so they read on the fairy ring's
  lit floor and in the crystal cut's dark. The hero stands in it through the windup (tiles 1-3)
  and is out of it before the blow (tiles 4-5); the impact frame lands on the empty zone.
- **The payoff on the impact frame.** The zone flashes white for a few frames, with the heaviest
  break's post flash and shake and a full-strength lens hit; the breath's fire bursts down the
  cone and burns the horde in it; the sweep's slash rings the dragon. In the crystal cut the
  breaths smash the two white-gold formations in the horde ring (`crystal-2`, `crystal-3`,
  tile 8).
- **Weak spots.** Even at 32-34 m the true-size hero is about 35 px tall at 1080p, a textured
  figure in the warm pool of its own light. In the crystal cut it is the one figure that doesn't
  glow, which finds it beside the gold dragon; in the default cut the dragon is green and the
  hero reads against it. A closer 30 m read better still but would not hold the density check
  under both runtimes (trailer.md, The boss fight). The sweep kills nothing: the horde is held
  back beyond its disc.

The checks (`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer
[--name crystal] --checks`; Unity's EditMode trailer tests pass too), shot 8, the action cut's row
then the crystal cut's:

```
shot  environment  on screen / target  swings/s  kills/swing (mean, peak)  hero acted  kite  liveness  blocks  lane  breaks
   8  forest             404 / 380         0.86         97.8, 534               100 %   2.5         0    63 %    94       0
   8  forest             343 / 340         0.86        102.0, 562               100 %   2.4         0    78 %    87       2
trailer checks: ok (both cuts, every shot)
```

Made with (a built player; `strips.py` lists the stills round each attack from the shot's dump,
then crops and labels them):

```
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer [--name crystal] --shot 8 --out-dir captures/fight-CUT
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal --trailer [crystal] --shot 8 --fixed-time --capture-stills captures/fight-stills-CUT \
  --stills-at $(uv run --project demo3d/tools python demo3d/evidence/trailer/fight/strips.py captures/fight-CUT CUT) --seconds 57.8
uv run --project demo3d/tools python demo3d/evidence/trailer/fight/strips.py \
  captures/fight-CUT CUT captures/fight-stills-CUT demo3d/evidence/trailer/fight
```
