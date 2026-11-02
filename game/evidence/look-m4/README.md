# The look polished across the zones and the run's peak (ps-msz.8.3)

M4's look pass ([unity-game.md, The Look](../../../docs/archive/design/unity-game.md#the-look)): every zone settled
and mid-ease at the run's density, the run's peak moments, each weapon's and evolution's effect in every
zone, measured with the look tools (`demo3d_tools.look`: `still_stats` and `game look --readability`)
against the 3D demo's reference stills ([arena/reference](../arena/reference/)). Windowless fixed-time
stills of the Mac player (Mono), on jarvis, 2026-10-31.

## What changed

| Change | Where | Why |
|---|---|---|
| The sword's slash, spin and slam at HDR 0.3, 0.15 and 0.7 (were 1.2, 1.3, 1.2) | Content/effects.json | The heavy strike's spin blew 2 to 5% of the frame to white in one crescent. Hovl's slash material is emission 7 and opacity 20, so the scale is far from linear: 0.4 still clipped, 0.1 reads as a soft silver crescent |
| A strike's crystal ring: ordinary 0.2, heavy 0.8 (were 0.8 and 1.2) | Client/Effects/CrystalWaves.cs | The ordinary ring was white-hot on every beat, the brightest thing on screen and as bright as a heavy strike's; now an ice ring under the effects, the heavy one white-hot |
| The post's flash: 0.35 stops of exposure (was 0.7) | Client/Look/PostFeel.cs | A full flash (an accent, a break or 24 kills on one impact) lifted every highlight past white; aberration and saturation unchanged |
| A tinted spawn's heat ramp at 0.6 of its emitter's gain | Client/Effects/Shaders/EffectParticles.shader | Bloody Tear's crimson spins went white-hot where they overlap; its slashes, the gem bursts and the weapons' sparks hold their colour further into the core |
| The lightning's glow at 2.5 (was 4) around its white core | Resources/Weapons/LightningBolt.shader | The whole bolt's width blew to white; now a white-hot core in the weapon's colour |
| Holy Wand's prism afterimages at 0.55 of the bolt's length (were 1.3), full colour, glow 0.4 | Client/Weapons/WandView.cs | At 18.9 m/s the afterimages are 0.6 m apart but were 2 m long, so the six hues overlapped three deep and summed to white; now bands of colour along the gold stream |
| Unholy Vespers' crystal at 0.6 of its violet, its afterimages at 0.4 | Client/Weapons/BibleView.cs | Its shards read as white crescents; now lilac-violet |
| A hit lights a crystal half white, half its gem colour (was white) | Client/Arena/Shaders/CrystalSurface.hlsl | Under Garlic's and the wands' constant hits whole groups of amber flowers washed to flat pale peach, and so did a mass kill's shards on their break frame |
| Snow's moon at 0.55 (was 0.8); crypt's moon a cool slate at 0.55 (was grey 0.9), its ambient and fog darker | Content/arena-layout.json | Snow and crypt read as overcast dusk (ground p50 12 against neon's 4 and the reference's empty shots' 3.5 to 8); now nights in their hue |

Neon's thin margin (+19.7 settled in ps-msz.6.24's take, 148 monster pixels on screen) no longer
shows: the sparse neon opening on house-128 reads +57 to +63 ([slice](slice/)), the run-density neon
stills +37.7 to +43.1 (+37.7 to +48.2 before: neon's palette is unchanged, but the dimmer ordinary
ring lights the monsters near the hero less). The rest of the
gap list was clipping, the ring and the effects, above.

## How they were made

From the worktree's root, after `uv run --project demo3d/tools game build --backend mono`:

