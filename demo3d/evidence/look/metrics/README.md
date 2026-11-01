# The neon look's metrics, calibrated on v2 (ps-wnj.2, 2026-11-01)

`look-metrics` measures a directory of stills against a palette and a
thresholds file and exits 1 when a still breaks a rule. It adds the three
measures the neon look (docs/design/neon-look.md, **Look metrics**) is judged
by: palette conformance, value structure and swarm separation. This folder is
its run on fifteen frames of the v2 crystal cut, the capture viewers reviewed.

```sh
uv run --project demo3d/tools demo3d look-metrics STILLS --palette PALETTE.json --out DIR
uv run --project demo3d/tools game look-metrics STILLS --palette PALETTE.json   # the same, for game stills
```

`--thresholds` defaults to
[docs/design/neon-look-thresholds.json](../../../../docs/design/neon-look-thresholds.json),
a list of `{metric, min/max, why}` rules over the names in `metrics.json`.
Its values are provisional until the style guide (ps-wnj.1) edits them.

## What is here

- [metrics.md](metrics.md): every still's numbers and the rules it breaks.
- [metrics.json](metrics.json): the same, plus each palette role's share of the
  bright pixels.
- `v2-tNN-swarm.jpg`: each frame (at NN s into the cut) with every dense
  clump tile outlined. Red tiles are under the separation floor, 0.3; green
  ones are over it.
- [v2-palette.json](v2-palette.json): v2's colours in the palette format. v2
  had no palette, so these values were measured from its frames' bright hue
  peaks: nine type colours, the gold formations, white blade and sparks, and
  the grey-blue ground.

The frames were taken with
`ffmpeg -ss NN -i captures/trailer-crystal-v2.silent.mp4 -frames:v 1`, at 1,
4, 8, ... 56 s. They are video frames (H.264), not windowless stills. The
numbers would shift a little on lossless stills, but not the verdicts.

## What the metrics flag in v2

Every frame breaks at least one rule. Each viewer complaint shows up in a
metric:

- **"When the enemies clump up they look like a blob more than a swarm."**
  Every frame fails swarm separation. The median dense tile ranges from 0.00
  to 0.24 across the frames, against a floor of 0.3. Of all 1,363 dense
  tiles, 85% are under 0.3 and 67% are under 0.15. For reference, the unit
  tests' synthetic crowds score 0.00 (flat overlapping discs) and 0.36 (the
  same discs with a 3 px dark rim, the "solid" look's outline). The worst
  frames are the masses viewers described:
  - t28: the cyan horde, 0.01;
  - t56: the finale's wall of cyan and green, 0.01;
  - t20: the green and red masses, 0.10;
  - t32 and t36: the mixed climax, 0.08 and 0.12.
- **"Something about the bright colour low detail... the large number of
  low-poly entities makes it feel 'flat'."** 19-61% of the screen emits
  light (7 of 15 frames are over the 30% cap). The floor's p90 is 18-41 L*
  (14 frames are over 20), because the ground is lit grey or
  orange instead of falling to black. Geometry Wars' grammar is the
  opposite: a black field with a small share of bright emitters.
- **"A bunch of unrelated stitched together parts."** The floor's p50 jumps
  between shots, from 2.2-4.9 in the dark crypt and neon shots to 8.7-13.4
  in the lit ones (t04, t08, t28, t32, t36 and t56 break the 8 L* cap). Each shot's world
  has its own ground and grade.

What the metrics do **not** flag in v2:

- **Conformance** is 96-100% (95.6% at worst) against v2's own nine-hue palette. v2's
  colours were consistent with themselves; the trouble was too many of them.
  Conformance is a check that new looks keep to their palette. It does not
  measure cohesion on its own.
- **Hue families** count 1-4 per frame. The per-frame budget mostly held,
  because each section showed only a few types.
- **Clipping** is at most 1.4%.

## How each metric works, and why

All colour work is in CIELAB (D65). The code is in
`demo3d/tools/src/demo3d_tools/look.py`, section *The neon look's metrics*.

### Palette conformance

Bright pixels are those at L* >= 50. A chromatic bright pixel conforms when
its CIELAB hue is within 20 degrees of a chromatic palette role's hue. A
neutral one (C*ab < 15) conforms when the palette has a bright neutral role
(white).

Hue is used rather than full colour distance because bloom and tonemapping
move an emitter's lightness and chroma far more than its hue. Every
`#rrggbb` anywhere in the palette JSON is a role, so the style guide can add
roles without a code change.

Hue families count the 30-degree hue bins that hold at least 3% of the bright
chromatic pixels and are no lower than either neighbour. Counting peaks this
way means one hue split by a bin edge counts once.

### Value structure

The value-structure metrics are:

- the frame's L* p50 and p98;
- **emitted**: the share of pixels at L* >= 60 or C*ab >= 45;
- **the floor**: every other pixel, reported as its L* p50 and p90;
- **clipped**: pixels with every channel >= 250, and the largest connected
  clipped region.

The floor is an estimate. Without a depth or ID pass it also takes in dark
monster sides and fog. It was chosen so that the command runs on any still,
including video frames like these. In the void world, where everything that
is not emitting should be near black, the estimate and the real floor
converge.

### Swarm separation: luminance edges, not an ID pass

The swarm-separation metric works like this:

1. A candidate monster pixel is coloured and lit (C*ab >= 30, L* >= 25). It
   takes the palette enemy hue nearest its own.
2. The still is measured at 1920 px wide (resampled if needed). A 64 px tile
   is dense when one enemy hue covers half of it, after gaps of up to 4 px are
   closed. At the trailer's 32-42 m framing, that means several monsters
   overlapping.
3. In each dense tile, separation is the share of the clump's pixels on a
   boundary: pixels whose 3x3 window spans 30 L* or more. Dark gaps,
   outlines, rims and facets all count. A flat colour mass scores only its
   outer edge.

Why luminance edges instead of an ID pass:

- The calibration has to run on v2, which exists only as video. No ID pass
  can be taken from it.
- The question is perceptual: do neighbours *look* separate? The ID render
  demo3d already writes (`--capture-mask`) marks all monsters alike, so it
  could not tell two touching monsters apart anyway. An instance-ID pass
  would need a new player flag in both projects. It would also measure
  geometry, not what the viewer sees.
- The same command then measures demo3d and game stills alike, with no
  player change.

Tuning on v2:

- At full resolution, a 20 L* edge picked up the crystal facets inside
  bodies. 30 L* keeps the dividers and drops most facets.
- Measuring at a fixed 1920 width makes a 960 px still score the same as a
  1920 px one (the tests check this).

Known limits:

- A large body of an enemy hue reads as a clump. Examples are a giant (t01's
  brutes), a boss, or a structure that shares an enemy's hue (v2's blue walls
  at t40 and t44 share the trolls' blue). Such a body fails when it is flat.
  For the new looks that is intended: a flat giant is the "flat" feedback.
  But a v2 structure scoring 0.00 is a false "blob", so read the overlays.
- A clump that is mostly dark between lit edges (the edge and hologram looks)
  never covers half a tile, so it is not measured. It cannot read as a colour
  blob.
- The style guide must keep the shatter colour's hue clear of the enemy
  hues. Otherwise lit formations would count as clumps.
