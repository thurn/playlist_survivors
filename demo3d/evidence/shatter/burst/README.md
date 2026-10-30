# Crystal shatter: the burst, the light and the sound (ps-2iq.3)

What a crystal death adds on its release beat to the shards themselves (epic ps-2iq; the shards in
[../motion](../motion/README.md), the design in
[effects.md](../../../../docs/tech-demo-3d/effects.md#the-look),
[camera-and-sound.md](../../../../docs/tech-demo-3d/camera-and-sound.md#sound) and
[crowd.md](../../../../docs/tech-demo-3d/crowd.md#monster-styles)). The strips were rendered by the
Mac's batch-mode IL2CPP player in fixed time on 2026-10-30, using every 2nd 1/60 s frame and
**with** the effect library.

| file | what | command (`demo3d filmstrip ...`) |
|---|---|---|
| [skeleton.jpg](skeleton.jpg) | the lone skeleton released at 41.617: the glints and the flash on the pop, then the dust settling, 1.2 s | `skeleton --at 41.55 --frames 36 --every 2 --columns 6 --scale 0.8 --crop 760,0,400,300 --player-args "--monster-style crystal --envelope 24 --view-width 12" --evidence shatter/burst` |
| [crowd.jpg](crowd.jpg) | a crowded beat in the song's last bars at a 48 cap: skeletons released on 197.25 and goblins on 197.483, under the hero's final slam | `crowd --at 196.95 --frames 32 --every 2 --columns 4 --scale 1.25 --fps 30 --crop 0,140,1920,940 --player-args "--monster-style crystal --envelope 48 --view-width 12" --evidence shatter/burst` |

## What plays

Everything is worked out from the timeline when the run starts (`CrystalShatter.Releases`, at
`ClipSchedule.FadeStart`), so a seek and the sound lookahead see it too.

- **The burst** (`crystal-burst`, the selection's `client.crystalShatter`) replaces the death
  burst for crystal types only. It spawns at the held pose's centre and is scaled by the
  monster's height. It is built in (no vendor prefab) from two twinkling spark layers:
  - 48 fine stretched glints at 3 to 9 m/s;
  - 40 grains of crystal dust that burst out, stop (limit 1.5 m/s) and drift down.

  Each spawn is tinted by the type's gem colour, so the ramp runs from the gem squared through
  the gem to a white core (gain 0.12, so only the hottest pixels go white). It has no glow quad.
- **The light**: a 0.3 s flash in the gem colour, intensity 3.5 and range 4.5 m. It sits 1.2 m
  above the burst (the cue's new `height`), so it washes the floor and the neighbours rather than
  burning a hot spot under a body lying on the floor.
- **The sound** (`ShatterSounds`), three layers per voice:

  | layer | clips | level | when |
  |---|---|---|---|
  | shatter-body | RPG3_IceMagic2_IceBreak01/02/03 (RPG Magic Sound Effects Pack 3): a crunch with half its energy under 300 Hz | 0.55 | the release |
  | shatter-glass | SHATTER_Glass_Medium_02/04/05 (Universal Sound FX): a bright spray, centroid 7 to 9 kHz | 0.45 | the release |
  | shatter-tinkle | SHATTER_Glass_Fragment_RR1/RR4/RR7 (Universal Sound FX): short fragments | 0.22 | about 0.38 s later (the shards landing), scaled by size |

  Per beat and layer there are at most 3 voices. They go to the beat's largest distinct types
  first, so a troll, a skeleton and a slime dying together are each heard. Each voice is pitched
  by its type's size, (1.8 m / height)^0.3 within 0.8 to 1.3, with ±4% by seed (the tinkle
  another ±5%). The gain grows with log2 of the deaths on the beat, as the choreography's own
  sounds do. The creature's death cry at the blow is unchanged: it belongs to the blow, and the
  glass belongs to the release a beat or two later.

## Tuning, and why

- **The light burned a hole.** At first the cue sat at the burst, and most held poses lie on the
  floor, so the pose centre is a few centimetres up. At intensity 5 and range 5 m that made a
  white-cyan blob on the floor under the body for a third of a second, which hid the shards'
  lower half. It read as the glow quad the bead rules out (`--light-pool 0` confirmed it was the
  light). Halving the intensity didn't help, since the blob comes from the distance. Raising the
  light 1.2 m turned it into an even wash. 7 washed the whole view pale, and 3.5 at 4.5 m reads as
  a flash without bleaching the frame.
- **The glints didn't read.** At 0.03 m and HDR 4 they were thin hairs, and the slow dust
  clumped into an additive smudge. Bigger, brighter glints (0.045 m, HDR 8, longer streaks) and
  less, faster, wider-spread dust read as sparkle. A heat gain of 0.6 turned every glint white;
  0.12 keeps the gem's colour on all but the cores.
- **Crowded beats.** Lights within 1.5 m cluster with their intensities summed (effects.md,
  Effect lights). So the seven goblins on 197.483, under the hero's final slam, tint the view
  green for about 0.15 s. That is about as strong as the orange wash the stock death bursts and
  the slam already make on that beat under other styles (`--monster-style painted`, the same
  frames), only in the gem's hue. Scale is the next epic's.

## The mix

`demo3d mix` now resamples pitched events and reports the peak before its soft limiter. The sound
lists are from `--dump-sounds`, and every shatter cue has a clip.

| run | peak before the limiter | samples over the knee (0.8) | over 1.0 | out peak |
|---|---|---|---|---|
| the crowded beat above (196.95 to 198.0, 48 cap) | 1.18 | 0.09% | | 0.948 |
| the whole song at the 48 cap, timeline sounds only / with the shatter | 1.48 / 1.47 | 3.10% / 3.15% | 0.11% / 0.12% | |
| the whole song at its own density, timeline sounds only / with the shatter | 1.71 / 1.80 | 4.29% / 4.74% | 0.51% / 0.67% | |

Nothing clips: the limiter's ceiling holds the output under 0.95, and the shatter barely moves
how often it works. The first levels (1.0 / 0.8 / 0.4) pushed the full song's peak to 1.96 and
doubled the share over 1.0, so the layers were brought down to 0.55 / 0.45 / 0.22. At the song's
own density, 3,165 of the 5,699 sound events are the shatter's.

## Checks

- Only crystal types change: other styles have no shatter, so their death bursts and sounds are
  untouched (EditMode `CrystalDeathsSwapTheirDeathBurstForTheShatterOnTheRelease`; the release
  time is `ADeathsFadeStartsABeatAfterItsClipComesToRest`; the aggregation is in
  `ShatterSoundTests`).
- The full song windowless at its own density under `--monster-style crystal` ran without errors:
  2,336 deaths and 9,660 effect spawns, with at most 12,051 shards live.
- The judgement with the music in a window (how loud the glass should sit, whether the flash is
  too much at the climax) is the operator's (plan.md, Pending on the operator, in the showcase
  bead).
