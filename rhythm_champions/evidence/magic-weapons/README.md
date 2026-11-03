# Lightning and Ember Field — ps-3wc.10

Reviewed on jarvis, 2026-11-02. Unity runtime geometry and actual sim-event
captures; private generated images/bakes are not committed.

## Reproduce

```sh
uv run --project rhythm_champions/tools champions setup
uv run --project rhythm_champions/tools champions build --development
uv run --project rhythm_champions/tools champions stills --scene gallery --scene magic-combat --out /tmp/magic-gallery
```

Gallery retains all previous entries and adds 97: eight forms × two coverage
states × six ordered runtime phases, plus a bolt-link sample. Labels start
`lightning-bolt`, `lightning-fork`, `lightning-sigil`, `lightning-constellation`,
`ember-seal`, `ember-fissure`, `ember-brazier`, `ember-procession`. Phases sample
.025/.105/.185/.265/.345/.425 seconds after activation, rounded to capture
frames. Same camera, post, grid, 22 baked skeletons and a red warning lane.
The gallery uses authored representative bolt endpoints. `magic-combat` uses
**actual simulation events and targets**, with the baseline Sword retained,
Lightning and Ember Field, Tempo/Persistence/Pierce/Echo, and levels 1/3/5/8.
Its eight normal/empowered stills are at 1.55/3.05 through 10.55/12.05 seconds.
The scene directly grants the same build empowerment as a dash to keep the
camera/target arrangement fixed; it does not claim to exercise a dash input.

Private outputs on jarvis:
`/Users/dthurn/Library/Caches/playlist-survivors/champions/evidence/ps-3wc.10/`:
`gallery/` contains 246 PNGs with ID passes, view metadata and cost JSON.
`lightning-forms.png`, `ember-forms.png`, `actual-combat.png` and
`motion-phases.png` were inspected. Eight `*-motion.gif` files concatenate
actual ordered rendered samples without interpolation. These are short runtime
samples, not a continuous played six-weapon/boss session or GPU timing.

## Visual critique

| Form | Observed read at game scale |
| --- | --- |
| Lightning 1 | Thin angular connected bolt, small closed endpoint node; clean against cyan bodies. White strokes stay sparse. |
| Lightning 3 | Raised Y-shaped casting gesture and separate roots, distinct from the single line; no filled fan. |
| Lightning 5 | Low octagonal sigil with three spokes becomes the bolt origin. Clearly separates the construct from hero position. |
| Lightning 8 | Articulated linked nodes move from the previous cardinal position toward the current discharge point and turn during settle; thin sigil remains. Small at game scale, but the moving elevated nodes distinguish it from the static sigil in ordered phases. |
| Ember 1 | Closed octagonal ground seal with a dark empty centre. Empowered radius is visibly larger. |
| Ember 3 | Connected jagged fissure extends beyond the seal; low pointed nodes erupt then settle. Thin gaps leave the warning readable. |
| Ember 5 | Closed faceted bowl tilts and a short stream grows toward the ground, with retained seal and fissure. Bowl reads as a construct rather than bright fire. |
| Ember 8 | Second seal advances through three placements on successive activations; low triangular eruptions distinguish it from the single brazier. Earlier components remain. |

Normal/empowered sheets show greater coverage without a solid white puddle.
The red warning lane stays continuous between the sparse strokes in both the
isolated and combined scene. The combined final form is busier, especially
with the baseline Sword/Encore ring, but dark ground and cyan crowd gaps remain.
The six-phase sheet verifies bolt decay, moving constellation nodes, fissure
rise/settle, bowl tilt/pour and eruption settle using actual runtime poses.
No numerical image score accepted these visuals.

## Sources, bake and cost

Exact Hovl U1–U5 paths/GUIDs are in `docs/champions/combat-assets.md` and now
manifest entries: Lightning strike, electro projectile, electro/fire circles
and Meteor hit 2. The selected, previously documented fallback is used:
authored jagged closed prism links, octahedral nodes and a closed eight-sided
brazier shell, rather than unverified stock particle shaders. `MagicBaker`
reproducibly creates them under ignored `Assets/Baked/Effects/Resources/`;
`StyleMesh.Unweld` adds closed feature edges over dark cores, using the existing
MeleeEdge shader and effects ID 252. No vendor bytes/derived art are tracked.
Empty JSON layer lists are intentional for these rigid cues; `MagicVisuals`
owns their geometry. No render foundation or trailer shader changed.

Maximum primitive costs per construct cue: 1/4/11/14 draws and
8/40/132/156 triangles for Lightning, plus 6 draws/68 triangles per actual bolt
link; Ember 8/16/19/30 draws and 96/176/264/384 triangles. These are submission
counts, not measured hardware time. Idle seals replace prior pulses at the
same anchor, so Tempo/Echo cannot accumulate idle copies. Shared shader budget:
**277/300** in a clean-cache variant build. Magic adds no shader variants or
compute dispatches. Quiet owned Hit/Slam cues are reused per discharge/pulse,
not per chain segment; empowered activation keeps the existing Accent.

## Rules and tests

`Simulation.Magic` binds both stable IDs when `SurvivorWeapons` is enabled.
Lightning roots search a 7 m hero-centred circle, chain hops are at most 3 m
(both grow with sqrt coverage), and every chain/fork is still bounded by the
original search circle. Nearest distance then stable enemy ID breaks ties.
Forms author 1/2/2/3 roots and 1/2/3/4 chain targets. Multishot adds distinct
roots, Pierce adds chain targets, Split adds bounded reduced forks from captured
impact positions. Shared generation-aware strike ledgers prevent boss farming
and avoid recycled-slot confusion. Forks never revisit their parent target,
even when echoed. Tempo supplies independent strikes; Echo supplies one reduced
repeat with the shared lineage budget, rather than recursive spawning.

Ember places at the hero and stays there for 3 seconds; Persistence extends
that lifetime before the next placement. Each cadence activation pulses the
retained seal, fissure lane, brazier ribbon and current procession placement
through **one shared target ledger**. Additional forms add coverage, not
extra overlapping damage on one target. Might/Tempo/Reach/Echo remain active
through all forms; Multishot/Pierce/Split remain Lightning-only. Every level
adds inherited damage. Empowerment is coverage-only for one activation and
its children, with the idle ring settling back afterward. Holds shift lifetime
and Echo deadlines, freeze visual age, and cause no catch-up damage burst.
A pending Echo exposed a shared scheduling bug: its deadline is now hidden
while held so it cannot pin the simulation's clock.

Validation: all **352** dotnet sim tests (19 focused magic cases); **115/115**
Unity EditMode tests; development builds, clean-cache variant check and
246-still runtime capture passed. Tests cover every level, all form support
inheritance, one boss hit per strike plus reduced Echo, root/hop limits,
L1 Multishot/Split utility, crowd proc bounds, stationary placement/Persistence,
long pauses with pending Echo, one-pulse empowerment, moving-node targeting,
and identical single-frontier/incremental playback hashes. Renderer tests
cover paused lifetime and bounded pulse geometry. Enact validates the rebased
final tree. Full six-weapon/boss composition, speaker balance and hardware
frame time remain operator/assembly checks.
