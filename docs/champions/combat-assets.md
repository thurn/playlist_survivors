# Combat asset map (ps-3wc.1)

Implementation handoff for the 2026-11-02 combat design. Mapping and disposable
prototypes only: this does **not** declare the new combat shipped. Four forms
mean levels **1, 3, 5, 8**; 2/4/6/7 improve stats. The eight positioning roles
and all breakpoints in [design.md](design.md) remain unchanged.

## Decision and evidence standard

Use the existing knight sword clips where already baked. Hammer, spear and
bow are independently moving **spectral weapons**, not promises of new
retargeted knight animations. Sentinel uses the knight rig with existing sword
roles, with a knight frozen in a sampled existing rig pose plus an independent
weapon sweep as the fallback. A rest-pose/T-pose knight is rejected.
Lightning and Ember Field use authored connected
geometry and timed pulses. Six equipped weapons never fight for the hero's
body animator. Do not speed a body clip beyond 1.15×; extra strikes are spectral
copies or grid-bound attacks. Do not use 2D HeroEditor animations.

Status vocabulary:

- **Catalogued**: exact owned file/GUID exists. This does not establish animation,
  render-pipeline or shader suitability.
- **Existing bake**: current `monsters.json`/renderer uses the role, with its
  measured impact/end. This is evidence for that role, not every proposed combo.
- **Probe**: disposable Blender composition was rendered and inspected. It tests
  source geometry and authored motion, not Unity retargeting or final game art.
- **To author**: implementation required in the owning weapon/encounter bead.

The source prefabs' stock colorful additive particles are **not ready** for the
white hero/dark-body style. The chosen default is authored geometry through the
existing render foundations. Optional native prefab ports require their own
Unity motion preview before use; none below is a dependency on that experiment.

## Exact private sources

Paths beginning `Assets/` in the Synty table are **archive pathnames** in the
named `/Users/dthurn/synty/` package, not paths in a frozen project. Extract via
`champions setup` after adding selected entries to `manifest.json`; preserve GUIDs
and dependency closure. The disposable recipe extracts only FBXs to its private
output directory. A `.unitypackage`'s `<GUID>/pathname`, `<GUID>/asset`, and
`<GUID>/asset.meta` identify the source precisely.

| Key | Package in `/Users/dthurn/synty/` | Exact archive pathname | GUID | Status / work |
| --- | --- | --- | --- | --- |
| H | `POLYGON_Dungeon_Unity_2021_3_v1_9_5.unitypackage.gz` | `Assets/PolygonDungeon/Models/SM_Wep_Hammer_Large_Metal_01.fbx` | `08c18a17006ad9a4eb9d96fe947f2af0` | Probe; rigid mesh, normalize handle pivot and head direction; no hammer body clip selected |
| P | same Dungeon package | `Assets/PolygonDungeon/Models/SM_Wep_Spear_01.fbx` | `d9846e0c0b6923d48a1a17570257c243` | Catalogued; normalize shaft axis/tip; rigid authored thrust |
| B | `POLYGON_BowAndCrossbow_Unity_2021_3_v1_0_1.unitypackage.gz` | `Assets/PolygonBowCrossbow/Models/Rigged_Bow_Testing.fbx` | `2e3787f6a1057be42bdf7fb80d0d69b4` | Probe geometry only; do not require its rig/animation; rigid recoil plus authored string |
| A | same BowAndCrossbow package | `Assets/PolygonBowCrossbow/Models/SM_Arrow_01.fbx` | `1c5b37fd30472d24fbd3e315d52fafaf` | Probe; normalize arrow tip/flight axis separately from bow |
| K | `POLYGON_Knights_Unity_2021_3_v1_5_1.unitypackage.gz` | `Assets/PolygonKnights/Prefabs/Characters/Character_Knight_01_Orange.prefab` | `360c73b9bf9a34d4c84bc0581d0499eb` | Existing hero bake, 1.85 m; sentinel reuses this figure |
| KM | same Knights package | `Assets/PolygonKnights/Models/Characters/Characters_POLYGON_Knights.fbx` | `d288a0e4a2d923b46898986e979da643` | Probe selected `Character_Knight_01`; whole FBX also contains other figures/items, never draw them all |
| S | same Knights package | `Assets/PolygonKnights/Prefabs/Weapons/SM_Wep_Broadsword_01.prefab` | `f61def225eac3e247985f6679965a655` | Existing hero dependency; rigid spectral sword alternative |
| SH | same Knights package | `Assets/PolygonKnights/Prefabs/Weapons/SM_Wep_Shield_01.prefab` | `a7b97d24ba2370040ba159577de7b931` | Existing hero left-hand shield |
| T | `POLYGON_Fantasy_Rivals_Unity_2021_3_v1_3_1.unitypackage.gz` | `Assets/PolygonFantasyRivals/Prefabs/Characters/Character_BR_Troll_01.prefab` | `2250a4a4fc528684c85ba955eb992ec6` | Existing troll bake; Conductor starts from this, not a new purchase |
| TW | same Fantasy Rivals package | `Assets/PolygonFantasyRivals/Prefabs/Weapons/SM_Wep_Troll_01.prefab` | `39e0fb4b243ffb645a622d1945004ee5` | Existing troll right-hand weapon; conductor's baton gesture can reuse it |

