# Structure styles: the looks (ps-suy.1)

`--structure-style <name>` draws the arena's structures in a monster style's treatment
([environment.md, Structure styles](../../../../docs/tech-demo-3d/environment.md#structure-styles)).
Every style on the forest and on crypt, from TD15's six beauty shots, with readability numbers.
Made on the Mac (M5 Max) with the windowless IL2CPP player in fixed time, 2026-10-30.

- [forest.jpg](forest.jpg) and [crypt.jpg](crypt.jpg): one column per style, rows the courtyard, the
  glowing tree and the graveyard empty, then the courtyard with the horde.
- `<environment>-<style>/`: each set's `look.md` and `look.json` and its beauty shots (horde and empty),
  from `demo3d look structures-<environment>-<style> --beauty-only --evidence structures/looks/<environment>-<style>
  --baseline structures/looks/<environment>-baseline "--player-args=--structure-style <style> [--environment crypt]"`.
- [song-120-fire.jpg](song-120-fire.jpg): the song at 120 s under `--structure-style fire` (a full windowless song
  run, no errors). [song-100-crypt-hologram-match.jpg](song-100-crypt-hologram-match.jpg): crypt at 100 s under
  `--monster-style hologram --structure-style match` (the horde's post look wins).

The player styles 1,320 of the forest's 2,584 kit renderers and 1,067 of crypt's 2,529 (the courtyard and ruins, the
landmarks and the trees; `StructureStyle: ... on N of M kit renderers` in its log).

## Readability

Each cell: the horde against the ground ring (TD15's |dL*|, floor 17) / the horde against the structure pixels
3-24 px around it (the new measure; - where too few). Every set passes the floor; the lowest is crypt-spectrum's
glowing tree at 17.0. Crypt's own margin is about 1 L* (17.9 at the opening, 18.1 at the glowing tree): its
ruins and dead trees are near black against light stone, and they are in the horde's ring.

| set | opening | path | glowing-tree | graveyard | courtyard | forest-edge |
|---|---:|---:|---:|---:|---:|---:|
| [forest-baseline](forest-baseline/look.md) | 21.6 / 25.2 | 24.3 / 22.0 | 21.7 / 23.6 | 23.7 / 31.0 | 21.0 / 19.8 | 20.0 / 20.7 |
| [forest-painted](forest-painted/look.md) | 21.4 / 22.3 | 23.8 / 22.1 | 20.8 / 22.1 | 23.0 / 31.3 | 21.6 / 23.3 | 18.5 / 15.1 |
| [forest-fire](forest-fire/look.md) | 21.1 / 22.0 | 24.3 / 26.0 | 22.6 / 27.9 | 23.6 / 29.9 | 21.6 / 21.7 | 21.3 / 24.4 |
| [forest-crystal](forest-crystal/look.md) | 21.3 / 21.8 | 25.0 / 26.6 | 21.6 / 25.7 | 23.5 / 30.4 | 21.9 / 24.0 | 20.4 / 24.1 |
| [forest-spectrum](forest-spectrum/look.md) | 21.3 / 23.3 | 26.4 / 33.0 | 23.0 / 27.3 | 24.7 / 31.8 | 21.7 / 25.9 | 23.4 / 28.5 |
| [forest-hologram](forest-hologram/look.md) | 20.9 / 19.2 | 24.9 / 28.2 | 21.1 / 26.1 | 24.7 / 33.2 | 21.5 / 25.5 | 19.9 / 23.6 |
| [forest-chrome](forest-chrome/look.md) | 21.8 / 34.7 | 27.1 / 38.9 | 23.6 / 35.0 | 24.8 / 42.6 | 22.8 / 41.3 | 23.4 / 35.2 |
| [forest-void](forest-void/look.md) | 21.8 / 39.4 | 25.3 / 35.5 | 21.7 / 34.1 | 25.1 / 41.9 | 22.7 / 40.4 | 20.2 / 27.2 |
| [crypt-baseline](crypt-baseline/look.md) | 17.9 / 19.1 | 23.8 / 27.9 | 18.1 / 31.8 | 32.6 / 37.4 | 19.7 / 19.1 | 26.8 / 29.7 |
| [crypt-painted](crypt-painted/look.md) | 18.0 / 20.5 | 23.5 / 27.7 | 17.1 / 24.8 | 31.9 / 34.5 | 20.3 / 22.7 | 25.1 / 26.7 |
| [crypt-fire](crypt-fire/look.md) | 17.8 / 17.7 | 23.4 / 24.6 | 17.6 / 25.4 | 31.8 / 32.5 | 20.2 / 21.5 | 25.6 / 26.6 |
| [crypt-crystal](crypt-crystal/look.md) | 17.9 / 19.0 | 23.9 / 26.8 | 17.5 / 25.5 | 31.8 / 33.1 | 20.5 / 23.3 | 26.2 / 28.8 |
| [crypt-spectrum](crypt-spectrum/look.md) | 17.8 / 21.1 | 23.4 / 27.9 | 17.0 / 26.2 | 32.2 / 34.5 | 20.3 / 25.5 | 26.7 / 30.7 |
| [crypt-hologram](crypt-hologram/look.md) | 17.6 / 15.9 | 23.3 / 27.3 | 17.3 / 27.3 | 32.0 / 35.5 | 20.3 / 24.7 | 26.0 / 31.3 |
| [crypt-chrome](crypt-chrome/look.md) | 18.2 / 32.8 | 24.3 / 39.3 | 17.6 / 34.6 | 33.0 / 45.5 | 21.6 / 40.4 | 27.7 / 42.6 |
| [crypt-void](crypt-void/look.md) | 18.3 / 39.3 | 24.5 / 37.7 | 17.9 / 37.1 | 32.7 / 44.1 | 21.2 / 39.3 | 27.2 / 36.6 |

## Without the flag

[forest-baseline](forest-baseline/look.md) is the whole look set (song stills and beauty shots) without the flag,
against [look/final](../../look/final/README.md): the same within 0.01 L* in every still's p2, p50 and p98, and
0.05 to 0.10 L* per pixel (JPEG against JPEG, 960 px).

| still | dL* p2 | dL* p50 | dL* p98 | mean abs dL* per pixel |
|---|---:|---:|---:|---:|
| song-025 | +0.00 | +0.00 | -0.01 | 0.08 |
| song-060 | +0.00 | +0.00 | +0.00 | 0.05 |
| song-100 | +0.00 | +0.00 | +0.00 | 0.08 |
| song-145 | +0.00 | +0.00 | -0.01 | 0.10 |
| song-175 | +0.00 | -0.00 | +0.00 | 0.09 |
| opening | +0.00 | +0.00 | +0.00 | 0.07 |
| path | +0.00 | +0.00 | +0.00 | 0.07 |
| glowing-tree | +0.00 | +0.00 | -0.00 | 0.09 |
| graveyard | +0.00 | +0.00 | +0.00 | 0.08 |
| courtyard | +0.00 | -0.00 | +0.00 | 0.07 |
| forest-edge | +0.00 | +0.00 | +0.00 | 0.05 |

largest percentile delta: 0.01 L*

The crowd's code moved into include files (`<Style>Input.hlsl`, `<Style>Forward.hlsl`). Against a player built from
the commit before (2c6f4712), at song time 60 s without effects, the monster pixels (the ID render, eroded 2 px) differ
in 16 of 263,497 under baseline, 16 under void, 60 under chrome, 96 under painted, 125 under spectrum and 234 under mix:
the ambient particle lights differ between the two builds' bakes (outside the monsters about 54,000 pixels differ under
baseline too, where no crowd shader changed). The see-through styles show that background through themselves
(fire's flames 1,229, crystal 3,176, hologram 17,838; hologram differs between two runs of one player too).

## Tuning

The set sits under the horde and the effects (environment.md, Value structure). Values in `StructureStyle`:

| style | gain | peak | what the set looks like |
|---|---:|---:|---|
| painted | 1.0 | 0.3 | moonlit stone in the horde's lacquer: slate to a muted bone, violet shadows, a warm rim, sparse teal runes on the beat wave |
| fire | 0.7 | 0.1 | charcoal split by molten cracks a structure's size; flames lick from the tops (a second renderer) |
| crystal | 0.16 | 0.2 | a dark sapphire geode: world-space cells, each facet tilted its own way, refracting half the light behind it |
| spectrum | 0.45 | 0.2 | black lacquer, a metre neon lattice, equalizer bars climbing each piece on the beat, the film at grazing edges |
| hologram | 0.3 | 0.07 | projected glass: scanlines, a rolling band, a grid, glitch slices on the off-beats, over glass taking 75% of the light behind |
| chrome | 0.8 | 0.3 | dark mirrors of the synthwave world's grid floor and horizon |
| void | 0.5 | 0.3 | holes into a starfield, ringed by a violet edge that flares on the beat wave |

Every structure shader also holds each channel under the bloom's knee (0.7, `SL_STATIC_CHANNEL_MAX`). What changed on
the way, measured on these shots:

- **Walls are seen edge-on.** From the game camera a wall's faces are near grazing, so the fresnel terms flooded fire
  (a molten rim over the whole wall) and spectrum (a wall of rainbow). Structures scale them down (`SL_STATIC` in
  FireForward.hlsl and SpectrumForward.hlsl), and fire's flames burn from a piece's top (FireShellForward.hlsl).
- **The night's grade, not the stylized one.** With the monster styles' post look (contrast 20, saturation 25, their
  full fringe) the baseline horde lost up to 10 L* against the ground (forest-edge under hologram: 9.9). A structure
  style's post look is the night's own with the style's bloom and an eighth of its chromatic fringe.
- **Structures beside the horde stay dark.** The ring's structure pixels decide the misses: a style that lifts crypt's
  near-black ruins by 10 L* lifts the ring's median by 1.4. Crystal and hologram also show the lighter ground through
  themselves. Hence crystal's low gain and half the light behind, hologram's dim glass, and painted's and spectrum's
  lower gains. Gain alone did not fix hologram (15.1 at 0.25); its fringe and the light it let through did.
- **Painted's strip.** Bone-white facets on the canopies read harsh beside the horde: the strip tops out at a muted bone.
- **No ink line on structures.** An inverted hull splits apart on the kit's hard edges.

`--structure-tune key=value,...` (the `--style-tune` keys) overrides these for a run.
