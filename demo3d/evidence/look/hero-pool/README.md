# Look set: hero pool (ps-e8r.5, 2026-10-29)

A lit stage around the hero: a soft, warm light follows the hero and lights
the set and the horde in a pool that fades out before the frame's edges, and
a stronger, cool-tinted vignette darkens those edges. Rendered by the look
harness (IL2CPP player, Apple M5 Max, Metal) and compared with the quick
wins' last step, [quick-wins/3-grade](../quick-wins/3-grade/look.md).
The numbers are in [look.md](look.md).

[contact-sheet.jpg](contact-sheet.jpg): 3-grade on the left, this set on the right.

## What changed

- **The hero's light** (`ArenaRuntime.CreateHeroLight`, `FollowHero`): one
  unshadowed realtime point light above the hero's feet. It is not one of the
  8 flame lights and not in the effect-light pool. The renderer is Forward+,
  and CrowdSkinned, LitStaticShadow and RockTriplanar all run URP's cluster
  light loop (StaticShadow.hlsl), so the light shades the horde, the hero and
  the set, adding to the set's lightmaps. I chose a light over a falloff term
  in the shaders because the light reaches every lit shader, kit materials
  included, with no shader change.
- **The vignette** (`NightLook.CreateProfile`) now reads its intensity,
  smoothness and colour from the layout. It is stronger, with a cool
  blue-violet tint (epic Decision 1: a warm stage with cool accents).
- **Layout parameters** (arena-layout.json `lighting`, parsed by `ArenaLighting`):

  | parameter | value |
  |---|---|
  | `hero.heightM` | 6 |
  | `hero.rangeM` | 15 (reaches 13.7 m on the ground) |
  | `hero.intensity` | 60 (0 leaves the light out) |
  | `hero.color` | [1.0, 0.78, 0.7] |
  | `vignette.intensity` | 0.29 (was 0.26 in code) |
  | `vignette.smoothness` | 0.5 (was 0.45) |
  | `vignette.color` | [0.02, 0.025, 0.07] (was [0.01, 0.015, 0.04]) |

  The player flags `--hero-light`, `--hero-height`, `--hero-range`,
  `--hero-color r,g,b` and `--vignette` override these values for tuning.
- **The stage measure** (`demo3d look`): beside each still, the player now
  writes `view-<t>.json`, holding the camera's view-projection and the hero's
  feet. The harness uses it to measure the ground within 6 m of the hero and
  adds a Stage table to look.md. Song stills now also capture the ID mask, so
  monsters are kept out of that ground.
- No beat pulse. The bead listed it as optional, and the music-reactive post
  bead (ps-e8r.18) owns the beat-driven look.

## Tuning

Trial values, all with the song stills' stage dL* and near-black share:

| light (intensity, height, colour) | vignette | stage dL* | near-black | note |
|---|---|---|---|---|
| off (3-grade) | 0.26 | 3.2-5.7 (song-175 43.1) | 14.6-25.8% | evenly dim |
| 20, 4 m, [1, 0.76, 0.5] | 0.40 | 13.9-19.9 | 40-49% | the vignette does the work and crushes the edges |
| 80, 4 m, [1, 0.76, 0.5] | 0.29 | 26.8-31.1 | 14.7-23.6% | saturated yellow pool; monsters next to the hero blow out (about 5 linear units on the ground under a 4 m light) |
| 55, 6 m, [1, 0.85, 0.68] | 0.29 | 19.6-22.8 | 11.7-22.2% | soft and even, but the ground's green albedo turns it olive |
| **60, 6 m, [1, 0.78, 0.7]** | **0.29** | **19.0-22.2 (song-175 51.8)** | **11.9-22.4%** | chosen: peach light takes the olive cast toward brown |

- Each step of vignette intensity above 0.3 drives the frame's border to
  about L* 10 and puts 8-10 points on the near-black share. The epic's
  whole-run criterion caps that share at the baseline's, so the light makes
  the stage and the vignette only frames it.
- A higher light (6 m) spreads the pool more evenly: the ground 6 m out
  gets about a third of the light under it (half by distance, times the
  45° incidence), against a ninth from a 4 m light, and the horde near the
  hero no longer clips.
- The ground still reads slightly olive inside the pool, because it is dark
  green and nearly textureless. The ground bead will give it warm, mid-value
  albedo, and the light's effect on the ground will grow with that albedo, so
  that bead should re-check `hero.intensity` against the stage and |dL*|.

## Acceptance

- **Stage:** in every song still, the median ground L* within 6 m of the hero
  is at least 15 above the median L* of the frame's outer 15% border. It is
  19.0-22.2, and 51.8 at the final peak (before: 3.2-5.7, and 43.1). The
  beauty shots' stage numbers are reported for information only. The opening
  shot's 6.1 is low because its horde fills the border.
- **One realtime light added**, per epic Decision 5.
- **Readability:** |dL*| is 31.7-39.5 in every beauty shot against a floor
  of 17 (before: 35.4-41.3). The light lifts the ground ring around monsters
  near the hero, so |dL*| drops by 1.8-3.8.
- Against 3-grade, every song still gains mean chroma (+0.7 to +5.7). The
  near-black share falls in four song stills and rises 4.9 points at the
  final peak (19.8%, against a baseline of 23.8%). RMS contrast is +0.4 to
  +0.9, except -1.3 at the final peak, where the lit ground sits closer to the
  area attack's clipped blob (the effects bead, ps-e8r.7).

## Cost

One unshadowed pixel point light in Forward+'s cluster loop. Each lit pixel
inside the light's 15 m range does one more light evaluation. There is no
extra pass, no shadow map and no extra texture sample. The vignette was
already part of the uber pass. The frame cost on Cygnus is unmeasured; the
S0/S3 re-check row that the palette bead adds covers it.