Except O (the separate MoreAssets root below), these paths are relative to the exact local root
`/Users/dthurn/unity_assets/UnityAssets/Assets/`. U1–U6 are **catalogued candidates**;
we choose authored replacements, so none is called a ready runtime effect.

| Key | Exact relative path | GUID | Suitability / selected fallback |
| --- | --- | --- | --- |
| U1 | `Hovl Studio/AOE Magic spells Vol.1/Prefabs/Lightning strike.prefab` | `dfabbd05c7735314e97de47b3d7695c4` | Lightning timing reference; use authored segmented bolt links instead of unvalidated stock shader |
| U2 | `Hovl Studio/AOE Magic spells Vol.1/Prefabs/Meteor hit 2.prefab` | `a7cb112e9339d01408d0002aede091cf` | Eruption reference; low faceted spikes and `LeapSlam` ring replace stock smoke/fire |
| U3 | `Hovl Studio/Magic circles/Prefabs/Magic circle fire.prefab` | `7558be78bd626304dbbc627760117bd5` | Seal reference; closed polygon ring, radial fissures and beat pulses |
| U4 | `Hovl Studio/Magic circles/Prefabs/Magic circle electro.prefab` | `b2b32816444d787499f1e6efe5c08f7f` | Storm sigil reference; sparse polygon plus explicit bolt endpoints |
| U5 | `Hovl Studio/AAA Projectiles Vol 1/Prefabs/Projectile VFX loop/Projectile 2 electro.prefab` | `93070f52828035b4199ff2f22368a3de` | Travel reference only; do not import looping particle lifetime as a damage rule |
| U6 | `KriptoFX/ArcherEffects/Prefabs/Effect6/ArrowEffect.prefab` | `5ce5c10aeaebad54fbd49fc812cdae53` | Arrow trail reference; A plus thin white path ribbon is the selected feasible default |
| O | `HeroicFantasyCreaturesFullPackVol1/Must Have Fantasy Villains Pack/Ogre/Prefabs/FatOgre.prefab` | `e040fed9364558541bb6583b064025c2` | Existing bake from `more_assets` source; source root is listed below |
| D | `Magic Pig Games (Infinity PBR)/Characters/Dragons/_Prefabs/Dragon_LP.prefab` | `a1e28cc5277b74ff0ad8566e5e54bee5` | Existing dragon bake, hide `saddle_LP1` |
| DM | `Magic Pig Games (Infinity PBR)/Characters/Dragons/Models/Dragon_LP.fbx` | `367e6650649952543b0c5dc153649036` | All selected dragon clips, exact names in boss table |

O's `more_assets` source root is
`/Users/dthurn/unity_assets/MoreAssets/MoreAssets/Assets/`, resolved by
`tools/src/champions_tools/paths.py`;
the authoritative manifest entry contains its source SHA. Do not silently substitute
a different Ogre pack. Synced selected assets live in ignored
`rhythm_champions/Assets/Vendor/`, preserving archive-relative paths after `Assets/`.
The literal private source root is recorded in the generated source index below.

### Existing effects and body clips

`Content/effects.json` is the exact recipe source for `SwordSlash`,
`SpectralSlash`, `SwordSpin`, `LeapSlam`, `LeapRing`, `BladeRain`, `Phantom`,
`Legion`, `orbitBlades`, etc. Their names describe the old combat too; reusing a
visual layer does not restore old dash types or the old evolution rules.
`EffectBaker.HovlDir` reads synced `Assets/Vendor/Hovl Studio/HSFiles/Models/`
`Slash1.fbx` through `Slash4.fbx` and `MagicSword.fbx`, and
`Textures/Noise43b.png`. [Source index](combat-assets-sources.json) records exact
original paths/GUIDs, including these dependencies, selected clips and bosses.
Do not copy their derived meshes into tracked files.

