# Monster style: fire (ps-a54.2)

> **Tuned since (ps-a54.4).** The stills, look.md, look.json and contact sheet here are now
> the tuned set: the night's tonemapper and exposure, and the horde graded at gain 1, peak 0.15
> ([all/README.md](../all/README.md) has the values, the numbers and why). The notes below
> describe the untuned set they were written for.

`--monster-style fire`: stylelab's "Living flame", drawn through the bone-texture crowd's
indirect draws ([crowd.md, Monster styles](../../../../docs/tech-demo-3d/crowd.md#monster-styles)).
The body (FireBody) is charcoal split by live cracks, with a molten rim and a heat shimmer. The
flames (FireShell) are the type's shell draw: the same instances drawn again, pushed out and
licking up, additive. Each species has its own fire colours from the lab's palettes, and the
dragon (the boss) burns at 0.45 intensity rather than 0.7. Death burns from the feet up. The post
look is the lab's stylized grade with bloom 1.0. ps-a54.4 has since tuned it. Embers are left out, since demo3d's effects already throw sparks.

How the set was made, on the Mac, windowless, IL2CPP player:

```
uv run --project demo3d/tools demo3d look ../styles/fire --baseline final --player-args "--monster-style fire"
```

- [look.md](look.md) and [look.json](look.json) hold the look harness's numbers against
  `look/final`. The harness exits 1 when the readability floor fails, as noted below.
- [contact-sheet.jpg](contact-sheet.jpg) shows `final` on the left and fire on the right.
- The same command without the flag (slug `ps-a54.2-noflag`, not committed) matched `look/final`
  within 0.1 L* on p2, p50 and p98 in all 11 stills, so the default path is unchanged.
- Every player log says `DemoMain: --monster-style fire's post look on the night volume`.

## What the numbers say

- **Readability passes easily.** The monsters' |dL*| is 46 to 55 in every beauty shot, against
  final's 20 to 24 and a floor of 17. The flames make every monster a light source.
- **A dense horde turns into a wall of flame.** In the song stills the packed horde washes out
  towards white, and the species colours survive only at its edges.
- **The effects lose to the flames.** In 8 of 11 stills the effects' L* p98 is below the rest of
  the frame's. This is the tuning bead's to fix (flame intensity, or the effects' gain).

## Shadows and TAA

- The body casts the shadow, with the shimmer and the burn in its shadow pass, so the shadow
  follows the burn. The flames cast no shadow: they are additive light, and the lab's do not
  either.
- [taa-crop.jpg](taa-crop.jpg) is a full-resolution crop of song-145 under the default MSAA 4x
  (left) and under `--aa taa` (right, `--player-args "--monster-style fire --aa taa"`, not
  committed as a set). TAA softens the flames' fine noise a little, and shows no trails or
  ghosting in the still.
- **Limitation:** the flames are transparent, so they write no motion vectors. Under TAA they
  reproject with the vectors of what is behind them: the body inside the silhouette, the ground
  outside it. On a fast-moving monster the tongues outside the body may lag in motion. A still
  cannot show that; it is for the operator to judge in motion.
