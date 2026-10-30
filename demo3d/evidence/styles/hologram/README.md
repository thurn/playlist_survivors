# Monster style: hologram (ps-a54.3)

`--monster-style hologram`: stylelab's "Hologram" treatment, drawn through the bone-texture
crowd's indirect draws on the unwelded style mesh
([crowd.md, Monster styles](../../../../docs/tech-demo-3d/crowd.md#monster-styles)).
Additive projections in the transparent queue, so they need no sort. They have a fresnel
edge, scanlines, a rolling band, a dim wireframe from the style mesh's barycentrics, red and
cyan fringes, flicker, and glitch slices just after each off-beat. Death scans the body out
from the head down, in its own `SL_DISSOLVE`. A projection casts no shadow, so the shader
has no ShadowCaster. Unlike the lab's, its added light fades into the fog.

It uses the lab's palette per type and the lab's post look as PostFeel's base: ACES, exposure +0.25, contrast +12, saturation +15, bloom 1.35, chromatic aberration 0.35.
Nothing is tuned yet; the epic's last bead tunes each treatment.

How the set was made, on the Mac, windowless, IL2CPP player:

```
uv run --project demo3d/tools demo3d look ../styles/hologram --baseline final --player-args "--monster-style hologram"
```

- [look.md](look.md) and [look.json](look.json) hold the look harness's numbers against
  `look/final`.
- [contact-sheet.jpg](contact-sheet.jpg) shows `final` on the left and hologram on the right.

The same command without the flag (slug `ps-a54.3-noflag`, `--trial`, not committed)
matched `look/final` within 0.1 L* on p2, p50 and p98 in all 11 stills, with contrast and
chroma within 0.3. So the default path is unchanged. Every one of the 28 player logs says
`DemoMain: --monster-style hologram's post look on the night volume`, and none has an exception.

## What the numbers say

- **The horde reads well.** The monsters' dL* is 39 to 46. It is lower than the opaque
  styles' because the ground shows through.
- **Monster C*ab is 46 to 48**, close to final's.
- **Near-black is 19 to 38% of the song stills**, the least of the four styles so far.
- **The effects are not the brightest values in 7 of 11 stills.**
- **Chromatic aberration 0.35** gives every bright edge a visible fringe.

The epic's tuning bead owns all of this.
