# 3D Tech Demo: Camera and Sound

## Camera

The camera is a game camera, so the measured counts apply to a playable
view. Its framing has to fit hundreds of monsters at once, which is the
premise of the genre.

- **Geometry.** A perspective view at 55° pitch with a 30° vertical field of
  view, following the hero. Its height is set by the **view width**: the
  width of ground visible across the middle of the screen.
- **Follow.** A critically damped spring with a 0.4 s time constant.
- **Zoom.** Changes ease over 2 bars at section boundaries.
- **Who computes what.** The choreographer runs the follow and zoom and stores
  them as the timeline's framing track (see
  [The choreographer](choreography.md#the-choreographer)). The client adds punch and shake
  on top.
- **Punch.** A 2% zoom-in over a sixteenth note on accents, released over a
  beat.
- **Shake** grows sublinearly with the number of hits, so 100 hits on one beat
  never shake the screen 100 times harder. It offsets the camera in world
  metres:

```
amplitude = min(0.25, 0.08 × strongestImpact + 0.02 × log2(1 + hitsThisBeat))
```

Here `strongestImpact` is the beat's largest action strength, from 0 to 1.

### Blow weight

An intensity overlay's `feel` object (overdrive's, epic ps-5pd) makes the
game's own feel scale with how many each of the hero's blows kills, so a
60-kill slam lands like a hammer and a 5-kill slash stays crisp. A normal run
has no `feel` and is unchanged. `BlowFeel` gives each blow a weight from its
kills, the deaths on its impact:

```
weight = clamp01((ln(1 + kills) − ln(1 + crispKills)) / (ln(1 + heavyKills) − ln(1 + crispKills)))
```

| Field | Overdrive | What it does |
|---|---|---|
| `crispKills`, `heavyKills` | 8, 60 | Weight 0 at or below the first, 1 at or above the second. |
| `punch` | 0.05 | Every weighted blow punches like an accent, zooming in by 2% + punch × weight. A zoom-in only hides more of the spawn band. |
| `shake`, `shakeCap` | 0.3, 0.5 m | The beat's heaviest blow adds shake × weight to the shake above, under this cap instead of 0.25 m, well inside the 2 m spawn margin. |
| `hitStop`, `hitStopCatchUp` | 0.06, 0.12 s | The hit-stop: from a blow's impact, the horde and the hero hold for hitStop × weight, then catch up over the next hitStopCatchUp (at most 2x speed). The impact lands on time and the world is back on song time 0.18 s later, before the next eighth at 155 BPM. The music, camera, effects and lights keep song time, so the punch, sparks and light play over the frozen blow. |
| `trail` | 0.6 | The hero's trails and shocks, and the hit sparks of its blows, grow by 1 + trail × weight. |
| `spark`, `deathBurst` | 1.4, 0.8 | Hit sparks' and death bursts' size factors, so sparks read through a packed crowd and hundreds of bursts don't mush together. |
| `lightKnee` | 10 | Each effect light's intensity saturates toward this knee as knee × (1 − e^(−I / knee)), so dozens of bursts summed in one cell are one wave of light, not a white-out. |
| `shardCap` | 240 | The crystal shatter's cap on live shard sets: deaths in view take shards in time order while fewer are live, and the rest break by the Crystal shader's own shatter with the stock burst and sound. |

The tuning and its frame sequences are in
[overdrive/feel](../../demo3d/evidence/overdrive/feel/README.md).

The camera's distance and height follow from the view width at 16:9:

```
hfov     = 2 × atan(tan(15°) × 16 / 9)          ≈ 50.9°
distance = viewWidth / (2 × tan(hfov / 2))      ≈ viewWidth / 0.953
height   = distance × sin(55°)
```

How the three measured view widths frame the horde (approximate; TD4 measures
monster heights):

```
view width  camera height  visible ground  at 1-2 per m²   monster height
30 m        26 m           ~690 m²         ~700-1,400      ~65-95 px
40 m        34 m           ~1,220 m²       ~1,200-2,400    ~50-70 px
55 m        47 m           ~2,300 m²       ~2,300-4,600    ~35-50 px
```

Monsters off screen cost nothing to draw, except that their shadows reach in.

## Sound

Sound is not part of the measured question, but the demo needs it to feel
right. It must stay as punchy at 100 hits a beat as at one.

- **Music.** The supplied *Action* MP3 is converted to streamed Ogg Vorbis
  and started with `PlayScheduled`. Setup accepts the local source path
  from Decision 13 (or an explicit replacement path), converts it into the
  gitignored baked folder, and copies the tracked `songs/action/` song map
  there. The map and playback use the same time origin; TD0 fixes the
  conversion command and checks that it preserves beat alignment, including
  any encoder delay. Neither the source MP3 nor any conversion or crop of it
  is committed: `.gitignore` excludes audio under `songs/action/` and
  `public/audio/action*`, and the vendor guard fails on it (see
  [The vendor guard](vendor-assets.md#the-vendor-guard)). TD0 records the NCS
  usage terms and required credits for the demo build and shared capture;
  the previous recording's public-domain status does not apply.
- **Effects** come from the vendor sound packs: Magic Spells SFX Bundle, RPG
  Magic Sound Effects Pack 3 and Universal Sound FX.
- **Aggregation happens in the choreography layer,** so the timeline's
  `SoundEvent`s are final: the live client plays them, and the video's offline
  mix renders the same list. Per beat and per sound category, at most 3
  voices play. Their gain grows with log2 of the number of hits, and
  variants are chosen by seed. An intensity may lower the gain's curve and layer the ring's
  heavy impact under a mass kill (choreography.md, Tuning fields: `soundGain`,
  `heavyBlowKills`).
- **The crystal shatter's sounds** (epic ps-2iq) are the client's own, worked out from the
  timeline when the run starts (`ShatterSounds`), so the live scheduler, `--dump-sounds` and
  the offline mix share them. Each crystal death's killing blow, where its shards break, gets
  three layers on top of the creature's death cry at the same blow: a crunching ice break (RPG3
  IceBreak), a bright glass spray (Universal SHATTER Glass Medium) and, as the shards land about
  0.4 s later, a quieter fragment tinkle. They are aggregated as above, at most
  3 voices per beat and layer, going to the beat's largest distinct types first. Each voice is
  pitched by its type's size, as (1.8 m / height)^0.3 within 0.8 to 1.3, with ±4% by seed. So a
  troll breaks low, a slime rings high, and a crowded beat isn't one sample. The mix resamples
  pitched events, and with the glass and the cry landing together the song's peak before the
  limiter is 1.78 against 1.71 without the shatter, so the levels stand. Choices and the mix check:
  [shatter/impact](../../demo3d/evidence/shatter/impact/README.md).
- **Scheduling.** The client schedules each sound event with `PlayScheduled`
  at its DSP time once it comes within 150 ms, using the timeline's
  lookahead. A pool of 32 voices covers the demo.
