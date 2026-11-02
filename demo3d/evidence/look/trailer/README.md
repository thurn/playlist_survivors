# The crystal cut in all four presets (ps-wnj.19, 2026-11-01)

The crystal cut (`--trailer crystal`) in every look preset, judged on a still
from every shot against the style guide's rules
([neon-look.md, The Trailer](../../../../docs/design/neon-look.md#the-trailer);
[neon-look-style.md, Rules and Thresholds](../../../../docs/design/neon-look-style.md#rules-and-thresholds)).

```sh
just trailer-looks                 # the operator: captures/trailer-crystal-{edge,solid,hologram,prism}.mp4, windowed
just trailer-looks --stand-in      # agents: every step but the window (the cues, the mix and its checks)
# windowless stills of an IL2CPP Mac player of this change, per preset (edge, solid, hologram, prism)
APP="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --look PRESET --capture-stills "$PWD/DIR" \
  --stills-format png --capture-mask --stills-at 1.6,4,12,20,28,36,44,47.2,50.2,52,55.95,56.3 --seconds 57
uv run --project demo3d/tools demo3d look-metrics DIR --palette PALETTE.json --preset PRESET \
  --impact still-055.950 --out metrics-PRESET
```

55.95 s (the finale's breath, a special moment) is the one impact frame;
56.3 s, its shards 0.35 s on, is past the moment and judged as a steady
still. The video captures are the operator's (a windowed player;
game/evidence/operator-pending.md). `just trailer-looks --stand-in` passes
every mix check in all four presets.

## Files

- `sheet-v2.jpg`: v2 (captures/trailer-crystal-v2.mp4) at the same twelve
  moments, for comparison. Its shots were Synty environments, its monsters
  bright crystal.
- `sheet-PRESET.jpg`: the twelve stills of each preset (1.6 s is shot 1, the
  duel; 4 and 12 s shot 2; 20 s shot 3; 28 and 36 s shot 4; 44 s shot 5;
  47.2 s shot 6; 50.2 and 52 s shot 7; 55.95 and 56.3 s the finale).
- `metrics-PRESET/`: every rule on every still (with its ID render and view
  json) and the cut's own rules.

## What the stills broke, and the fixes

Rules broken over the twelve stills, at staging before this bead (43c6fbda)
and after, both judged by the rules as they are now:

| preset | before | after |
| --- | --- | --- |
| edge | separation 10, bright share 1, key 1, floor p99 1 | separation 10, bright share 1, key 1 |
| solid | separation 7, bright share 2, key 2, floor p99 1, impact halo 1 | separation 3, bright share 2, key 2 |
| hologram | separation 9, bright share 4, key 4, floor p99 1, conformance 1; the cut's key | separation 7, key 4, bright share 1; the cut's key (0.616) |
| prism | key 10, families 5, separation 5, bright share 4, floor p99 4, conformance 4, form 1; the cut's key (0.38) | separation 5, key 3, floor p99 2, bright share 1 |

Before this bead the halo broke on every still of every preset (0.3-12
against 15); see the first fix.

1. **The halo flares in a special moment.** At five times edge's calm halo
   (`--post-tune halo.intensity=3`) the steady stills reached only 6-10
   while the grid's floor p99 broke (20-27 against 20); solid's broke its
   hero margin too. The calm look and a steady glow can't both hold, so the
   post's halo now grows with `_LookSpecial` (`halo.special`: 1.5, solid 7)
   and the rule is `halo_impact_min`. Steady stills are unchanged; the
   impact frame measures edge 25.3, solid 15.7, hologram 26.4, prism 25.1.
2. **Capture-true lines.** The edge, hologram and gem looks drew their lines
   in pixels of the frame drawn, so a capture (drawn at 2x, downsampled) had
   them half a pixel wide. They are pixels at 1080 rows now. This raised
   edge's bright share about 10% and moved little else: thin lines weren't
   what broke the rules.
3. **Prism's lens.** Its chromatic aberration (0.2) sprayed rainbow fringes
   over the frame's edges. At 0.04 the key went from 0.29-0.50 to 0.38-0.92
   (the cut's median 0.38 to 0.63), conformance from 0.59-0.85 to 0.88-1,
   and no still has five families.
4. **Resting light, not a lower floor** (ps-wnj.24's open question):
   hologram's lines 3.0 and rim 2.0 (were 2.4, 1.6), the gem's shine, rim
   and lines 1.5, 1.7, 1.0 (were 1.1, 1.3, 0.7). Hologram's and prism's
   sparse shots (28, 47.2, 52 s) now clear `bright_share_min` (0.054-0.065,
   were 0.026-0.040). Hologram's brighter lines also part its clumps:
   hologram broke separation in nine stills before, seven after.
5. **Solid's outline** 3.5 px (was 2.5): separation breaks in three stills,
   down from seven (the clumps at 4 and 47.2 s: 17.5 and 12.4 to 19.6 and
   22.5).
6. **The spells' vendor effects.** The ring and lightning spells still drew
   their Hovl prefabs under every vfx option: the lightning's magic circles
   on the floor broke edge's, solid's and hologram's floor p99 at 50.2 s
   (23-26). Each vfx option now maps them to its slam ring and hit sparks
   (50.2 s: 6-10).

Tried and dropped: brighter or thinner edge lines (`--monster-tune
Edge=3.6,Rim=2.6`, `EdgePx=0.8`, `Rim=1`) moved edge's separation by 1 at
most; the prism ring spell as one ring instead of three left prism's floor
p99 unchanged.

## Still breaking (follow-ups ps-wnj.28 and ps-wnj.29)

ps-wnj.29 fixed all but swarm separation: [last-breaks](last-breaks/README.md).

- **Swarm separation**: edge in ten of eleven steady stills (8.1-12.1). Its
  finely cut types (the magenta and orange ones) draw so many feature lines
  at the cut's 10-20 px that each body fills with them, and touching
  neighbours merge. Also hologram (7.9-12.0 in seven stills), prism (9.8-12
  in five), solid (10.7-11.7 in three).
- **The key in shot 3** (20 s, every preset, 0.32-0.44): the shot puts
  three enemy families on screen at once. Also hologram at 36, 47.2 and
  56.3 s and its cut median (0.616 against 0.62), prism at 44 and 47.2 s,
  and solid at 56.3 s.
- **Bright share in the duel** (1.6 s, every preset, 0.011-0.018): three
  elites alone in the void, by staging. Also solid at 28 s (0.021).
- **Prism's floor p99** at 12 and 36 s (27.5, 34.4 against 26): its
  special-moment slam rings and the formation shards on the floor.

## Checks

- `trailer --name crystal --checks` and `--name action --checks`: ok.
- **The action cut is unchanged**: windowless stills at 3, 12, 24, 36, 48
  and 56 s from a player of this change and one of the same tree with the
  package and effects as at 43c6fbda differ by at most 16 levels on 0.03-0.2%
  of their pixels; two runs of the same player differ as much (at most 7
  levels on 0.03-0.2%).
- Unity EditMode (`demo3d test`): 537 of 540 pass, 3 skipped, none fail; the
  post tune, the special halo and `MaterialTune` among them.
