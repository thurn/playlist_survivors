# Monster styles: all nine, tuned (ps-a54.4)

Every `--monster-style` name side by side, after the tuning pass in demo3d's scenes
([crowd.md, Monster styles](../../../../docs/tech-demo-3d/crowd.md#monster-styles)). Each column is
one look set, left to right: baseline (the committed `look/final`, which is what the flag's
default draws), painted, fire, crystal, spectrum, hologram, chrome, void and mix.

- [song.jpg](song.jpg): the five song stills (25, 60, 100, 145 and 175 s), one row each.
- [beauty.jpg](beauty.jpg): TD15's six beauty shots with the horde, one row each.
- The full-size stills, look.md and look.json for each treatment are in `../<style>/`, each
  with a contact sheet against `look/final`.

Made on the Mac, windowless, with the IL2CPP player:

```
uv run --project demo3d/tools demo3d look ../styles/<style> --baseline final --player-args "--monster-style <style>"
uv run --project demo3d/tools demo3d look-sheet ../styles/all/song.jpg final ../styles/painted ../styles/fire \
  ../styles/crystal ../styles/spectrum ../styles/hologram ../styles/chrome ../styles/void ../styles/mix \
  --stills song-025,song-060,song-100,song-145,song-175
```

(`beauty.jpg` is the same command with `--stills opening-horde,path-horde,glowing-tree-horde,graveyard-horde,courtyard-horde,forest-edge-horde`.)
The same look run without the flag (not committed) matched `look/final` within 0.1 L* on p2,
p50 and p98 in all 11 stills, so the default path is unchanged.

## What was wrong before tuning

- **The frame was dark.** The lab's grade (ACES, exposure 0.25, contrast 12, saturation 15)
  suits its bright little arena. On demo3d's night it left 20 to 65% of every song still near
  black, against the baseline's 3 to 17%.
- **The self-lit treatments beat the effects.** Fire, crystal, spectrum and hologram are made of
  light. Their brightest pixels, mostly pale flames, glints, wires and overlapping holograms,
  topped the effects' in 5 to 9 of 11 stills (look.md's Effects table, "brightest").
- **Chrome and void had no read.** Black bodies on a black frame: |dL*| of 2 to 4.

## What was tuned

Two knobs, both in `MonsterStyle`:

1. **The post look** (`PostLook.Stylized`) takes the night's tonemapper, exposure and bloom
   threshold (neutral, 0.8, 1.5), with contrast 20 and saturation 25, between the night's 25/30
   and the lab's 12/15. Each treatment keeps the lab's bloom intensity, chroma and vignette. At
   the night's threshold only the hottest effects bloom, so bloom intensity barely changes a
   still (painted at bloom 0 and 3 differed by at most 13 of 255 in any pixel).
2. **The horde grade** (`SL_Grade` in StyleCommon.hlsl, `MonsterStyle.Gain` and `Peak`): each
   style shader scales its forward colour by a gain, then soft-limits its luminance to a peak,
   `c / (1 + L(c) / peak)`, which keeps the hue.
   - Gain alone could not do it. Fire needed gain 0.05 to let the effects win, and by then the
     monsters were darker than the ground. A pale flame is far brighter than a red one at the
     same values, and additive flames sum where monsters overlap.
   - A limit on the brightest channel failed for the same reason, so the limit is on luminance.

| style | gain | peak | why |
|---|---:|---:|---|
| painted | 2 | 0.4 | the colour strip reads dull under the night's grade; the peak holds its sheen below the effects |
| fire | 1 | 0.15 | dense additive flames, the pale ones the hottest |
| crystal | 1 | 0.3 | facet glints |
| spectrum | 1 | 0.3 | wireframe and equalizer bars |
| hologram | 1 | 0.1 | additive, and overlapping holograms sum |
| chrome | 0.5 | 0.4 | dark mirrors on the lit ground; at gain 2 to 4 they matched the ground (dL* -4 to +8) |
| void | 0.5 | 0.4 | likewise, a hole in the ground |
| mix | per type | per type | each type is graded as its own style |

`--style-tune key=value,...` overrides any of these for a run without a rebuild, for example
`--monster-style fire --style-tune gain=0.8,peak=0.2,exposure=0.6`. Each candidate was a
`demo3d look --trial` set (about 35 s on the Mac); the values above are the ones that kept every
still's effects on top with the most margin while the horde still read.

## The numbers

From each set's look.md. "Song" and "all": stills where the effects' L* p98 is above the rest of
the frame's (the look harness's effects-outshine check), and the smallest margin. dL*: the
readability measure in the six beauty shots (floor |dL*| >= 17). Near-black: share of the song
stills with L* < 10.

| style | effects brightest, song | all 11 | dL* | near-black |
|---|---|---|---|---|
| baseline | 5/5, +2.6 | 11/11, +2.6 | +20.0 to +24.3 | 3 to 17% |
| painted | 5/5, +3.3 | 11/11, +3.3 | +16.8 to +21.8 | 4 to 17% |
| fire | 5/5, +1.3 | 11/11, +1.3 | +16.5 to +23.5 | 3 to 13% |
| crystal | 5/5, +8.8 | 11/11, +8.8 | +24.5 to +33.5 | 3 to 15% |
| spectrum | 5/5, +9.2 | 11/11, +9.2 | +25.9 to +32.3 | 2 to 14% |
| hologram | 5/5, +1.9 | 11/11, +1.9 | +17.1 to +20.3 | 2 to 11% |
| chrome | 5/5, +9.2 | 11/11, +9.2 | -16.1 to -11.6 | 7 to 26% |
| void | 5/5, +10.9 | 11/11, +10.9 | -17.9 to -13.3 | 9 to 30% |
| mix | 5/5, +5.9 | 11/11, +5.9 | +21.6 to +32.1 | 3 to 16% |

- **The effects are the brightest values in every still of every treatment.** Fire (+1.3 at the
  final peak) and hologram (+1.9) have the thinnest margins; a lower peak buys more at the cost
  of the flames' and holograms' glow.
- **Readability misses the floor in a few shots.** Painted misses in one (16.8 at the opening,
  the dark ink line counting as monster), fire in one (16.5 at the glowing tree), and chrome and
  void in all six (dark silhouettes, |dL*| 11.6 to 17.9). The look harness exits 1 for these sets.
  Raising the gain lifts them only by giving away the effects' margin, so they are left for the
  operator to judge in motion.
- **Chrome and void stay the darkest frames** (near-black up to 26 and 30%), since their bodies
  are dark by design.

## Not done here

- **The hero keeps its own shader.** The lab paints its knight under every non-baseline style,
  but demo3d's hero is drawn by `StockCrowd`, not the bone-texture crowd, so the style shaders'
  skinning does not apply to it. The bead left this optional.
- **Motion, the music and the pick** are the operator's: docs/tech-demo-3d/plan.md, Pending on
  the operator, "Pick a monster style".
