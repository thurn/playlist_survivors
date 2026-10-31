# 3D Tech Demo: Manual QA

## Manual QA

Manual QA watches the demo and its tools the way the operator will use them,
on Cygnus. The development build's command-line flags put it into each state needed
here, since the demo itself takes no input:

```
--overlay           on-screen counts, frame times against the pass lines
--click             mix a click track: every beat, louder on downbeats
--start-at <s>      seek the song; --seconds <n> to exit after n seconds
--sound             play sound (a built player starts muted; `just demo3d` passes it)
--fixed-time        deterministic 1/60 s frames, sound muted
--scene <S0..S6|song> --n <count> --view-width <m> --load <preset> --aa <mode>
--freeze-at <s>     hold one frame for inspection
--show-lod          tint monsters by LOD; --overdraw for a heat map
--show-nav          draw the flow field and blocked cells over the ground
--intensity <normal|overdrive>
                    the choreography's intensity: overdrive is the trailer's
                    horde (choreography.md, Intensity overlays)
--resident <env,...> load these environments too, resident beside
                    --environment's, which plays first (trailer.md,
                    Environments in one player)
--switch-every <s>  with --resident, cycle the drawn environment every s song
                    seconds from --start-at
--structure-style <name>
                    the arena's structures in a monster style's treatment
                    (environment.md, Structure styles)
--structure-breaks <walls>
                    the hero's bolts break walls on the beat, and the horde
                    paths through the openings (choreography.md, Structure
                    breaks)
--trailer [name]    play the trailer file trailer-NAME.json (default action):
                    its shots cut on the beat across their environments
                    (trailer.md). Look and cast flags apply to every shot;
                    --intensity, --scene, --envelope, --view-width, --hold-at,
                    --beauty-empty, --structure-breaks, --resident and
                    --switch-every fail, naming the conflict
  --environment <name>  pin every shot to one environment
  --start-at, --stills-at, --seconds, --freeze-at, --gpu-probe-at
                    take trailer seconds (song seconds less the file's startS)
  --shot <n>        only shot n, from its cut to its end, music included
  --from-preroll    with --shot, from the start of its pre-roll
  --overlay         adds the trailer time, the shot and its liveness
                    violations (live only; a capture warns and leaves it out)
  --no-trailer-look no per-shot grade, grain, lens dirt or lens hits, for
                    comparing the environments against normal runs
--supersample <1-2> render at that multiple of the output resolution and
                    downsample (trailer.md, Look): a trailer's captures
                    default to 2, its live run to performance.md's factor,
                    anything else to 1
```

The trailer's finished video (trailer.md, Capture) comes from one tool
command, which opens the windowed player, so the operator runs it, on the Mac
or Cygnus. `just trailer` builds the player first, then captures
(`--out` defaults to `captures/trailer.mp4`):

```
just trailer --out captures/trailer-neon-crystal.mp4 -- --palette neon --monster-style crystal
```

It is `uv run --project demo3d/tools demo3d trailer`, which doesn't build.
Player flags go after `--`. It prints the mix's checks and exits 1 if one
fails. `--stand-in` runs the same steps windowless, with a black picture.

### First assembled check

Run this as soon as TD4 has landed, before the GPU crowd or effects
exist. It exercises the main integration boundary: the choreographer's
timeline driving the client's crowd and hero on the audio clock. TD4's stock
baseline crowd handles a few hundred monsters, which is enough.

- Launch the song with `--click --overlay --seconds 60` and about 300
  monsters.
- **Expect** every monster's gait step, every hit flinch and every death to
  land on a click, with no visible drift over the minute.
- **Expect** feet that do not slide, and crossfades without pops.
- **Expect** the overlay's counts to match the level script's targets.
- Seek with `--start-at 45`. **Expect** the horde's state at 0:45 to appear
  immediately, with the same positions as a run from the start.
- Seek into the mapped ending. **Expect** hero slams on its curated accents.
  If TD0 identifies a free section, seek there too: **expect** monsters to
  advance smoothly instead of stepping to beats.
- Seek into a laser sweep, once TD10 has added it. **Expect** swept monsters
  to flash without flinching or sliding back.

### Vendor guard drill

- In a scratch worktree on Cygnus, force-add a file from the vendor folder
  and run the pre-push guard. **Expect** a failure naming the file, and no
  push.
- At integration, do the same in a scratch worktree on the Mac, with a file
  from the Mac's sources, and run `scripts/check-fast`. **Expect** the same
  failure.
- Copy a vendor PNG into a tracked folder under `demo3d/`. **Expect** the
  binary-file failure.
- Copy a vendor prefab with its `.meta` into a tracked folder. **Expect** the
  GUID failure.
- Commit a capture-tool still under the evidence. **Expect** a pass.

### The full demo

- Run the showcase build windowed at 1080p with `--overlay`.
- **Expect** the full *Action* recording (about 3:19), with the arc from a
  handful of monsters to the envelope count at the mapped final energy
  peak, then the ending's accents clearing the screen.
