# Overdrive: the showcase (ps-5pd.4)

The trailer's headline combination, `--intensity overdrive --palette neon --monster-style crystal`,
after the tuning ([../tuning/](../tuning/README.md)), the feel ([../feel/](../feel/README.md)) and
this bead's last pass, plus a spot-check of other looks and rosters. All windowless, on the Mac's
IL2CPP player. The windowed recording with the music is in plan.md's Pending on the operator.

## The last pass: a hero you can see

At overdrive's 45 m views the stock-sized hero is about 4% of the frame's width. In the frame
sequences the blows read (the slam's wave of light, the slash's arc) but the hero who makes them
was a dark speck in the cleared disc, findable only on an impact frame. For a power fantasy that
was the biggest thing missing.

The overlay's `feel` now has `heroScale` 1.7: the hero is drawn 1.7 times its size, and its walk's
stride stretches with it, so its feet still plant (camera-and-sound.md, Blow weight). It is
client-only: the sim's reach and radius, and so the timeline, don't change, and a normal run has
no `feel`. [hero-scale.jpg](hero-scale.jpg): centre crops, 1 on the left and 1.7 on the right, at
the final peak (170.52 s, between slashes) and drop-1 (49.98 s, after the slam).

## The numbers against Decision 3

The CLI's tables ([report-overdrive.txt](report-overdrive.txt), with every timeline check's
verdict; .NET, seed 1):

```
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- song --intensity overdrive --checks
```

They match the tuning bead's report line for line: the feel bead and this one only touch the
client. The beat is 2.58 a second.

| section | swings/s | kills/swing mean / peak | spell share | on screen / target (target) | Decision 3 |
|---|---|---|---|---|---|
| drop-1 | 3.53 | 11.6 / 125 | 0.06 | 1.11 (548) | every beat, with flurries; within ~10% |
| groove-2 | 3.39 | 13.6 / 113 | 0.03 | 0.90 (638) | every beat, with flurries |
| build | 2.58 | 8.2 / 21 | 0.02 | 1.07 (638) | every beat; the travelling hero tops out near 28 deaths/s |
| drop-2 | 2.34 | 28.7 / 209 | 0.04 | 1.06 (1020) | 91% of beats struck; 40+ on its big blows |
| final-peak | 2.50 | 34.0 / 129 | 0.02 | 0.96 (1339) | 97% of beats; 1,291 on screen, 3x normal's 427 |

- **Every beat:** met. **One blow** (fodder has 1 hit point): met. **The hero's kills:** spells
  take 2% to 6%. **3x on screen within ~10%:** met everywhere but drop-1's 11% overfill.
- **40+ kills a swing:** the peaks' means are 29 and 34, and their biggest blows 129 to 252
  (the tuning README's "What limits it": a higher mean emptied the screen round the hero, so the
  look won). Every timeline check passes.

The biggest blows the sequences below are built round (deaths on the impact, from the dump):

| section | blow | kills |
|---|---|---|
| drop-1 | slam at 49.558 s | 203 |
| drop-2 | slam at 136.268 s, slash at 141.300 s, slam at 148.655 s | 252, 87, 91 |
| final-peak | slash at 170.332 s, slam at 173.429 s | 129, 160 |

## Neon + crystal

[neon-crystal-stills.jpg](neon-crystal-stills.jpg): eight stills across drop-1, groove-2, drop-2
and the final peak, each labelled.

Frame sequences (`demo3d filmstrip SLUG --at T --frames 24 --every N --columns 6 --player-args
"--intensity overdrive --palette neon --monster-style crystal" --evidence overdrive/showcase`),
each tile labelled with its song time:

| sequence | --at, --every | what it shows |
|---|---|---|
| [drop-1-slam.jpg](drop-1-slam.jpg) | 49.45, 2 | drop-1's 203-kill slam: a ring of warm light across the crowd on the impact, the punch in, then the crowd pouring back into the disc over the beat |
| [drop-2-flurry.jpg](drop-2-flurry.jpg) | 138.10, 3 | 1.2 s of drop-2: a blow a beat, each slash's arc (the blue sweep) cutting across the crowd, the horde closing in between |
| [final-peak-slash.jpg](final-peak-slash.jpg) | 170.22, 2 | the final peak's 129-kill slash and the next beat's: wide arcs through a packed screen, the hero (now drawn larger) in the middle |
| [final-peak-slam.jpg](final-peak-slam.jpg) | 173.32, 2 | the final peak's 160-kill slam: the impact's burst, the shock ring expanding, sparks through the crowd, the frame settling over the beat |

Near-white pixels (L* above 95) and mean L* per frame, from the full-size PNGs, before the impact
/ the impact's peak / 0.3 s later:

| sequence | near white | mean L* |
|---|---|---|
| drop-1 slam | 0.0% / 0.1% / 0.1% | 25 / 38 / 31 |
| drop-2 run of blows | 0.0% throughout | 23 to 29 |
| final-peak slash | 0.0% / 0.1% / 0.0% | 29 / 37 / 32 |
| final-peak slam | 0.0% / 1.0% / 0.4% | 29 / 44 / 37 |

Big blows lift the frame 8 to 15 L* for a beat with no white-out. The slam reads as the hammer;
the slashes as wide, fast cuts.

## Other combinations

[spot-check-drop-2.jpg](spot-check-drop-2.jpg) (145 s) and
[spot-check-final-peak.jpg](spot-check-final-peak.jpg) (175 s, a ring sweep): twelve
combinations under overdrive, each one windowless player run from 140 s:

stock; lava + fire; snow + hologram; crypt + void; desert + chrome; blossom + painted;
neon + spectrum; the style mix; `--roster forest`; forest + neon; `--roster heroic`; heroic + lava.

Every run finished without exceptions and drew both stills. Findings:

- **They all carry overdrive:** the packed screen, the cleared disc round the hero, the ring
  sweep. The rosters play the same overdrive timeline (the overlay sets its counts and views
  outright), with their own monsters.
- **Forest and heroic take only the baseline style.** `--roster forest --monster-style crystal`
  refuses at start-up ("draws through the stock crowd"), as at normal intensity, so the spot-check
  pairs forest with neon instead.
- **Snow is near white** (51% to 54% of the frame above L* 95 at 175 s with hologram) at normal
  intensity too: the snow environment's own look, already pending the operator.
- **Desert + chrome flares** during the ring's sweep: 9.7% near white at 175 s, against 2.2% at
  normal. The ring's light falls on a cleared, sun-bleached floor that normal's crowd covers. It
  is one combination and one moment; the operator row asks whether another look films better.

## The full song

Under overdrive, windowless, the whole song plays to `done at song time 198.99` with no
exceptions and no effect spawns rejected. Its cost is in
[performance.md, Overdrive's offline cost](../../../../docs/tech-demo-3d/performance.md#overdrives-offline-cost).
