# The roster palette across the seven environments (ps-bop.4, 2026-10-30)

The forest and epic ps-bop's six environments, rendered by the look harness
from one IL2CPP player (Apple M5 Max, Metal) at staging `7dd7b0fe` plus this
bead. The comparison covers readability per shot, the monsters' chroma,
whether the effects stay the brightest values, the cool share, and each type
by hue. Each environment's own README (`env-<name>/README.md`, and
[final](final/README.md) for the forest) has its kit, palette table and
lighting choices; this page compares them and ends with recommendations.

| file | what |
|---|---|
| [environments/song.jpg](environments/song.jpg) | the five song stills, a column per environment |
| [environments/beauty.jpg](environments/beauty.jpg) | the six beauty shots with the horde, a column per environment |
| [environments/empty.jpg](environments/empty.jpg) | the same shots empty |
| `environments/<name>/look.md`, `look.json` | each environment's numbers, the six against the forest |

The full PNGs are in the gitignored `captures/look/cmp-<name>/`. Render them
again with:

```
uv run --project demo3d/tools demo3d look cmp-forest --evidence look/environments/forest
uv run --project demo3d/tools demo3d look cmp-<name> --evidence look/environments/<name> \
  --baseline look/environments/forest --player-args "--environment <name>"
```

## Why one fresh build

