# Combat asset probe — ps-3wc.1

Performed 2026-11-02 on jarvis, Blender 5.2.3 LTS. This is a source/motion
feasibility review for [combat-assets.md](../../../docs/champions/combat-assets.md),
not acceptance of final gameplay art. No runtime registry or trailer renderer
was changed. The schema executor confirmed the same support matrix: all eight
Might/Tempo/Reach/Echo; Bow/Spear/Lightning projectile mechanics; only Ember
Field/Sentinel Persistence; Sentinel remains a melee summon.

## Private evidence and reproduction

`tools/recipes/combat/preview.py` renders 30 PNGs: six examples × five ordered
phases, at 640×360, on a dark grid with seven cyan faceted crowd stand-ins and
one retained red threat line. See the mapping document's invocation. Private
outputs on this host: `/tmp/ps-3wc-preview/`; selected source FBXs under its
`sources/`; render log `/tmp/ps-3wc-assets/render.log`. Temp files can disappear:
the committed recipe and exact source index are the durable reproduction.
Do not commit or publish vendor-derived renders, GIFs, meshes or Blender files.

An initial inspection/contact sheet exposed a gray default Blender world, a
center-pivot hammer and T-pose sentinel. The recipe was corrected to a dark
world and a measured handle pivot. Final phase PNGs were visually re-inspected.
The point of these findings is to reject unsuitable assumptions before the
weapon tasks, not to approve art from a metric.

A separate diagnostic run with `--retarget-probe` samples the actual Synty
`A_Attack_LightCombo01A_Sword.fbx` and transfers matching-name bone rotations
to the knight. **Rejected:** equal bone names do not imply equal bind frames;
the knight stretches into long rays. Never use that transfer in runtime.
Use the existing Unity `MonsterBaker`/`RigPose` path, which already supplies the
knight's slash/parry/leap roles, and freeze a sampled pose if a rigid construct
is needed. The default recipe renders the rest-pose rigid source as an honest
geometry diagnostic; it does not call a T-pose a suitable sentinel animation.

## Visual findings

| Example | What the actual frames establish | Critique and implementation decision |
| --- | --- | --- |
| Hammer swing → slam | Real Dungeon hammer has a readable head and shaft at game-like size. Horizontal handle rotation reads differently from overhead descent; an expanding contact ring is distinct from a side swing. | First center-pivot motion looked like spinning a prop. Corrected to thin-end handle pivot; final slam head reaches the ring at ground height. Full topology is still denser than desired; use measured creases over dark core, not every triangulation line. Author the head contact point explicitly, not mesh center. No hammer humanoid clip was validated or required. |
| Bow fan | Real BowAndCrossbow arrows separate into five lanes and advance through the stand-in crowd, leaving dark gaps. Bow mesh imports independently of a hero. | The bow reads as a thin white curve at this view; add authored string pull/recoil and keep the bow's plane readable. Arrows need measured local flight axis. There is no demonstrated knight archer animation. Use independently aimed rigid bow constructs, never a 2D clip or a filename claim. |
| Lightning | Connected angular link geometry gives a branching action distinguishable from arrows and rings. Red threat stays visible through gaps in this sparse scene. | These are geometric prototypes, not native Hovl particle playback. Straight long bars are too mechanical for final lightning: use short irregular connected prism segments, brief discharge, endpoint accents and actual chosen targets. Avoid sustained white webs. |
| Ember Field | Retained outer seal, expanding inner pulse and rising fissure strokes are distinct phases while the red line remains visible. | Outline-only prototype is sparse enough to preserve floor visibility; final fissures need closed facets/dark bodies, and the L5 brazier must be an actual moving bowl/pour, not another larger ring. Native colored fire/smoke is not approved. |
| Sentinel | Actual knight mesh imports and moves as an independent root; the naive clip transfer visibly fails. | The rigid bind T-pose is **not acceptable** final motion, and every triangle lit makes a white clump. Select an existing evaluated slash/parry pose through Unity's working rig, or play those roles on `MonsterFigure`; reserve dimmer white for copies. No new retargeting or mannequin clip is claimed ready. |

The five-phase sequences were inspected as ordered motion storyboards, including
wind-up/impact/settle. They are not a recorded Unity playthrough or a frame-rate
animation judgement. Seven stand-ins establish only a small composition, not
600–1,000 enemies, six simultaneous weapons, final camera/post, boss legibility,
performance or speaker timing. Those remain assembly checks, and operator taste
checks are in `evidence/operator-pending.md`. The family tasks must supply actual
Unity motion/crowd evidence for their delivered forms; these prototypes cannot
substitute for it.

## Verified delivery checks

- Every listed GUID/path was resolved against the owned archive or local asset
  and meta file; 46 source references are recorded without vendor bytes.
- Blender completed both geometry/choreography and diagnostic retarget probes;
  the retarget failure is retained as a finding, not papered over as readiness.
- Tools lint and required fast checks run before staging delivery; the Enact
  commit/push record is the authoritative result. No runtime behavior changed,
  so no new sim test that merely mirrors the asset table was added.
