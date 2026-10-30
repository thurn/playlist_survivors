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
0.06. So the default path is unchanged.

## What the numbers say

- **Readability passes in every beauty shot.** The monsters' |dL*| is 30 to 34 against
  final's 20 to 24, above the floor of 17.
- **The horde is less saturated than final.** Mean C*ab is 3 to 11 lower. The look's
  saturation is +15 against the night's +30, and its chroma lives in the colour strips
  and rims, not the vendor albedo.
- **The effects are not always the brightest values.** In 6 of 11 stills, the
  effects' L* p98 is below the rest of the frame's. The rims, sheen and glow spots
  reach the top of the range under ACES. The epic leaves this to its tuning bead
  (Decision 5).

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
220, 340 and 500 px. That is the wave sweeping out through the horde.
