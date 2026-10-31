# Trailer: the choreography pass (ps-7mx.1)

The trailer's horde and hero reworked toward the normal `just demo3d` run's
feel ([trailer.md, Kiting and formations, "Outcome (ps-7mx.1)"](../../../../docs/tech-demo-3d/trailer.md#kiting-and-formations)):

- **Like monsters in blocks.** Formations deal their monsters by type blocks
  of ground, so waves walk in as coloured masses, not confetti.
- **Blows on the major beats.** The hero's blows land on the strong beats
  (and accents, cuts and the final slam), at a natural clip rate, with the
  hero running between them, not flailing on every beat and eighth.
- **Never surrounded.** Monsters keep out of the hero's escape lane behind
  them and out of the ground they are about to run over, so the horde closes
  from the front and flanks and trails behind in a wake.

This is validated on the current ten-shot list; the 60 s re-cut (ps-7mx.3)
builds its new shots on it.

## Checks

`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --checks`
(the same table for `--roster forest` and `--roster heroic`):

```
shot  environment  on screen / target  swings/s  kills/swing (mean, peak)  hero acted  kite  liveness  blocks  lane
   1  crypt                - / 3           1.21          1.0, 1                 100 %     -         0       -   103
   2  desert             608 / 600         0.97         98.3, 256               100 %   2.4         0    83 %    79
   3  snow               710 / 700         1.13         72.7, 184               100 %   2.4         0    76 %   140
   4  forest             522 / 500         1.29         44.2, 119               100 %   2.3         0    75 %   105
   5  crypt              404 / 420         1.29         42.0, 63                100 %   2.4         0    79 %    83
   6  desert             380 / 400         1.29         44.0, 81                100 %   2.3         0    80 %   101
   7  snow               408 / 420         1.29         94.0, 159               100 %   2.5         0    69 %    98
   8  crypt              490 / 500         1.29         23.0, 24                100 %   2.3         0    82 %    80
   9  desert             619 / 600         0.65         43.0, 43                100 %   2.4         0    82 %    83
  10  forest            1247 / 1300        1.03        467.8, 1560              100 %   2.3         0    82 %   110
trailer checks: ok
```

The same three checks run on the trailer before this bead (its code and
trailer file, with only the new checks added) show what they catch:

```
shot  swings/s  blocks  lane
   2      3.55    24 %    41
   3      2.42    25 %   133
   4      2.58    28 %    46
   5      2.58    38 %    12
   6      1.29    28 %    37
   7      1.94    26 %    10
   8      2.58    26 %    13
   9      1.94    25 %    44
  10      2.84    26 %   111
trailer checks: 123 problems
```

- **blocks:** the mean share of a visible monster's neighbours within 3 m
  that share its type. A random mix of these shots scores about 25%; the
  check wants 60%.
- **lane:** the narrowest the widest monster-free gap round the hero gets
  within 6 m, over the beats, in degrees. The check wants 60°.
- **swings/s:** the hero's blows a second. On the strong beats at 155 BPM a
  blow every two beats is 1.29; spins and slams take the place of the blow
  before them, and one-bar shots have two.

Robustness (all at the same commit):

- Seeds 3, 5, 7, 11, 13 and 21 pass every check; seeds 9 and 42 miss one
  bar's density (shot 5 or 6, and the climax on seed 9) by a few points.
- Pinned to each environment, crypt, desert, forest, blossom, lava and neon
  pass; snow misses the lane once (57° in shot 2).
- The choreography tests pass (`dotnet test demo3d/dotnet/Demo3D.Choreography.Tests`
  and demo2d's), the normal timelines keep their pinned hashes.

## Top-down sheets

`topdown.py` draws a shot's timeline from above, monsters coloured by type
(white skeleton, green goblin, cyan slime, purple bat, orange mushroom, red
demon, brown Big Ork, blue Troll), the hero yellow with the open-lane
check's 6 m circle, turned by the shot's heading so up is the camera's
forward. Each frame is a beat; "blow" marks the hero's blows. Every pair is
the same shot and beats before (`-before`) and after this bead:

| files | shot | what to look for |
|---|---|---|
| `charge-shot2-before.jpg`, `charge-shot2.jpg` | 2, desert charge, 81.1–84.3 | before: a confetti wave the hero wades into, closing round them; after: blocks of skeletons, demons and goblins, the crater kept open behind the hero |
| `converge-shot4-before.jpg`, `converge-shot4.jpg` | 4, forest, four streams, 89.1–90.4 | streams of one colour each pour in from ahead and the flanks; the hero's pocket stays open behind them |
| `vortex-shot6-before.jpg`, `vortex-shot6.jpg` | 6, desert vortex, bar 93 | before: the hero circling inside a disk of monsters; after: the horde starts in a ring outside the loop and spirals in behind |
| `climax-shot10-before.jpg`, `climax-shot10.jpg` | 10, forest climax, 97.1–98.4 | a patchwork flood of blocks, Big Orks and Trolls in their own masses, the hero's lane open |

## Player stills

From the IL2CPP player on the Mac (Metal), windowless at fixed time with the
trailer's full look, built from this bead's tree. `stills.py` makes the
sheets; the command is in its docstring.

- **`mid-shots.jpg`:** a still in every horde shot (trailer seconds 3.3–29.8).
  Coloured masses are visible: red demon blocks against skeleton and goblin
  fields, the elite charge's Big Orks and Trolls in their own masses (19.5),
  the climax's patchwork round the laser (28.6, 29.8). Under the default
  palette skeletons, goblins and bats share tan tones, so the demons' red
  blocks read strongest; `--signature-monster-colors` separates the others
  (a Manual QA row).
- **`blow-89.3.jpg`:** shot 4's cleave on 89.3 (15.642 s), every other frame
  from 15.30 s, cropped round the hero. The sword winds up over about a third
  of a second, the cleave's ring lands on the impact frame, the
  follow-through plays out, and the hero runs on. Nothing reads as
  flailing, and the monsters stay off the hero's path.
