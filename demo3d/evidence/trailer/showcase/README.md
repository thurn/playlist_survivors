# Trailer: the shot list (ps-hgs.7)

The trailer's final pass: the ten shots tuned against frame sequences and the
`trailer --checks` numbers ([trailer.md, The Shot List](../../../../docs/tech-demo-3d/trailer.md#the-shot-list)).
Everything here comes from the IL2CPP player on the Mac (Apple M5 Max, Metal),
windowless at fixed time with the trailer's full look (2× supersampled
stills), built at staging `28b84049` plus this bead.

**Verdict:**

- **Every check passes:** for the default roster, `--roster forest` and
  `--roster heroic`, pinned to each of the four environments, under .NET and
  under Unity's Mono, and every bar's density holds across five trailer seeds.
- **The duel hooks:** a brute falls on each of break-2's stabs.
- **The drop lands:** the drop's flash cut opens on a crater blown through
  the desert's wall.
- **It escalates:** from the drop's crater, through the vortex's red rings
  and the elite charge's lightning, the burst's spins, slam and strikes, to
  the climax's flood.
- **The final slam clears the screen:** the slam on 99.1 kills all 1,532 in
  view, and by the cut to black the field lies dead rather than mid-fall.

## What changed

Two simulator fixes (trailer shots only; normal timelines keep their pinned
hashes):

- **Shot 2's spins land.** The spins on 82.3 and 84.3 never fired. A
  flurry's blow on the eighth before each waited for the next decision, and
  by then the spin's 0.48 s windup no longer fit. A shot's fixed entry is now
  scheduled past such a point (`TrailerTests.EveryBarBeatBlowLands`).
- **The dead fall before black.** Death clips run about 1.2 s, longer than
  the aftermath's 0.77 s, so the horde stood mid-fall at the cut to black
  (ps-hgs.6's [trailer-031.100](../playback/trailer-031.100.jpg)). The final
  slam's deaths now play faster where they must, 1.4–3.5×, so every body is
  down by three quarters of the aftermath
  (`TrailerTests.TheFinalSlamsDeadFallBeforeTheCutToBlack`).

The trailer file's tuning:

| shot | change | why |
|---|---|---|
| 2 | path turns back at (54, 46) toward (64, 20) | past the crater the hero ran out of the wave: bars 83–84 killed 1–9 a swing; turning back through the chasers they kill up to 20 and the 84.3 spin 64 |
| 5 | heading 180° → 135°, `shapeScale` 2.0 → 1.8 | bar 92 drained to 85% of its target under Mono (the test failure ps-hgs.4 found) and −7 to −14% across seeds; now −9% at worst |
| 9 | `shapeScale` 2.0 → 2.4 | the burst's slashes before the wind-up kill a little more |
| 10 | view 50 → 52 m, `densityCap` 1.8, rings on 98.1 only | the cut's slam, laser and ring together drained bar 97 to −11 to −13%; now −5 to −6% |

Tuning against every seed, not just seed 7, matters because the simulation
is chaotic, and Mono and IL2CPP differ from .NET in the last bits of floating
point ([choreography.md](../../../../docs/tech-demo-3d/choreography.md)).
Under Mono, shot 5's bar 92 sat 7 points below .NET's. A bar that passes by a
hair under .NET can fail in the player's runtime.

Tried and dropped:

- **Shot 8:** `shapeScale` 1.6 or 2.0 (2.0 drained to −16%).
- **Shot 9:** a ring on its cut (it kills 181 but leaves −21% and nothing to
  hit during the wind-up); flurries (more swings, half the kills).
- **Shot 5:** other paths (−20 to −44% in bar 91).
- **Shots 4, 8 and 10:** higher density caps alone (within a point).
- **Lens hits:** kept at `TrailerLook.MassKillWeight` 0.5. The default
  trailer has 27, a little over one a bar: the slams, rings, the spins on 82.3 and
  84.3, and the heaviest slashes.

## Checks

`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --checks`
(the same table for `--roster forest` and `--roster heroic`):

```
shot  environment  on screen / target  swings/s  kills/swing (mean, peak)  hero acted  kite  liveness
   1  crypt                - / 3           1.21          1.0, 1                 100 %     -         0
   2  desert             609 / 600         3.55         24.8, 198               100 %   2.4         0
   3  snow               710 / 700         2.42         47.8, 160               100 %   2.4         0
   4  forest             501 / 500         2.58         20.8, 111               100 %   2.3         0
   5  crypt              535 / 540         2.58         26.2, 94                100 %   2.4         0
   6  desert             389 / 400         1.29         38.5, 43                100 %   2.3         0
   7  snow               471 / 500         1.94         78.0, 164               100 %   2.5         0
   8  crypt              453 / 500         2.58         18.0, 26                100 %   2.3         0
   9  desert             605 / 600         1.94         17.3, 20                100 %   2.4         0
  10  forest            1287 / 1300        2.84        171.7, 1532              100 %   2.3         0
trailer checks: ok
```

Each bar's mean on screen against its target (the density check allows
15%), at seed 7 and the worst of seeds 3, 7, 11, 21 and 42:

| shot | bar | seed 7 | worst seed |
|---|---|---:|---:|
| 2 | 81–84 | +6.0, −3.2, −2.0, +6.7% | |
| 3 | 85–88 | −1.1, −4.7, +4.3, +6.9% | |
| 4 | 89, 90 | −9.6, +7.6% | −9.6, +10.0% |
| 5 | 91, 92 | −4.1, +1.5% | −9.3, −5.7% |
| 6 | 93 | −2.7% | |
| 7 | 94 | −5.8% | |
| 8 | 95 | −9.4% | −9.4% |
| 9 | 96 | +0.8% | |
| 10 | 97, 98 | −5.2, +2.1% | −5.8, +3.6% |

Shots 4 and 8 sit near −9%, but their spread across seeds is under 4
points (shot 5's bar 92 spread over 7), so a runtime's drift moves them
less.

Unity's EditMode run (Mono) passes `TrailerTests` with this file. Its two
other failures, `PresentationTests` (song-overdrive) and
`StructureBreaksTests`, are normal-song tests. They failed before the tuning
too, and both simulator fixes are gated on trailer fields (a fixed entry, a
final slam) that normal songs don't have.

## Files

| file | what |
|---|---|
| [first-frames.jpg](first-frames.jpg) | every shot's first frame: the duel, then the flash cuts' white (2.481, 14.868, 27.255) and the hard cuts' opening blows |
| [mid-shots.jpg](mid-shots.jpg) | a frame inside each shot: 1.2 (the duel), 5.6, 11.8, 16.4, 19.5, 21.85, 23.4, 24.95, 26.5, 29.2 |
| [cut-81.jpg](cut-81.jpg) | the drop: the third brute falling (2.42, 2.465), the white (2.481, 2.50), the crater coming out of it (2.533–2.70), the hero running into the wall (3.0) |
| [cut-89.jpg](cut-89.jpg) | bar 89: the snow vortex's end (14.80, 14.85), the flash over the forest's ring (14.868–14.98), the streams meeting (15.1, 15.4) |
| [cut-97.jpg](cut-97.jpg) | bar 97: the desert's wind-up (27.20, 27.24), the flash over the climax's opening slam (27.255–27.37), the flood (27.5, 27.8) |
| [final-slam.jpg](final-slam.jpg) | the climax before the slam (30.20, 30.335), the slam's frame and its lens hit (30.352, 30.367), the screen burning (30.40–30.80), the dead field (30.95, 31.10), black (31.13, 31.50) |
| [spot-check.jpg](spot-check.jpg) | the spot-check grid: a row per flag combination at 4.0 (desert), 11.0 (snow), 16.0 (forest) and 29.5 (the climax, before the final slam) |
| [drop-crater.jpg](drop-crater.jpg), [climax.jpg](climax.jpg), [aftermath.jpg](aftermath.jpg) | 2.70, 29.20 and 31.10 at 960 px |
| [showcase.py](showcase.py) | the sheets, the grid and the stills |

## The spot-check grid

Its rows: the default, then `--palette` snow, desert, lava, neon, blossom
and crypt, then `--monster-style` crystal, mix and painted, then neon +
crystal, then `--roster forest` and `--roster heroic`, then
`--classic-monster-colors` and `--signature-monster-colors`.

Every combination plays the same shots without errors, each in its own look.
What to note:

- **Styles keep each environment's exposure.** Under crystal, mix and
  painted, the desert and snow keep their own exposure (ps-hgs.3's style post
  fix).
- **The classic colours are the default.** `--classic-monster-colors` is now
  a no-op, so its row matches the default's. `--signature-monster-colors`
  brings back each type's signature colour.
- **The roster sets** swap the horde and keep every shot's counts and paths.
- **Bright palettes wash out the bright shots.** The snow, desert and blossom
  palettes over the trailer's desert and snow shots (4.0, 11.0) push the
  ground close to white, and the horde reads mostly by its shadows. The
  palette applies its ground tint over each environment's own, as in a
  normal run, so this is the palettes' finding, not the trailer's. Lava, neon
  and crypt read well everywhere.

## The runs

Still names are trailer seconds, rounded up to the next 60 fps frame. Each
set is one windowless run:

```
P="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
C=$PWD/captures/trailer/showcase
"$P" -batchmode -force-metal -logFile $C/first/player.log --trailer --fixed-time --capture-stills $C/first --stills-format png \
  --stills-at 0,2.481,8.674,14.868,17.965,21.062,22.610,24.158,25.707,27.255 --seconds 27.4
# mid: --stills-at 1.2,5.6,11.8,16.4,19.5,21.85,23.4,24.95,26.5,29.2
# cut81: 2.42,2.465,2.481,2.50,2.533,2.60,2.70,3.00
# cut89: 14.80,14.85,14.868,14.884,14.917,14.98,15.10,15.40
# cut97: 27.20,27.24,27.255,27.27,27.30,27.37,27.50,27.80
# final: 30.20,30.335,30.352,30.367,30.40,30.50,30.65,30.80,30.95,31.10,31.13,31.50
# grid/NAME: --stills-at 4.0,11.0,16.0,29.5 plus the row's flags (the roster rows need
#   demo3d setup --roster NAME and demo3d unity BakeRoster -roster NAME first)
S="uv run --project demo3d/tools python demo3d/evidence/trailer/showcase/showcase.py"
$S sheet first-frames $C/first      # and mid-shots, cut-81, cut-89, cut-97, final-slam
$S grid spot-check $C/grid/default $C/grid/palette-snow ...   # the rows in the order above
$S still climax $C/mid/still-029.200.png
```

The seed sweep copies the trailer file with another `seed` and `name`
(`trailer-zzN.json`, not committed) and runs `trailer --checks --name zzN`.
