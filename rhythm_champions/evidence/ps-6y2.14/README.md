# ps-6y2.14: the world

Measured on jarvis (M5 Max), 2026-11-02, in the palette's zones (Content/palette.json, ps-6y2.13). The stills are
1080p renders of the world's scripted scene, shrunk to 960 px (the tier crops at 2x), from:

```sh
just champions-tool stills --scene world
just champions-tool look captures/stills --impact '*34.050*'
just champions-play --headless --fixed-time --capture-stills DIR --stills-scenes world --tier low
```

The scene (`WorldScene`, one of `champions stills`' scenes) plays a scripted song through the real sim on its own
clock: 120 BPM to 30 s with a section change at 12 s, then no beat to 40 s, a dash at 34 s. The hero stands at the
origin, a white ring at its feet standing in for it until its edge cut lands (an effect to the type-ID pass). "Floor
max" below is over the full-size still less a 90 px circle round the stand-in (sRGB to CIE L*); the value ladder's
ground light is L* 10-35. [look/metrics.md](look/metrics.md) is `champions look` on the same stills.

## The beat wave at three phases (a downbeat at 9.0 s)

| Still | Phase | Floor max | `floor_p99` (look, 960 px) |
| --- | --- | --- | --- |
| [wave-downbeat-9.030.jpg](wave-downbeat-9.030.jpg) | 0.03 s: the ring leaves the feet | 22.5 | 15.2 |
| [wave-downbeat-9.120.jpg](wave-downbeat-9.120.jpg) | 0.12 s, 2.6 m out: the lines bend and brighten | 31.6 | 16.7 |
| [wave-downbeat-9.250.jpg](wave-downbeat-9.250.jpg) | half a beat, 5.5 m out; inside it the floor has settled | 29.6 | 16.7 |
| [wave-downbeat-9.500.jpg](wave-downbeat-9.500.jpg) | a beat: the next ring leaves, this one 11 m out and fading | 25.8 | 16.1 |

No floor pixel in any still rises above L* 32: at rest the lines are the zone's `line` colour (L* 10-20), under a
downbeat's front its `pulse` colour (at most L* 35). `floor_p99` stays at or under 19.3 everywhere, inside the
rule's 20. EditMode tests hold the rest: 11 m a beat, settled within half a beat at 80, 128 and 174 BPM, every zone's
pulse at most L* 35, two overlapping rings never past a downbeat's front.

## A zone change and beatless play

- [zone-12.150.jpg](zone-12.150.jpg), [zone-12.300.jpg](zone-12.300.jpg): the section change at 12 s moves the zone on
  the beat at 12 s (the sim's `Zone` event), teal sweeping out from the hero behind its front (10.8 m and 21.6 m out)
  over cobalt. Floor max 27.8.
- [beatless-33.000.jpg](beatless-33.000.jpg): no beat since 30 s (beatless from 32 s): no beat rings; the floor's max
  is the quiet teal grid's 18.8, as at 31.5 s.
- [beatless-dash-34.050.jpg](beatless-dash-34.050.jpg): a dash beatless is on the beat, so its event ring goes out (a
  special moment: the grid round it bends, the camera kicks, the post flashes for under a quarter second).

## Tiers

`--tier high|medium|low` logs its settings at boot (`Quality: High: bloom from half resolution, scatter 0.80,
8 iterations; MSAA 4x; 3000 shard pieces; slash distortion, line halos and chromatic flashes on`, and so on).
[tier-high-low-9.250.jpg](tier-high-low-9.250.jpg): the same frame at High (left) and Low (right): the stand-in's edge
antialiased by MSAA and a tighter bloom at High, aliased with a coarser quarter-resolution bloom at Low. The grid
itself is the same (it antialiases its lines analytically, and nothing on it crosses the bloom threshold): the floor's
numbers at 9.25 s are equal at both tiers. At 34.05 s High adds its chromatic flash.

## The music path

[music-house-128-10.000.jpg](music-house-128-10.000.jpg): `--fixed-time --file rhythm_champions/plugin/fixtures/house-128.ogg`,
the plugin's beats driving the rings (the beat at 9.87 s 3 m out, the one before 14 m out). Floor max 29.7.

## Look metrics: what breaks, and why it's recorded rather than fixed

`champions look` passes every rule it can judge on these stills (`floor_p50/p90/p99`, black share, clipping, white,
families, key60, palette conformance) but two:

- `background_light_max` (75-80% against 15%): the world scene has nothing in it but the grid and a stand-in, so
  nearly all its light is background by construction. The rule asks that the world never drowns the swarm; it
  judges frames with a swarm (the assembly's stills), where the same grid is a small share.
- `halo_impact_min` on the dash's impact frame (3.3 against 15): the halo is measured round emitters, and the only
  emitter is the stand-in ring. Monsters' lines glowing in a special moment are the crowd's to show.

## Budgets

- `champions check --variants`: 247 compiled variants of 300 (229 on staging before): GridFloor 4, Glow 2, and 12 more UberPost
  variants for the chromatic flash, which URP keeps only because a profile under Assets/ has chromatic aberration on
  (`Resources/World/PostFlash.asset`, the flash's peaks).
- EditMode tests 54 of 54 (with the edge crowd's and the palette's, which landed alongside: the edge crowd's
  EdgeLines and EdgeBody now read the world's beat wave, a flare and a bob, and a test compiles every shader that
  includes World.hlsl); dotnet 177 of 177, including 9 zone tests. The fixture-walk session was re-recorded: its
  song's section change now moves the zone key (one more event).
