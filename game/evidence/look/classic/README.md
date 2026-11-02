# The classic look in game (2026-11-01)

`--look classic` draws the game as it was before the neon look (ps-wnj), for
parity comparisons ([neon-look.md, Flags](../../../../docs/archive/design/neon-look.md#flags)):
every axis `classic`, so the Synty arena, the crystal monsters and structures,
and the night post (`NightLook`'s grade and LUT, restored for it) at the
night's beat feel.

## Against the tree before the epic

`pre-wnj/`: a build of `34950f68^` (the commit before the epic's design doc);
`classic/`: this tree under `--look classic`. Same moments as the post stack's
evidence (minute 25 with the crowd at 300, the third still on a structure
break):

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
EV=evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5
uv run --project demo3d/tools game look classic --at 20,45,64.1 --evidence look/classic/classic \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,break@64 --look classic"
uv run --project demo3d/tools demo3d flip PRE_WNJ_CAPTURES captures/game/look/classic
```

FLIP over the three full-size frames: mean 0.00104, p99 0.0781, the same
image up to particle and shard noise. The player logs
`Look: look classic, palette ion; monsters classic, ... motion none` and no
"not built" warning.

Not covered here: demo3d's crystal cut under `--look classic` keeps its
environments' night volumes, shot grades and trailer dressing, but draws its
monsters through the package's crowd (ps-wnj.22's differences), so it is
close to v2, not identical.
