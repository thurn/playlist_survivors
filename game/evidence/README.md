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
- [shatter](shatter/README.md): the GPU monster shatter, single and mass deaths breaking on the
  impact frame from the body's pose, and the bench of 1, 50 and 300 deaths at once (ps-msz.3.9).
- [fx](fx/README.md): the sword's slashes and crystal waves, mass crystal death bursts and the
  cleave wave as filmstrips, and the scheduled sounds found on their samples in Unity's mix
  (ps-msz.3.11).
- [title](title/README.md): the title screen's source picker windowless, the player's signature
  and usage description, and headless `--app` and title-to-run-to-title runs (ps-msz.5.3).
- [structures](structures/README.md): crystal structures shattering into PhysX shards on the impact
  frame, glinting and erupting on regrowth, with the physics and shadow bench (ps-msz.3.12).
- [slice](slice/README.md): the assembled cinematic slice on house-128, its stills and filmstrips against
  the reference look, the headless File run's lead and slow CI's measured File run (ps-msz.3.13).
- [run](run/README.md): the run's start and its music-stopped freezes and resumes on a headless File run over
  the survivors test playlist (ps-msz.6.1).
- [arsenal](arsenal/README.md): gems, experience and the level-up's cards on a headless File run,
  stills of the crystal gems, their flight and the cards, and the shots' throughput on the grid
  (ps-msz.6.4).
- [calibration](calibration/README.md): the video and input calibration screens, their per-device file, the
  CALIBRATE rows on the title screen and the pause menu over a frozen run (ps-msz.6.11).
- [replay](replay/README.md): a headless run recorded as a session and replayed to its hash, in the IL2CPP player
  and in .NET, and a changed record caught (ps-msz.6.10).
- [schedule](schedule/README.md): the full schedule on a 30-minute .NET run over the survivors test playlist's
  dump: every minute's roles and all 20 bosses, never above the cap of 300, and victory at 30:00 (ps-msz.6.2).
- [cast](cast/README.md): every role and boss in its crystal and gem colour, and bosses telegraphing, charging and
  slamming on the beat (ps-msz.6.2).
- [music-events](music-events/README.md): a zone change easing the arena's lighting, every swarm, set piece and the
  signature minotaur as filmstrips, and a 30-minute run's events under the cap (ps-msz.6.3).
- [weapons](weapons/README.md): Garlic's aura pulsing on the beat and Fire Trail's wake following the
  hero's tiles, as filmstrips, and their rules' tests against native's numbers (ps-msz.6.7); Magic
  Wand's violet bolts and King Bible's gold shards as filmstrips, and a headless run's shots, hits
  and rings measured on the beat grid (ps-msz.6.5); Lightning Ring's strikes, Thunder Loop's returns and
  Chain Lightning's jumps as filmstrips, every bolt on a sixteenth, and their sounds found on their
  samples in Unity's mix (ps-msz.6.6).
- [hud](hud/README.md): the crystal HUD in every screen state (play, waiting, music stopped, pause, the
  level-up's cards, a chest preview, victory and YOU FELL) and the beat gem's ring landing on the beat frame (ps-msz.6.9).
- [full-run](full-run/README.md): the 30-minute File run to the victory card with the bot, its pacing and
  minute 25's stills (ps-msz.6.14), and retaken at fixed time in 4 minutes, with its replay and a still at
  25:00 of a real run (ps-msz.7.5).
- [look-m4](look-m4/README.md): M4's look pass, the five zones settled and mid-ease at the run's density, the
  peak's heavy strike, break and mass kill, and every weapon's and evolution's effect in every zone, against the
  reference's stills, with before and after (ps-msz.8.3).
- [sweeps](sweeps/README.md): the positioning bot's four 30-minute .NET runs over the survivors test playlist,
  all to the victory card above 25% health, and the idle bot falling at 2:05, with each run's pacing against
  `startLevels`, its boss chests' drop-to-take delays and the late crowd's role mix (ps-msz.6.13, ps-msz.8.4).
  all to the victory card above 25% health, and the idle bot falling at 2:06, with each run's pacing (ps-msz.6.13).
- [perf](perf/README.md): the run's performance on the Mac from `game perf`: the windowless frame probe and
  the CPU frame profile at the peak, a structure break, the finale's mass kill, the run's first minute and the
  30-minute fixed-time run (ps-msz.8.2); the peak's frame split into CPU and GPU with each pass's cost, and SSAO
  at half resolution, the tails shorter and the look unchanged (ps-msz.8.8).
  30-minute fixed-time run (ps-msz.8.2); no frame over 33 ms, the allocations removed by the census, and every
  first sighting probed on a cold launch with and without the boot's graphics-state warm-up (ps-msz.8.9).
- [beat](beat/README.md): the on-the-beat audit, every key moment on the first frame at or after its song time at the
  run's peak and over an early stretch, and every sound on its event's song time, with the late moments it found
  fixed (before and after filmstrips); and the key rules' late steps measured in .NET and tuned (ps-msz.8.5).
- [payoff](payoff/README.md): the 30-minute run's sounds by category before and after (none dropped now, a tenth as many
  cut off), the finale's blast flashing a survivor, and filmstrips of mass kills, structure breaks, regrowth and the
  finale at the peak with the live-shard count over a peak minute (ps-msz.8.6).
- [recap](recap/README.md): the victory card from the sim's recap at the peak, and a headless run's summary
  reporting no presenter faults, its session replayed to its hash (ps-msz.8.7).
