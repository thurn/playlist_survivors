# Structure breaks on screen

Bead ps-suy.3 (epic ps-suy): `--structure-breaks walls` under each structure style, as the Mac's batch-mode
IL2CPP player renders it in fixed time, with effects on. The design is in
[environment.md, Breaks on screen](../../../../docs/tech-demo-3d/environment.md#breaks-on-screen); the
choreography (the bolts, the openings, the horde's numbers) in
[choreography.md, Structure breaks](../../../../docs/tech-demo-3d/choreography.md#structure-breaks).
Rendered 2026-10-30 from the branch on staging 2632ccf6.

The walls schedule has three moments, two bolts each:

| moment | song time | target | pieces | shards (crystal) | wall size |
|---|---|---|---|---|---|
| gate | 111.49 s (the build's downbeat) | ruins-gate/1 and /2, the hero in the gap | 5 + 5 | 77 + 75 | 5.8 m each |
| court-south | 123.88 s (drop-2's downbeat) | courtyard/south-west and south-east | 3 + 3 | 47 + 49 | 4.7, 4.6 m |
| court-east | 136.27 s (drop-2's second phrase) | courtyard/east-south and east-north | 3 + 3 | 52 + 43 | 4.6, 4.4 m |

At most 152 shards are live at once (the gate). Each break re-renders the static shadow map in 5 ms and patches
the ground's 2048² shadowmask in 17 to 23 ms (the log's `ArenaRuntime` lines), on the impact frame.

## Strips

`<moment>-<style>.jpg`: the break, every 4th frame (1/15 s) from just before the strike, cropped to the walls.
`<moment>-<style>-horde.jpg`: the whole view every half second for 5 s from the strike, the horde pouring through
the opening. Commands (`demo3d filmstrip ...`, `<preset>` = `--structure-breaks walls --structure-style <style>`):

| moment | the break | the horde |
|---|---|---|
| gate | `gate-<style> --at 111.45 --frames 12 --every 4 --columns 4 --scale 0.9 --crop 0,40,1920,640 --player-args "<preset>" --evidence structures/breaks` | `gate-<style>-horde --at 111.45 --frames 10 --every 30 --columns 5 --scale 0.6 --player-args "<preset>" --evidence structures/breaks` |
| court-south | the same `--at 123.84 --crop 150,0,1400,560` | the same `--at 123.84` |
| court-east | the same `--at 136.23 --crop 1000,100,920,700` | the same `--at 136.23` |

| style | gate | court-south | court-east |
|---|---|---|---|
| crystal | [break](gate-crystal.jpg), [horde](gate-crystal-horde.jpg) | [break](court-south-crystal.jpg), [horde](court-south-crystal-horde.jpg) | [break](court-east-crystal.jpg), [horde](court-east-crystal-horde.jpg) |
| fire | [break](gate-fire.jpg), [horde](gate-fire-horde.jpg) | [break](court-south-fire.jpg), [horde](court-south-fire-horde.jpg) | [break](court-east-fire.jpg), [horde](court-east-fire-horde.jpg) |
| hologram | [break](gate-hologram.jpg), [horde](gate-hologram-horde.jpg) | [break](court-south-hologram.jpg), [horde](court-south-hologram-horde.jpg) | [break](court-east-hologram.jpg), [horde](court-east-hologram-horde.jpg) |
| baseline | [break](gate-baseline.jpg), [horde](gate-baseline-horde.jpg) | [break](court-south-baseline.jpg), [horde](court-south-baseline-horde.jpg) | [break](court-east-baseline.jpg), [horde](court-east-baseline-horde.jpg) |

The spot-check, `--structure-breaks walls --monster-style crystal --intensity overdrive` (structures baseline),
uncropped, every 6th frame (overdrive's wider view and route put the walls elsewhere in the frame):
[gate](gate-overdrive.jpg), [court-south](court-south-overdrive.jpg), [court-east](court-east-overdrive.jpg).

What they show:

- **Crystal**: the strike frame already shows the break: the seams nearest the bolt light and those shards fly,
  and the light runs out along each run from the strike (the crack front at 22 m/s: about half a second to the far
  end of the gate's 10 m runs), each shard going ice-white as it steps out of the wall and bursting 0.07 s later,
  until the walls are a sapphire scree the horde walks over.
- **Fire**: the wall flares molten on the strike, then slumps into charcoal chunks glowing gold inside, burning
  away from the base over two seconds, with the explosion at the strike and embers over the rubble.
- **Hologram**: a cyan flash and the heavy hit's sparks, the pieces glitching sideways and scanning out from the
  top as they drift down.
- **Baseline**: the kit's stone and timber break into rubble along the bolt, with a ground shock at the strike and
  dust at each piece.
- **The horde** comes through every opening in the strips after it (the break table's 3 to 15 monsters per opening
  in 4 bars; on the courtyard's south side the horde that filled the courtyard comes at the hero).

## No ghost shadows

[no-ghost-shadows.jpg](no-ghost-shadows.jpg): `--beauty-empty --no-effects` (no horde, the kit's own materials),
without the flag (left) and with it (right), at 116.0 s (after the gate), 128.5 s (after the courtyard's south
walls) and 140.5 s (after its east walls). The hero's route differs with the flag, so the camera does too; compare
the ground where each wall stood. The broken walls' shadows go with them, on the ground (the patched shadowmask)
and in the static shadow map, while the walls still standing keep theirs. What stays is a faint thin line along
each broken wall's base: the ground's baked indirect light (its contact darkening), which no shadow term holds.

## Without the flags

Every code path is gated on a break, and `demo3d look` without either flag matches
[look/final](../../look/final/README.md) in the six beauty shots (largest percentile delta 0.02 L*; readability
the same within 0.04). The song stills follow the choreography, which staging has changed since look/final was
made, so the whole set (song stills and beauty shots, with and without the horde) is also compared against a
player built from staging's 2632ccf6, the commit this change sits on: full-resolution PNGs, L* per pixel.

| still | dL* p2 | dL* p50 | dL* p98 | mean abs dL* per pixel |
|---|---:|---:|---:|---:|
| courtyard-empty | +0.00 | +0.00 | +0.00 | 0.00 |
| courtyard-horde | +0.00 | +0.00 | +0.00 | 0.00 |
| forest-edge-empty | +0.00 | +0.00 | +0.00 | 0.00 |
| forest-edge-horde | +0.00 | +0.00 | -0.00 | 0.00 |
| glowing-tree-empty | +0.00 | +0.00 | +0.00 | 0.00 |
| glowing-tree-horde | +0.00 | +0.00 | +0.00 | 0.00 |
| graveyard-empty | +0.00 | +0.00 | +0.00 | 0.00 |
| graveyard-horde | +0.00 | -0.00 | +0.02 | 0.00 |
| opening-empty | +0.00 | +0.00 | +0.00 | 0.00 |
| opening-horde | +0.00 | +0.00 | +0.00 | 0.00 |
| path-empty | +0.00 | +0.00 | +0.00 | 0.00 |
| path-horde | +0.00 | +0.00 | +0.00 | 0.00 |
| song-025 | +0.00 | +0.00 | -0.03 | 0.00 |
| song-060 | +0.00 | -0.00 | +0.00 | 0.00 |
| song-100 | +0.00 | +0.00 | -0.01 | 0.00 |
| song-145 | +0.00 | +0.00 | +0.00 | 0.00 |
| song-175 | +0.00 | +0.00 | +0.00 | 0.01 |

largest percentile delta: 0.03 L*