Knight animation source package:
`/Users/dthurn/synty/ANIMATION_Sword_Combat_Unity_2021_1_v1_1_1.unitypackage.gz`.
The exact FBX paths/GUIDs are in the index; role names and measured timing in
`Assets/Champions/Editor/Monsters/monsters.json`:

| Role | Clip | Impact / end seconds | Appropriate reuse |
| --- | --- | --- | --- |
| slashA | `A_Attack_LightCombo01A_Sword` | .350 / .717 | One sword sweep; no claim this is a hammer animation |
| slashB | `A_Attack_LightCombo01B_Sword` | .267 / .450 | Second sword cut |
| spin | `A_Attack_HeavyFlourish_Sword` | 1.433 / 1.650 | Full sword flourish; lead-in is long, schedule early or use spectral motion |
| stab | `A_Attack_HeavyStab01_Sword` | .883 / .983 | Existing stab pose; spear remains independent until its hand alignment is checked |
| leap | `A_Attack_LightLeaping01_Sword` | 1.100 / 1.167 | Sentinel sword landing pose, not a verified hammer vault |
| parry | `A_Parry_F_Sword` | .233 / .400 | Shield beat before a sentinel sword cut |

## Shared compatibility contract

This matrix was agreed with ps-3wc.2. Stable weapon IDs are `sword`, `hammer`,
`spear`, `bow`, `orbitBlades`, `lightning`, `emberField`, `sentinel`;
the schema bead documents them in `combat-contracts.md` and `Content/combat.json`. Every row applies to **all four forms**. A late form may add
components; it never drops an earlier interaction. Damage is resolved by the
sim; visual copies cannot create extra hits.

All eight accept Might, Tempo, Reach and Echo. Reach and dash empowerment scale
**coverage area** (circular radii by square root), with a visible swell/settle
for that activation only. Tempo adds fractional extra strikes/pulses on the
grid. Echo schedules one later grid activation, never itself recursively.
Attraction, Stride, Vitality, Armor, Regeneration and Landing Blast are global
supports and do not require a weapon-specific renderer feature.

| Weapon | Multishot | Persistence | Pierce | Split | Coverage / repeated-hit rule |
| --- | --- | --- | --- | --- | --- |
| Sword | — | — | — | — | Oriented melee footprint; one hit/target/strike, including dance's sweep |
| Hammer | — | — | — | — | Close swing/slam footprint; each authored fall is a strike, not a hit every render frame |
| Spear | Yes | — | Yes | Yes | Treat spectral thrust heads as finite-lived projectiles from L1; formation adds lanes, keeps thrust heads |
| Bow | Yes | — | Yes | Yes | Arrow footprint widens without shortening range; arcing form keeps eligible arrows plus impact area |
| Orbit Blades | — | — | — | — | Persistent visual, finite activation sweeps; one hit/target/strike, inner guard remains as crown opens |
| Lightning | Yes | — | Yes | Yes | Multishot adds independent bolt roots; Pierce adds distinct chain targets; Split adds bounded child forks |
| Ember Field | — | Yes | — | — | Field lifetime and pulse footprint; one hit/target/pulse, overlapping seals share an activation hit ledger |
| Sentinel | — | Yes | — | — | Finite-lived sentry attacks; one hit/target/strike across overlapping formation members |

Lightning exposes branching components **from L1** so these supports are
useful before a later form. Its forks/chains use a visited-target set per
strike and a bounded child generation. Echo/Tempo add sentinel attacks, not
endlessly spawning more sentinels. No projectile support requires adding a
new missile attack to a melee summon. These authored components are work to
implement, not supplied by a clip.

## All 32 forms

Source keys above are exact references, inherited by every cell that names
them. Every cell names its required action and a fallback; no cell depends on
an unverified vendor animation. “Faceted” means authored closed geometry over
a dark core, white edges, not a full-white billboard. Fallbacks preserve the
listed action, positioning role and support behavior.

