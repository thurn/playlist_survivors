# The crystal cut's last style-guide breaks (ps-wnj.29, 2026-11-01)

ps-wnj.19 left the crystal cut ([the parent folder](../README.md)) breaking
four of the style guide's rules besides swarm separation (ps-wnj.28): the key
in shot 3 and a few other stills, bright share in the duel, and prism's floor
p99 at 12 and 36 s. All four pass now in every preset. The recipe is the
parent's, on an IL2CPP Mac player of this change; the "before" stills are
from a player of staging at d8dd782d.

## Files

- `sheet-PRESET.jpg`: the twelve stills of each preset, as the parent's.
- `metrics-PRESET/`: every rule on every still, and the cut's.
- `shot3-before-after.jpg`: shot 3 at 20 s in each preset, before and after.
- `id-render.jpg`: prism at 12 s and its ID render, which now draws the
  structures (the crystal formations and their shards) blue and the
  effects' light green.

## What broke, and the fixes

| preset | before (ps-wnj.19's IL2CPP stills) | after |
| --- | --- | --- |
| edge | separation 10, bright share 1, key 1 | separation 8 |
| solid | separation 3, bright share 2, key 2 | separation 1 |
| hologram | separation 7, key 4, bright share 1; the cut's key (0.616) | separation 6 |
| prism | separation 5, key 3, floor p99 2, bright share 1 | separation 2 |

The cut's key median is now edge 0.86, solid 0.85, hologram 0.80, prism 0.74
(ps-wnj.19: 0.74, 0.68, 0.62, 0.63).

1. **The key is staged.** A type's share of a still's colour is not its
   share of the mix. An elite, three times a fodder's size, took 40-48% of
   shot 3's colourful pixels in edge, solid and hologram from 8% of the mix.
   The accent goblins draw far more colour than the key skeletons: at 36 s
   edge's goblins held 73% of the colour from 3 parts in 12, since a vortex
   brings its squads by in clumps. And the key light slot's pale hues
   (ion's `#1fe2ff`, spectrum's `#6cd4ff`) render at C* 17-28, mostly under
   the colourful cut (C* 30); cyan gets little deeper at that lightness
   (`#00b4ff` is C* 49). So the mixes changed, in trailer-crystal.json:
   - shot 3: `skeleton 5, demon 4, bigork 0.3, troll 0.2` (was skeleton 4,
     goblin 3, demon 2, bigork 0.6, troll 0.4): the key family and a few
     elites, no goblins. 20 s keys at 0.81-0.92 (was 0.32-0.44).
   - shot 4: `skeleton 5, bat 2, slime 2, demon 1` (was skeleton 4, goblin
     3, bat 2, slime 1): all key family. Half a part of goblins still left a
     clump that put prism's 36 s at 0.46.
   - shot 5: `skeleton 4, goblin 2, mushroom 1, demon 4` (goblins were 3,
     mushrooms 2, demons 2); shot 6: goblins 2 (were 3), demons 3 (were 2).
   - shot 8, the finale: no goblins (were 3), demons 3. 56.3 s keys at
     0.58-0.75 (was 0.41-0.67).
   Every trailer check still passes, under dotnet (`trailer --name crystal
   --checks`) and Unity (TrailerTests). The density check (each bar's
   monsters in view within 15% of the target) swings with the mix, and the
   two runtimes' float rounding drifts apart: a shot 3 mix that passed
   dotnet dipped to 347 against 440 in Unity. Shots 3 and 5 take the mixes
   whose bars stayed within 9% under dotnet, and both pass.
2. **The duel is a staged sparse still.** Three elites alone in the void
   cover 3% of the frame, and its bright share is 0.011-0.018 in every
   preset; the sparsest crowd still covers 18%. `bright_share_min` now skips
   a still whose monsters cover under 10% (`unless_below`, on the new
   `monster_share` metric from the ID render). Its light is still judged by
   the hero margin and readability.
3. **Effects are not floor.** Prism's floor p99 at 12 and 36 s (28-37) was
   its effects: the three-ring slam and the three-ribbon swings fill more of
   the floor than one ring, so the floor's top 1% landed on them, while its
   grid lines measure 10. The ID render didn't mark effects; now
   EffectParticles draws green there where an effect's light shows (linear
   luminance over 0.12, before the post: 0.03 marked half its pixels on
   floor that showed nothing), and look-metrics leaves effects out of the
   floor and the background. Prism's floor p99 is 9.5 and 11.0 there.
4. **The formations were monsters.** CrystalProp drew its gem colour in the
   ID render, whose red channel counted the formations and their shards as
   monsters, in the hero margin, monster form and the new monster share. They
   draw the structures' blue now.
5. **Solid's violet was under its rung.** arcade's key `#a84dff` (L* 53)
   rendered at L* 43 (p90 49) under solid's shading, below the style guide's
   L* 50 for solid bodies, and the all-violet 28 s broke bright share
   (0.021). It is `#bc72ff` (L* 62, the same hue): 28 s measures 0.078.

## Still breaking

- **Swarm separation** (ps-wnj.28): edge in eight steady stills, hologram
  six, prism two, solid one.
- Prism's 28 s bright share is 0.044 and its 47.2 and 56.3 s keys 0.58-0.59:
  passing, near their thresholds.

## Checks

- `trailer --name crystal --checks`: ok; the choreography tests under dotnet
  (281) and Unity's EditMode run, TrailerTests among them, pass.
- `demo3d look-metrics`: the rules above, nothing else broken; the halo on the
  impact frame measures edge 21.2, solid 34.6, hologram 22.6, prism 18.1.
- The look tools' tests (`uv run --project demo3d/tools pytest`), with
  `monster_share`, effects left out of the floor and `unless_below`.
