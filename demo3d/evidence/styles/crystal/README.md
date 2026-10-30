# Monster style: crystal (ps-a54.3)

> **Tuned since (ps-a54.4).** The stills, look.md, look.json and contact sheet here are now
> the tuned set: the night's tonemapper and exposure, and the horde graded at gain 1, peak 0.3
> ([all/README.md](../all/README.md) has the values, the numbers and why). The notes below
> describe the untuned set they were written for.

`--monster-style crystal`: stylelab's "Living crystal" treatment, drawn through the bone-texture
crowd's indirect draws on the unwelded style mesh
([crowd.md, Monster styles](../../../../docs/tech-demo-3d/crowd.md#monster-styles)).
Faceted gem bodies drawn in the transparent queue (Transparent-10). Each facet refracts the
camera's opaque texture with a little dispersion, tinted by the type's gem colour. The game
camera turns that texture on only under this style (`MonsterStyle.NeedsOpaqueTexture`).
Facet edges and glints come from the style mesh's barycentrics and per-triangle values, and
death shatters the monster in every pass.

It uses the lab's palette per type and the lab's post look as PostFeel's base: ACES, exposure +0.25, contrast +12, saturation +15, bloom 1.0.
ps-a54.4 has since tuned it.

How the set was made, on the Mac, windowless, IL2CPP player:

```
uv run --project demo3d/tools demo3d look ../styles/crystal --baseline final --player-args "--monster-style crystal"
```

- [look.md](look.md) and [look.json](look.json) hold the look harness's numbers against
  `look/final`.
- [contact-sheet.jpg](contact-sheet.jpg) shows `final` on the left and crystal on the right.

The same command without the flag (slug `ps-a54.3-noflag`, `--trial`, not committed)
matched `look/final` within 0.1 L* on p2, p50 and p98 in all 11 stills, with contrast and
chroma within 0.3. So the default path is unchanged. Every one of the 28 player logs says
`DemoMain: --monster-style crystal's post look on the night volume`, and none has an exception.

## What the numbers say

- **The horde reads strongly.** The monsters' dL* is 56 to 66 in the beauty shots, against
  final's 20 to 24. Refraction makes each body brighter than the ground behind it.
- **The frame is dark around them.** Near-black is 23 to 45% of the song stills.
- **Monster chroma is about 0.6x final's** (C*ab 28 to 36). The gem tints are pale.
- **The effects are not the brightest values in 5 of 11 stills.**
- **Shards.** Monsters dying in spell blasts show as clouds of white shards, for example on
  the left of [graveyard-horde.jpg](graveyard-horde.jpg).

The epic's tuning bead owns all of this.
