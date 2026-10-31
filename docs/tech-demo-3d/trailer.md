# 3D Tech Demo: Trailer Mode

The 3D tech demo is a non-interactive Unity build in which a hero carves
through a horde of animated monsters to *Action* by Alessia Labate & Conrad
Taylor, as if a perfect player were in control (see [the demo's
overview](README.md)). Today it plays the whole 3:19 song as one continuous
battle.

This document specifies **trailer mode**, a `--trailer` flag on the same
player. It turns the demo into a gameplay sizzle reel of about 30 seconds:
31.1 s of gameplay, then a 1.5 s fade on black.

- **Ten shots,** cut on the beat across four environments, set to bars 79–99
  of *Action*.
- **No text and no UI.**
- **Live and captured.** It plays live in the normal build with sound. Through
  the existing video capture (`--capture-video`), it also renders a
  frame-perfect 1080p60 video with mixed audio.

Every frame is real gameplay from the demo's own simulation, but each shot is
staged for it:

- **Its own short simulation.** Each shot has its own environment, horde
  formation, density and set of hero moves.
- **Everything always moves.** The hero runs and strikes on every beat, and
  the horde chases them.
- **Every monster dies to one blow.**

The trailer's job is to make a viewer want to play. That sets its goals:

- **Hook within three seconds.** The song's heaviest drop lands 2.48 s in,
  and the first cut blows a crater in a 600-strong horde on it.
- **Feeling over explanation.** Impact, speed, power, chaos and satisfaction,
  with nothing that explains mechanics.
- **Everything on the music.** Blows, kills, spells, cuts, flashes and the
  horde's steps all land on the song's beat grid.
- **Rising intensity.** Density, view width, cut pace and the hero's moves all
  escalate to a climax that ends in a screen-clearing slam.
- **No dead air.** Every shot opens on a blow, and no beat goes by without an
  action.

These constraints are fixed:

