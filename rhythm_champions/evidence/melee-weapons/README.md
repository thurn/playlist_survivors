# Sword and Hammer — ps-3wc.7

Reviewed on jarvis, 2026-11-02. Runtime Unity render evidence, not the earlier
Blender asset probe. Private vendor-derived images and bakes are deliberately
not committed. Reproduction is the committed gallery, baker and manifest.

## Reproduce

From an Enact worktree:

```sh
uv run --project rhythm_champions/tools champions setup
uv run --project rhythm_champions/tools champions build --development
uv run --project rhythm_champions/tools champions stills --scene gallery --out /tmp/melee-gallery
```

The gallery preserves its 29 previous entries and adds 112 melee entries:
eight forms × normal/empowered × seven phases. Their labels begin
`sword-sweep`, `sword-cross`, `sword-spin-form`, `sword-dance`, `hammer-swing`,
`hammer-slam`, `hammer-eruption`, `hammer-procession`. Phases sample the actual
runtime transform at −.25, −.175, −.10, −.025, +.05, +.125, +.20 seconds from
impact (rounded to the 60 Hz capture frame). Same game camera, grid, post and
22 baked skeletons, with a hero-sized white marker. These are ordered runtime
motion samples, not a continuous recording or a full six-weapon playthrough.

Private final outputs on jarvis:
`/Users/dthurn/Library/Caches/playlist-survivors/champions/evidence/ps-3wc.7/`:
`gallery/` holds original PNGs, ID passes, view files and `gallery-costs.json`;
`sword-forms.png`, `hammer-forms.png` and `motion-phases.png` are review sheets;
`*-motion.gif` concatenate the seven actual samples, without interpolation.
These caches can be removed; the source above regenerates the evidence.

## Visual critique and changes

The final normal/empowered contact sheets and ordered wind-up/contact/settle
samples were inspected. Each form changes the action, not only its scale:

| Form | Visible action / verdict |
| --- | --- |
| Sword 1 | One dark broad blade with connected white facets sweeps an oriented lane. Its guard/grip has more detail than the blade, but the blade silhouette stays clean at game scale. |
| Sword 3 | Two intersecting blade sweeps and crossing arcs. Distinguishable from the single sweep without adding a solid white wedge. |
| Sword 5 | A blade turns around the root and the footprint closes into a full ring. The dark interior and crowd gaps survive. |
| Sword 8 | Three blades advance along the captured aim while sweeping; the traveling outer ring reads apart from the centered spin. Initially too dim/edge-on; wider feature strokes and brighter primary edges corrected it. Copies remain below the primary edge gain. |
| Hammer 1 | A broad closed head and narrow shaft swing horizontally. Clearly different silhouette from Sword. |
| Hammer 3 | An overhead pitch lands the head at ground height in the circle. Initial moving-handle compensation kept the head fixed: rejected and changed to a fixed handle pivot with a real falling head. |
| Hammer 5 | The hammer root vaults upward before contact, with a short row of rising faceted blades as fissure/spike constructs. Sparse ground strokes preserve crowd visibility; the spikes are deliberately small, so the vault is the primary distinguishing action. |
| Hammer 8 | One current hammer falls, with two dimmer elevated upcoming hammers extending the lane. Damage placement advances over three successive grid activations; the upcoming copies are visual previews, not extra off-grid damage. |

Empowerment expands useful coverage by sqrt(1.5), including rigid length,
footprint and advancing placement, with no damage bonus or extra legacy 1.15
visual multiplier. Final captures visibly enlarge both weapons without a
screen flash or filled white area. Main Sword body action uses the existing
knight slash at natural speed; all Hammer actions, Tempo strikes and Echo
repeats are independent spectral constructs. No hammer humanoid clip or
retargeting is claimed. The gallery samples the constructs, not body playback.

The gallery costs include the rigid meshes: 2/3/2/4 draws for Sword forms,
2/2/7/8 for Hammer, maximum 4,072 submitted triangles per cue including its
thin recipe layers. No additional compute or particles. This is a structural
cost count, not a GPU timing measurement. Development build: **277/300**
compiled shader variants. No trailer crowd shader was modified.

## Rules and integration evidence

`MeleeWeaponsTests` exercises all eight levels of both weapons, inherited
Might/Tempo/Reach/Echo, increasing per-level damage, one hit per overlapping
strike, one reduced Echo repeat, no legacy duplicate strike, empowerment
consumption/radius, crowd aim, advancing lane/falls, recycled enemy slots,
nested support queries and v5 input round-trip/replay determinism.

`SimConfig.SurvivorWeapons` installs `Simulation.Melee` and its two scheduler
bindings. New normal games explicitly enable it through `RunInputs`; v1–4
sessions leave it off. The archived Arsenal's timing queues are drained without
executing its attacks in this mode. Other family handlers can bind their
reserved IDs to the same scheduler. The build continues to own six slots and
all support/form contracts. Progression/card migration and complete run
assembly remain their existing ps-3wc owners; this bead adds no alternate
progression implementation.

Local validation: 16 focused tests and all **300** sim tests pass,
Unity EditMode **111/111**, and the
final development build/capture exited 0. Enact fast checks validate the final
rebased tree. Speaker timing, operator taste, full six-weapon/boss composition
and hardware frame time remain operator/assembly checks, not claims made by
these sparse gallery samples.
