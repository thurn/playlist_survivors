# ps-6y2.30: structures in the edge look over their zone fill

Rendered on jarvis (M5 Max), 2026-11-02, in the palette's first zone (cobalt) unless named. Art is judged by eye
(design.md, Judgment over rules); the numbers below are diagnostics.

```sh
just champions-tool structure review all        # the sheets and review.json here
just champions-tool stills --scene structures   # and --look trailer; --scene level
```

## What draws

- Each kind is its Synty prefab (`Assets/Champions/Editor/Structures/structures.json`, ps-6y2.15's picks), combined
  at its height on its footprint's middle, its foundation dropped, made a style mesh as the monsters' are
  (`StyleMesh.Unweld`: its creases over 24 degrees draw) and cut into chunks outlined along their cracks
  (`MonsterShardBaker`).
- `StructureEdge.shader`: the monsters' pixel-wide lines (`MonsterLines.hlsl`) in the zone's `edge` colour at three
  times its value, flaring as the beat wave passes, and as wide a line where the structure meets the ground (its
  foundation goes under it, so an outline would stay open at the base), over a flat fill at half the zone's `fill`,
  calibrated through the grade as the floor is. A hit flashes the lines; the fill lifts a little.
- A break (`StructureShatter`, ps-6y2.15's PhysX shatter on the new chunks; `StructureEdgeShard.shader`): every
  chunk launched on the impact frame, thrown along the blow, each a closed solid with every edge of its inside faces
  lit as well as its crack, hottest on the break's frame and cooling over 0.6 s; they clatter down and sink after 2 s.
- `--look trailer` draws every kind unsimplified (the vendor mesh, creases over 24 degrees). The default changes
  only two kinds' crease angle, by eye:
  - **rock, 8 degrees**: a boulder of a few big facets; at 24 only three strokes cross it and its outline stays open.
    Over 8 every facet draws, closed, without the slivers a near-flat face's triangulation draws at 1.
  - **tree, 12 degrees**: its leaves are two faceted blobs whose soft creases fall under 24, leaving their outline
    open; over 12 they close into balls of facets.

| Kind | Height | On the ground (x by z) | Sim footprint | Triangles | Lines (trailer) | Pieces |
| --- | --- | --- | --- | --- | --- | --- |
| cottage | 5.0 m | 4.24 x 4.79 m | box 3.6 x 4.4 | 9,547 | 8,289 | 20 |
| hut | 4.6 m | 4.69 x 4.96 m | box 4.46 x 4.46 | 7,224 | 6,778 | 20 |
| tower | 10.0 m | 4.34 x 4.34 m | disc r 1.7 | 32,196 | 31,780 | 22 |
| wall | 1.2 m | 5.65 x 0.67 m | box 5.6 x 0.6 | 846 | 855 | 13 |
| fence | 1.1 m | 2.43 x 0.15 m | box 2.42 x 0.3 | 276 | 214 | 10 |
| pine | 8.0 m | 4.10 x 4.16 m | disc r 1.7 | 1,220 | 1,103 | 36 |
| tree | 5.0 m | 2.48 x 2.05 m | disc r 0.7 | 254 | 304 (240) | 14 |
| rock | 1.3 m | 3.13 x 3.40 m | disc r 1.5 | 42 | 54 (38) | 12 |

Tried and rejected, by eye:

- **45 degrees for the cottage, hut and wall**: their roof slabs and stones lose parts of their outlines and break
  into scratches; for the tower nothing changes (its bricks and balconies are square lips).
- **Decimation** (Unity's mesh LODs, as the monsters' far LODs): the tower's roof turns into spikes and the cottage's
  into jagged scratches.
- **The first material** (the zone's full `fill`, shaded by slope, under lines at the zone's `edge`): the first blind
  critique below read it as "lit navy solids with weak, barely glowing lines", another renderer than the monsters'.

## Sheets

- [field.jpg](field.jpg): every kind together at the game camera (a 1080-row frame), the default look;
  [field-trailer.jpg](field-trailer.jpg) the same in `--look trailer`.
- `KIND-game.jpg` at the game camera's distance (three headings; for the rock and the tree, `--look trailer`'s row
  under it; then the shatter at 0, 0.08, 0.2, 0.4, 0.9 and 1.6 s) and `KIND-near.jpg` at half the distance:
  [cottage](cottage-game.jpg) ([near](cottage-near.jpg)), [hut](hut-game.jpg) ([near](hut-near.jpg)),
  [tower](tower-game.jpg) ([near](tower-near.jpg)), [wall](wall-game.jpg) ([near](wall-near.jpg)),
  [fence](fence-game.jpg) ([near](fence-near.jpg)), [pine](pine-game.jpg) ([near](pine-near.jpg)),
  [tree](tree-game.jpg) ([near](tree-near.jpg)), [rock](rock-game.jpg) ([near](rock-near.jpg)).

## The field and a break, in play

The structures scene (`StructureScene`) plays level 1's structures through the real sim on seed 41 to a scripted
120 BPM song; the hero (a white ring) walks to a tree and at 6 s dashes through it with Blade Rush.

- [field-start-0.500.jpg](field-start-0.500.jpg) (and [`--look trailer`'s](trailer-field-start-0.500.jpg)),
  [field-wave-4.270.jpg](field-wave-4.270.jpg): the field round the hero, a beat ring passing.
- [break-impact.jpg](break-impact.jpg): the impact frame; [break-flying-6.250.jpg](break-flying-6.250.jpg),
  [break-landed-7.500.jpg](break-landed-7.500.jpg): the chunks flying, then down.
- [level-cards-violet.jpg](level-cards-violet.jpg): the level scene (`--scene level`), the run's own presenters, a
  pine stand on the violet zone beside the horde: structures take the zone's key.

## Blind critiques

Each by a fresh agent given only the images (these stills and sheets, the operator's five synthwave references and
the trailer's opening-duel frame), no docs, asked whether each figure reads as a retro 3D neon wireframe (closed
facets and silhouette, dark inside) or as lit patches, glassy fills or broken lines.

### 1. The first material

Its verdict, condensed to its findings (the full text is on the bead): the structures "don't read as retro neon
wireframe yet. Only the rock, the round tree and the pines get close, and all of them share one problem: a lit navy
body under a dim blue line."

> - **Cottage: fails.** It reads as shaded blue clay. The bevelled, lumpy roof slabs carry crease fragments: short
>   dashes and "crack" strokes that never close into facets.
> - **Hut: fails, a little less badly.** The deck planks are the best linework on any building, but the roof is the
>   same clay-and-cracks as the cottage.
> - **Tower: fails worst.** Hundreds of tile edges make a sawtooth fringe. Close up it's lacework, and at game scale
>   it's scribble.
> - **Wall: fails.** It looks like a strip of gravel. **Fence: half-succeeds close up**; at game scale it nearly
>   vanishes.
> - **Pine cluster: closest to success per tree.** The stacked cones have closed facets and clean outlines. But the
>   trunks are single unfaceted lines.
> - **Round tree: mostly succeeds.** **Rock: the best of the set.** Big facets, a closed outline, crisp lines.
> - They are distinct from the monsters, but for the wrong reason: they look like a different renderer. Monsters are
>   bright lines on black with bloom; structures are lit navy solids with weak, barely glowing lines.
> - The break: the impact frame works; in the flying frame many shards are "dark slabs with missing or partial edges".
>
> Its changes, in order: fix the material (a near-black, unshaded body; brighter lines with bloom; a hue apart from
> the floor's); designed low-poly meshes for the cottage, hut, tower, wall and fence, every edge drawn; a shatter of
> closed shards with every edge lined, fewer and bigger, hotter while fresh.

What changed in response: the fill went flat at half the zone's `fill` and the lines to three times its `edge`
(the material), the shards' inside faces draw every edge and fly hotter (2.5 times the lines on the break's frame),
in fewer pieces (10 to 22 a kind), and the cottage and hut went back to 24 degrees (45 broke their outlines once the
fill was dark). Not taken here: the hue (the zone's key is the design's: design.md, Zones, "Structures take the new
key") and the designed meshes (below).

### 2. As landed

Its verdict on the structures as landed, condensed (the full text is on the bead):

> - **Rock: passes, the best of the set.** It has big facets, a closed outline and a dark body, the same idea as the
>   low-poly mountain references. [...] the lower outline is missing: the side faces fall into darkness with no edge.
> - **Pine cluster: mostly passes.** The stacked faceted cones have clean closed facets, much like the palm reference.
>   But each trunk is a single hairline [...] the cluster looks like a volley of rockets.
> - **Round tree: passes on style, fails on identity.** The silhouette is closed and the body is dark, but [...] it
>   reads as an egg, a geode or a second rock.
> - **Cottage, hut, tower: fail.** These are detailed meshes with only their sharp edges drawn. The roofs break into
>   short open dashes that end in the middle of a face, like cracks. The hut's plank decks become barcode hatching.
>   The tower roof is hundreds of tiny tile boxes, a lace doily. The bodies are dark, which is right, but the lines
>   never close into large facets.
> - **Wall: fails.** Dozens of small stones turn into scribble. **Fence: fails.** The rails are double hairlines.
> - They separate from the monsters by hue and by brightness. That ranking is right: set dressing sitting under the
>   cast. The problem is the floor. Structures and grid share a hue, so the structures sink into the floor.
> - The break: "On the impact frame (04), the flash to bright cyan with bloom plus the grid ripple is the strongest
>   moment in the set. The angular rock-like shards [...] are good closed outlined pieces." But "the pine stacks fly
>   off as whole trees", and the red and green fringing doubles lines near the frame's edge.
>
> Its changes, in order: designed low-poly meshes for the cottage, hut, tower, wall and fence, every edge drawn (and
> faceted pine trunks, a lobed crown for the round tree); brighter resting lines and a hue of the structures' own;
> more, smaller shards thrown every way.

What changed in response: the line along the ground closes every outline at its base (the rock's), and the pine
stand breaks into 36 pieces, so its tiers come apart. The stills and sheets above are after both.

## Known

- **The cottage, hut, tower, wall and fence fail the wireframe aim** (both critiques): drawn at its creases, the
  vendor detail (shingles, planks, roof tiles, stones, hairline rails) never closes into large facets, and no crease
  angle or decimation fixes it (tried, above). The fix is a mesh designed for the look, every edge drawn, as the
  monsters get theirs: filed as a follow-up of ps-6y2.30 for the run. `--look trailer` keeps the vendor meshes. The
  rock, the round tree and the pines pass.
- **The structures share the floor's hue**: by design (the zone's key), and both critiques would give them their
  own. A question for the look pass (ps-6y2.23) and the operator, noted on that bead.
- **The special moments' chromatic flash** (high tier) fringes lines near the frame's edge on a break; both critiques
  disliked it. Noted on the look pass.
- The pines' trunks are hairlines, and the round tree's crown reads as an egg or a geode; the designed meshes'
  follow-up may take them too.
- Variants 273/300 (`champions check --variants`); EditMode 109/109; tools 161.
