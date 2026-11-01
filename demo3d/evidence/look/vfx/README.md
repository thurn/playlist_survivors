# Procedural slashes, spins, streaks and smears in the crystal cut (ps-wnj.15, 2026-11-01)

The vfx axis's options in demo3d's crystal cut
([neon-look.md, VFX](../../../../docs/design/neon-look.md#vfx)):
`light-streaks` (edge, solid, hologram) and `prism-streaks` (prism).
`EffectDriver` maps the timeline's effects through the selection's `vfx`
table (Data/effects.json): the slash, thrust and spin trails, the slam's shock
(the formation smashes' too), the bolt, the crystal burst, the hits and the
monster's dust puff. The slash's arc is centred on the blow and sweeps
clockwise; the right-to-left cuts (slashL, slash3) are drawn mirrored. The
action cut applies no look, so it keeps the vendor effects.

Windowless fixed-time Mono Mac player, on jarvis, `--look edge --vfx V`
(`classic` draws the vendor effects), from the worktree's root:

```sh
for V in classic light-streaks prism-streaks; do
  uv run --project demo3d/tools demo3d filmstrip duel-$V --at 1.70 --frames 18 --every 2 --columns 6 \
    --crop 600,200,720,560 --backend mono --evidence look/vfx --player-args "--trailer crystal --look edge --vfx $V"
  uv run --project demo3d/tools demo3d filmstrip shot2-$V --at 2.48 --frames 24 --every 4 --columns 6 \
    --backend mono --evidence look/vfx --player-args "--trailer crystal --look edge --vfx $V"
done
```

- `duel-V.jpg` (1.70 s on, every other frame): the opening duel's slashR
  at about 1.75 s, its ribbon sweeping over the hero from left to right, and
  the spin straight after it, a ribbon a little past a full turn round the
  hero; classic draws Hovl's crescents. `prism-streaks` splits both into
  three bands in the palette's key, accent and counter hues.
- `shot2-V.jpg` (2.48 s on, every fourth frame): the cut into the crypt's
  charge at 2.5 s with its slam, a thin ring of light racing out over the
  horde (three rings, one behind the other, under `prism-streaks`), then the
  cleaves at about 3.3 s, drawn with the spin's ribbon.

The crystal cut fires no projectiles (its spells are lightning and the
ring), so the bolt's light streak (`streak-bolt`, a glowing head with a
streak behind it; `prism-bolt`, three nested streaks) isn't in these strips;
the converter's test covers its emitters. The game's bolts are in
[game/evidence/look/vfx](../../../../game/evidence/look/vfx/README.md).
