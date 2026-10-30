# Overdrive: the feel of a mass kill (ps-5pd.3)

At overdrive's density the hero kills hundreds a second, and a 250-kill slam and
a 5-kill slash used to get the same camera, the same trail and a sound clipped to
the same gain. Now the overlay's `feel` object
([intensity-overdrive.json](../../../Assets/Demo3D/Choreography/Data/intensity-overdrive.json))
weights each blow by its kills, and the game's own camera, hit-stop, effects,
lights and sound follow that weight. The fields and formulas are in
[Camera and Sound, Blow weight](../../../../docs/tech-demo-3d/camera-and-sound.md#blow-weight).
Normal runs have no `feel` and don't change.

## The tuning values

| field | value | why |
|---|---|---|
| `crispKills`, `heavyKills` | 8, 60 | Overdrive's blows run from a handful (the build, flurries) to 250 (drop-2's slam). The peaks' median is 26 kills (weight 0.57) and their top tenth 51 to 78, so the scale saves its top for the real hammers. |
| `punch` | 0.05 | A weight-1 blow zooms in 7% over a sixteenth, back out over a beat: obvious on a 45 m view, and only ever a zoom-in, so it hides more of the spawn band. |
| `shake`, `shakeCap` | 0.3, 0.5 m | The old cap of 0.25 m was hit by any busy beat, so every overdrive beat shook the same. 0.5 m is a quarter of the 2 m spawn margin. |
| `hitStop`, `hitStopCatchUp` | 0.06, 0.12 s | 60 ms is the longest hold that reads as weight rather than a hitch. With the catch-up, the world is back on song time 0.18 s after the impact, before the next eighth (0.194 s), so a flurry's next blow is never delayed. |
| `trail` | 0.6 | A weight-1 blow's trail or shock is 1.6x its reach-matched size. The arc then cuts through the packed crowd around the kill ring (the spin's ring of fire below). |
| `spark`, `deathBurst` | 1.4, 0.8 | Sparks bigger, so each struck monster flashes through the crowd; bursts smaller, so 250 of them read as one wave instead of a blanket. |
| `lightKnee` | 10 | About one large death burst and a half. A cell holding dozens of bursts saturates there instead of summing to hundreds. |
| `shardCap` | 240 | See Crystal shatter below. |
| `soundGain` | base 0.22, perDoubling 0.1, max 0.8 | The default curve reaches its cap of 1 at about 45 requests (at strength 0.5), and overdrive's peaks have more than that on most beats, so they all sounded the same. Now a beat of 5 deaths plays at 0.38 and one of 60 at 0.69 (strength 0.5), with headroom under 0.8. |
| `heavyBlowKills` | 40 | A blow killing 40 or more adds the ring's heavy fire impact under its death sounds: one layered boom. |

## Frame sequences

Consecutive windowless fixed-time frames (`demo3d filmstrip`). Each tile is
labelled with its song time.

| sequence | blow | command (`demo3d filmstrip SLUG ...`) |
|---|---|---|
| [slam-neon-crystal.jpg](slam-neon-crystal.jpg), [slam-stock.jpg](slam-stock.jpg) | drop-2's slam at 136.268 s, 252 kills (weight 1) | `--at 136.15 --frames 24 --every 2` |
| [spin-neon-crystal.jpg](spin-neon-crystal.jpg), [spin-stock.jpg](spin-stock.jpg) | groove-2's spin at 105.300 s, 82 kills, then flurry blows of 14 and 2 | `--at 105.20 --frames 24 --every 2` |
| [flurry-neon-crystal.jpg](flurry-neon-crystal.jpg), [flurry-stock.jpg](flurry-stock.jpg) | a groove-2 flurry, 103.365 / 103.558 / 103.752 s, 40, 34 and 13 kills | `--at 103.30 --frames 24` |

Player args are `--intensity overdrive --palette neon --monster-style crystal` for the
neon-crystal runs, and `--intensity overdrive` (the stock look) for the rest.
The hit frames at 960 px: [spin-stock-hit.jpg](spin-stock-hit.jpg) and
[slam-neon-crystal-hit.jpg](slam-neon-crystal-hit.jpg).

