# Monster style: painted (ps-a54.1)

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

- **Readability passes in every beauty shot.** The monsters' |dL*| is 23 to 26, against
  final's 20 to 24 and a floor of 17.
- **The frame is dark.** Near-black is 25 to 50% of the song stills, against final's 3 to
  17%.
- **The horde is half final's chroma.** Monster C*ab is about 28 against 55.
- **The effects are not always the brightest values.** In 6 of 11 stills, the effects'
  L* p98 is below the rest of the frame's.

The epic's tuning bead owns all of this.

## The beat wave

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