- **Expect** the hero to travel between the level script's goal areas,
  fighting on the way, and every screen to show a landmark or edge
  decoration.
- **Expect** the overlay's rolling 20 s p99 times to stay under the pass lines
  all the way through, with no frame over twice its line.
- **Expect** every hit and death to be visible, with no effect missing in the
  densest beats and no lights flickering out.
- Watch the frames right after the densest downbeats, where a few hundred
  effects spawn at once. **Expect** no stutter on the beat, and the overlay's
  largest spawn count per frame to match the stress estimates (about 150 at
  demo load).
- Compare an early fight against one monster with the climax by eye. **Expect**
  each monster to look and react the same.
- Listen at the densest beats. **Expect** weight, not clipping or a wall of
  identical sounds.

### Sweeps and the machine lock

- Run a small sweep (one scene, two points). **Expect** result rows with
  timings, counters, machine state and a pass flag.
- Start a heavy build in another terminal, then run the sweep. **Expect** a
  bounded wait, then runs marked noisy, repeated, and counted in the report.
- Start a second sweep while one runs. **Expect** it to wait for the lock.
- Kill a sweep with `taskkill /F`, then start another. **Expect** the new
  sweep to take the lock at once.
- Switch the power plan to Balanced. **Expect** a refusal naming the plan.
- Lock the desktop, then start a sweep. **Expect** a refusal.
- Let the display turn off, then start a sweep. **Expect** the display to
  wake and the sweep to run with its window composited.
- Run three sweeps back to back. **Expect** any thermal throttling reported,
  never hidden.
- Launch with a different window size. **Expect** a refusal naming the
  backbuffer size.
- Check every result row. **Expect** `jobWorkers: 6`.

### Walls and the sim

- Run S6 windowed with `--overlay`, about a minute.
- **Expect** the horde to flow around the courtyard walls and through its
  gaps, and never to pass through a wall.
- **Expect** jams at the gaps to read as monsters marking time on the beat,
  not jittering or vibrating against each other.
- **Expect** elites and the boss to fit through every gap.
- Check the result rows of S6 and one S3 frontier point. **Expect** the
  sim's tick p99 at or under 3.5 ms.

### The scene

- Flip through the six beauty shots, empty and with the horde. **Expect** a
  scene that looks finished: a landmark or edge decoration in every shot,
  readable monsters, and effects that stand out from the set.
- Run S0 windowed with `--overlay` at 1× and 2× density. **Expect** S0's GPU
  p99 under 1.5 ms at the authored density.
- Walk the hero along a forest edge (`--start-at` a section that does).
  **Expect** canopies near the hero to dither out, and never to hide it.
- Look for floating props, clipping, and obvious repeating patterns in the
  scatter. **Expect** none.
- Compare the final scene with rung E0 in the report. **Expect** the same
  look at a fraction of the cost.

### Structure styles

- Run the song windowed with `--structure-style <name>` for each of painted,
  fire, crystal, spectrum, hologram, chrome and void, on the forest and with
  `--environment crypt`, and once with `--monster-style fire --structure-style
  match`.
- **Expect** the courtyard's walls, the landmarks and the trees in the style,
  pulsing with the music as the horde does, and the ground, mist, runes and
  small props as they are. Trees near the hero still dither out.
- **Expect** the set to stay behind the horde and the effects: no wall
  brighter than the monsters in front of it, and no structure blooming between
  beats.
- **Expect** an unknown name to stop the player at start-up, listing the known
  ones.

### Structure breaks

- Run the song with `--structure-breaks walls --start-at 108 --seconds 35`,
  and again with `--intensity overdrive` and with `--environment crypt`.
- **Expect** a bolt from the hero to each wall it breaks, striking on the
  build's first downbeat (the ruins gate's two wall runs, the hero in its
  gap), on drop-2's first downbeat (the courtyard's south walls) and on
  drop-2's second phrase (its east walls), every one in view.
- **Expect** the horde to walk through the openings after each break, and
  never through a wall still standing.
- **Expect** an unknown schedule to stop the player at start-up, listing the
  known ones.
- **Expect** each struck piece to shatter on the bolt's impact
  ([Breaks on screen](environment.md#breaks-on-screen)): its shards burst
  from the strike and along the bolt, with a flash, a shake and the break's
  sound, and sink into the ground a few seconds later. Under
  `--structure-style crystal` the seams glow across the wall from the strike
  as it bursts; under `fire` the wall collapses and burns out in embers;
  under `hologram` its pieces glitch and scan out.
- **Expect** no shadow left where a broken piece stood, on the ground or on
  the monsters walking through.

### A/B review

- For each accepted cut, flip between its A and B stills at 100% zoom, and
  watch its video pair.
- **Expect** no visible difference. Any visible one goes back to its task as a
  failed cut.
- For each converted effect, compare its still with the stock prefab's.
- **Expect** the same shape, color and timing.

### Edge cases

- **A missing vendor cache:** run setup in a fresh worktree with the cache
  folder renamed. **Expect** a clear error naming the missing source.