The committed sets came from six staging commits, from `af223e69` (the
forest's `final`) to `7dd7b0fe` (the crypt). Since `final`, the drops cleave
(`09b85282`), every monster but the boss dies in one hit at about 3x the
death rate (`74989741`), and the kiting formations changed how the horde
moves (`578732dc`). Re-rendered from one build:

- **The beauty shots reproduce.** The forest's six match `final` to 0.1 L*,
  and every environment's readability is within 3 L* of its README (snow's
  path -17.2 is now -18.6, the desert's glowing-tree -26.8 is now -23.7).
  The forest is unchanged, as decision 1 requires.
- **The song stills moved.** The final peak's effects now cover 28-38% of
  the frame in every environment (the forest's `final`: 14.8%; snow, desert,
  lava, neon and blossom: 8-15% when rendered). The crypt, rendered at the
  same commit, reproduces exactly. So the song-still findings below are
  current, and some differ from the environments' READMEs.

Staging has since moved to `f0369f97` (trailer playback). Song mode is
unchanged by it (PostFeel's refactor keeps song mode's flashes as they were),
so these numbers stand.

## The seven at a glance

| environment | fighting ground | horde reads | \|dL*\| per shot | floor | monster C*ab (× forest) | effects brightest | cool share, song stills | stage dL*, min |
|---|---|---|---|---|---|---|---|---|
| forest | warm brown, mid | lighter | 20.0-24.3 | 6/6 | 54-58 (1.00x) | 10/11 | 0.1-0.7% | 26.3 |
| snow | near-white snow | darker | 18.0-24.2 | 6/6 | 48-55 (0.88-0.98x) | 9/11 | 16-42% | 17.4 |
| desert | pale bleached sand | darker | 18.0-31.8 | 6/6 | 51-55 (0.91-0.99x) | 9/11 | 0.1-1.3% | 17.1 |
| lava | near-black basalt, lava rivers | lighter | 25.1-30.9 | 6/6 | 45-51 (0.82-0.89x) | 11/11 | 0.3-1.2% | 17.0 |
| neon | near-black wet street, neon pools | lighter | 19.4-24.7 | 6/6 | 38-44 (0.67-0.78x) | 11/11 | 9-21% | 20.7 |
| blossom | pale white stone, pink edges | darker | 13.2-19.2 | **4/6** | 47-49 (0.83-0.89x) | 10/11 | 0.2-1.9% | 17.6 |
| crypt | grey stone, the forest's values | lighter | 21.2-27.4 | 6/6 | 54-60 (1.00-1.05x) | 11/11 | 0.3-3.9% | 23.0 |

Readability per shot (dL*, monsters less the ring around them; floor |dL*| >= 17):

| environment | opening | path | glowing-tree | graveyard | courtyard | forest-edge |
|---|---:|---:|---:|---:|---:|---:|
| forest | 21.5 | 24.3 | 21.7 | 23.7 | 21.0 | 20.0 |
| snow | -24.2 | -18.6 | -18.9 | -20.0 | -18.0 | -23.3 |
| desert | -31.8 | -24.4 | -23.7 | -18.0 | -24.6 | -24.7 |
| lava | 30.9 | 25.1 | 29.0 | 27.8 | 27.8 | 27.2 |
| neon | 24.0 | 22.4 | 24.7 | 21.2 | 21.3 | 19.4 |
| blossom | -17.7 | -19.2 | -17.3 | **-14.8** | -18.4 | **-13.2** |
| crypt | 25.7 | 25.3 | 21.2 | 27.4 | 22.8 | 26.2 |

- **Every environment but the blossom passes the floor as a whole horde.**
  The blossom's graveyard and forest edge fail where a third of the horde
  stands in the pink edge bands and the cherry trees' shade, at the horde's
  own value (its README).
- **The ring is the horde's own shadow, and the lighting sets its value.**
  Under the forest's and the crypt's dim ambient the monsters' shadows take
  a mid-value ground down to L* 16-24, and the horde (L* 41-49) reads
  lighter. Under the snow's, desert's and blossom's bright sky fill the
  shadows stay pale (ring L* 47-74), so those grounds had to be pale and the
  horde reads darker. On the lava's and the neon's black grounds the ring is
  L* 2-6, and any lit monster reads.
- **Chroma follows the bounce.** The crypt keeps all of it (1.00-1.05x: the
  horde is the only colour in the frame); the desert and snow most; the
  neon loses the most (0.67-0.78x): on a near-black set a neutral key
  leaves the monsters' dark sides black (L* p10 2-5), and only their lit tops
  carry colour.

## By type

The ID render marks every monster alike, so the look harness now classes each
colourful monster pixel (C*ab > 20) by the roster hue nearest its own and
measures each type against a ring around it (look.md, "Types"; mac.md). It is
a colour estimate, with two limits:

- **The Big Ork (cyan, 180) can't be measured.** Its teal is low in chroma
  (C*ab 25-31). Its pixels fall under 1% of the horde, partly classed as the
  slime and the troll, and none at all in the forest and the lava.
- **The forest's warm key moves the cool types.** It has no `keyColor`, so
  its warm moon at 3x tints the horde. The bats' violet drifts into the
  dragon's slot (the forest's "dragon" row is mostly bats), and the trolls'
  azure loses chroma (C*ab 27 against the crypt's 48). Read the crypt as the
  roster's values under a neutral key.

dL* by type, pixel-weighted over the six shots, with the worst shot in
brackets (**bold**: under 17 either way):

| type (hue) | forest | snow | desert | lava | neon | blossom | crypt |
|---|---:|---:|---:|---:|---:|---:|---:|
| skeleton (20) | +17 (**+15**) | -24 (-19) | -31 (-27) | +29 (+28) | +28 (+27) | -18 (**-15**) | +24 (+22) |
| mushroom (60) | +50 (+43) | **+10 (+4)** | **+7 (+1)** | +66 (+63) | +61 (+56) | **+15 (+12)** | +60 (+57) |
| goblin (100) | +46 (+40) | **+3 (0)** | **-3 (+1)** | +52 (+49) | +52 (+50) | **+5 (+1)** | +53 (+49) |
| slime (140) | +27 (+22) | -20 (**-15**) | -30 (-17) | +32 (+30) | +28 (+26) | -23 (-21) | +30 (+25) |
| troll (220) | **+14 (+11)** | -20 (-17) | -29 (-25) | +28 (+27) | +21 (+18) | -19 (**-15**) | +31 (+28) |
| bat (260) | **+13 (+8)** | -26 (-23) | -27 (-22) | +32 (+31) | +27 (+26) | **-15 (-12)** | +27 (+23) |
| dragon (300) | +33 (+28) | -18 (**-15**) | -28 (-24) | +35 (+30) | **+8 (+6)** | **-13 (-13)** | +22 (+19) |
| demon (340) | **+12 (+11)** | -33 (-26) | -37 (-20) | +23 (+21) | +17 (**+13**) | -21 (**-14**) | **+11 (+7)** |

The monsters' median L* against their ring's:

| type | forest | snow | desert | lava | neon | blossom | crypt |
|---|---:|---:|---:|---:|---:|---:|---:|
| skeleton | 40 / 23 | 41 / 64 | 36 / 67 | 32 / 4 | 31 / 3 | 33 / 51 | 45 / 21 |
| mushroom | 74 / 23 | 75 / 65 | 72 / 64 | 69 / 3 | 64 / 2 | 67 / 52 | 79 / 20 |
| goblin | 67 / 21 | 68 / 64 | 60 / 63 | 56 / 3 | 54 / 2 | 57 / 52 | 71 / 19 |
| slime | 49 / 22 | 45 / 65 | 36 / 66 | 37 / 4 | 31 / 3 | 30 / 54 | 53 / 23 |
| troll | 30 / 16 | 42 / 63 | 35 / 63 | 31 / 3 | 24 / 3 | 31 / 50 | 49 / 18 |
| bat | 30 / 18 | 37 / 64 | 36 / 63 | 35 / 3 | 30 / 2 | 35 / 50 | 44 / 17 |
| dragon | 53 / 21 | 46 / 64 | 34 / 62 | 39 / 4 | 40 / 32 | 39 / 52 | 40 / 18 |
| demon | 36 / 24 | 31 / 64 | 31 / 68 | 26 / 4 | 22 / 5 | 30 / 51 | 33 / 22 |

The ring's share in the type's own hue (within 20°, C*ab > 20), where it is
over 5%: the hue collisions.

| type | where | ring in its hue | dL* there |
|---|---|---:|---:|
| skeleton (rust) | desert: the sand | 71% | -31 |
| troll (azure) | snow: the shaded snow | 71% | -20 |
| dragon (magenta) | neon: the magenta pools | 61% | **+8** |
| demon (crimson) | blossom: the pink edge bands and canopies | 56% | -21 |
| skeleton (rust) | forest: the warm brown ground | 47% | +17 |
| skeleton (rust) | lava: the lava's banks | 14% | +29 |
| Big Ork (cyan) | neon: the cyan pools | 14% | +32 |
| desert's mushroom, neon's troll | | 7-9% | |

### What fails, and why

**Value, not hue, is what fails.** The roster's lit values span L* 33-79
under the crypt's neutral key (30-74 in the forest): the ochre mushroom and
the green goblin are light, while the crimson demon, the violet bat and, under
the warm key, the azure troll are dark. A ground can clear a 45 L* spread
from one side only if it is near black, which is why the lava and the neon
pass every type:

- **On the light grounds the light types vanish by value.** The mushroom
  and the goblin are as light as the snow, the sand and the stone: dL* -3 to
  +15, worst shots 0 to +12. They read only by chroma (C*ab 70-84) and
  their yellow-green against a white or golden ground, a hue difference
  the environments' READMEs credited. A ground lighter than a mushroom
  (L* 75) would clip and outshine the effects, so no ground fix exists for
  them.
- **On the mid grounds the dark types sink.** In the forest the bat, the
  troll and the demon sit at +12 to +14 (worst +8 to +11); in the crypt the
  demon at +11 (worst +7). These are the forest's own shots, which pass as a
  whole horde (20-24) on the light types' margin: the median monster pixel is
  a skeleton, slime or goblin.
- **The skeleton is the forest's thinnest margin.** It is the most numerous
  type (17-31% of the horde's coloured pixels) and in the forest stands on
  its own hue at +17 (worst +15). In the desert it shares the sand's hue
  (71%) but reads by value (-31).
- **Hue collisions decide only where value is thin as well.** The neon's
  dragon in the magenta pools (ring L* 32, dL* +8) is the one type that
  fails by both hue and value. The snow's troll (71% of its ring in its
  hue), the desert's skeleton (71%) and the blossom's demon (56%) collide by
  hue but clear the floor by value.
- **The demon is under the floor in four of seven** (the forest and the
  crypt, and the neon's and the blossom's worst shots): crimson at 0.8
  saturation is dark (L* 22-36), and it shares its hue and value with its
  own red death bursts (below).

**By environment, the type that suffers most:**

| environment | weakest type | why |
|---|---|---|
| forest | bat, demon, troll (+12 to +14) | dark types on a mid ground; the warm key darkens the cool ones |
| snow | goblin (+3), mushroom (+10) | as light as the snow; the troll matches the shade's hue but reads by value |
| desert | goblin (-3), mushroom (+7) | as light as the sand; the skeleton matches its hue, read by value |
| lava | none under 21 | black ground; the skeleton matches the lava's banks by hue (14%) |
| neon | dragon (+8), demon (+17, worst +13) | the magenta pools light the ground under them in the dragon's hue |
| blossom | goblin (+5), dragon (-13), mushroom (+15), bat (-15) | the narrowest ground: stone at L* 50-54 in the ring leaves no room either side |
| crypt | demon (+11, worst +7) | the darkest type on mid grey: the control shows it is the roster's value |

## Effects

The effects' L* p98 against the other pixels' (ps-e8r.10: effects are the
brightest values). The beauty shots pass everywhere (the light grounds by
0.7-7.5 L*). The song stills:

| still | forest | snow | desert | lava | neon | blossom | crypt |
|---|---:|---:|---:|---:|---:|---:|---:|
| song-025 | +19.4 | +5.0 | +5.3 | +30.5 | +22.3 | +10.9 | +10.2 |
| song-060 | +18.4 | **-1.2** | **-11.9** | +22.5 | +0.2 | +1.5 | +5.7 |
| song-100 | +12.2 | +2.0 | +5.9 | +17.2 | +11.1 | +10.6 | +8.9 |
| song-145 | **-0.6** | +0.1 | **-4.0** | +10.4 | +0.3 | **-6.4** | +0.4 |
| song-175 | +7.8 | +0.0 | +1.1 | +8.1 | +9.8 | +9.7 | +3.9 |
| effects' share of song-175 | 38% | 37% | 28% | 38% | 34% | 35% | 38% |

- **Drop-2 (song-145) fails in three of seven, the forest included, and
  ties within 0.4 L* in three more.** Its effects are mostly the demons'
  red death bursts. Saturated red tops out near L* 55, so a red burst can't
  be the brightest thing over a lit ground or the hero's pool (the rest's
  p98 is L* 74-95); only white-hot cores could be, and the bursts' are
  small. The forest's `final` passed it by 2.6; the
  denser deaths since then tipped it (-0.6). Only the lava's black ground
  keeps a margin (+10.4).
