# Boss Rush fight: gorgon

Written by `champions boss review gorgon` and `champions boss review gorgon --motion` (boss-rush.md, Fights): the fight
in its arena, dressed with the act 2 kit, a hero with six weapons wandering the temple, at 1920 by 1080. The raw PNG
stills stay out of the tree; a few are kept here as tagged JPEGs. They are for judging by eye; nothing here passes or
fails.

## Stills

- [gorgon-entrance.jpg](gorgon-entrance.jpg): she arrives at the temple's heart as her rays sweep out round it. Round
  her stand six lotus columns, six more in an outer ring and six rock spires by the wall, with pyramids beyond.
- [gorgon-gaze-heart.jpg](gorgon-gaze-heart.jpg): the first taught gaze at 60% filled, from the heart. The red cone is
  cut into lit slices; behind each column its shadow is a gap (the short slices stop at the stone), and lines of her hue run
  down the shadows' edges.
- [gorgon-gaze-perch.jpg](gorgon-gaze-perch.jpg): the second gaze, from her first perch by the east wall, across the
  temple: the shadows fall somewhere new.
- [gorgon-volley.jpg](gorgon-volley.jpg): her snake heads' venom round the hero's spot.
- [gorgon-gaze-heart-again.jpg](gorgon-gaze-heart-again.jpg): back at the heart, another gaze.
- [gorgon-stone-ring.jpg](gorgon-stone-ring.jpg): the stone ring's telegraph at her feet, before it rolls out on the
  downbeat.
- [gorgon-motion-sheet.jpg](gorgon-motion-sheet.jpg): 12 of the motion frames (4 to 12 s), left to right and top to
  bottom. Her entrance rays, the first gaze filling from the heart, its strike (the solid red with the columns' shadows
  black), and her glide to the perch.

## Notes

ps-8z4.21, Boss Rush boss 8, act 2 ("Line of sight"). The design as built is in
[boss-rush.md](../../../../docs/champions/boss-rush.md) (The roster, The Gorgon). The code:

- the module: `Assets/Champions/Sim/Rush/GorgonModule.cs`;
- its tests: `dotnet/Champions.Sim.Tests/Rush/GorgonTests.cs`;
- its effects: `Client/Bosses/GorgonPresenter.cs`.

**The fight.** Its arena is `gorgonTemple` in Content/arenas.json: a circle 34 m across, dressed with the `sunkenSands`
kit, held whole by a fixed camera. Lotus columns (0.9 m, unbreakable by the hero) stand in a ring of six at 6.5 m and a
ring of six at 12 m, and rock spires (0.8 m) in a ring at 14.5 m. The hero starts 11 m south of the heart, behind the
inner ring's south column. Every move is planned on a beat (the song's, or the 0.5 s fallback):

- *The gaze* (`Breath`, the Snakeheads clip; the signature, taught alone twice first). She holds still and stares at
  the hero's spot: a cone 35° either side (40° in phase two), filling over at least a bar and 2 s to a downbeat, 20
  damage, holding a beat. The cone is laid as its lit slices: each standing prop's (and statue's) shadow is cut out of
  it (`GorgonModule.Sightlines`, `Slices`), a shadowed slice reaching only to its stone, each slice clipped at the wall.
  The hit test is the framework's own (the hero's whole body must be in a shadow), so the red fill is exactly her line
  of sight.
- *The crack*: at the strike, the stone whose shadow holds the hero (the nearer, where shadows overlap) cracks. Cracked
  once it is drawn grey (petrified); cracked twice it crumbles as the stare ends, on the beat after the strike.
- *The glide*: before each gaze after the first she glides two beats, out from the heart to her next perch (9 m east,
  then north-west, then south-west) or back to the heart, so the shadows move. Her glides cross no column.
- *The volley* (`Boulders`, Attack2): five 1.7 m globs of venom round the hero's spot (seven in phase two), three
  beats, cover or none.
- *The coil* (`Tail`, Attack3): a 6 m disc round her on a downbeat, in place of a volley when the hero is within 4 m of
  her body's edge.
- *The stone ring* (the dash wave, `Wave`, the Taunt clip): a 0.8 m ring rolling out from her at 16 m/s across the whole
  temple, through every column, on a downbeat. Two rings, a dash cooldown and a beat apart, in phase two.
