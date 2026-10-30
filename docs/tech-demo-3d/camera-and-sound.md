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
  variants are chosen by seed.
- **The crystal shatter's sounds** (epic ps-2iq) are the client's own, worked out from the
  timeline when the run starts (`ShatterSounds`), so the live scheduler, `--dump-sounds` and
  the offline mix share them. Each crystal death's release beat gets three layers: a crunching
  ice break (RPG3 IceBreak), a bright glass spray (Universal SHATTER Glass Medium) and, as the
  shards land about 0.4 s later, a quieter fragment tinkle. They are aggregated as above, at most
  3 voices per beat and layer, going to the beat's largest distinct types first. Each voice is
  pitched by its type's size, as (1.8 m / height)^0.3 within 0.8 to 1.3, with ±4% by seed. So a
  troll breaks low, a slime rings high, and a crowded beat isn't one sample. The mix resamples
  pitched events. Choices and the mix check:
  [shatter/burst](../../demo3d/evidence/shatter/burst/README.md).
- **Scheduling.** The client schedules each sound event with `PlayScheduled`
  at its DSP time once it comes within 150 ms, using the timeline's
  lookahead. A pool of 32 voices covers the demo.
