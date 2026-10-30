# PX9: the art pass (2026-10-30)

Windowless stills from the IL2CPP Mac player (operator's Mac, Metal, 1920×1080), drawn at fixed
song times, and two editor tasks:

```
APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
"$APP" -batchmode -force-metal --capture-stills "$PWD/demo2d/evidence/px9" --stills-at 6,30,89.35,119.7,166,176
uv run --project demo3d/tools demo2d unity ReferenceStills -out "$PWD/demo2d/evidence/px9"
uv run --project demo3d/tools demo2d unity Readability -out "$PWD/demo2d/evidence/px9"
```

## References

- [ref-forest-camera.jpg](ref-forest-camera.jpg) and [ref-forest-scene.jpg](ref-forest-scene.jpg):
  AncientForest's own `Demo.unity` (now in the manifest), its camera's framing and the whole scene.
  It holds only the seven swaying trees on a blue clear colour (its two whole-sheet sprites point at
  sheets the demo doesn't sync); they show the art's own colours in daylight.
- CFXR's `CFXR Demo.unity` draws with the built-in pipeline's materials, which are pink under URP, so
  the effects' reference stays PX2's gallery (`../px2/cfxr-*.jpg`), the prefabs as authored.
- [before/](before/): the same moments from PX8's build (119.66, 166 and 176 s), for comparison.

## The six beauty shots

The checklist: one focal point, the hero readable, effects carrying light, no flat or muddy ground,
no tiling repetition visible. Each is the agent's judgement; the operator's is pending (docs/tech-demo-2d/README.md, Pending on the operator).

| Shot | Time | Focal point | Hero | Effects carrying light | Ground |
|---|---|---|---|---|---|
| [Intro](still-006.000.jpg) | 6 s | the knight's slash in his warm pool | outlined, alone | the slash arc and the hero's light | camp glade's grass with twig and pebble patches, the dirt path |
| [First wave](still-030.000.jpg) | 30 s | the hero ringed by skeletons | outlined over the grunts in front | the downbeat ring and the hero's light | the moss clearing's edge and the dirt path |
| [Caster volley](still-089.350.jpg) | 89.35 s | the hero under five bolts in view | outlined in the crowd | bolts in flight and their impacts | dirt and grass round the stream, the bridge |
| [Elite kill](still-119.700.jpg) | 119.7 s | an ogre falling to the hero's blow inside the ring | outlined, mid-swing | ring, lightning, hit sparks | the bridge-head's stone and dirt, the stream and cliff |
| [Boss entry](still-166.000.jpg) | 166 s | the hero's ring, the boss walking in at the right | outlined at the centre | ring, a caster bolt's fire trail, a bolt impact | the great clearing, the dressed ledge |
| [Climax](still-176.000.jpg) | 176 s | the hero at full density, a bolt impact over him | outlined at the centre of about 400 | impacts, hellfire, sparks | mostly crowd; the dressed ledge and dirt round it |

## Readability

[readability.txt](readability.txt): every type's walk frame lit by the night, scored by colour (ΔE
from the ground and the nearest type, under the moon and near the hero) and by silhouette (overlap
with the most similar mask, feet aligned). PASS: every type is distinct by silhouette (largest
overlap 0.59, between elites); by colour alone most are not (nearest ΔE 4.5 to 11 under the moon, as
PX3 found: the night pulls them together), so silhouette carries them. The hero's colour sits within
ΔE 5.3 of the cyclops, so his outline does the work: ΔE 64 from the ground and 52 from the nearest type.

## What changed

- **The hero** draws over the grunts in front of him and carries a one-texel warm outline.
- **The ground** varies: plain variants and patches of the sheet's detail tiles per ground; the
  plateau's top too, and a ledge-top landmark of plants dresses it (it was a flat rectangle in the
  climax's view). The song's timeline is unchanged by the dressing (a hash of every entity's
  positions every 0.5 s matched before and after).
- **Effects:** the death smoke is smaller, fainter and shorter (it buried the hero in white clouds);
  the downbeat ring is dimmer and lives less; casters' bolts are larger and lit brighter.
- **Night:** a brighter, less blue moon (0.24 to 0.32), a stronger hero light, fainter shafts with a
  wider falloff (their parallelograms read as hard edges), exposure 0.15 to 0.25.