```sh
GC="$PWD/public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg"
H="$PWD/public/reactive/house-128/house-128.ogg"
Z="change@20,change@30,change@40,change@50"
L="uv run --project demo3d/tools game look"
$L look-m4-peak --backend mono --evidence look-m4/peak --readability \
  --at 13,16.1,19.5,22.4,28,32.5,38,42.2,48,52.6,55.5,56.65,60,64.05,68.4,68.8 \
  --player-args "--file $GC --start-minute 25 --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,evolve:unholy-vespers@5,evolve:thunder-loop@5,$Z,accent@56,break@64,finale@68"
$L look-m4-weapons-a --backend mono --evidence look-m4/weapons-a --readability \
  --at 17,19.86,20.45,24.3,29.0,33.18,37.6,42.3,47.0,56.13,60.03 \
  --player-args "--file $GC --run --no-cards --bot positioning --arsenal sword:8,garlic:8,magic-wand:8,king-bible:8,lightning-ring:8,chain-lightning:8 --debug jump:25@4,invulnerable@4,$Z"
$L look-m4-weapons-b --backend mono --evidence look-m4/weapons-b --readability --at 19.4,28,39,48,58 \
  --player-args "--file $GC --run --no-cards --bot positioning --arsenal fire-trail:8,bloody-tear:1,soul-eater:1,holy-wand:1,unholy-vespers:1,thunder-loop:1 --debug jump:25@4,invulnerable@4,$Z"
$L look-m4-ring-ordinary --backend mono --evidence look-m4/ring --at 4.1 --frames 18 --strip 6 --crop 560,240,800,600 --player-args "--file $H --debug accent@4.6"
$L look-m4-ring-heavy --backend mono --evidence look-m4/ring --at 5.05 --frames 18 --strip 6 --crop 560,240,800,600 --player-args "--file $H --debug accent@4.6"
$L look-m4-slice --backend mono --evidence look-m4/slice --readability --at 5.5,6.0,6.4 --player-args "--run --file $H --debug change@12,change@19,change@26,change@33"
M="uv run --project demo3d/tools python game/evidence/look-m4/measure.py"
$M stats game/evidence/look-m4/peak game/evidence/look-m4/weapons-a game/evidence/look-m4/weapons-b game/evidence/look-m4/slice
O=captures/game/look/look-m4-ring-ordinary; HV=captures/game/look/look-m4-ring-heavy
$M light $O/still-004.233.jpg $O/still-004.250.jpg $O/still-004.233.jpg $O/still-004.267.jpg \
  $HV/still-005.167.jpg $HV/still-005.183.jpg $HV/still-005.167.jpg $HV/still-005.200.jpg
```