- **No idle crowds.** The hero and the monsters never stand around (see
  [Rules for Every Frame](#rules-for-every-frame)).
- **Gameplay framing.** The game's own camera is used in every shot, and no
  text or UI is drawn over the picture.
- **The normal flags still work.** The demo's look and cast flags work in
  trailer mode as they do in a normal run (see
  [Command Line](#command-line)).
- **Visuals first.** Performance is secondary. The capture is offline, so it
  has no frame budget.

## Related Information

- [3D Tech Demo](README.md): the demo this mode extends. A plain C#
  simulation, the **choreographer**, decides everything at load, and Unity
  only draws its output.
- [Choreography](choreography.md): how the choreographer works and the
  **level script**, the JSON file of per-section tuning it reads. It also
  covers intensity overlays and the tuning fields the trailer builds on.
- [Camera and Sound](camera-and-sound.md): the game camera, the blow-weight
  feel and the sound aggregator, all reused here.
- [Environment](environment.md): the four environments, how each is baked and
  lit, and the arena plan they share.
- [Crowd and Hero](crowd.md): the monster roster, monster styles and roster
  sets, which the trailer's flags select.
- [Running on the Mac](mac.md): building the player, windowless stills, and
  the video capture's limits on the Mac.
- [Manual QA](qa.md): the player's existing flags.
- [Performance](performance.md): where measured costs are recorded.
- [Action's song form](../../songs/action/form.md): the song's grid, sections,
  anchors and accents, which the shot list is cut to.
- Epic ps-5pd in the repo's bead tracker (closed): `--intensity overdrive`,
  whose tuning and feel are the trailer's starting point (see
  [Overdrive](#overdrive-the-starting-point) below). Its evidence is in
  [demo3d/evidence/overdrive/](../../demo3d/evidence/overdrive/).
- Epic ps-bop: the environments still to come (neon city, blossom temple, a
  crypt rework). They join the trailer as data once they exist.

## Background

This section introduces the existing pieces the design reuses, so later
sections can use their names.

### The simulation and the beat grid

The choreographer is a deterministic simulation. It runs at load over the
whole song, at 60 ticks per second, and produces a **timeline**: every
entity's motion, plus every action, effect and sound, stamped in song
seconds. Each frame, the client evaluates the timeline at the current song
time and draws the result.

Decisions in the simulation are quantized to the song's **grid**, the beat
positions from the song map:

- **Monsters** take one **gait step**, a stride of their type's length, on
  each beat.
- **The hero** strides or attacks on each beat.
- **Blows** land on grid positions.

The level script names which moves fire where, as a cycle of combo entries
plus positioned entries. An entry is a move, optionally followed by `@` and a
grid position:

```json
"combo":  ["slashL", "slashR", "slashR@eighth", "spin@accent", "slam@phrase"],
"spells": ["ring@downbeat", "bolt@accent"]
```

- **Plain entries cycle**, one per action.
- **Positioned entries preempt the cycle** on their position: `beat`,
  `eighth`, `downbeat`, `phrase` (the downbeat that opens a phrase) or
  `accent` (a curated hit in the song map).
- **`@eighth` entries are flurries**: extra slashes on the eighth notes
  between beats.
- **Spells fire** on every occurrence of their position.

### The hero's moves

The hero is a knight with a crystal sword and a shield. These are their moves:

- **Combos:** `slashL` and `slashR` (arcs), `thrust` (a line), `spin` (a ring
  around the hero) and `slam` (a larger ring and a shockwave).
- **Spells:**
  - `ring`: a shockwave expanding around the hero;
  - `bolt`: a projectile at one monster;
  - `lightning`: a strike on an area;
  - `laser`: a beam swept along a line that hits everything in it on every
    eighth note.
- **Parry:** a monster's blow that would land is blocked, with a spark. The
  hero has no health; they are a perfect player.

Each blow has a **hit shape**, its area (an arc, ring, line or circle). The
shape is tested against monster positions at the impact.

A **reach** is how far an attack can strike: 2.4 m for the hero's slashes,
and each monster type's own, from 0.4 m for a bat to 1.2 m for the boss.

### The horde

The roster has three ranks:

- **Fodder:** six small monster types, from skeletons to demons.
- **Elites:** two big brutes, the Big Ork and the Troll.
- **The boss:** a dragon.

`--roster forest` and `--roster heroic` swap in **roster sets**, other
monster packs that keep the same nine type slots. The trailer names monsters
by slot, so it works with every set.

The song's simulation also has these rules. The trailer changes the first
four:

- **Kill budget.** Deaths per second are held to a target that the level
  script sets per section.
- **Kill-rate feedback.** Hit shapes grow while kills run behind the target.
- **Clear ring.** A radius around the hero that monsters stop at, so they
  never reach melee range. It is the level script's `clearRadiusM`.
- **Goal areas.** The hero heads for each section's goal area, then steps
  toward the densest arc of nearby monsters.
- **Refill.** On beats, new monsters spawn just outside the view's edge, to
  keep the on-screen count at the section's target.

### Overdrive, the starting point

`--intensity overdrive` (epic ps-5pd) is an **intensity overlay**: a JSON
file merged over the level script before the simulation runs. It turns the
song up for a trailer:

- the hero swings on every beat, with flurries in the peaks;
- each swing kills dozens;
- the horde holds about 1,300 on screen at the final peak.

It also loosens the **combo speed limits**, how far the hero's attack clips
may speed up or slow down to land on the grid. Its gain curve for aggregated
sounds keeps mass kills loud without clipping (see [Sound](#sound)).

It also carries a **blow-weight feel** for the client. Each of the hero's
blows gets a weight from 0 to 1, from how many it kills. That weight drives:

- the camera's punch (a brief zoom-in) and shake;
- a **hit-stop**, a freeze of the horde and hero of up to 0.06 s after a heavy
  blow;
- the size of the blow's trails and sparks.

Overdrive also draws the hero 1.7× larger (`heroScale`), so they stay readable
at wide views.

The trailer uses overdrive as the base of every shot (see
[The trailer file](#the-trailer-file)). Everything above stays in force
unless this document changes it.

## What the Viewer Sees

The trailer runs 32.6 s from first frame to silence:

- **0 s: the cold open.** It opens already in motion, on a hero facing three
  giant brutes in a torchlit crypt. One falls on each of the music's stabs.
- **2.48 s: the drop.** The drop lands with a white flash, and the picture
  cuts to a golden-hour desert. There, the hero's slam blows a crater in a
  charging wall of monsters.
- **The middle.** Shots alternate between snow, the night forest, the crypt
  and the desert. The hero runs fast across each one, carving through a horde
  that streams after them, spirals around them or pours in from every side.
  The cuts come faster and faster.
- **The climax.** In the night forest, monsters flood in from all sides, a
  dragon storms in, and a laser sweeps the field on every eighth note. The
  final slam, on the downbeat of bar 99 at 30.35 s, kills everything on
  screen.
- **30.35–31.13 s: the aftermath.** Two beats on the dying horde, the
  shockwave and the embers, all still moving.
- **31.13–32.63 s: black.** The picture cuts to black, and the music fades out
  over 1.5 s.

There is no title, text, logo, HUD or letterboxing.

## The Music Window

The trailer plays the song from song second 121.40 to 154.026. The picture
ends at 152.526, the cut to black, and the music fades out over the last
1.5 s. **Trailer time** is song time minus 121.40.

*Action* has a constant 155 BPM grid in 4/4. Beat 0 falls at 0.00982 s, and a
beat lasts 0.387097 s. Bar `n`, beat `b` (written `n.b`) falls at:

```
songTime(n, b) = 0.00982 + ((n - 1) * 4 + (b - 1)) * 0.387097
```

The window covers two kinds of section:

- **Break-2, bars 79–80.** A **free section**: the song map marks it as
  having no audible beat, because the kick and sub-bass drop out. The grid's
  beat times still exist there, since the tempo never changes, but they are
  silent. What the music does mark are three **anchors**, curated stab times.
  In a free section, monsters move smoothly instead of stepping on beats (see
  [Free sections](choreography.md#free-sections)).
- **Drop-2, from bar 81.** A pulsed section with a steady, audible beat.

The window's musical events:

| Trailer s | Song s | Position | The music |
|---|---|---|---|
| 0.000 | 121.400 | just before 79.3 | Break-2. The trailer starts here. |
| 0.180 | 121.580 | anchor 1 | A stab |
| 1.753 | 123.153 | anchor 2 | The song's strongest stab |
| 2.407 | 123.807 | anchor 3 | A pickup into the drop |
| 2.481 | 123.881 | 81.1 | Drop-2 lands: the heaviest sub-bass of the song |
| 4.803 | 126.203 | 82.3 | Beat 3, with an extra hit 31–33 ms before it |
| 7.900 | 129.300 | 84.3 | Beat 3, with an extra hit 31–33 ms before it |
| 10.997 | 132.397 | 86.3 | Beat 3, with an extra hit 31–33 ms before it |
| 13.707 | 135.107 | 88.2 | The sub-bass cuts out for beats 2–4 |
| 14.868 | 136.268 | 89.1 | Re-entry into the full arrangement |
| 26.481 | 147.881 | 96.3 | The sub-bass cuts out for beats 3–4 |
| 27.255 | 148.655 | 97.1 | Re-entry into the song's hottest phrase |
| 30.352 | 151.752 | 99.1 | The final slam |
| 31.126 | 152.526 | 99.3 | The cut to black; the fade begins |
| 32.626 | 154.026 | | Silence; the trailer ends |

Some notes on the table:

- **Accents.** The song map marks 1.753, 2.481, 14.868 and 27.255 as accents.
- **The extra hits.** The table gives the beat times; the hits themselves land
  31–33 ms earlier. They are not accents. The grid stays on the beat, so
  blows placed there land on the beat itself.
- **Drop-2's character.** Bars 81–88 carry the heaviest sub-bass but sparse
  mids. From bar 89 the full arrangement plays.

## The Shot List

The trailer has ten shots. A **shot** is a continuous stretch of picture
between two cuts, made by its own short simulation in its own environment.

Every cut lands on a beat, and every shot that follows a cut (shots 2–10)
opens on the hero's blow landing on its cut beat. Shot 1 starts the trailer
rather than following a cut. It opens 0.18 s before its first blow, on the
first anchor.

After the duel, the shot length halves, group by group:

- one 2.5 s duel;
- two 4-bar shots (6.2 s each);
- two 2-bar shots (3.1 s);
- four 1-bar shots (1.55 s), as a burst;
- one unbroken climax.

| # | Trailer s | Bars | Environment | Formation | On screen | View | Heading |
|---|---|---|---|---|---|---|---|
| 1 | 0.000–2.481 | 79–80 | crypt | Duel: 3 elites | 3 | 20 m | 0° |
| 2 | 2.481–8.674 | 81–84 | desert | Charge | ~600 | 45 m | 0° |
| 3 | 8.674–14.868 | 85–88 | snow | Vortex | ~700 | 50 m | 45° |
| 4 | 14.868–17.965 | 89–90 | forest | Converge, 4 streams | ~500 | 40 m | 0° |
| 5 | 17.965–21.062 | 91–92 | crypt | Charge with elites | ~540 | 40 m | 180° |
| 6 | 21.062–22.610 | 93 | desert | Vortex | ~400 | 35 m | −45° |
| 7 | 22.610–24.158 | 94 | snow | Charge | ~500 | 35 m | 90° |
| 8 | 24.158–25.707 | 95 | crypt | Converge, 3 streams | ~500 | 40 m | 180° |
| 9 | 25.707–27.255 | 96 | desert | Converge, 4 streams | ~600 | 45 m | −90° |
| 10 | 27.255–31.126 | 97–98, plus 99.1–99.2 | forest | Converge, 6 streams, plus the boss | ~1,300 | 50→65 m | 0° |

The columns:

- **On screen:** the shot's target count of live monsters in view.
- **View:** the **view width**, the width of ground visible across the middle
  of the screen. It sets the game camera's height.
- **Heading:** the camera's turn around the vertical axis. It makes shots in
  the same environment show different sides of it.
- **Formation:** the horde's opening layout (see
  [Kiting and formations](#kiting-and-formations)).

The shot list escalates on four tracks at once:

- **Moves.** Single blows in the duel, then slams, spins and rings, then
  lightning, and finally the laser and the final slam.
- **Density.** 3 monsters, then 400–700, then 1,300 at the climax.
- **View.** Tight for the duel and wide for the hordes. The burst opens tight
  at 35 m and widens shot by shot into the climax, the widest of all.
- **Pace.** The shot lengths halve, group by group, until the climax plays
  unbroken.

The table's counts, views and headings are starting points. Implementers tune
counts, widths, headings, paths and moves against frame sequences, but they
keep each shot's bars, environment, formation and role.

### Shot by shot

Each shot is built around particular moves, listed here in order.

1. **Duel (crypt, break-2).** Two Big Orks and a Troll close in from three
   sides, walking smoothly and winding up their swings. The hero holds ground,
   turning and stepping on the silent beats to face the next brute.
   - On anchor 1 (0.180 s), a slash fells the first brute.
   - On anchor 2 (1.753 s), a spin fells the second.
   - On anchor 3 (2.407 s), a slash fells the third.
   - The hero strikes only on anchors. On the silent beats between them, they
     take short repositioning steps. Shot 1's pre-roll is just the brutes
     walking in.
   - Big monsters falling to single blows is the hook before the hook.
2. **The drop (desert, charge).** A white flash covers the cut, and the
   hero's slam lands on 81.1. It blows a crater in a charging wall of fodder.
   - The hero runs straight through the wave: slashes on every beat, flurries
     on the eighths, and spins on 82.3 and 84.3.
   - The wave turns and streams after them.
3. **Vortex (snow).** The hero runs a wide loop, and the horde spirals in
   after them.
   - Spins on 86.3 and rings on the downbeats carve the spiral.
   - In bar 88's sub-bass cut, the hero strides on beats 2–4 as the spiral
     tightens. The cut comes on the release.
4. **Converge (forest, bar 89).** A white flash covers the cut. A ring lands
   on 89.1 as four streams meet the hero.
   - The hero cuts across the streams, slashing on every beat.
5. **Elite charge (crypt).** Big Orks and Trolls in the charging wave fall to
   single slashes, so their size sells each blow. Lightning strikes on
   downbeats.
6. **Burst 1: spins (desert, vortex).** A spin on every beat as the hero
   circles. The spiral is shredded from the inside.
7. **Burst 2: the slam (snow, charge).** Slashes into the oncoming wave on
   beats 1–3, then a slam on beat 4 that flattens its front rank.
8. **Burst 3: lightning (crypt, converge).** Lightning strikes on every beat,
   one per stream, while the hero slashes at the meeting point.
9. **Burst 4: the wind-up (desert, converge).** Slashes on beats 1 and 2.
   Then, in the sub-bass cut, the hero winds up a slam (`slam@97.1`) through
   beats 3 and 4 as the streams close in.
   - The picture cuts at 97.1, before the impact.
   - Shot 10 opens on its own slam landing on 97.1, so the wind-up and the
     impact read as one move across the cut.
10. **Climax (forest, bar 97).** A white flash covers the cut, and the hero's
    slam lands on 97.1 as monsters pour in from six directions.
    - The boss enters view during bar 97, storming through the horde from
      ahead of the hero.
    - A laser fires on 97.1 and again on 98.1. Each lasts a bar, sweeping
      the field and hitting everything in its line on every eighth note,
      while the view widens from 50 to 65 m.
    - Refill holds the flood at about 1,300 despite the laser.
    - The final slam on 99.1 kills every monster in view, the boss included.
      The aftermath's two beats show the horde breaking apart. Refill stops
      at the final slam, and the hero plays out the slam's recovery.

## Rules for Every Frame

These rules hold in every shot, from its first frame to its last. This
section is the authoritative statement of them: the shot simulation's
features exist to satisfy them, and the trailer's timeline checks enforce
them (see [Checks and Evidence](#checks-and-evidence)).

### Motion

Nothing on screen ever stands around. These are the **liveness** rules:

- **The hero acts on every beat.** In every beat interval, the hero strides at
  least half a step, or plays an attack or cast clip. Wind-ups count.
  - In break-2, the silent beats count too, and the hero also acts on every
    anchor.
  - Holding ground in the duel, a stride is a short repositioning step of at
    least 0.3 m.
  - The rules apply from the first frame. The partial interval before 79.3
    counts as one interval.
  - In the horde shots, eighth-note flurries fill the gaps between beats.
- **The hero steps in snaps.** Each stride covers its distance in a burst of
  about 0.12–0.16 s from the beat, then holds until the next beat. This is the
  game's rhythm signature, not idling.
  - The demo's hero glides across each beat today. Trailer mode sets a
    **step time**, the length of that burst: the roster hero's existing
    `stepS` field, 0.14 s by default. demo2d (the repo's 2D demo, which shares
    the choreographer) already uses it.
- **The hero kites.** In every horde shot, the hero moves along a path at
  about 2.5× the horde's speed (within 2–3×). The horde's speed is the mean
  gait of the shot's mix, weighted by the mix. Every blow is a moving strike
  that carries them forward. Only the duel holds ground.
  - The kite ratio and the step time are tuning targets. The trailer report
    prints the ratio per shot, but neither is a pass/fail check.
- **Every visible monster walks or attacks on every beat.** In each beat
  interval, every live monster within the view (plus a 2 m margin) does one
  of two things:
  - it moves at least a quarter of its gait step;
  - or it plays an attack, hit, death or spawn clip. Spawns happen in the
    margin, just outside the view.
  - A monster that would stand still is a bug in the shot, not a pose.
- **The horde never jams.** Monsters chase the hero's current position, so
  the hero's speed keeps them streaming and bending.
  - When neighbours block a monster's step, it steps sideways around them
    instead of standing.
- **Only monsters within reach attack.** There is no clear ring, so monsters
  close to striking distance. A monster within reach winds up so its blow
  would land on the next beat its clip fits.
  - The hero's blow on that beat usually kills it first. A blow that would
    still land is parried.

### Kills

Every monster dies to one blow:

- **One hit kills every rank.** Fodder, elites and the boss all have one hit
  point.
- **Every monster a blow touches dies.** The kill budget is off, so no death
  is held back.
- **Trampling kills too.** Small monsters the boss tramples die, as they do
  today.
- **Deaths pay off on the impact frame.** The death burst starts on the frame
  the blow lands, with no build-up. Under `--monster-style crystal`, dying
  monsters break into glass shards, and the shatter starts on that frame too.
- **The boss dies once, to the final slam** (see [The boss](#the-boss)).

### Picture

The picture is gameplay and nothing else:

- **No text or UI.** There are no titles, captions, logos, HUD, debug overlay
  or development-build watermark.
- **The game camera in every shot.** The game's own follow view, with
  overdrive's punch and shake. Shots vary only what a game could vary: view
  width, zoom easing and heading (see [Camera](#camera)).
- **Full 16:9.** The picture fills the 1920×1080 frame, with no letterbox.

## How Trailer Mode Works

Trailer mode reuses the song player's architecture: a simulation decides
everything at load, and the client draws it at the current time. Two things
change:

- **Ten simulations.** There are ten short simulations, one per shot, instead
  of one long one.
- **Four environments.** Four environments are loaded in one player, instead
  of one.

At load, the player:

1. Reads the trailer file and the flags.
2. Loads every environment the shots use, and warms each one.
3. Runs every shot's simulation, which gives one timeline per shot.
4. Starts the music at song second 121.40, and draws shot 1's first frame at
   that clock time.

Then, each frame, it:

1. Reads the song clock.
2. Finds the active shot.
3. On a cut, switches the environment and resets presentation state.
4. Draws the active shot's timeline at the song time, as the song player
   does today.

```
at load:    timelines = trailer.Shots.Select(shot => Choreographer.Run(ShotScript(shot)))
            environments.LoadAll(trailer.Environments); environments.WarmUp()
each frame: t = songClock.Now()
            shot = trailer.ActiveShotAt(t)
            if (shot != last) Cut(last, shot)   // environment, effects, camera
            Draw(timelines[shot], t)            // the song player's frame
```

The rest of this section covers each piece in turn: the trailer file, the
shot simulations, kiting and formations, the boss, environments, playback,
the camera, sound and the look.

### The trailer file

The **trailer file** is a tracked JSON file, like the level scripts, that
describes one trailer: its music window, its base tuning and its shots.
`--trailer` (or `--trailer action`) reads the one described here.

Shot boundaries are written as bar.beat positions, so they always resolve
exactly onto the song map's grid. This excerpt shows the first shot and the
climax:

```json
{ "name": "action", "song": "action", "base": "overdrive",
  "startS": 121.40, "blackAt": "99.3", "fadeOutS": 1.5, "seed": 7,
  "shots": [
    { "cut": "start", "environment": "crypt", "viewWidthM": 20,
      "hero": { "at": "camp", "holdGround": true },
      "duel": { "bigork": 2, "troll": 1 },
      "combo": ["slashR@anchor", "spin@anchor", "slashL@anchor"] },
    { "cut": "97.1", "environment": "forest", "viewWidthM": 50, "viewWidthToM": 65,
      "path": { "via": ["courtyard", "glowing-tree", "crystal-ring"], "stepM": 1.9 },
      "formation": { "kind": "converge", "streams": 6, "onScreen": 1300,
                     "mix": { "skeleton": 4, "goblin": 3, "bat": 2, "demon": 2,
                              "bigork": 0.4, "troll": 0.3 } },
      "boss": "ahead", "shapeScale": 2.5, "grade": { "contrast": 6, "saturation": 8 },
      "combo": ["slam@cut", "slashL", "slashR", "slashR@eighth", "slam@final"],
      "spells": ["laser@downbeat", "ring@downbeat"] } ] }
```

Top-level fields:

- **`base`**: the intensity overlay every shot's script starts from.
  `overdrive` gives the trailer ps-5pd's tuning instead of a copy of it:
  combo speed limits, the sound gain curve and the blow-weight feel. See
  [How a shot uses the base](#how-a-shot-uses-the-base).
- **`startS`**: the song second of the first frame.
- **`blackAt`**: the bar.beat of the cut to black.
- **`fadeOutS`**: the fade after the cut to black.
- **`seed`**: the trailer's seed. Shot `n` (numbered from 1) simulates with
  `seed + n`.

Per-shot fields:

- **`cut`**: the shot's first beat (`start` for shot 1). A shot ends at the
  next shot's cut, or at `blackAt` for the last one.
- **`environment`**: one of the player's environments.
- **`viewWidthM` and `viewWidthToM`**: the view width, and optionally a width
  to ease to across the shot.
- **`yawDeg`**: the camera's heading (default 0).
- **`hero`**: the hero's start (`at`), as a named area or metres,
  `holdGround` for the duel, and `stepS`, its step time (default 0.14 s).
  When `at` is omitted, the hero starts at the path's first waypoint.
- **`path`**: the kite path, as named areas or metre waypoints.
  - `stepM` is the hero's stride per beat.
  - `loop` closes the path into a circuit.
  - The hero follows the path from the start of the pre-roll.
- **`formation`**: the horde's opening layout, its kind, `streams`,
  `onScreen` target and `mix` (see
  [Kiting and formations](#kiting-and-formations)). The mix's weights are
  relative, as in the level script.
- **`duel`**: a fixed cast with no refill, for shot 1.
- **`combo` and `spells`**: the level script's grid syntax, with four new
  positions:
  - `cut`: the shot's first beat (not valid in shot 1);
  - `anchor`: the next unused anchor of a free section (each entry consumes
    one, in order);
  - `final`: the downbeat of `blackAt`'s bar, the final slam;
  - an explicit bar.beat such as `82.3`.
- **`shapeScale`**: the scale of the hero's hit shapes in this shot (default
  1). It replaces the kill-rate feedback.
- **`densityCap`**: the most monsters per square metre a refill may spawn into
  (default 1.2).
- **`boss`**: the direction the boss enters from: `ahead`, `behind`, `left`,
  `right`, or a named area. Directions are relative to the hero's direction
  of travel at the cut.
- **`grade`**: the shot's colour-grade offsets (see [Look](#look)).
- **`prerollBeats`**: how many beats the shot simulates before its cut
  (default 16).

#### How a shot uses the base

A shot's script starts from the roster's level script, merged with the
`base` overlay's global settings: its top-level fields, such as the combo
speed limits, the sound gain curve and the feel. Then the shot's own fields
apply:

- **Not inherited.** The base's per-section entries never apply to a shot.
- **Replaced outright.** The shot's `combo`, `spells`, `formation` and view
  replace the section's.
- **Inherited.** A field the shot doesn't set keeps the merged global value.

When two positioned entries fall on the same grid position, the higher one
in this order wins:

1. `final`
2. `cut`
3. an explicit bar.beat
4. `anchor`
5. `phrase`
6. `downbeat`
7. `accent`
8. `beat`

On the final slam's beat, nothing else fires: no other blow and no spell.

Each beat advances the kiting hero `stepM` once, whether they stride or
strike. Flurries on the eighths add no extra movement.

**Named areas** such as `camp`, `courtyard` and `glowing-tree` come from the
arena plan. The **arena plan** is the layout skeleton every environment shares:
the same 160 m footprint, open areas, goals and hero start, dressed with each
environment's own structures and props.

- Every environment keeps the named areas walkable.
- The simulation routes each path around the active environment's own
  structures.

That is why a shot's path works in any environment, and why `--environment`
can pin every shot to one environment for checks.

New environments, such as ps-bop's neon city and blossom temple, join the
trailer by being named in the file, with no code change. An unknown
environment, area or grid position fails at load and names the bad value.

### Shot simulations

A **shot simulation** is one run of the existing choreographer over a short
span of song time. It starts `prerollBeats` before the shot's cut and ends at
the shot's last frame. The **pre-roll** is the hidden lead-in before the cut:

- The formation is placed, and the hero starts along the path.
- Combos and spells run as usual. Refill replaces whatever the hero kills.
- By the cut, the horde is flowing and the hero is mid-stride.

Each shot simulation takes:

- the song map;
- the shot environment's layout, so monsters walk around that environment's
  own structures;
- the roster that `--roster` selects;
- a **shot script**: a level script built from the trailer file's `base`
  overlay and the shot's fields;
- the shot's seed.

It produces an ordinary timeline in song seconds. The client evaluates it as
it evaluates the song's timeline today. Within a shot, every existing client
feature works unchanged: the crowd, effects, lights, monster styles, crystal
shatter, feel and sound.

The shot script needs these choreographer features. Each is an opt-in
level-script field, and each is listed with what the choreographer does
today:

- **A span.** Today the simulation covers the whole song. A shot simulates
  only its own span.
- **A hero path.** Today the hero heads for goal areas and then holds ground
  near the densest monsters. A path replaces both:
  - the hero strides `stepM` along it on every beat;
  - each blow also advances them `stepM`, so they strike on the move;
  - a looped path is a circuit.
- **A hero step time.** Today demo3d's hero glides. demo2d's hero already
  steps, and trailer mode uses that field.
- **Formations.** Today monsters spawn at the view's edge. Formations set the
  opening placement and the refill streams (see
  [Kiting and formations](#kiting-and-formations)).
- **Never jam.** Today a blocked monster stands still. Instead:
  - it sidesteps around its blockers;
  - refill never spawns where local density exceeds `densityCap`.
- **No clear ring.** `clearRadiusM: 0`, an existing setting.
- **One-hit kills.** `hitPointScale` multiplies each rank's hit points, and
  every monster keeps at least one. A scale of 0 for every rank therefore
  leaves each monster with exactly one hit point.
- **The kill budget off.** Every monster inside a blow's hit shape dies.
- **A fixed hit-shape scale.** `shapeScale` sets the shot's hit-shape size,
  and the kill-rate feedback is off. Kills follow density and shape size.
- **The new grid positions:** `cut`, `anchor`, `final` and explicit
  bar.beat.
- **A camera heading.** Today the view's footprint is computed for a heading
  of 0. It rotates with `yawDeg`, so spawns stay outside the rotated view and
  on-screen counts stay right.
- **A screen-wide final slam.** The `final` slam's ring covers the whole view.
- **The boss rule** (see [The boss](#the-boss)).

**Normal runs must not change.** Without these fields, demo3d's and demo2d's
timelines stay byte-identical to today's. The existing tests that pin each
timeline's hash prove it.

**Outcome (ps-hgs.2).** The fields are the level script's
[shot fields](choreography.md#shot-fields), and `Trailer.ShotScript`
writes them from the trailer file. Some details of the first cut:

- **The hero strides every beat.** A kiting hero's stride is separate from
  its blows, so it keeps about 2.4 times the horde's speed even when a long
  clip (a spin, a slam's windup) skips a beat's blow.
- **The duel is staged.** Each brute starts as far out as it walks by its
  anchor, a third of a turn from the last, so it arrives just inside that
  blow's reach then: one falls on each anchor, and the spin on anchor 2
  can't reach the third. The hero steps to and fro across its spot.
- **The horde.** The formations replaced the first cut's placeholder horde
  (see [Kiting and formations](#kiting-and-formations), "Outcome").
- **Tuned to pass.** The climax holds about 1,300 at `shapeScale` 2.0 (at
  2.5 it kills faster than refill fills), and the paths cross the
  north-east field's middle, where the views find the most open ground.

### Kiting and formations

**Kiting** is the hero outrunning a horde that chases them.

- Monsters always step toward the hero's current position, along a flow field
  rebuilt as the hero moves.
- The hero is faster, so the horde trails behind and bends around the hero's
  turns.
- Every monster re-aims on every beat. That constant re-aiming keeps the
  whole horde moving and stops clumps from forming.

This is the look of the genre's best gameplay footage.

A **formation** is the horde's layout at the start of the pre-roll, plus
where refills come from during the shot. There are three:

- **`charge`**: a dense wave on the hero's path, advancing toward them.
  - The simulation places the wave so that its front meets the hero exactly
    at the cut. It computes where the hero will be at the cut along the path,
    then sets the wave back by how far it will walk in the pre-roll.
  - The hero runs into it head-on and blows a channel through it.
  - The wave turns and chases.
  - Refill comes from ahead.
- **`vortex`**: the horde spread around a looped path. As the hero circles,
  the chasers spiral inward after them. Refill comes from outside the loop.
- **`converge`**: two to six streams (`streams`) pouring in from different
  directions and meeting at the hero, who cuts across them. Refill keeps each
  stream fed. The streams are evenly spaced around the hero (squeezed into
  the arc a kiting hero can be reached from, see "Outcome"), the first one
  ahead, relative to the hero's direction of travel at the cut.

Refill keeps each shot near its `onScreen` target. It spawns outside the
view, so no monster ever appears in view. The view used is the simulation's
recorded framing, the lagging camera itself, rotated by the shot's heading.

**Outcome (ps-hgs.5).** The level script's `formation` and `densityCap`
([shot fields](choreography.md#shot-fields)) carry a shot's formation into
its simulation. Each formation places its opening at the pre-roll's start,
from where the hero's path puts it, and refills from then on:

- **Charge.** A wave across the path, as wide as the view's far edge and as
  deep as the view is long plus 4 m, at 1.55 times the shot's density (its
  target over the view's area). Its front meets the hero 4 beats before the
  cut rather than on it, so the cut's blow lands inside the wall and blows
  a crater out of it; met on the cut, half the screen behind the hero was
  empty. Refill waits for the cut, then comes from within 75° of the
  hero's travel.
- **Vortex.** The target's worth of monsters spread over a disk round the
  loop's centre, 8 m wider than the loop. Refill comes from the whole edge,
  and monsters behind the hero stay, since the hero circles back.
- **Converge.** The streams fan over the 200° a horde can reach a kiting
  hero from, 100° either side of its travel at the cut, spaced as a circle
  squeezed into that arc, the first one ahead. A stream from farther behind
  could never catch a hero running 2.5 times its speed. Each stream opens as
  a column just outside the view, holding half the target between them.
  Refill feeds the streams from the pre-roll's start, from sources a
  quarter of the view wide. Each source is led toward the hero's travel,
  since the hero sweeps past it.

Refill aims to have the target in view two beats on. It predicts that from
every monster walking its gait toward where the hero will be, and from the
view following the hero's path, at its width then. It spawns the shortfall
plus the kills expected meanwhile, and a slowly built bias corrects the
prediction near the target. It holds the off-screen stock that will walk
in within 6 beats to half the target, plus the kills meanwhile, since a
deeper stock arrives after the prediction and overshoots. Refill spawns
only where the shot's `densityCap` is not exceeded within 2 m, and at
least 1 m from structures.

Never jam, in a formation's shot:

- Every monster chases on every beat, off screen too.
- Its heading bends away from neighbours within 1.6 m of touching, so a
  horde chasing one point fans out instead of packing into a block.
- A monster whose step is blocked waits for its neighbours to plan, up to
  twice. Then it follows a touching neighbour's step, or sidesteps at ever
  wider turns and shorter lengths.
- Boxed in on every side, it shoves: a third of a step, pressing into its
  neighbours by up to 40% of the smaller radius.
- A formation's crowd packs to 20% overlap instead of 10%.
- A windup the hero's blow cuts short is kept in the timeline. It plays
  until the death takes over, as "the hero's blow usually kills it first"
  describes. Windups still under way when the shot ends are kept too.

The liveness check holds: zero violations in all ten shots, for the
default roster and the forest and heroic sets. The density check passes
too, and both hold across other trailer seeds: seeds 3, 5, 9, 10, 11, 13,
17, 21, 27, 28, 29 and 42 pass every check.
Pinned to one environment, shot 2 or 5 can still miss a bar's density by
up to 20%. Tuning changes from the first cut:

- shots 6 and 8 run at 1.6 and 1.3, since spins and lightning on every beat
  killed faster than their streams could fill;
- shot 5's elite charge aims at 540, since its big bodies jam a 600-strong
  wave.

The kite ratio is 2.3-2.5 in every horde shot, measured up to the final
slam. Frame sequences are in
[demo3d/evidence/trailer/kiting](../../demo3d/evidence/trailer/kiting/README.md).

### The boss

The boss, a dragon in every roster set, appears only in the climax. Its job
is to make the final slam bigger. These rules are part of the shot simulation:

- **It enters from the shot's `boss` direction.** It appears outside the view
  during bar 97 and storms in through the horde, trampling small monsters in
  its way. The level script's existing `bossFrom` setting, which walks the
  boss in from a named direction, already moves it this way.
- **No blow reaches it early.** It never comes closer to the hero than the
  shot's largest hit shape, plus 1 m. With the climax's `shapeScale` of 2.5,
  the slam's 3.6 m ring reaches 9 m, so the boss keeps at least 10 m away.
  No blow passes through it.
- **Spells skip it.** The laser, rings and lightning ignore it until the final
  slam.
- **The final slam kills it.** Nothing else fires on that beat, so the slam is
  the first and only hit it takes. It dies on 99.1 with everything else in
  view.

### Environments in one player

The player loads every environment the trailer uses before the first frame:
crypt, desert, snow and forest. All four stay resident, and exactly one is
active at a time. This is new: today a player loads one environment for its
whole run, and loading it changes global rendering state.

Each environment loads as its finished version. Its rendering is on the E2
rung, the top of the environment's measured optimization ladder, with baked
static lighting and a static shadow map. Its decoration is at density 2, the
authored prop density. That is the one baked variant all four environments
have.

Loading an environment captures everything it would set globally, so the
player can switch to it at a cut. That **environment state** includes:

- its scene roots, active or inactive;
- its moon, ambient light, fog and render settings;
- its lightmaps and light probes;
- its static shadow map (a 4096² map, rendered once at load);
- its post-processing volume and the crowd lighting's shader globals;
- its flame lights and hero light;
- its palette and the roster set's own lighting adjustments, applied as in a
  normal run.

Requirements:

- **Switching costs no visible frame.** A cut applies the new environment's
  state within one frame. No frame at a cut takes more than 2 ms longer than
  its neighbours, measured with the existing windowless GPU probe
  (`--gpu-probe-at`) at and around each cut.
- **Each environment looks as it does alone.** With all four resident, each
  environment's stills match a normal single-environment run of it within
  1 L*.
  - The look harness (`demo3d look`) renders a fixed set of stills and
    compares their CIELAB lightness (L*). 1 L* is its usual pass line.
  - The comparison runs with the trailer's own look additions off
    (`--no-trailer-look`: no per-shot grade, grain, lens dirt or lens hits),
    since those differ from a normal run on purpose.
- **Nothing compiles mid-trailer.** At load, every environment is drawn once
  off-screen with representative effects. That compiles its shaders and
  pipeline states before the first frame.
- **A failed load stops the player.** If any environment fails to load or
  warm, the player exits before the first frame with an error naming it. It
  never plays a partial trailer.

The scenes are arranged by this decision and its test:

- **First choice: overlap at the origin.** Every baked scene sits at the world
  origin, and inactive environments have their roots disabled.
- **The test.** Each environment must pass the look requirement above while
  all four are resident.
- **Fallback.** If overlapping light probes or lightmaps fail the test, the
  environments are placed far apart in world space. Shot simulations keep
  local coordinates, and the client adds each environment's offset.
- **Outcome (ps-hgs.1): they overlap.** Every environment's stills, taken on
  the very frame that switches back to it, match its single-environment run
  within 0.02 L*, in all 28 look-set stills of each
  ([evidence](../../demo3d/evidence/trailer/residency/README.md)). No offset
  exists, and shot simulations use world coordinates as they are.

How it works (`ArenaSet`, `ArenaRuntime.Resident`, `Show` and `Apply`):

- **Lightmaps** need nothing: every loaded scene's lightmaps join one list,
  and each renderer indexes its own.
- **Light probes** are swapped. Unity keeps one scene's probes after
  additive loads (each scene's 882, never all four), so a switch sets
  `LightmapSettings.lightProbes` to the new scene's own
  (`LightProbes.GetSharedLightProbesForScene`).
- **Visibility** is a layer per scene (8 upwards) and the camera's culling
  mask, so a switch touches no renderer. Only the scenes' realtime lights
  (the moon, the flames, the hero's light) and post volumes are switched off
  and on. Deactivating the roots instead cost over 1 ms a switch.
- **Globals** are put back on a switch: the active scene, which holds each
  scene's render settings, then the sun, ambient, fog and its fitting hook,
  the crowd's lighting globals, the camera's clear colour, the shadowmask
  mode and the static shadow map.
- **The post feel** runs one instance per environment, each bound to its own
  volume, and a monster style's grade goes on every volume before any binds.
- **The warm-up** draws each environment off-screen once, on its own frame,
  at the first frame's time, with the horde, hero and effects live. The music
  starts after it.
- **The development flags** `--resident <env,...>` (beside `--environment`'s,
  whose layout the timeline plays) and `--switch-every <s>` (cycle every s
  song seconds from `--start-at`) exercise it until trailer playback exists.

The memory cost is accepted: roughly four scenes, four lightmap sets and four
64 MiB shadow maps. The trailer runs on a powerful machine.

### Playback and cuts

The trailer's clock is the song clock, the existing audio-driven clock.
Music plays from song second 121.40, and each frame draws the song time the
clock reports.

On every frame, the active shot is the last shot whose cut is at or before
the current time. When the active shot changes, the player makes the cut. A
**cut** is a one-frame switch from one shot to the next:

1. **Switch the environment.** Apply the new shot's environment state, if its
   environment differs.
2. **Clear presentation state.** Drop the previous shot's particles, effect
   lights, crystal shards and hit-stop.
3. **Fade ringing sounds.** Sounds still playing from the previous shot fade
   out over 60 ms.
4. **Seek into the new shot.** The **effect driver**, the client part that
   spawns effects from timeline events, seeks into the new shot's timeline at
   the cut, as it does for `--start-at` today. Effects launched during the
   pre-roll that are still alive appear, already in flight.
5. **Jump the camera.** Read the new shot's framing at the cut. The shot
   simulation records its framing track, the follow and zoom, through the
   pre-roll like any other motion, so the camera is already settled. The
   client replays nothing.

**Flash cuts.** Three cuts land on the trailer's big musical steps: 81.1 (the
drop), 89.1 and 97.1. These cut under a white flash.

- The flash is the **post feel**'s hit flash. The post feel is the client part
  that pulses exposure, bloom and vignette on beats, and flashes on accents
  and big hits.
- On these cuts, the flash peaks near-white on the cut frame and decays over
  an eighth note.
- The cut hides under the flash, and the new shot's opening blow bursts out of
  it.

The other cuts are plain hard cuts.

The trailer's start and end:

- **The first frame.** The screen stays black while the player loads,
  simulates and warms. Then the music and shot 1 start together, 0.18 s
  before the first kill.
- **The end.** On 99.3 the picture cuts to black, and the music and sounds
  fade to silence over `fadeOutS`.
- **Exit.** The live player then exits. A capture ends with the fade.

The timeline checks run offline. The live player plays whatever the file
produces, even if a check would fail. `--overlay` shows any liveness
violations live.

**Outcome (ps-hgs.6).** `--trailer` is DemoMain's trailer mode
(`DemoMain.Trailer.cs`). Some details of what shipped:

- **Load.** The ten shot simulations take about 0.6 s in the IL2CPP player.
  A global black volume (a colour filter of black, priority 1000) covers the
  screen until the warm-up ends and the clock starts, and again from the cut
  to black.
- **The cut.** One frame switches the environment, then swaps in the shot's
  own timeline, evaluator, effect driver, blow and camera feel and crystal
  breaks. The effect driver's seek clears particles and effect lights. The
  crystal shatter is retargeted, which clears its shards. The horde never
  poses before the shot's first frame, which clears the hit-stop. Crowd slots
  are keyed per shot, so no slot carries a monster's state across the cut.
  A cut costs 0.1–0.3 ms, plus up to about 1.2 ms of CPU on its frame while
  the new shot's live effects re-spawn
  ([evidence](../../demo3d/evidence/trailer/playback/README.md)).
- **Which cuts flash.** A cut flashes when it lands on one of the song map's
  accents, within 10 ms. In *Action* those are exactly 81.1, 89.1 and 97.1,
  so the trailer file needs no field for it. The flash is +5 stops, −70
  saturation and +4 bloom on the cut frame, decaying quadratically over an
  eighth note, with one post feel per environment holding every shot's big
  hits and the flash cuts. With `--shot`, only that shot's own cut and hits
  flash.
- **Sound.** One scheduler plays every shot's cues within its visible span.
  Its gain envelope fades a cue from before a cut over 60 ms from the cut, and
  every cue and the music from the cut to black. With `--shot n
  --from-preroll` the pre-roll's own sounds play too, since its whole span is
  what that run shows. `--dump-sounds` writes the merged cue list; the cut and
  end fades in it are the capture's (ps-hgs.4).
- **Time flags.** `--start-at`, `--stills-at`, `--seconds`, `--freeze-at` and
  `--gpu-probe-at` take trailer seconds. With `--shot`, the run starts at the
  shot's cut (or its pre-roll) unless `--start-at` says otherwise, and ends at
  the shot's end.
- **Rejected too.** `--structure-breaks` (the shots break nothing),
  `--resident` and `--switch-every` (the trailer makes its own environments
  resident) fail beside the doc's list.

### Camera

Every shot uses the game camera:

- a 55° pitch and a 30° vertical field of view;
- a critically damped follow of the hero, with a 0.4 s time constant;
- zoom easing between view widths;
- overdrive's punch and shake on top.

What varies per shot:

- **View width.** Set per shot. `viewWidthToM` adds an eased zoom within a
  shot.
- **Heading.** `yawDeg` turns the camera around its vertical axis, as a game
  could fix each level's camera heading. Today the camera's heading is always
  0, so the camera rig gains a heading, and the shot simulation's view
  footprint turns with it.
- **The hero's drawn size.** Overdrive's 1.7× keeps the hero readable at
  45–65 m views.

No shot adds a virtual zoom, an orbit, a punch-in or any camera move a player
wouldn't see.

### Sound

The audio is the song plus the game's own combat sounds:

- **Music.** The song plays from song second 121.40.
- **Combat sounds.** Only the active shot's sound events play, and only within
  its visible span. Pre-roll events never play.
- **Aggregation.** Sounds go through the **sound aggregator**, which merges a
  beat's many requests for one sound into a single cue whose gain grows with
  the count. Overdrive's gain curve applies, so a 60-kill slam is one huge
  boom rather than 60 samples.
- **Cuts.** Sounds ringing at a cut fade out over 60 ms, so no tail carries
  into the next shot.
- **The end.** From the cut to black, the music and every sound fade together
  over 1.5 s.

The live player and the capture's offline mix use the same cue list (see
[Capture](#capture)), so the two sound identical.

### Look

Trailer mode starts from the look a normal run produces for the same flags:

- each environment's own lighting and post-processing;
- the palette;
- the roster set's own lighting adjustments;
- the monster style.

It then adds to that look. Visual quality wins over frame rate throughout.

**Per-shot grade.** A shot's `grade` adds offsets to its environment's base,
in URP's own units:

- **exposure:** in stops;
- **contrast and saturation:** URP's −100 to 100 scale;
- **temperature:** the white balance's −100 to 100 scale.

Neighbouring shots then read as distinct, while the trailer still reads as
one piece. The post feel's beat pulse and flashes run on top.

```json
"grade": { "exposure": 0.1, "contrast": 6, "saturation": 8, "temperature": -5 }
```

**Supersampling.** The picture renders at a multiple of its output
resolution and is downsampled to 1080p, for clean edges on blades, monsters
and particles.

- The capture always renders at 2× (3840×2160).
- Live mode takes `--supersample <1–2>`. Its default is the highest factor at
  which the climax holds 60 fps on the Mac, measured and recorded in
  [performance.md](performance.md).
- If even 1× can't hold 60 fps at the climax, the default is 1×. The live
  build then drops frames there, which is accepted.

**Impact lens hits.** Slams, rings and mass kills hit the lens:

- **What.** A radial blur and a chromatic-aberration spike, both centred on
  the impact's screen position.
- **Strength.** Each scales with the blow's overdrive weight, so it grows
  with kills.
- **Timing.** It peaks on the impact frame and decays smoothly to nothing
  within an eighth note (0.19 s).
- **Cap.** At full weight, a blur streak is at most 3% of the screen's width
  long.
- **Overlap.** Overlapping hits add, up to the same cap.
- **No flicker.** A lens hit never toggles on and off between frames, and an
  impact point re-triggers at most once per eighth note.

**Grain and lens dirt.**

- Subtle film grain runs over every frame.
- A lens-dirt texture lights up where bloom flares, so effect-heavy moments
  glow as if seen through a real lens.
- The dirt texture is original: generated in code or made for the project,
  never a vendor asset.

### The style post fix

This change also affects normal runs. A **monster style**
(`--monster-style`) is a stylized shader treatment for the horde. Every style
except `baseline` also brings a post-processing look.

- **The bug.** Today a style's look overwrites the scene's exposure,
  contrast, saturation, vignette, bloom and tonemapping with fixed values,
  which were tuned in the forest. That flattens other environments' own
  values. Snow and desert, for example, lose their lower exposure.
- **The fix.** A style's post look becomes offsets from the forest's own
  values, and those offsets apply to the active environment's values.
- **What changes.** The forest under any style looks exactly as it does
  today. Every other environment keeps its own base under a style.
- **Scope.** The fix changes pixels, not timelines. A non-forest environment
  under a non-baseline style is the only normal-run case that looks
  different, and every timeline stays byte-identical.

**Outcome (ps-hgs.3).** The look shipped as designed. Some details
([evidence](../../demo3d/evidence/trailer/look/README.md)):

- **The per-shot grade** is the post feel's (`PostFeel.ShotGrade`). Each
  shot's grade applies from its first frame, switching with the cut, and the
  beat pulse, section grades and flashes ride on top. It is the trailer's
  look, not the feel, so `--no-feel` and `--no-post-feel` keep it.
- **The style post fix** is `MonsterStyle.PostOver(night, forest)`. The forest's
  values come from its own lighting under the run's palette, roster set and
  post overrides, so the forest under any style gets exactly the tuned values.
  `--style-tune`'s post keys still set the forest's values. In a normal run at
  drop-1, snow under crystal has a p50 L* of 64.3 (its baseline: 67.2; the old
  overwrite: 71.7), and the desert 67.3 (70.1; old: 76.1).
- **Supersampling** is URP's render scale (`Supersampling`), downsampled
  with linear filtering, a 2×2 box at 2×. From about 1.41× bloom starts at a
  quarter of the render resolution instead of half, so its glow keeps its
  size on screen. A trailer's captures, video and stills, default to 2×; its
  live run defaults to 1× ([performance.md](performance.md#the-trailers-look));
  a normal run renders at 1×. `--supersample` overrides any of them.
- **Lens hits** are `TrailerLook` and the renderer's `LensHitFeature`, a pass
  after the post stack (`Hidden/Demo3D/LensHit`). The pass draws only on frames
  with a hit, so a run without one, every normal run, pays nothing.
  - **What hits.** Each `slam-shock` and `ring` effect (the hero's slams, ring
    spells and the final slam), and every hero blow of weight 0.5 or more
    (about 22 kills under overdrive's feel).
  - **Strength.** A slam or ring starts at 0.3 at weight 0 and rises to 1 at
    weight 1. A mass kill gets the same mapping.
  - **Merging.** Hits within an eighth note of an earlier one merge into it,
    at the stronger one's place and strength.
  - **Envelope.** Full strength holds for 1/60 s, so the frame drawn on the
    impact always peaks. It then falls as e^(−10x)·(1 − x) to nothing at the
    eighth: about a fifth is left two frames on.
  - **Blur and aberration.** At strength 1, the streak reaches 3% of the
    width and the red–blue split 0.2%. Both grow smoothly with distance from
    the impact, to full length 0.6 of the width away. A cut clears a shot's
    hits.
  - **Tuning.** A first tuning (a 0.6% split, decaying as (1 − u)²) smeared
    the climax's packed horde for most of the eighth.
- **Grain** is URP's film grain (Thin2, response 0.8), at 0.18 times the
  supersampling factor, since the downsample averages pixel-sized grain away.
- **Lens dirt** is `LensDirt`, a 1024×576 texture generated from a fixed
  seed: soft smudges gathering toward the edges, water spots, dust specks and
  finger wipes. It goes on URP's bloom at intensity 12.
- **`--no-trailer-look`** drops the grade, grain, dirt and lens hits, and
  keeps the run's supersampling.

## Command Line

Trailer mode is one flag on the normal player:

```
just demo3d --trailer --sound
just demo3d --trailer --sound --palette neon --monster-style crystal
just demo3d --trailer --sound --roster heroic
```

`--trailer` takes an optional name, which defaults to `action`. A following
argument that starts with `--` is the next flag, not a name.

These work as in a normal run and apply to every shot:

- **`--palette <name>`:** recolours each environment's moon, ambient light,
  fog and ground.
- **`--roster <name>`:** picks the roster set.
- **`--monster-style <name>`:** picks the horde's style. As today, a roster
  set can't take a style other than `baseline`.
- **`--classic-monster-colors` and `--style-tune`.**
- **`--crowd`, `--aa` and `--sound`:** the crowd renderer, anti-aliasing,
  and sound (a built player starts muted).
- **The post overrides:** `--post-exposure`, `--bloom-threshold` and the
  others.
- **`--no-feel` and `--no-post-feel`.**

These change meaning in trailer mode:

- **`--environment <name>`:** pins every shot to one environment, for checks.
- **`--start-at`, `--stills-at` and `--seconds`:** take trailer seconds (0 to
  32.6), not song seconds. Still times round to the next 60 fps frame.
- **`--shot <n>`:** plays only shot `n`, from its cut to its end, with the
  music from the cut.
- **`--from-preroll`:** with `--shot`, plays the shot from the start of its
  pre-roll, music included, to inspect how the formation sets up.
- **`--overlay`:** in live mode only, adds the shot number and any liveness
  violations to the existing debug text. A capture ignores it, with a warning.
- **`--fixed-time`:** as today, deterministic 1/60 s frames with sound muted.
  Capture and stills imply it.
- **`--no-trailer-look`:** turns off the per-shot grade, grain, lens dirt and
  lens hits, for comparing environments against normal runs.
- **`--capture-video`, `--capture-stills` and `--dump-sounds`:** capture the
  trailer (see [Capture](#capture)).

These are rejected with an error that names the conflict:

- **`--intensity`:** the trailer carries its own tuning, built on overdrive.
- **`--scene`, `--envelope`, `--view-width`, `--hold-at` and
  `--beauty-empty`:** these control the song's own timeline.

Every roster set plays the same trailer: the file sets counts and views
outright, as overdrive does.

## Capture

The capture renders the trailer at fixed time: 60 frames per second of song
time, whatever each frame costs. The video is therefore frame-perfect,
however heavy the climax gets.

One tool command produces the finished, mixed video:

```
uv run --project demo3d/tools demo3d trailer --out captures/trailer.mp4 \
  -- --palette neon --monster-style crystal
```

It runs three steps:

1. **Render the video.** It launches the windowed player with `--trailer`,
   the given player flags, `--capture-video` and `--dump-sounds`. The player
   renders each frame at 2× and downsamples it. The result is a silent
   1920×1080 60 fps H.264 file.
2. **Write the cue list.** Trailer mode's `--dump-sounds` writes exactly the
   cues the live run would play, in song seconds. That includes the cut fades
   and the end fade.
3. **Mix and mux.** It mixes the song window under those cues with the
   existing mixer (`demo3d mix`), applies the 1.5 s fade, and muxes the audio
   into the video.

The capture needs a windowed player. Agents never open player windows, so
recording it is a task for the **operator**, the developer who runs the demo
and reviews its evidence. Agents check the trailer with windowless stills and
frame sequences instead.

## Checks and Evidence

The trailer's rules are checked on its timelines, without rendering. The
choreography's command-line tool gains a trailer report:

```
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- \
  trailer --checks
```

`--roster <set>` and `--environment <name>` take the player's meanings,
`--shot <n>` runs one shot, `--name <trailer>` reads another trailer file,
and `--out-dir <dir>` writes each shot's timeline dump (its span only). It
exits 1 when a check fails.

It runs every shot's simulation and prints a table with one row per shot:

- the on-screen count against target;
- the hero's swings per second;
- kills per swing, mean and peak (every death on the blow's instant, so a
  spell landing with it counts too);
- the share of beats the hero acted on;
- the kite ratio, the hero's speed over the horde's;
- liveness violations: monster beat intervals that break the monster rule.

These checks fail the run, naming the shot, the time and the monster:

- **Cuts on the grid.** Every cut lands exactly on a beat.
- **Opening blows.** Every shot after the first has a hero impact on its cut
  beat. Shot 1 has one on anchor 1.
- **Impacts on the grid.** Every impact lands on a grid position, or on an
  anchor in break-2.
- **Liveness.** The monster and hero rules in [Motion](#motion) hold in every
  beat interval of every visible span. Break-2's intervals are its silent
  beats.
- **One hit kills.** No monster takes a hit and survives.
- **The boss.** It takes exactly one hit, from the final slam on 99.1.
- **The final slam.** It kills every monster in view.
- **No visible spawns.** Every spawn lies outside the rotated view.
- **Density.** In each shot from its second beat, the on-screen count is
  sampled on every beat. Each bar's mean of those samples stays within 15% of
  the target. Two stretches are exempt:
  - shot 1, the fixed-cast duel;
  - the two beats after the final slam.
- **Normal runs unchanged.** demo3d's and demo2d's normal timelines keep
  their pinned hashes.

The environment requirements are checked in the player:

- **Look.** The look harness compares each resident environment against its
  single-environment run, with `--no-trailer-look`, within 1 L*.
- **Timing.** A windowless GPU-probe run around every cut shows no frame more
  than 2 ms slower than its neighbours.

Visual evidence comes from windowless, fixed-time player runs:

- a still at every shot's first frame and mid-shot;
- frame sequences across the three flash cuts and the final slam;
- a spot-check grid of palette, style and roster combinations.

Small JPEGs go into a trailer evidence folder with a README, as overdrive's
evidence does. Each carries the `ps-render` tag in a JPEG comment, which the
repo's vendor guard requires of every evidence image.

Two checks need a window or speakers, so the operator does them:

- watching the live trailer with sound;
- recording the capture.

They are rows in the operator's pending table in
[the implementation plan](plan.md#pending-on-the-operator).

## Manual QA

Start at the first cut. That boundary exercises everything new at once: two
resident environments, two shot simulations, the cut and its flash, and the
opening blow.

**Early check: the first cut.** Render windowless stills around the drop,
which lands at 2.481 s:

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal -logFile demo3d/Logs/trailer-cut.log \
  --trailer --fixed-time --capture-stills captures/trailer-cut \
  --stills-at 2.45,2.50,2.60,3.00 --seconds 3.5
```

Expect:

- **2.45.** The torchlit crypt, with the third brute falling and the hero
  mid-strike.
- **2.50.** A near-white flash over the desert.
- **2.60.** The desert in its own golden-hour light, with the crater and
  bodies breaking.
- **3.00.** The hero running into the charging wall, with every monster in
  motion.
- **Lighting.** Render a still at 4.0 with `--no-trailer-look --no-feel
  --no-post-feel`, after the drop's flash and lens hit have cleared. Compare
  it with a normal `--environment desert` still taken with the same flags:
  the ground, props and sky lighting should match.

**The whole trailer, live.** This is an operator check, with a window and
speakers:

```
just demo3d --trailer --sound
```

Expect:

- **The opening.** Gameplay on the first frame, and the drop at 2.5 s.
- **The cuts.** Ten shots, every cut on the beat and every shot opening on a
  blow.
- **Motion.** No monster frozen anywhere, the hero acting on every beat, and
  the horde chasing and streaming.
- **The end.** The final slam clears the screen, then two beats of aftermath,
  black and a 1.5 s fade.
- **Picture.** No text and no UI at any point.
- **Smoothness.** No hitch at any cut.

**Flags.** Run the live trailer, or windowless stills at 4.0 (desert), 11.0
(snow), 16.0 (forest) and 29.5 (the climax, before the final slam), with
each of these:

- every `--palette`;
- `--monster-style crystal`, `mix` and `painted`;
- `--roster forest` and `--roster heroic`;
- `--classic-monster-colors`.

Each should play the same shots in its own look. The snow and desert shots
should keep their own exposure under a non-baseline style.

**Edge cases.** Each of these should behave as described:

- **`--environment snow`.** Every shot plays in snow, with paths intact.
- **`--intensity overdrive`.** Fails at start and names the conflict.
- **`--roster heroic --monster-style crystal`.** Fails at start, as in a
  normal run.
- **`--start-at 27.3`.** Opens just after the climax's cut, with the horde
  already pouring in.
- **`--shot 3 --from-preroll`.** Shows the vortex forming before its cut.
- **`--overlay`.** Live mode shows the shot number and zero liveness
  violations. A capture with it warns and leaves it out.
- **A trailer file naming an unknown environment.** Fails at load and names
  the bad value.

**Capture.** This is an operator task:

```
uv run --project demo3d/tools demo3d trailer --out captures/trailer.mp4
```

Expect a 32.6 s, 1920×1080, 60 fps video with mixed audio. It should match
the live run: the same shots, cuts and sounds, with no dropped frames.

**Debug surfaces.**

- **`trailer --checks`** in the choreography's command-line tool. It prints
  the per-shot table and every failed rule, with the shot, time and monster.
- **`--shot` and `--from-preroll`.** They isolate one shot, with or without
  its pre-roll.
- **`--overlay` in live mode.** It shows the shot number and liveness
  violations as they happen.