What they show:

- **The slam** is one wave: a ring of warm light across the crowd round the hero
  on the impact frame, the camera punched in and shaking, decaying over the beat.
- **The spin** is an unmistakable ring of fire round the hero, through the packed
  crowd. The flurry blows after it are visibly lighter.
- **The flurry**: each eighth's slash is a wide arc across the crowd. The
  40- and 34-kill blows punch, shake and hold (weights 0.79 and 0.71); the
  13-kill one only lightly (0.23, a 14 ms hold).

## White-out

The share of near-white pixels (L* above 95) and the mean L* per frame, from the
filmstrip PNGs. Before the impact, then the impact's frame, then 0.33 s after:

| sequence | near white before / impact / after | mean L* before / impact / after |
|---|---|---|
| slam, neon + crystal | 0.02% / 0.30% / 0.20% | 25.5 / 39.0 / 30.2 |
| slam, stock | 0.0% / 2.3% / 0.36% | 21.9 / 46.6 / 33.7 |
| spin, stock | 0.0% / 6.3% / 0.36% | 30.9 / 45.2 / 33.5 |
| flurry, neon + crystal | 0.19% / 0.32% / 0.20% | 31.7 / 35.4 / 32.6 |

A mass kill lifts the frame by 9 to 25 L* for a beat, but at most 6% of it goes
white, and that 6% is the spin trail's hot core (see the hit frame), not a wash
over the crowd. Neon's darker grade and crystal's bloom caps keep its heavy hits
lower than the stock look's.

## Sound

`demo3d mix` of the final peak (161.0 to 185.8 s) from each run's `--dump-sounds`:

| run | sound events | peak before the limiter | samples over the knee (0.8) | out peak |
|---|---|---|---|---|
| the music alone | 0 | 0.95 | 2.00% | 0.916 |
| normal | 407 | 1.66 | 5.83% | 0.950 |
| overdrive | 276 | 1.58 | 5.01% | 0.950 |
| overdrive, neon + crystal (with the shatter's sounds) | 848 | 1.64 | 6.05% | 0.950 |

Nothing clips: the limiter's ceiling holds the output under 0.95, and overdrive
pushes it less than normal does, though it kills 3 times as many. The
aggregation is the reason. A slam's dozens of deaths share one instant, so they
are at most 3 layered voices at one gain, plus the boom. The test
`OverdriveMassKillsLandAsOneHeavyLayeredHit` checks the boom, the cap, and that
a 60-kill beat's death sound plays well above a 4-kill one's.

## Crystal shatter

Crystal shatter (ps-2iq) had landed, so Decision 7 applies. Unbounded, overdrive +
crystal would break 9,001 deaths into PhysX shards, about 600 sets live at the
peaks. `shardCap` 240 gives shards to deaths in view, in time order, while fewer
than 240 sets are live. A set counts from a beat before its release to its
sinking's end. The other deaths break by the Crystal shader's own shatter, with
the stock death burst and sound. That is 4,675 of 9,001 releases.

The whole song windowless (`--fixed-time --intensity overdrive --palette neon
--monster-style crystal`) reached `done at song time 198.98` with no exceptions:
15,225 effect spawns, 4,637 deaths shattered, and at most 7,475 shards live. That
is fewer than normal crystal's 12,051 at its own density
([shatter/impact](../../shatter/impact/README.md)), a load the shatter already carries.

## Without the flag

Normal runs have no `feel`. `soundGain` and `heavyBlowKills` default to the old
formula's constants (doubles, so the gains are bit-identical), and the normal
demo3d and demo2d timelines still match their pinned hashes
(`NormalTimelinesAreByteIdenticalToBeforeTheFields`).

The look set without the flag (`demo3d look feel-normal --baseline final --trial`)
matches [look/final](../../look/final/look.md) exactly: every song still and beauty
shot has the same L* p2, p50 and p98 (a difference of 0.0).
