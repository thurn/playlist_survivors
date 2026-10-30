# Monster style: chrome (ps-a54.2)

> **Tuned since (ps-a54.4).** The stills, look.md, look.json and contact sheet here are now
> the tuned set: the night's tonemapper and exposure, and the horde graded at gain 0.5, peak 0.4
> ([all/README.md](../all/README.md) has the values, the numbers and why). The notes below
> describe the untuned set they were written for.

`--monster-style chrome`: stylelab's "Liquid chrome", drawn through the bone-texture crowd's
indirect draws ([crowd.md, Monster styles](../../../../docs/tech-demo-3d/crowd.md#monster-styles)).
A mirror finish reflects a computed synthwave world (a sunset sky with a striped sun, studio
strips overhead, a neon grid floor), tinted per species. A ripple runs over the surface on the
beat. Death melts the monster into a puddle round its feet, which dissolves in the last third of
the death. The ripple and the melt are in every pass, so shadows, depth and motion vectors follow
them. The post look is the lab's stylized grade with bloom 1.0. ps-a54.4 has since tuned it.

How the set was made, on the Mac, windowless, IL2CPP player:

```
uv run --project demo3d/tools demo3d look ../styles/chrome --baseline final --player-args "--monster-style chrome"
```

- [look.md](look.md) and [look.json](look.json) hold the look harness's numbers against
  `look/final`. The harness exits 1 when the readability floor fails, as noted below.
- [contact-sheet.jpg](contact-sheet.jpg) shows `final` on the left and chrome on the right.
- The same command without the flag (slug `ps-a54.2-noflag`, not committed) matched `look/final`
  within 0.1 L* on p2, p50 and p98 in all 11 stills, so the default path is unchanged.
- Every player log says `DemoMain: --monster-style chrome's post look on the night volume`.

## What the numbers say

- **The monsters are nearly black, so readability fails.** Monster L* is about 2, and |dL*| is
  2 to 4 against a floor of 17. From demo3d's near-overhead camera most surfaces reflect up into
  the synthwave world's dark zenith; only the sides catch the bright horizon and sun. The lab's
  own top-down still (stylelab/evidence/treatments/gameplay.jpg, panel 7) is just as dark, so
  this is the treatment as designed, not a porting error.
- The red and blue highlights that do show are the horizon and the grid.
- Fixing this means changing the look (a brighter zenith, or tilting the world towards the
  camera). That is ps-a54.4's call.
