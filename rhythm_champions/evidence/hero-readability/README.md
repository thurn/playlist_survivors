# Hero and local threat hierarchy — ps-3wc.16

2026-11-03, jarvis / Apple M5 Max, actual Metal IL2CPP player at 1920×1080.
This follows the confirmed visual finding in
[the final review](../warden-review/README.md), without changing simulation,
damage, collision shapes, clocks, warning deadlines or retained entities.

## Drawing changes

The default live hero uses the existing posed knight and lighting in a
foreground depth range, retaining its own surface ordering. A small,
camera-facing soft contrast pocket reduces competing light immediately behind
it. Warning marks draw after the pocket; the knight draws after warnings.
Its edges remain white when hurt, while the glass, rim and core retain the
hurt tint. The foreground treatment ends on invisibility/death and follows
the actual lifted pose. It is not applied to spectral Sentinels. The pinned
trailer shader, meshes, rig and animations remain unchanged.

Elite warnings use one stable boundary instead of an additional expanding
inner circle. A dark shoulder separates it from amber crowd edges.
Ground-aligned hatching and a dim interior identify the danger side within
5 m of the player, fading out by 8 m. The boundary brightens toward impact;
the routine landing wash is reduced so overlapping slams retain dark
interiors. Boss red timing fronts and their landing brightness remain intact;
their outlines also receive a dark shoulder. Shared world-space hatch lines
coincide across overlaps rather than forming a moving moiré pattern.

## Reproduce and inspect

Use [the full-build setup](../full-build/README.md), then:

```sh
uv run --project rhythm_champions/tools champions build --backend il2cpp
uv run --project rhythm_champions/tools champions play --backend il2cpp --headless \
  --fixed-time --tier low --capture-stills /tmp/hero-readability \
  --stills-scenes full-ogre-storm,full-conductor-storm,full-dragon-storm,full-ogre-barrage,full-conductor-barrage,full-dragon-barrage
```

Repeat Conductor/Storm, Dragon/Storm and Conductor/Barrage with
`--look trailer`; compare Conductor/Storm and Dragon/Storm with `--tier high`.
The test runs real six-slot level-8 weapon/support schedulers, retains 1,000
ordinary bodies plus the boss and structures, and overlays the same 384
pickup-render representations as the prior review. These are the legal
Storm/Barrage upper-bound builds described in the full-build README, not
builds earned from an ordinary run. The unusually tough bodies deliberately
survive damage. Nine ordered samples span each traveling wave's continuous
simulation, including elite landings, hero hurt and dash poses; they are
sampled motion, not a real-time video.

Private captures on jarvis:
`~/Library/Caches/playlist-survivors/evidence/ps-3wc.16/`.
`first-low/` has the initial hero/outline repair; `final-low/` adds hatched
warnings before the critique-driven landing/hurt repair; `verified-low/`
is the delivered six-scene drawing. The private `compare.png` places the
trailer and default side by side for Conductor impact and Dragon wave.
Trailer/High comparisons of the delivered
drawing are `verified-trailer/` and `verified-high/`. No vendor-derived
image, mesh or bake is committed.

## Visual judgement

The first fresh agent received only images labelled A/B/C, no implementation
or design documents. It found B's hero immediately in Conductor wall phases
2/3 and Dragon, unlike the trailer's obscured figure, and found hatched,
dark-interior discs much clearer than the earlier concentric empty rings.
It called the red wall unmistakable and continuous and the default amber
crowd/red dragon stronger closed volumes than the trailer's fine wiring.
It also found a substantive defect in phase 4: yellow landing flashes erased
interior detail, while the hurt hero lost its white identity. Both findings
were reproduced directly and repaired before the delivered captures.

On the delivered ordered phases the same image-only critic reported:
“The substantial player/flash problem is resolved in these D samples.”
It could track the white-edged, dark-bodied player through all four phases
and trace every overlapping disc perimeter, with visibly different interiors
and exteriors through impact. The remaining busy cyan/white detail around the
hazards did not erase that local hierarchy. Author inspection agrees across
the game-scale Conductor wall, Dragon wave and six-scene stress samples.
The foreground depth range also retains the hero's own limb/shield surface
ordering instead of drawing all back layers through one another. Direct
Low/High comparison retains the same local hierarchy; the quality tier does
not restore the impact wash or cover the player with spectral weapons.

The critique also observed that a single red-wall still does not explain
its direction of travel, the Dragon front can be clipped at the edge of the
view, and the cyan crowd remains finer and noisier than the amber figures.
Ordered phases show wall travel. Only the red strip itself damages during a
traveling wave; neither adjoining half-plane is labelled safe by this evidence.
This delivery does not redesign wave grammar
or the independent skeleton mesh. Those observations are retained rather
than represented as full art approval.

## Validation and limits

Rendered regression tests cover a live foreground figure behind an opaque
occluder, the contrast pocket preserving object IDs, hatched danger interiors,
an unmarked gap and ring hole, and complete cleanup after warning call-off.
The pinned trailer shader hashes remain covered by the existing tests.
123/123 Unity EditMode tests pass with no skips. IL2CPP builds pass; a clean
Mono shader-budget build reports 283/300 variants. Required delivery checks
and staging SHA are recorded on the bead.

This is presentation evidence at extreme retained load, not proof of a
collision-free route through an invulnerable crowd. Hand-play readability,
operator taste, real speakers, target-Mac GPU cost and real-time-video
judgement remain unperformed. The simulation/GPU performance work is separate;
no new timing or M1 claim is made here.
