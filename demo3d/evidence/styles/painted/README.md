# Monster style: painted (ps-a54.1, ink line ps-a54.2)

`--monster-style painted`: stylelab's "Polished painted" treatment, drawn through the
bone-texture crowd's indirect draws ([crowd.md, Monster styles](../../../../docs/tech-demo-3d/crowd.md#monster-styles)).
It uses the lab's palettes and its stylized post look as PostFeel's base. Nothing is tuned
yet; the epic's last bead tunes each treatment.

How the set was made, on the Mac, windowless, IL2CPP player:

```
uv run --project demo3d/tools demo3d look ../styles/painted --baseline final --player-args "--monster-style painted"
```

The `../styles/` slug puts the set here rather than under `evidence/look/`.

- [look.md](look.md) and [look.json](look.json) hold the look harness's numbers against
  `look/final`.
- [contact-sheet.jpg](contact-sheet.jpg) shows `final` on the left and painted on the right.

The same command without the flag (slug `ps-a54.1-noflag`, not committed) matched
`look/final` within 0.11 L* on p2, p50 and p98 in all 11 stills, with contrast and chroma within
0.06 (checked again after the review fixes). So the default path is unchanged.

## What the numbers say

The lab's grade (ACES, exposure +0.25, contrast +12, saturation +15) replaces the night's
(neutral, +0.8, +25, +30) under the flag, as epic Decision 5 asks. That grade was set for the
lab's brighter arena, so here it is dark. Every player log says
`DemoMain: --monster-style painted's post look on the night volume`.

- **The ink line (ps-a54.2) costs readability.** The stills and numbers here were re-rendered
  with the line, which is on by default as in the lab. It is a dark inverted hull round every
  monster, and the ID render counts it as monster: the monster pixels grow by about 15% and
  their p10 falls to L* 0.3. The monsters' |dL*| falls from 23 to 26 without it to 12 to 15, below
  the floor of 17, so the harness exits 1. A thinner or lighter line, or turning it off
  (`MonsterStyle.Outlines`), is ps-a54.4's call.
- **The frame is dark.** Near-black is 26 to 54% of the song stills, against final's 3 to
  17%.
- **The horde is well under half final's chroma.** Monster C*ab is about 22 with the line (28
  without it) against 55.
- **The effects are not always the brightest values.** In 5 of 11 stills, the effects'
  L* p98 is below the rest of the frame's.

The epic's tuning bead owns all of this.

## The beat wave

This sequence was rendered by ps-a54.1, before the ink line.

[beat-wave.jpg](beat-wave.jpg) has five stills of the same frame, each a fifth of a beat apart
in the music. It starts at song time 100.268 s, the beat that opens groove-2, with a beat
length of 0.387 s. Effects are off.

The horde and camera are frozen with `--freeze-at F --time-offset (100.268 - F) --no-feel`:
the timeline stays at 100.268 s while the song as heard, which drives the beat globals,
moves to F. The still time was listed 60 times, so the saved still is the 60th frame, after
the arena has loaded.

The right column shows each still's difference from the first, ×4. Only the rims and glow
spots change. The brightest change sits farther from the hero in each still: about 140,
180, 340 and 500 px. That is the wave sweeping out through the horde.
