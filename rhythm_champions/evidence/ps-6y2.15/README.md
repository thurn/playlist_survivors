# ps-6y2.15: structures

Measured on jarvis (M5 Max), 2026-11-02, in the palette's first zone (cobalt). The stills are 1080p renders of the
structures' scripted scene, shrunk to 960 px, from:

```sh
just champions-tool edgecut review cottage   # and hut, tower, wall, fence, tree, pine, rock
just champions-tool stills --scene structures
just champions-tool look captures/stills --out rhythm_champions/evidence/ps-6y2.15/look
```

## The cuts

Each kind's recipe (tools/recipes/edgecut/KIND.py) cuts its Synty prefab to clean solids measured off the vendor mesh
(boxes, gables, prisms and pyramids for the buildings) or hulls (rocks, trees, pines, walls, fences), lit along their
sharp edges, over a fill in the zone's `fill` colour; the review sheets put each beside its vendor original at the
game camera's angle and the monsters' distances: [cottage](../edgecuts/cottage/README.md),
[hut](../edgecuts/hut/README.md), [tower](../edgecuts/tower/README.md), [wall](../edgecuts/wall/README.md),
[fence](../edgecuts/fence/README.md), [tree](../edgecuts/tree/README.md), [pine](../edgecuts/pine/README.md),
[rock](../edgecuts/rock/README.md).

| Kind | Height | On the ground (x by z) | Fill triangles | Line segments | Pieces | Hits | Sim footprint |
| --- | --- | --- | --- | --- | --- | --- | --- |
| cottage | 4.9 m | 4.1 x 4.6 m | 56 | 59 | 24 | 3 | box 3.6 x 4.4 m |
| hut | 4.5 m | 4.5 x 4.8 m | 32 | 35 | 24 | 2 | box 4.46 x 4.46 m |
| tower | 9.3 m | 4.2 x 4.2 m | 46 | 33 | 23 | 3 | disc r 1.7 m |
| wall | 1.1 m | 5.6 x 0.6 m | 20 | 15 | 20 | 1 | box 5.6 x 0.6 m |
| fence | 1.1 m | 2.4 x 0.1 m | 96 | 8 | 36 | 1 | box 2.42 x 0.3 m |
| tree | 5.0 m | 2.4 x 2.0 m | 72 | 37 | 20 | 1 | disc r 0.7 m |
| pine | 8.0 m | 4.0 x 4.1 m | 212 | 107 | 30 | 1 | disc r 1.7 m |
| rock | 1.3 m | 3.1 x 3.3 m | 28 | 26 | 20 | 1 | disc r 1.5 m |

## The field and a break

The scene (`StructureScene`) plays level 1's structures through the real sim on seed 41, whose start stands between a
hamlet, a lone tower and a grove, to a scripted 120 BPM song. The hero (a white ring standing in) walks to a tree it
can reach straight from the start and at 6 s dashes through it with Blade Rush.

- [field-start-0.500.jpg](field-start-0.500.jpg), [field-wave-4.270.jpg](field-wave-4.270.jpg),
  [field-before-5.900.jpg](field-before-5.900.jpg): the structure field round the hero, the edges flaring as a beat
  ring passes.
- [break-impact-6.017.jpg](break-impact-6.017.jpg): the impact frame, the scene's `break` moment (kept on the first
  frame that shows the break, 6.017 s, the sim's `StructureBreak` time): the tree flashes toward its edge colour, its
  pieces a frame into their flight, the structure-break ring bending the grid (a special moment).
- [break-flying-6.250.jpg](break-flying-6.250.jpg): the pieces thrown along the dash, tumbling; the dash's path broke a
  second tree and the pines on its way (each flashing on its own frame).
- [break-landed-7.500.jpg](break-landed-7.500.jpg): the pieces lie where they fell; at 2 s they sink, gone at 3 s.

[look/metrics.md](look/metrics.md) is `champions look` on the scene's stills. Its one broken rule,
`background_light_max` on the steady stills (0.19 against 0.15), is the world's grid: the world scene alone measures
0.73-0.80 (ps-6y2.14's evidence; the floor is unmarked in the type-ID pass), and structures in frame bring the share
down. Shader variants: 251 of the 300 budget after this bead (`champions check --variants`).