- *Phase two* below half health: four untouchable beats while she returns to the heart and four grey statues (hexagonal
  wall obstacles, 0.9 m) rise round the temple, clear of the hero; they shade and crack like the columns. Each gaze is
  then followed by a volley, and she rests one beat between moves instead of two.

Her XP bursts fall one gem behind each of the six covers nearest her, in their shadows.

**Fairness** (the framework's harness, `TheGorgonIsFairInHerTemple`): at 90 and 180 BPM, act 2 (three-beat
telegraphs), a 140 s fight worn down evenly in her temple, the slot's health. There were no problems:

| BPM | Hazards | Sequences | Dash waves | Needing the dash |
| --- | --- | --- | --- | --- |
| 90 | 186 | 27 | 3 | 3 |
| 180 | 228 | 44 | 6 | 6 |

A gaze's hazards are its slices (about five to twelve). Walking out of the cone sideways is the other way out. The harness's screen circle is the whole arena
(`TheCameraHoldsHerTempleWhole`). Beatless, she fights on the fallback beat, and a recorded fight replays to its hash.

**Dashes.** The framework spends a hazard on its first touch, dodged or not, so a dash on the strike dodges a gaze as
it does any boss's landing. The stare's one-beat hold catches a hero stepping out of cover as it strikes
(`HidingBehindAColumnIsSafeAndStandingInSightIsNot`), but not one who dashed in place. That keeps the main game's verbs
unchanged (Decision 7); the operator row asks whether cover still matters by hand.

**Reference builds** ([reference-runs.tsv](reference-runs.tsv), from the Explicit
`MeasureTheReferenceBuildsAgainstTheGorgon`). These runs pair the scripted `positioning` player with the slot's
reference builds at `--boss 8`, at the slot's health (2090), on the 128 BPM playlist, with the hero unkillable:

| Build | Kill time | Damage taken |
| --- | --- | --- |
| Broad | about 98 s | 0 |
| Focused (the sword first) | about 293 s | 0 |

The scripted player dashed through every one of the 43 to 144 hits that reached it (her moves never overlap, so its dash is always
ready). Their mean, about 195 s, is above the slot's 137.5 s target, with the same spread as the earlier fights.
Retuning it is the pacing bead's job.

**Look and sound.**

- Colour: violet (`enemies.accent.violet`, apart from the Bandaged King's cyan next door) over act 2's dim amber;
  petrified stone in the reserved wall grey. The review stills were rendered in cyan, before the rebase onto the
  Bandaged King's mesh took cyan; only the hue changed.
- Her `BossLook`: light flowing along her coils and snakes, a stony glint on her facets.
- Mesh: the vendor's Medusa (Medusa_LP_Serpent) until the mesh bead (ps-8z4.22) lands. `champions monster review gorgon
  --look trailer` measures her standing 4.8 m.
- `GorgonPresenter` draws, in her hue on the world's Glow material, the sightlines (the cone's and each shadow's edges,
  brightening to the strike and flashing as it lands), her entrance rays and her death's ring of stone shards; and in
  grey, cracks up and a collar round every petrified column, spire and statue.
- No shader variants are added.
- Her bank (`bosses.gorgon`), all from the Universal Sound FX:
  - entrance: a snake's rattle;
  - ticks: snake hisses, climbing 10% a beat to the strike (`TickRise`);
  - impacts: stone on stone;
  - signature: a pickaxe on rock and a deep stone impact (the stone crack);
  - death: a monster's cry.

**Clips and size.** This bead registered the source (the Medusa_LP_Serpent prefab and FBX in manifest.json) and the
`gorgon` type in monsters.json at `heightM` 4.6. The FBX's one take holds every clip at 30 fps. Impacts were measured in
Blender from the bones' speed, frame by frame:

| Clip | Role | Timing |
| --- | --- | --- |
| Idle, Walk | idle, walk | Walk's stride 3.0 m at this height |
| Snakeheads | breath (the gaze) | held at 2.0 s, the snakes writhing |
| Attack1 | smash | the sword's swing at 1.0 s |
| Attack3 | tail (the coil) | the tail's whip at 0.9 s |
| Attack2 | throw (the volley) | the fling at 0.6 s |
| Taunt | cast (the stone ring), roar (the entrance) | arms raised at 2.43 s |
| GotHit | hit | |
