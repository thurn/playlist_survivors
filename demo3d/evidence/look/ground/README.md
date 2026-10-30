# Look set: ground (ps-e8r.6, 2026-10-29)

A warm, textured, mid-value ground. The clearings and paths are warm earth with
a broken, cracked crust and pebbles, and the edge bands and forest stay dark.
Rendered by the look harness (IL2CPP player, Apple M5 Max, Metal) on top of
the effects bead (ps-e8r.7), and compared with its set,
[effects/after](../effects/README.md). I tuned against
[hero-pool](../hero-pool/README.md), the step before in this bead's lane. The
numbers are in [look.md](look.md).

[contact-sheet.jpg](contact-sheet.jpg): effects/after on the left, this set on the right.

## What changed

- **The ground's albedo** (`ArenaBaker.GroundColor`): the fighting ground is now
  warm, mid-value earth instead of dark moss. The edge band and forest floor
  are darker and warm too, so the clearing reads as a stage with dark edges.

  | type | before (sRGB) | after (sRGB) |
  |---|---|---|
  | clearing | 0.27, 0.31, 0.19 | 0.49, 0.45, 0.40 |
  | path | 0.33, 0.29, 0.21 | 0.54, 0.48, 0.41 |
  | edge band | 0.20, 0.23, 0.15 | 0.25, 0.22, 0.16 |
  | forest | 0.14, 0.15, 0.11 | 0.12, 0.11, 0.08 |

- **Large-scale variation:** a third octave of noise multiplies the value by
  0.75-1.25 in patches about 30 m across, on top of the existing 9 m and 2 m
  octaves. It breaks up the detail layer's tiling.
- **A tiling detail layer** (`ArenaBaker.DetailAlbedo`): a 512 px texture
  generated in code, tiling every 6 m (1.2 cm per pixel), saved as the gitignored
  `Baked/Arena/GroundDetail.png`. It is URP Lit's detail albedo (`_DETAIL_MULX2`,
  linear, 0.5 is neutral). Its terms all wrap at the tile's edges:
  - mottling, from two octaves of value noise;
  - cracks, 2-4 cm wide, along the edges of jittered Voronoi cells (7 x 7 per
    tile, about 0.85 m). A noise mask fades them out along about half the cell
    edges, so they read as a broken crust rather than paving;
  - 110 pebbles, 2-12 cm in radius, each with a lit side and a
    shadow on the side away from the moon.

  The kit's ground normals (`Ground_Normals_01`, referenced at bake time and
  never committed) are still the detail normal map, now at the same 6 m tiling
  (was 3 m).
- **Lighting, retuned for the lighter ground** (arena-layout.json `lighting`):

  | parameter | before | after | why |
  |---|---|---|---|
  | `moon.intensity` | 0.65 | 1.0 | lifts the ground away from the hero into the 30-50 band |
  | `crowd.key` | 4.6 | 3.0 | keeps the horde's key light the same (0.65 × 4.6 ≈ 1.0 × 3.0) |
  | `hero.intensity` | 60 | 40 | on the lighter ground the pool blew out to L* 55-60 at 6 m |
  | `hero.color` | 1.0, 0.78, 0.7 | 1.0, 0.86, 0.76 | the peach cast fixed hero-pool's olive ground; on warm earth it went orange |

## Tuning

Each trial was a full bake, build and look set. Values are the empty beauty
shots' fighting-ground median L* (the band is 30-50) and the beauty shots' |dL*|.

| trial | ground | lighting | fighting L* | |dL*| | note |
|---|---|---|---|---|---|
| before (hero-pool) | dark moss, no detail | moon 0.65, key 4.6, hero 60 peach | 13.1-19.2 | 31.7-39.5 | a void |
| 1 | earth 0.52, 0.42, 0.30, full crack net | same | 21.7-34.4 | 27.2-32.4 | saturated orange; the cracks read as paving |
| 2 | 0.50, 0.43, 0.34, broken cracks | moon 0.85, key 3.5, hero 40 | 26.5-39.2 | 25.5-30.1 | courtyard too dark; still ochre |
| 3 | 0.50, 0.45, 0.38, stronger mottle | moon 1.0, key 3.0, hero 40 whiter | 31.4-44.8 | 22.9-27.7 | passes |
| **4** | **0.49, 0.45, 0.40** | **same as 3** | **31.1-44.6** | **23.0-27.8** | chosen: a little less ochre |
| 4 + vignette 0.36 | same | same | 26.2-40.1 | 20.2-25.4 | rejected: the courtyard, all clearing, drops below 30 |

