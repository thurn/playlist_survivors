# The game's evidence

Each implementation bead of the Unity game (epic ps-msz, [the design](../../docs/design/unity-game.md))
keeps what it measured and saw in a folder here, `game/evidence/<slug>/`, with a short README:
what was run (the exact commands), the numbers, and what they show.

- **Stills and filmstrips**: small JPEGs (or PNGs) carrying the `ps-render` tag, which the vendor
  guard requires of every image under `game/evidence/`
  ([the vendor guard](../../docs/tech-demo-3d/vendor-assets.md#the-vendor-guard)). The game's
  own tools write them tagged: `uv run --project demo3d/tools game look <slug> [--at 1,5]` renders
  the player windowless and keeps 960 px stills in `look/<slug>/`, with the full-size ones in the
  gitignored `captures/game/look/<slug>/`.
- **Nothing derived from a vendor file or the player's music**: no audio, no vendor textures or
  meshes, no crops of them. Renders of the game are our own and fine.
- **Windowless only**: agents never open a player or editor window
  ([Running on the Mac](../../docs/tech-demo-3d/mac.md)). Checks only the operator can make (a
  window, speakers, a permission dialog, Bluetooth) go on [the operator's list](operator-pending.md).

## Sets

- [look/boot](look/boot/README.md): the empty boot scene, windowless, proving the build and stills
  pipeline (ps-msz.1.3).
- [spike](spike/README.md): the sync spike, File audio through the music plugin into Unity's
  output with a click on every beat, measured from Unity's own mix (ps-msz.1.6).
- [sim-enemies](sim-enemies/README.md): the sim's step time with 300 enemies alive (ps-msz.3.2).
- [sim-structures](sim-structures/README.md): crystal structures breaking on heavy strikes and
  cleave bands, and regrowing after zone changes, on a real dump in the game's arena (ps-msz.3.4).
- [loop](loop/README.md): the client's frame loop, its clocks, the lead and frame-exact fixed
  time, with the debug overlay and placeholder capsules (ps-msz.3.5).
- [arena](arena/README.md): the 240 m night arena with its crystal structures, windowless from the
  game camera's framing, beside the 3D demo's reference look (ps-msz.3.6).
- [look/feel](look/feel/README.md): the night post's beat feel from the live stream, the fast
  lane and the camera, as fixed-time filmstrips across a beat, an accent and a drop (ps-msz.3.7).
- [crowd](crowd/README.md): the crystal horde, 300 monsters from a synthetic feed over house-128,
  their readability and their footplants on the beat frame (ps-msz.3.8).
