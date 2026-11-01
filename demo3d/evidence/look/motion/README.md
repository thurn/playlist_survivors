# Motion layers in the crystal cut (ps-wnj.14, 2026-11-01)

The look package's springs and swarm in demo3d's crystal cut
([neon-look.md, Motion](../../../../docs/design/neon-look.md#motion); the game's evidence is
[game/evidence/look/motion](../../../../game/evidence/look/motion/README.md)). One Mono build,
`--motion none` for before:

```sh
uv run --project demo3d/tools demo3d filmstrip motion-breath-after --at 55.85 --frames 12 --every 2 --columns 4 --warmup 3 \
  --backend mono --player-args "--trailer crystal --look solid" --evidence look/motion
```

[motion-breath-before.jpg](motion-breath-before.jpg) and
[motion-breath-after.jpg](motion-breath-after.jpg): the dragon's breath and the mass kill at
55.9 s. After, the skeleton block (green, right) no longer reads as a stamped lattice: each body
stands a little off its spot, at its own size and tilt, and the gait steps after each beat lean the
bodies into their travel (the trailer's steps move after their plant, so the plant kicks the lean).
The breath, the kills on its impact frame and the shards are unchanged; deaths and shards take no
spring. The trailer's step timing is the timeline's and the springs move only the bodies' shape,
so its checks are unaffected; `--beat-ripple` stays off in every preset.