- **Drop-1 (song-060) has almost no effects on screen** (1-3% of the frame),
  so on a light ground the lit sand or snow under the hero's pool tops them
  (desert -11.9, snow -1.2). A thin moment rather than a ramp failure.
- **The final peak washes the type hues out in every environment.** The
  effects cover 28-38% of the frame and paint whole clumps orange-yellow
  (the whole-body orange looks like the hit flash's ember emission with the
  ring's and the bursts' light; this bead didn't isolate it): the dragon
  reads pale gold, not magenta, in every column of
  [song.jpg](environments/song.jpg)'s last row. In `final` it read magenta,
  with effects on 14.8% of the frame. This is the same in every environment,
  so it is the effects', not an environment's.
- **On light grounds the effects lead by bloom and hue, not value.** The
  white cores land on white snow, golden sand or white stone: margins of
  0-7.5 L* in the beauty shots. On the snow the hits read as orange-pink
  halos; on the sand and the pink as yellow-white blooms (their READMEs).
- **Fire reads by hue only on cool or neutral sets** (the neon, the snow,
  the crypt): it is the only warm light there. On the forest, the desert, the
  lava and the blossom, warm on warm, it reads by value and size.

## The cool share

The palette pass capped cool accents (HSV 180-280°, C*ab > 20) at 15% of a
song still, for the warm forest where cool is the accent:

- **Forest, desert, lava, blossom and crypt stay well under** (0.1-3.9%).
  There the cool pixels are the accents and the cool types.
- **The snow (16-42%) and the neon (9-21%) are over it by design.** Their
  shadows, fog and pools are the cool hue, so the measure counts the set,
  not its accents. On the snow the intended accents (the ice spire's glow,
  the rune circles) barely register, since additive light can't show on
  white.

## Recommendations

### The roster palette: compress the values, keep the hues

The hues are not the problem: every environment has a dominant hue, and with
nine types in 40° slots some type always shares it. Where one does (the
snow's troll, the desert's skeleton, the blossom's demon), value carries it.
What fails in every environment but the black ones is the value spread, so:

- **Give each type a lit value, not just a hue.** Today CrowdSkinned dyes
  toward the hue at the albedo's own luminance, so the mushroom's caps and
  the goblin's skin stay light and the demon's crimson dark. A per-type value
  (a target luminance, or a luminance scale) in roster.json's `typeColors`
  would set it.
- **Target a band about 15 L* wide, near L* 42-55 under the crypt's neutral
  key,** which each environment's `crowd.key` then places on its side of
  the ground:

  | type | crypt L* now | change |
  |---|---:|---|
  | mushroom (60) | 79 | darker, by about 24 |
  | goblin (100) | 71 | darker, by about 16 |
  | slime (140) | 53 | keep |
  | skeleton (20) | 45 | keep |
  | troll (220) | 49 | keep (but see the forest's key below) |
  | bat (260) | 44 | keep, or a little lighter |
  | dragon (300) | 40 | lighter, by about 3-5 |
  | demon (340) | 33 | lighter, by about 9-12; also lifts it off its red death bursts |

  With that band the light grounds (ring L* 63-68) keep every type 17 under
  them at a key that puts the band's top near L* 46, and the mid grounds
  (ring L* 16-24) keep every type 17 over them.
- **Lift the dark types along with darkening the light ones.** `final`
  tried darkening the mushroom, goblin and troll alone, and it pulled the
  whole horde's median down (forest-edge 20.0 → 18.3). Raising the demon
  and the dragon at the same time keeps the median where it is.
- **No hue changes.** The skeleton (rust) is the only candidate: it shares
  its hue with the forest's and the desert's grounds, the lava and the fire's
  orange. But it reads by value wherever it collides, and moving it would
  crowd the demon's or the mushroom's slot.

### The effect ramp

- **Give the death bursts a hot core.** Drop-2's red bursts are the frame's
  effects in six of seven environments and can't be the brightest values at
  red's L* 55. A white-to-yellow core on `death-burst` and
  `death-burst-large` (a higher `hdr`, or a ramp that holds white longer
  before the red) would put the drops back on top, the forest's included.
- **Keep the final peak from tinting the horde.** With effects on 28-38% of
  the frame, whole clumps turn orange and the dragon gold. The flash should brighten a monster without
  recolouring it, or be capped per frame, so the type hues survive the
  climax. This is also the forest's regression since `final`, not an
  environment's.
- **No ramp change for light grounds.** White cores can't lead white snow
  by value; bloom and hue carry them. Keeping the lit fighting ground at or
  under about L* 90 (the desert's and the snow's pools already do) is the
  environments' share.

### The environments

- **forest:** give the horde a near-neutral `crowd.keyColor`, as the six
  environments have. Under its warm moon at 3x the troll and the bat are
  14-19 L* darker than in the crypt (the warm types only 4-5) and the bats'
  violet drifts toward magenta.
  The forest stays the default and was left alone (decision 1), so this is
  the operator's call. With the roster's band, its ground needs no change.
- **blossom:** the only environment under the floor. Its ring, at L* 50-54,
  leaves no room either side. Darken the edge bands and the forest floor
  under the cherries to the forest's L* 2-21, so the horde there reads
  lighter against shade, as in the forest. Its README found that paler
  edges won only 1-3 L*. Then the white stone keeps the darker horde on
  fighting ground.
- **neon:** keep the magenta pools (the hologram tree's 7 m pool, the
  pylons' and blocks') off fighting ground, or turn them violet: the dragon
  is the one type failing by hue and value together. A
  stronger neutral rim or fill would also give back the chroma the black
  set takes (0.67-0.78x).
- **lava:** a stronger neutral rim, so the monsters' shadowed halves (L* p10
  3-7 on black) keep their silhouettes. Readability itself has the widest
  margins (25-31).
- **snow and desert:** no change for readability; both rely on the roster
  band above for the mushroom and the goblin. The desert's drops warm the
  grade (the post feel's hotter drops), turning the sand golden-orange
  under the skeletons; a desert-specific section grade would keep it pale.
- **crypt:** none: as the control, it shows that the demon's failure and
  the mushroom's and goblin's lightness are the roster's values, not any
  ground's.
- **The cool cap** should be read per environment: on a cool set (snow,
  neon) it measures the set. An accent measure would need the set's own
  hue excluded.

## Performance

Measured per environment (decision 8) in each README's "S0 at the authored
density", on the Mac's windowless probe, not against the forest's ceiling.
The scenes differ in triangles (the forest 0.80 M, the snow 1.15 M, the crypt
1.03 M, the desert 2.38 M) more than in cost: within the probe's run-to-run
spread on a loaded machine. The operator's Cygnus re-check (plan.md, Pending
on the operator) is where real S0 numbers come from.
