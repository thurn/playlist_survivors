# ps-6y2.14: the world

Measured on jarvis (M5 Max), 2026-11-02. The stills are 1080p renders of the world's scripted scene, shrunk to 960 px
(the tier crops at 2x), from:

```sh
just champions-play --headless --fixed-time --scene world --tier high --capture-stills DIR \
  --stills-at 9.03,9.12,9.25,9.5,12.15,12.3,33,34.05
```

The scene (`WorldScene`) plays a scripted song through the real sim on the frames' clock: 120 BPM to 30 s with a
section change at 12 s, then no beat to 40 s, a dash at 34 s. The hero stands at the origin, a white ring at its feet
standing in for it until its edge cut lands. Floor L* below is over the whole frame less a 90 px circle round the
stand-in (sRGB to CIE L*, the archived look harness's conversion); the value ladder's ground light is L* 10-35.

## The beat wave at three phases (a downbeat at 9.0 s)

| Still | Phase | Floor L* max | p99 | p99.9 |
| --- | --- | --- | --- | --- |
| [wave-downbeat-9.030.jpg](wave-downbeat-9.030.jpg) | 0.03 s: the ring leaves the feet (under the mask) | 18.8 | 11.6 | 15.5 |
| [wave-downbeat-9.120.jpg](wave-downbeat-9.120.jpg) | 0.12 s, 2.6 m out: the lines bend and brighten | 30.0 | 12.3 | 23.3 |
| [wave-downbeat-9.250.jpg](wave-downbeat-9.250.jpg) | half a beat, 5.5 m out; inside it the floor has settled | 27.4 | 12.4 | 22.3 |
| [wave-downbeat-9.500.jpg](wave-downbeat-9.500.jpg) | a beat: the next ring leaves, this one 11 m out and fading | 24.3 | 12.2 | 18.5 |

No floor pixel in any still rises above L* 30: a downbeat's front lifts the major lines to L* 32 before the
post (`_WaveL`), the quiet lines sit at about 11-12. Floor p99 stays at or under 14.2 everywhere, inside the archived
`floor_p99_max` of 20. EditMode tests hold the rest: 11 m a beat, settled within half a beat at 80, 128 and 174 BPM,
`_WaveL` at most 35, two overlapping rings never past a downbeat's front.

## A zone change and beatless play

- [zone-12.150.jpg](zone-12.150.jpg), [zone-12.300.jpg](zone-12.300.jpg): the section change at 12 s moves the key on
  the beat at 12 s (the sim's `Zone` event), the cobalt key sweeping out behind its front from the hero over that beat
  (10.8 m and 21.6 m out), the teal key ahead of it. Floor max 26.4.
- [beatless-33.000.jpg](beatless-33.000.jpg): no beat since 30 s (beatless from 32 s): no beat rings; the floor's max
  is the quiet grid's 14.7, as at 31.5 s.
- [beatless-dash-34.050.jpg](beatless-dash-34.050.jpg): a dash beatless is on the beat, so its event ring goes out (a
  special moment: the whole grid round it bends, the camera kicks, the post flashes for under a quarter second).

## Tiers

`--tier high|medium|low` logs its settings at boot (`Quality: High: bloom from half resolution, scatter 0.80,
8 iterations; MSAA 4x; 3000 shard pieces; slash distortion, line halos and chromatic flashes on`, and so on).
[tier-high-low-9.250.jpg](tier-high-low-9.250.jpg): the same frame at High (left) and Low (right): the stand-in's edge
antialiased by MSAA and a tighter bloom at High, aliased with a coarser quarter-resolution bloom at Low. The grid
itself is identical (it antialiases its lines analytically, and nothing on it crosses the bloom threshold): the floor's
numbers at 9.25 s are the same at Low. At 34.05 s High adds its chromatic flash.

## The music path

[music-house-128-10.000.jpg](music-house-128-10.000.jpg): `--fixed-time --file rhythm_champions/plugin/fixtures/house-128.ogg`,
the plugin's beats driving the rings (the beat at 9.87 s 3 m out, the one before 14 m out). Floor max 25.0.

## Budgets

- `champions check --variants`: 245 compiled variants of 300 (227 before): GridFloor 4, Glow 2, and 12 more UberPost
  variants for the chromatic flash, which URP keeps only because a profile under Assets/ has chromatic aberration on
  (`Resources/World/PostFlash.asset`, the flash's peaks).
- EditMode tests 39 of 39; dotnet 177 of 177, including 9 zone tests. The fixture-walk session was re-recorded: its
  song's section change now moves the zone key (one more event).
