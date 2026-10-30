# Monster style: spectrum (ps-a54.3)

> **Tuned since (ps-a54.4).** The stills, look.md, look.json and contact sheet here are now
> the tuned set: the night's tonemapper and exposure, and the horde graded at gain 1, peak 0.3
> ([all/README.md](../all/README.md) has the values, the numbers and why). The notes below
> describe the untuned set they were written for.

`--monster-style spectrum`: stylelab's "Spectrum" (Synth.shader) treatment, drawn through the bone-texture
crowd's indirect draws on the unwelded style mesh
([crowd.md, Monster styles](../../../../docs/tech-demo-3d/crowd.md#monster-styles)).
Black lacquer bodies with a thin-film rainbow that moves with view angle, height and distance
from the hero, plus a neon wire on every triangle edge of the style mesh. Equalizer bars climb
the body on each beat, and the bass swells the mesh along its normals. Death bursts into
rainbow shards. The swell and the shards are in `SL_DISPLACE`, so shadows, depth and motion
vectors follow them.

It uses the lab's palette per type and the lab's post look as PostFeel's base: ACES, exposure +0.25, contrast +12, saturation +15, bloom 1.2, chromatic aberration 0.12.
ps-a54.4 has since tuned it.

How the set was made, on the Mac, windowless, IL2CPP player:

```
uv run --project demo3d/tools demo3d look ../styles/spectrum --baseline final --player-args "--monster-style spectrum"
```

- [look.md](look.md) and [look.json](look.json) hold the look harness's numbers against
  `look/final`.
- [contact-sheet.jpg](contact-sheet.jpg) shows `final` on the left and spectrum on the right.

The same command without the flag (slug `ps-a54.3-noflag`, `--trial`, not committed)
matched `look/final` within 0.1 L* on p2, p50 and p98 in all 11 stills, with contrast and
chroma within 0.3. So the default path is unchanged. Every one of the 28 player logs says
`DemoMain: --monster-style spectrum's post look on the night volume`, and none has an exception.

## What the numbers say

- **The horde reads strongly and is the most colourful set so far.** The monsters' dL* is
  58 to 64 in the beauty shots, and their C*ab is 42 to 49, against final's 54 to 58.
- **Near-black is 22 to 42% of the song stills.**
- **The effects are not the brightest values in 9 of 11 stills.** The wires and rainbow
  compete with the spells. This is the main thing to tune.

The epic's tuning bead owns all of this.
