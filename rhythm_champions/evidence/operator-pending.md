# Operator-pending checks

Checks only the operator can make: hand-play balance, the speakers, a window
on screen, a low-end Mac. Beads add a row here instead of waiting; none of
them blocks work ([plan](../../docs/champions/plan.md)). The operator marks a
row done (or files a bead) after checking.

| Check | Why only the operator | Added by | Status |
| --- | --- | --- | --- |
| `just champions` opens a fullscreen 1080p window titled Rhythm Champions on the black void, and `--windowed` a window | A window on screen | ps-6y2.1 | pending |
| Install the Unity Accelerator on jarvis ([SETUP.md](../../SETUP.md#the-unity-accelerator)) and confirm `champions build` logs its connection | Downloading and installing an app | ps-6y2.1 | pending |
| Delayed listening on the speakers: with Music (or Spotify) playing, `just champions --app Music` mutes the app at once and its song comes back 3 s later through the game, in time and without dropouts; quitting the game unmutes the app | Real speakers and a real app playing | ps-6y2.2 | pending |
| The edge crowd on screen: `just champions-play --windowed --edge-crowd 1000 --seconds 20 --bench-out captures/edge-crowd.json`: the skeletons read as lit shapes with dark between them and the shards fly on the kill frame; the JSON's `windowedGpuFrameMs` is the GPU frame time a headless run can't read (Metal has no GPU recorder) | A window on screen, on a quiet machine | ps-6y2.8 | pending |
| The world in a window: `just champions --file <song>` shows the grid with a ring leaving the hero's spot on every beat you hear (stronger on the bar's first), the floor's colour sweeping to a new key at a section change, and nothing but the quiet grid in a beatless stretch; `--tier low` and `--tier high` look alike but for edges and bloom | A window on screen and the speakers | ps-6y2.14 | pending |
| Structures in a window, once ps-6y2.21 assembles level 1 (`just champions --file <song>`; until then the stills in [ps-6y2.15](ps-6y2.15/README.md)): the cottages, huts, tower, walls, fences, trees, pines and rocks read as solid shapes in the zone's dim fill, clearly apart from the monsters' dark bodies, their edges flaring as the beat wave passes; a dash through a fence or a tree shatters it on the frame it lands, thrown along the dash, the pieces clattering down and sinking about three seconds later | A window on screen, and judging the look | ps-6y2.15 | pending |