[before/](before/) holds the peak set and the strike strips taken from staging's tree before any
change (slugs `before-*`, the same commands). The before weapon sets (at 13, 19.5, 28, 38 and 48 s,
where 13 s falls in a chest's freeze, so the after sets moved to the moments below) are kept only in
[before-after-weapons.jpg](before-after-weapons.jpg). [measure.py](measure.py) is this bead's: `stats` (one row per still),
`light` (a strike's new light from consecutive frames) and `pairs` (the before/after sheets).

**The run's peak** (`--start-minute 25`, the hero built as the run builds it, level 27, and five
evolutions given at 5 s): the music's jump moves neon on to blossom at 14.11 s; the forced changes land
on their downbeats at 20.37 (snow), 30.54 (crypt), 40.20 (lava) and 50.63 s (neon); go-cart's two-bar
ease is 4 s. The forced accent's heavy strike lands at 56.63 s and breaks two structures on its
impact; `break@64` breaks the structures near the hero at 64.0 s; the finale's blast kills 210 at
68.37 s. Times from a headless fixed-time run's `--events-out`, which gives the same events.

**The weapon sets** (`--run` with `jump:25@4`, since `--start-minute` builds its own loadout over
`--arsenal`): set A is the six base weapons, set B Fire Trail and the five evolutions. Set A's stills
sit on a Lightning Ring strike in each zone (its events' times), since its strikes fall only on
monsters near the hero and are gone within a few frames.

## The numbers

Whole frame (`still_stats` on the 960 px stills; hue is the median HSV hue of the pixels above C*ab 10)
and the horde's readability (dL*, the floor 17, this bead's bar +25 settled):

| still | what | L* p50 | L* p98 | contrast | chroma | near-black | clipped | clipped blob | hue | dL* |
|---|---|---|---|---|---|---|---|---|---|---|
| reference, horde shots (4) | demo3d's stress scene, `--no-post-feel` | 23.5 - 44.1 | 62.2 - 65.7 | 20.4 - 21.5 | 28.6 - 31.6 | 16 - 33% | 0% | 0% | 202 - 230° | |
| reference, empty (4) | | 3.5 - 8.0 | 27.6 - 57.6 | 7.4 - 13.6 | 23.3 - 25.9 | 56 - 79% | 0% | 0% | 241 - 257° | |
| [13.0 s](peak/still-013.000.jpg) | neon, settled | 3.9 | 67.2 | 16.2 | 19.7 | 76% | 0.21% | 0.10% | 254° | +43.1 |
| [16.1 s](peak/still-016.100.jpg) | neon to blossom, mid-ease | 4.0 | 66.3 | 15.2 | 15.6 | 70% | 0.00% | 0.00% | 328° | +71.8 |
| [19.5 s](peak/still-019.500.jpg) | blossom, settled | 3.4 | 79.5 | 16.9 | 14.1 | 70% | 0.02% | 0.00% | 328° | +64.0 |
| [22.4 s](peak/still-022.400.jpg) | blossom to snow, mid-ease | 9.6 | 88.3 | 22.1 | 25.4 | 52% | 0.39% | 0.09% | 229° | +51.2 |
| [28.0 s](peak/still-028.000.jpg) | snow, settled | 9.4 | 93.5 | 24.7 | 23.8 | 53% | 0.45% | 0.11% | 229° | +51.3 |
| [32.5 s](peak/still-032.500.jpg) | snow to crypt, mid-ease | 6.9 | 95.2 | 24.0 | 12.0 | 58% | 0.63% | 0.07% | 231° | +44.6 |
| [38.0 s](peak/still-038.000.jpg) | crypt, settled | 6.7 | 91.7 | 22.1 | 13.5 | 60% | 0.58% | 0.28% | 237° | +44.1 |
| [42.2 s](peak/still-042.200.jpg) | crypt to lava, mid-ease | 3.6 | 87.9 | 22.1 | 18.7 | 66% | 0.12% | 0.01% | 27° | +45.0 |
| [48.0 s](peak/still-048.000.jpg) | lava, settled | 2.6 | 85.5 | 21.4 | 14.9 | 75% | 0.07% | 0.00% | 15° | +58.3 |
| [52.6 s](peak/still-052.600.jpg) | lava to neon, mid-ease | 6.5 | 85.4 | 24.7 | 26.3 | 57% | 0.23% | 0.06% | 255° | +54.2 |
| [55.5 s](peak/still-055.500.jpg) | neon, settled again | 3.2 | 49.5 | 12.5 | 15.8 | 77% | 0.00% | 0.00% | 238° | +37.7 |
| [56.65 s](peak/still-056.650.jpg) | the heavy strike, a frame after impact: Bloody Tear's crimson spin, two structures breaking, the flash | 13.2 | 95.1 | 29.2 | 39.7 | 44% | 0.65% | 0.31% | 255° | +55.9 |
| [60.0 s](peak/still-060.000.jpg) | an ordinary strike, Holy Wand's stream, Vespers' ring | 4.9 | 90.3 | 19.9 | 25.1 | 65% | 0.21% | 0.07% | 254° | +36.5 |
| [64.05 s](peak/still-064.050.jpg) | the forced break and a heavy strike's cleave | 6.6 | 91.2 | 21.5 | 30.7 | 60% | 0.28% | 0.03% | 255° | +37.8 |
| [68.4 s](peak/still-068.400.jpg) | the finale's mass kill: 210 monsters break into amber glass | 8.6 | 78.3 | 25.5 | 34.5 | 56% | 0.01% | 0.00% | 250° | +58.8 |
| [68.8 s](peak/still-068.800.jpg) | the glass settling | 5.9 | 69.9 | 22.1 | 28.4 | 63% | 0.01% | 0.00% | 249° | +51.0 |

Before ([before/peak](before/peak/)): the heavy strike clipped **5.48%** of the frame (one 4.59% blob),
the crypt and snow stills 0.69 to 1.08%, the ordinary strike 0.39%; snow's and crypt's ground p50 was
11.4 to 12.9. Every still passed readability before too (+37.7 to +65.3).

- **Every still passes** `--readability` ([peak](peak/readability.json), [weapons-a](weapons-a/readability.json),
  [weapons-b](weapons-b/readability.json), [slice](slice/readability.json)): settled +37.7 (neon) to +64.0
  (blossom), mid-ease +44.6 to +71.8, the weapon sets +42.1 to +69.1.
- **No still clips more than 1%**: the worst is 0.65% (the heavy strike, and set A's crypt at 37.6 s), its
  largest blob 0.31%.
- **An ordinary strike peaks dimmer than a heavy one** (`light`, house-128's opening before the horde,
  so the frame holds only the strike: [ring/](ring/)): the pixels a strike brightens by more than 10 L*,
  on its impact frame and the next:

  | strike | new-light px | L* p50 | L* p98 | clipped | before: px, L* p98, clipped |
  |---|---|---|---|---|---|
  | ordinary (4.233 → 4.250 s) | 56,055 | 44.3 | **90.0** | 0.03% | 79,817, 99.8, 0.25% |
  | ordinary, next frame | 69,030 | 45.5 | 91.4 | 0.02% | 91,820, 99.8, 0.26% |
  | heavy (5.167 → 5.183 s) | 312,067 | 37.9 | **99.6** | 0.44% | 510,007, 99.7, 2.11% |
  | heavy, next frame | 309,031 | 41.2 | 99.7 | 0.54% | 482,357, 99.7, 2.34% |

  Before, both peaked at white (p98 99.8 and 99.7); now the ordinary strike is an ice ring and a silver
  swirl, the heavy one white-hot.

Against the reference: the world is as dark (p50 2.6 to 9.6 between the big moments, the reference's
empty shots 3.5 to 8.0), the contrast and chroma match its horde shots (19.9 to 25.5 and 12 to 40 against
20.4 to 21.5 and 28.6 to 31.6; crypt is the low-chroma zone by design), and nothing now clips where the
reference clips nothing; the reference was rendered without the post's feel, so it has no flash to
compare with. The p50 stays below the reference's horde shots because the reference fills its frame with
thousands of monsters. The late crowd's being mostly amber flowers (the run-tuning bead's, ps-msz.8.4)
makes the fields amber; the colour comes from the bosses (the blue crystal minotaur), the structures and
the effects.

## Each effect in every zone

Settled in each zone (blossom's window is short: 18.1 to 20.37 s; its Bible still is at 17.0 s, 84% through
the ease from neon):

| weapon | blossom | snow | crypt | lava | neon |
|---|---|---|---|---|---|
| Sword: white slash, ice ring | [19.86](weapons-a/still-019.860.jpg) | [24.3](weapons-a/still-024.300.jpg) | [33.18](weapons-a/still-033.180.jpg) | [42.3](weapons-a/still-042.300.jpg) | [56.13](weapons-a/still-056.130.jpg) |
| Garlic: mint aura | 19.86 | 24.3 | 33.18 | 42.3 | 56.13 |
| Magic Wand: violet bolts | 19.86 | 29.0 | 37.6 | 47.0 | 56.13 |
| King Bible: gold shards | [17.0](weapons-a/still-017.000.jpg) | 24.3 | 37.6 | 42.3 | 56.13 |
| Lightning Ring: blue-white strike | 19.86 (left edge) | 24.3 | 37.6 | 42.3 | 60.03 |
| Chain Lightning: lemon arcs | 19.86 | 24.3 | 37.6 | 47.0 | 56.13 |
| Fire Trail: ember pools | [19.4](weapons-b/still-019.400.jpg) | [28.0](weapons-b/still-028.000.jpg) | [39.0](weapons-b/still-039.000.jpg) | [48.0](weapons-b/still-048.000.jpg) | [58.0](weapons-b/still-058.000.jpg) |
| Bloody Tear: crimson slashes | 19.4 | 28.0 | 39.0 | 48.0 | 58.0 |
| Soul Eater: teal aura | 19.4 | 28.0 | 39.0 | 48.0 | 58.0 |
| Holy Wand: gold stream, prism bands | 19.4 | 28.0 | 39.0 | 48.0 | 58.0 |
| Unholy Vespers: lilac shards, violet ring | 19.4 | 28.0 | 39.0 | 48.0 | 58.0 |
| Thunder Loop: magenta strikes, shock rings | 19.4 | 28.0 | 39.0 | 48.0 | 58.0 |

Judged by eye on the full-size stills; no exception. The weakest: Bloody Tear's crimson against lava's
red night (it reads by its brightness, not its hue) and Holy Wand at 58.0 s in neon (a gold bolt at the
hero, its stream clearer in other neon frames). Garlic's aura reads in blossom now (the
blossom-era strips' wash-out came from the old bright blossom ground). Lava's ember pools (Fire Trail)
read against lava's red night, the weakest pairing, but by their glow. Holy Wand's prism now shows as
coloured bands along the gold stream rather than fringes; whether it reads as a prism at full speed stays
the operator's (the evolutions row).

Before/after sheets: [the peak](before-after-peak.jpg) (heavy strike, ordinary strike, break, mass
kill), [the zones](before-after-zones.jpg) (neon, blossom, snow, crypt, lava settled), [the
weapons](before-after-weapons.jpg).

## The Warden (ps-msz.8.11)

Re-checked on staging 382b50a9 (after ps-msz.8.4 to 8.6 landed, so the run's crowd and chests differ
and the same times show other frames: 48.0 s now falls in a chest's freeze). The set's commands still
pass: every still in the peak and weapon sets passes `--readability` (settled +37.0 to +68.5, the
weapon sets +29.7 to +66.3); neon settled at 13.0 s reads +45.8 with 0.20% clipped (above: +43.1,
0.21%), the heavy strike at 56.65 s +48.8 with 0.76% clipped (above: +55.9, 0.65%); `light` gives the
ordinary and heavy strikes' L* p98 at 90.0 and 99.6, as above. Note that the peak set's 19.5 s
("blossom, settled") falls in a chest's freeze, so blossom's settled number is better read from the
weapon sets (19.86 and 19.4 s).

**What the set missed: a slam on its impact frame.** The peak set's zone stills sit two seconds after
each change, past the slam. On the slam's own frames (the change's downbeat, +1, +2):

| still | before: clipped, blob | after: clipped, blob | dL* |
|---|---|---|---|
| [30.55 s](warden/after/still-030.550.jpg), snow to crypt | 0.89%, 0.27% | 0.59%, 0.08% | +54.6 |
| [40.2 s](warden/after/still-040.200.jpg), crypt to lava | **1.44%**, 0.40% | 0.88%, 0.22% | +46.0 |
| [40.233 s](warden/after/still-040.233.jpg) | **1.21%**, 0.41% | 0.88%, 0.24% | +46.6 |

The blossom-to-snow and lava-to-neon slams clip 0.14% and 0.19%. What blew out was Thunder Loop's and
Lightning Ring's bolts landing on the downbeat: a bolt is 2.2 times overbright for its first 0.04 s,
which drove its whole ribbon, not only its core, to white under the slam's flash and crypt's
desaturated, higher-contrast grade (40.25 s, past the overbright, clipped 0.77%). Now the overbright
brightens only the white core, the core is thinner and the coloured glow is 2
(Resources/Weapons/LightningBolt.shader); the bolts still read magenta, blue and lemon around a thin
white-hot core ([before/after](warden/before-after-slams.jpg)). Taken with the peak command above and
`--at 30.55,40.2,40.233` into `--evidence look-m4/warden/before` (the reviewed shader) and
`look-m4/warden/after`.

**A stills bug.** Two `--at` times on one frame (40.217 rounds up to the 40.233 frame) drew the camera
twice, and the second drawing missed the draws the first spent (Graphics.RenderMesh*): no crowd, no
structures, no effects, and a readability FAIL. Stills due on one frame now share its one drawing
(Client/Stills.cs).

## Left to the operator

The flash, the ring and the effects in motion: [operator-pending.md](../operator-pending.md)'s beat-feel,
first-assembled-check and evolution rows say what to look for since this bead.
