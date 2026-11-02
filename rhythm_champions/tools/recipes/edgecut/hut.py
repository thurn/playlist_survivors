"""The hut's edge cut (ps-6y2.15): Synty's Fantasy Kingdom preset hut 01 (8,562 triangles, one mesh standing on its
corner, its foundation below the ground) redrawn as a cabin under a gabled roof on its raised deck.

- Size: 4.6 m from the ground to the ridge (the vendor's 6.7 m).
- Body (the fill): clean solids measured off the vendor mesh: the deck's slab (its stilts and rails go), the cabin's
  box at its back and the roof's gable over it, its ridge running across the hut.
- Lines: the solids' sharp edges, and the deck's front corner posts as strokes.
- Shards: the deck in eight pieces, the cabin in eight, the roof in eight.
"""

import numpy as np

from champions_tools.edgecut import Cut, Model, Source, solids

MODEL = Model(id="hut", prefab="3416a1f8a57501f41b39470697272c5e", heightM=4.6)
ROLE = "structure"
DECK, EAVES, RIDGE = 0.9, 2.2, 4.5  # m


def cut(src: Source) -> Cut:
    c = Cut(src)
    c.solid(solids.box(-4.46, 0.0, 0.0, DECK, -4.46, 0.0), "deck")
    c.solid(solids.box(-4.37, -0.76, DECK, EAVES, -4.37, -1.3), "cabin")
    c.solid(solids.gable(-4.5, -0.62, -4.85, -1.13, EAVES, RIDGE, along="x", ridge_at=-2.56), "roof")
    for part in ("deck", "cabin", "roof"):
        c.hull_edges(part, min_angle=30)
    for x in (-4.4, -0.06):
        c.stroke([np.array([x, DECK, -0.06]), np.array([x, DECK + 1.0, -0.06])], 0, "deck", importance=0.5)
    c.recentre()
    c.break_into({"deck": 8, "cabin": 8, "roof": 8})
    return c