| Weapon / role | L1 | L3 | L5 | L8 |
| --- | --- | --- | --- | --- |
| Sword / lane opener | **Sweep:** K+S, slashA/B, `SwordSlash`. Author aim lock. Fallback S spectral sweep. | **Cross-cut:** same sources, alternate A/B cuts on scheduled grid strikes. Fallback two intersecting spectral sweeps. | **Spin:** K+S spin, `SwordSpin`, retain oriented cut on same strike budget. Fallback orbiting S sweep with center coverage. | **Advancing dance:** K+S, dim spectral copies advance through chosen lane and alternate cuts. `SpectralSlash` trail. Fallback moving rigid S pairs; retain center cut and aim. |
| Hammer / close clear | **Side swing:** H + `SpectralSlash`; authored handle-pivot horizontal swing, heavy settle. Fallback closed cuboid head/shaft following same arc. | **Ground slam:** H raised then pitched down, `LeapSlam` at head contact; retain close sweep coverage. Fallback cuboid hammer drop and ring. | **Vault/eruption:** H vaults on parabola, ground fissures/spikes (U2 reference), slam and retained sweep. Fallback rigid H leap with polygon spikes; no knight vault required. | **Procession:** H copies fall in a short advancing sequence, each distinct placement on grid, retained nearby hit. Fallback faceted hammer falls and rings, never one enlarged slam. |
| Spear / narrow lanes | **Thrust:** P rigid thrust head travels forward, narrow ribbon; optional existing stab pose on hero. Fallback faceted shaft/tip projectile. | **Lunge formation:** P copies in parallel forward lanes with root lunge and retained thrust heads. Fallback three faceted spear tips/shafts. | **Phalanx sweep then thrust:** P lateral fan sweeps before forward thrusts; keep every prior projectile interaction. Fallback hinged spear rods then lane missiles. | **Marching phalanxes:** repeated P ranks advance and thrust, staggered grid volleys; inner lane remains. Fallback moving faceted spear ranks; no marching body clip required. |
| Bow / distant threats | **Shot:** B recoil and A release; U6 optional trail reference. Fallback faceted bow/string plus arrow head. | **Fan:** B plus A spread lanes, same per-arrow collisions. Fallback arrow rods in identical fan; no archer body clip. | **Arcing volley:** B+A rise and descend along explicit trajectories into target area, retaining arrows before impact pulse. Fallback segmented parabolic trails and arrow tips. | **Rotating battery:** B copies rotate to target, independently recoil and fire A fans; retain arcs/impact components. Fallback polygon bows rotating with strings and rods. |
| Orbit Blades / distance | **Circle:** `MagicSword`/S and `orbitBlades`, existing orbit layout. Fallback faceted closed blades. | **Sweep/return:** same blades change radial trajectories per activation, return visibly. Fallback hinged radial blade transforms. | **Inner/outward alternation:** same blades split inner guard and outer scythes, alternate grid sweeps. Fallback two radii with mirrored blade paths; center cannot become blind. | **Crown/arms:** same source blades on connected radial arms unfold and sweep, inner guard remains. Fallback closed rod arms plus faceted blades; no solid white disk. |
| Lightning / connected groups | **Bolt:** U1/U5 reference, authored jagged segments to target and eligible child paths. Fallback short connected white prism links with endpoint spark. | **Fork gesture:** U1 reference, multiple bolt roots fan out with bounded forks; one visited set per strike. Fallback two articulated prism branches. | **Storm sigil:** U4 reference, low polygon sigil delivers same bolt/fork paths from vertices. Fallback sparse ring and bolts; no field-sized white fill. | **Moving constellation:** U4/U5 reference, linked nodes move through a short route and discharge on grid, retain root/fork behavior. Fallback moving faceted nodes and explicit links, same reach/ledger. |
| Ember Field / loop back | **Seal:** U3 reference + existing ring geometry, placed persistent polygon pulses. Fallback closed white polygon with dark center. | **Fissure:** U2/U3 reference, cracks open under retained seal and emit brief low spikes. Fallback branching connected prism cracks plus ring pulse. | **Brazier:** U2/U3 reference, authored faceted bowl tips/pours a ground ribbon into retained seal/fissures. Fallback closed bowl and a segmented ground stream; no stock fire shader required. | **Volcanic procession:** U2/U3 reference, seal/brazier nodes advance and erupt in order, retaining earlier pulse area and duration rules. Fallback short row of polygon seals with alternating spike eruptions. |
| Sentinel / alongside summon | **Sentry:** K+S, slashA/B on independent figure, with finite summon lifetime. Fallback K frozen in an existing slash/parry rig pose and independently swept sword. | **Shield/sword:** K+SH+S, existing parry then slash; retain normal sword coverage. Fallback faceted shield pivots then rigid sword sweeps on same knight root. | **Leap/cleave:** K+S leap role plus `LeapSlam`/`SpectralSlash`; retain shield beat and close coverage. Fallback posed knight parabolic root and landing sword sweep; no retarget needed. | **Advancing formation:** K+SH+S instances march via root paths, slash/parry roles staggered; retain leaps and close cleaves as attacks. Fallback knight roots frozen in existing rig poses with independently moving shields/swords; finite summoned formation lifetime. |

