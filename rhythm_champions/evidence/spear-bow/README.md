# Spear and Bow (ps-3wc.8)

`SimConfig.SurvivorWeapons` enables the survivor build, which automatically binds these weapons when owned. All eight
levels execute; 2/4/6/7 add damage, with choreography changes at 3/5/8.
Archived sessions retain their previous behavior. Assembly must acquire these through
`SimState.Build`, not the archived attack IDs.

| Weapon | 1 | 3 | 5 | 8 |
| --- | --- | --- | --- | --- |
| Spear | One finite thrust head | Three parallel lanes | Five heads sweep into line, then thrust | Three staggered ranks of five, sweeping and advancing |
| Bow | One straight arrow | Three-arrow fan | Five parabolic arrows and impact footprints | Eight rotating/recoiling spectral bows release staggered arcing arrows |

Spear chooses the nearest body within 8 m; Bow chooses the farthest within
18 m. Deterministic slot order breaks distance ties. Spear starts with two
targets per head; Bow with one. Volley members share the strike's hit ledger:
more lanes do not multiply damage on one overlapping body. Tempo adds actual
independent spectral strikes. Pierce adds eligible bodies, Split forks two
heads from a collision, and Echo repeats in one shared reduced-damage
window. Every form retains Might/Tempo/Reach/Multishot/Pierce/Split/Echo.
Reach and empowerment widen footprints/formation coverage, without increasing
damage or shortening flight range. Rain's circle and arrow share one ledger;
its damage occurs at touchdown, never while visibly overhead. Holds freeze
flight age and construct recoil. Enemy generations prevent reused slots from
inheriting the previous body's hit immunity.

## Sources and reproduction

The manifest selects Dungeon `SM_Wep_Spear_01.fbx` (GUID
`d9846e0c0b6923d48a1a17570257c243`), BowCrossbow
`Rigged_Bow_Testing.fbx` (`2e3787f6a1057be42bdf7fb80d0d69b4`, submesh
`SK_Bow_01`) and `SM_Arrow_01.fbx` (`1c5b37fd30472d24fbd3e315d52fafaf`).
See [source mapping](../../../docs/champions/combat-assets.md). No knight bow
clip or marching body animation is used. RangedBaker normalizes the long axis,
puts projectile tips at the flight position, and supplies barycentric closed
facets over a dark core. All source imports and generated meshes remain in
ignored Vendor/Baked. The bake reports 384/544/214 source triangles for
spear/bow/arrow respectively. A single opaque shader has no feature keywords;
the existing trailer look and post-processing parameters are untouched.

From the assigned worktree:

```sh
uv run --project rhythm_champions/tools champions build --force
uv run --project rhythm_champions/tools champions stills --scene ranged-gallery --out /tmp/spear-bow
CHAMPIONS_RANGED_MOTION=1 uv run --project rhythm_champions/tools champions stills --scene ranged-gallery --out /tmp/spear-bow-motion
```

The default gallery records 48 stills: each form normal/empowered at three
phases. The optional motion mode records 20 actual simulation frames per
entry, 320 total; Spear samples every .035 s, Bow every .05 s. This captures
sweep-to-thrust, staggered ranks, arrow arcs and battery turning/recoil,
without accelerating a body clip. Collision dummies are dressed with the
existing skeleton crowd via presentation events, at their actual standing
positions. They intentionally do not walk. This is game-scale rendering with
the game's camera, hero, crowd and effects, not a Blender storyboard.

Images and motion frames contain vendor derivatives and stay private. Local
review captures live under
`~/Library/Caches/playlist-survivors/champions/evidence/ps-3wc.8/`.

## Verification and visual review

The first capture exposed invisible type-zero dummies and sample times after
basic arrows had already struck; the gallery now explicitly dresses the
standing collision bodies and samples each weapon's flight phase. Final
capture review and validation results are recorded below after inspection.

Speaker balance, hand-play timing and full six-weapon aesthetic judgement
remain operator checks in [operator-pending](../operator-pending.md).

Final local results: 327 sim tests pass, including the archived replay and
allocation checks, plus 35 new ranged behavior cases. The forced Mono player
build succeeds without C# or shader errors. The incremental variant check
reports 279 compiled variants against the 300 budget. The 48 default stills
and 320 motion frames were captured with matching ID passes; no presenter
exceptions were logged. Contact sheets and per-form sampled GIFs remain in
the private evidence directory above.

Visual critique after inspecting the full form/empowerment contact sheet,
full-resolution shot/battery/phalanx frames and the chronological motion
contact sheet: the single arrow/thrust is intentionally thin and modest;
three lanes/fan, the hinged five-spear sweep, three advancing ranks, elevated
rain and rotating bow circle have different silhouettes and timing. The
skeleton crowd remains cyan and individually readable behind the white
heads; dark interiors prevent large white filled masses. Empowerment expands
the real lane/arrow/impact coverage; its local white ring also makes the
consumption readable. The highest ranks are the busiest near the hero, but
their staggered releases separate into lanes rather than a continuous white
sheet. Sampled motion confirms spear sweep before forward travel, separate
rank delays, bow recoil/turning, arrow ascent/descent and small touchdown
rings. Basic arrows can disappear early on a hit; later captures correctly
show settled space. Low-level arrows are subtle at thumbnail scale and should
be judged at the native game scale. This review does not claim moving-horde
six-weapon taste or speaker validation.

Final regression review also verifies that L8 retains all five L5 lanes and that Split children leave their immune source body before spending their target allowance. Separate Echo tests observe real extra damage in every form.

After integration with landed Sword/Hammer and pickup work: the combined sim suite passes 352 tests. Spear/Bow follow the shared survivor-mode/session switch, reuse the existing enemy generation identity, and preserve the other families' event/source IDs.