I tried a stronger vignette to win back song-175's RMS contrast (see below). It
darkens the outer frame, and in the courtyard and opening shots the whole frame
is clearing, so it took the courtyard's median to 26.2. The vignette stays at 0.29.

## Acceptance

From the Ground table in [look.md](look.md), against the same measure on
hero-pool's captures (re-rendered unchanged in this worktree as a trial):

| shot | fighting L* before | after | detail before | after | outer L* before | after |
|---|---:|---:|---:|---:|---:|---:|
| opening | 13.8 | 37.7 | 2.6 | 3.6 | | |
| path | 16.6 | 41.7 | 2.4 | 3.1 | 8.7 | 14.4 |
| glowing-tree | 18.2 | 41.4 | 1.5 | 2.7 | 12.9 | 19.5 |
| graveyard | 16.4 | 42.7 | 1.5 | 2.4 | 1.1 | 1.8 |
| courtyard | 13.1 | 31.1 | 1.5 | 2.6 | | |
| forest-edge | 19.2 | 44.6 | 1.5 | 2.6 | 15.7 | 21.3 |

- **Clearing value:** the median L* of fighting ground is 31.1-44.6 in every
  empty beauty shot (band 30-50).
- **Local detail** (the mean standard deviation of L* in 16 px windows on
  fighting ground) is above the baseline's in every shot: 2.4-3.6 against 1.5-2.6.
- **Darker edges:** the edge bands' and forest's median L* is 1.8-21.3, below the
  clearings' in every shot that shows them. The opening and courtyard shots are
  clearing from edge to edge, so they have no outer pixels.
- **No obvious repeat** at the widest shot (opening, 40 m): I looked at the full
  1920 px capture. The detail tiles about 6.7 times across the frame, but the
  faded cracks and the 30 m value patches hide the grid. Neither the crack
  network nor the pebbles showed a repeating pattern.
- **Readability:** |dL*| is 23.0-27.8 in every beauty shot, against a floor of
  17 (identical with and without the effects bead underneath). The horde is still lighter than its ground. The ring around it rose from
  L* 9-12 to 18-23, so |dL*| fell by 8.5-13.7.
- demo3d's EditMode tests and the look harness's tests pass.

## Side effects on the run's measures

Against effects/after:

- Every song still gains mean chroma (+8.6 to +13.9) and loses near-black
  share (-8.6 to -16.6 points). It is now 3.5-10.9%, against the baseline's
  23.8-47.0%.
- RMS contrast falls by 0.9-3.7 in four song stills, because the dark half of
  the frame got lighter. Against the run's baseline it is still above in
  song-025 to song-145 (+6.1 to +9.8). **Song-175 is 26.1 against the
  baseline's 31.5.** The baseline's figure came mostly from the clipped blob
  that the effects bead removed: effects/after was already 29.8. A vignette of
  0.36 won back about 1.5 in a trial, but it broke this bead's clearing band
  (see Tuning). I left it for the palette pass (ps-e8r.10) to settle.
- The stage (ps-e8r.5) is still over its bar in every song still: 20.1-31.6.

## The ground measure

`demo3d look` now has a Ground table (`look.json`: `ground`). For each empty beauty
shot it projects every pixel's view ray onto the ground plane (from the
`view-<t>.json` the player writes) and classes the point with a port of
`ArenaLayout.FightingDistance`: fighting ground inside a clearing or path, outer
past them. Props are not masked out, so trees and rocks in the outer class darken it a little.

## Cost

The ground already sampled a detail normal map with `_DETAIL_MULX2`. URP Lit's
detail albedo sample was already in that variant, reading a grey default texture,
so the new detail albedo adds a 512 px texture (about 0.2 MB compressed with
mips) and no extra shader work. The lighting changes are intensity values only.