## Three bosses

Existing roles below are **baked**, not proof that revised encounters or their
wave mechanics are implemented. Always damageable; no Break. Damage/iframes,
wave arrival and retained-horde behavior belong to the encounter implementation.

| Boss | Source and selected motion | Authored presentation / fallback |
| --- | --- | --- |
| Fat Ogre, 5 min | O at 5 m. `Idle` (`6b5e201a6a4432d4180d70e7d9d9d93e`), `Walk` (`950067766357002429f0616dae6b2207`), `Run` (`b723a2a23ae2f9948b91122d94804a0f`); smash `Attack3` (`fb257973bbfc68149a4f05170713e05e`, .53/1.167); stomp `Attack1` (`c6c60bc28d145e341afa22a0cc166250`, .47/1.167); throw `Attack2` (`ce6e8484c21fc4b4b86347ffad099cb9`, .50/1.167). Exact source paths in index. | Orange dark-bodied figure, red boss stamps/lanes, yellow enemy projectiles. Smash lanes and stomp circles permit walking. Authored expanding ground ring for mandatory dash-through. If clip lead cannot fit beat, start warning earlier and hold wind-up; never accelerate recovery to force a beat. Fallback existing smash/stomp roles plus simple ring/line geometry. |
| Troll Conductor, 10 min | T+TW, start at distinct boss scale (~4 m, tune by eye), existing idle/walk; attack `A_Attack_LightLeaping01_Sword` (`72f114cf4075d3b4db5ce63cb994a704`, troll 1.033/1.567 seconds). | Amber figure plus sparse red conductor halo/baton arc, not hero-white. Ground-root movement and baton sweep launch alternating yellow fans with gaps; bounded summon groups signaled by red ground polygons. Traveling red pulse wall requires dash through. Fallback existing leap attack as emphatic conducting cue, no new conductor clip promised. Keep troll silhouette, distinguish scale, halo, fan timing and moves from elites. |
| Dragon, 15 min | D+DM at existing 3.39 m base height; `Breathe Fire` (.4/2.667), `Tail Whip R` (1.15/2.167), `Attack 2` (1.8/2.5), `Fly Idle`, `Idle Landing` (1.27/2.5), all GUID DM. Hide saddle. | Red body, red cone/tail/landing outlines, yellow shots. Concentric outward red rings require timed dash; avoid intersecting unavoidable patterns. Existing flight lift is 4.43 m; ground danger marker stays anchored under the moving model. Fallback grounded breath/tail/fireball plus authored rings if airborne composition obscures horde. Keep damageable during flight. |

All exact Ogre source files originate under the local `more_assets` root in the
index. Conductor timing uses troll's bake annotations, not hero's leap impact
(1.1 seconds). Boss warnings stay readable over all friendly constructs:
reduce friendly brightness/overlap, preserve dark gaps; do not erase the horde
on arrival. Operator taste checks are recorded separately.

## Renderer handoff (extend locally, preserve trailer)

Current interfaces and required extensions, relative to `rhythm_champions/`:

