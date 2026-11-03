# Orbit Blades and Sentinel — ps-3wc.9

Runtime Unity crowd captures inspected on jarvis, 2026-11-02. All eight forms
are bound in the survivor gameplay simulation and presentation. The private
vendor-derived bakes and images remain outside git.

## Reproduce

From an Enact worktree:

```sh
uv run --project rhythm_champions/tools champions setup
uv run --project rhythm_champions/tools champions build --development
uv run --project rhythm_champions/tools champions stills --scene gallery --out /tmp/construct-gallery
```

The existing gallery is retained. Added 144 entries: eight forms ×
normal/empowered × nine phases, labeled `orbit-circle`, `orbit-sweep`,
`orbit-scythes`, `orbit-crown`, `sentinel-sentry`, `sentinel-combo`,
`sentinel-leap`, `sentinel-formation`, followed by power and phase. Samples
show +.025 through +.625 seconds after activation in .075-second increments,
rounded to the actual capture frame. These are actual runtime draws of the
same action clocks used in gameplay, with the game camera, post, grid and
22 baked skeletons. They are ordered motion samples, not a continuous
played six-weapon run. Each entry has its ID pass, view and draw-cost report.

Private evidence on jarvis:
`/Users/dthurn/Library/Caches/playlist-survivors/champions/evidence/ps-3wc.9/`:
`gallery/`, `orbit-forms.png`, `sentinel-forms.png`,
`sentinel-motion-phases.png`, and eight `*-motion.gif` sequences assembled
from the nine runtime frames without interpolation. Full source frames,
rather than cropped review sheets, establish game-scale/crowd readability.

## Visual findings

The normal/empowered sheets and ordered motion samples were inspected.

| Form | Action and critique |
| --- | --- |
| Orbit 1 | A pair of closed dark swords circles the hero. The blade edges read white in the cyan crowd without a filled disk. The small crossguards are busier than the blade, but their silhouettes stay distinct. |
| Orbit 3 | Three blades sweep outward and return, with two retained inner guards. The different blade count, radial movement and inner guard read apart from the pair; this is not just the same effect enlarged. |
| Orbit 5 | Alternating outer scythes and inner blades add two long lobes while retaining close coverage. It remains sparse enough that the crowd's dark gaps survive. |
| Orbit 8 | Four connected blade arms open around a retained inner guard. The first prototype shortened alternate arms like form 5; this was corrected so the crown has four full outer arms matching its four damage lobes. Closed sword meshes serve as arms, rather than open bright lines. |
| Sentinel 1 | A white knight with an independent spectral sweep beside the hero. Its armor is denser than the swords, but stays confined to one smaller-than-hero figure. It uses evaluated sword poses, never a rest pose. |
| Sentinel 3 | Alternating parry and sword roles, retaining the basic sweep and adding a forward combo lane. A first gallery prototype sampled the same slash branch as form 1; the final combo entry samples its parry branch so the shield posture can actually be judged. |
| Sentinel 5 | The knight follows a short root arc while sampling the final .35 seconds before the existing leap clip's contact at natural speed, then settles. An independent close guard remains below it. The phase sheet shows ascent, forward travel and landing; no new rig retargeting is claimed. |
| Sentinel 8 | Three knights spread laterally and advance, with staggered parry/leap roles and the retained close guard. Three separate dark silhouettes survive at game scale; there is no accumulating army. |

Activation rings are thin, low-gain white bands, not screen flashes. Empowered
reach swells at activation and settles over .4 seconds for Sentinel/.5 for
Orbit. Compare early phases for the full enlargement; a late resting frame
is deliberately almost the same size. Sentinel's body flourish runs after
the beat pulse: damage belongs to the independent spectral volley, not to
an accelerated body clip or an additional collision on its landing. Its
landing/formation coverage is part of that finite volley. No new shader or
shader keyword was introduced, and `--look trailer`'s renderer is untouched.

## Sources and local baking

Reuse [combat-assets.md](../../../docs/champions/combat-assets.md) sources S,
K and SH, already imported by the manifest: Knight broadsword
`Assets/Vendor/PolygonKnights/Prefabs/Weapons/SM_Wep_Broadsword_01.prefab`
(`f61def225eac3e247985f6679965a655`), knight prefab
`Character_Knight_01_Orange.prefab` (`360c73b9bf9a34d4c84bc0581d0499eb`),
and its shield dependency (`a7b97d24ba2370040ba159577de7b931`).
`MeleeBaker` normalizes the rigid sword to unit length and closed feature
facets; `MonsterBaker`/`MonsterRigBaker` provide the existing hero figure and
`slashA`, `parry`, `leap` rig roles. `ConstructVisuals` uses the working
`RigPose`/`MonsterFigure` path, not matching-name bone transfer. The thin
activation ring reuses Hovl `Slash3` from `EffectBaker` through the existing
recipe renderer. Build reruns the existing bakes; no new vendor import,
manifest entry, generated mesh, image or Blender file is necessary in git.

## Runtime and verification

- All eight levels increase damage; forms switch at 3/5/8. Orbit retains its
  filled close sweep as outward lobes are added. An isolated target on the
  alternate scythe axis cannot leave the weapon stuck holding that phase.
- Sentinel anchors a roster alongside the hero for 2.4 seconds, with a new
  roster eligible after four seconds. Persistence increases its lifetime and
  volley uptime (at fallback cadence, level-5 Persistence adds the fourth
  volley). A refresh replaces the roster; three knights is the maximum.
- Might, Tempo, Reach and Echo apply to every form; Sentinel also accepts
  Persistence. Tempo adds independent pulses, never faster body motion. A
  target receives one hit per independent strike across overlapping guards,
  cleaves and formation members. Echo is one reduced independent repeat;
  it captures origin, aim, stats and empowerment. Its short spectral strike
  can finish after the original roster expires, without prolonging the next
  roster. Unsupported projectile supports do not gain artificial behavior.
- No target holds empowerment and fractional strike credit. Empowerment is
  1.5× area, changes no damage, retains the inner footprint, and is gone on
  the next normal activation. State hashing includes the anchored roster.
- 333 simulation tests pass, including every level/form, overlap, exact
  Tempo/empowerment accounting, no-target holds, isolated scythe targeting,
  Persistence uptime and chunked replay/held-world behavior.
- 113 Unity EditMode tests pass, including repeated volleys replacing a
  bounded roster and expiring; gallery recipe coverage also passes.
- Development player builds successfully. Incremental shader report: 277
  variants against budget 300; no shader change in this delivery.
- Standalone public-API probe: default Sword plus both constructs and five
  supports, 48 targets, 12 seconds, at levels 1/3/5/8. Each produced 70 Orbit
  actions and 34 Sentinel actions including Tempo/Echo. Whole-run and .1 s
  chunked runs matched hashes `F67A5B99EDEDA801`, `40397B6EBF2BCCC4`,
  `4D9FE585042AD5E6`, `7C04AF8FCE5D38A6`; total damage increased at each form.

Speaker balance, operator taste and target-hardware timing in the assembled
six-weapon run remain in [operator-pending.md](../operator-pending.md).
