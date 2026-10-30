# Monster style: void (ps-a54.2)

> **Tuned since (ps-a54.4).** The stills, look.md, look.json and contact sheet here are now
> the tuned set: the night's tonemapper and exposure, and the horde graded at gain 0.5, peak 0.4
> ([all/README.md](../all/README.md) has the values, the numbers and why). The notes below
> describe the untuned set they were written for.

`--monster-style void`: stylelab's "Void", drawn through the bone-texture crowd's indirect draws
([crowd.md, Monster styles](../../../../docs/tech-demo-3d/crowd.md#monster-styles)). Each body
shows a starfield and a faint nebula fixed to the screen, like a hole into the night, ringed by
an intense coloured edge per species that flares as the beat wave passes. Death implodes the
monster to a point at its middle instead of dissolving, in every pass. The post look is the lab's
stylized grade with bloom 1.3. ps-a54.4 has since tuned it.

How the set was made, on the Mac, windowless, IL2CPP player:

```
uv run --project demo3d/tools demo3d look ../styles/void --baseline final --player-args "--monster-style void"
```

- [look.md](look.md) and [look.json](look.json) hold the look harness's numbers against
  `look/final`. The harness exits 1 when the readability floor fails, as noted below.
- [contact-sheet.jpg](contact-sheet.jpg) shows `final` on the left and void on the right.
- The same command without the flag (slug `ps-a54.2-noflag`, not committed) matched `look/final`
  within 0.1 L* on p2, p50 and p98 in all 11 stills, so the default path is unchanged.
- Every player log says `DemoMain: --monster-style void's post look on the night volume`.

## What the numbers say

- **The bodies are dark by design, so readability fails.** Monster L* is about 2, and |dL*| is
  2 to 4 against a floor of 17. The silhouettes read through their edges, which the median does
  not measure. The lab's top-down still (stylelab/evidence/treatments/gameplay.jpg, panel 8)
  looks the same.
- **The effects are the brightest values in every still.**
- Brighter edges, or a lighter nebula, are ps-a54.4's call.