| Existing surface | Reuse | Required local addition in weapon tasks |
| --- | --- | --- |
| `Render/Effects/EffectCommands.cs`: `IEffectCommands.Fire(EffectCue, in EffectShot)` | Impact time, At/To, facing, radius/arc, duration; existing slash/ring/spark recipes | Stable per-form cues, independent event/strike identity. Existing `EffectShot` has no projectile ID, control points, lifetime update or summon pose. Do not encode persistent state in arbitrary cue repeats. |
| `Render/Effects/Effects.cs`: `Fire`, `Present`, `Queue`, `Clear`; `IEffectCommands.Orbit` | Instanced mesh buckets, shader time, existing orbit basis | Rigid weapon transforms (pivot, quaternion, scale), per-blade radial motion/inner guard; projectile pose/lifecycle by ID; bolt endpoints/segments; persistent seal nodes/lifetime. Keep deterministic sim paths authoritative. |
| `Editor/Effects/EffectBaker.cs`: `Measure`, `EffectLibrary.Meshes` | Normalize source mesh frames and dependency references | Selective H/P/B/A imports and axis/pivot metadata; `Measure(solid:true)` assumes a sword-like longest axis and is not sufficient hammer-pivot data. Facet edge extraction/dark core for rigid models; do not render whole models as glowing solid white. |
| `Render/Crowd/MonsterFigure.cs`: `Set(slot, in MonsterInstance, palette)`, `Draw(camera,count)` and `RigPose` | Existing CPU-posed knight rig; multiple figure slots | Independent finite sentinel poses/time/aim/root motion. Capacity is fixed at construction; budget formation slots deliberately. Freeze an existing evaluated rig pose for the fallback, never the bind T-pose. Naive Blender rotation transfer is rejected by the probe; use the existing Unity rig sampling path. |
| `Client/Hero/IHeroCommands.cs`: `Walk`, `Swing`, `Dash` | Preserve natural hero locomotion and existing sword swings | Body arbitration: only main sword uses hero's swing; other weapons and Tempo/Echo copies draw separately. Never route hammer/bow into an invented knight animation. |
| `Render/Effects/Shaders/Slash.shader`, crowd edge shaders, `IdPass` | White hero core/edge palette, dark body, existing post and ID tagging | Segmented link/field primitives and rigid edges must honor effects ID 252 and stills requeue. No global renderer replacement; `--look trailer` remains unchanged. |

Pause/resume freezes projectile, field and summon visual age with combat time.
Empowerment is supplied as actual footprint from the sim, not legacy
`EffectPower.OnBeat`'s unrelated 1.15 scale multiplier. Field/orbit/summon pulses
consume it once per activation; their idle objects do not stay permanently big.

## Reproduce and inspect the disposable probe

Recipe: `rhythm_champions/tools/recipes/combat/preview.py`.
Run from the worktree after setup, or pass `--vendor` to an existing **read-only**
local synced vendor directory:

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b \
  --python rhythm_champions/tools/recipes/combat/preview.py -- \
  --out /tmp/ps-3wc-preview \
  --vendor /Users/dthurn/playlist_survivors/rhythm_champions/Assets/Vendor
```

Five ordered phases each: `hammer-swing`, `hammer-slam`, `bow-fan`, `lightning`,
`field`, `sentinel`. Sources are extracted to OUT/sources; outputs stay private.
The probe is an authored transform storyboard in a disposable Blender scene,
with a quiet grid, retained cyan crowd stand-ins and a red threat crossing the
scene. It deliberately does not instantiate unverified Hovl/KriptoFX particle
prefabs, play a knight bow/hammer clip, or claim final Unity game evidence.
See [probe findings](../../rhythm_champions/evidence/combat-assets/README.md).

## Implemented Sword/Hammer handoff (ps-3wc.7)

The eight Sword/Hammer cells now have runtime sim handlers, rigid render
choreography, private baked meshes and a normal/empowered Unity crowd gallery.
`MeleeBaker` selects source S's broadsword mesh and source H's exact FBX,
normalizes the handle/head frame, and uses the existing 24° feature-edge bake
over a dark body. Only H's manifest reference was added; the broadsword was
already owned/imported. Outputs stay under ignored `Assets/Baked/Effects/`.
Sword's main body action uses the existing slash role; the long spin and Hammer
vault are spectral constructs. Hovl `Slash2` supplies thin contact outlines;
S's own rigid blade supplies sparse ground fissure spikes. No stock colored
prefab, new purchase or unvalidated hammer body clip is used.

The procession uses one advancing fall per authored activation (three-step
placement cycle), preserving close damage and drawing upcoming hammers dimmer.
This keeps all damage on the common grid without a hidden off-grid repeat
scheduler. The selected fallback and visual critique are recorded in
[melee runtime evidence](../../rhythm_champions/evidence/melee-weapons/README.md).

## Delivered Spear/Bow implementation (ps-3wc.8)

`RangedWeapons` retains finite projectile heads through every form and uses
shared Multishot/Pierce/Split/Echo execution. Dungeon P and BowCrossbow B/A are
now selective manifest entries; `RangedBaker` generates local closed-facet
meshes with dark bodies and white edges. Spectral thrusts/ranks and rigid bow
rotation/recoil use authored motion; no unverified character clip is required.
The `ranged-gallery` scene records all eight forms normal and empowered,
with an optional dense simulation-frame motion capture. See the
[implementation and visual evidence](../../rhythm_champions/evidence/spear-bow/README.md).
