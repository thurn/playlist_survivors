# 3D Tech Demo: Trailer Mode

The 3D tech demo is a non-interactive Unity build in which a hero carves
through a horde of animated monsters to *Action* by Alessia Labate & Conrad
Taylor, as if a perfect player were in control (see [the demo's
overview](README.md)). Today it plays the whole 3:19 song as one continuous
battle.

This document specifies **trailer mode**, a `--trailer` flag on the same
player. It turns the demo into a gameplay sizzle reel of about a minute:
59.0 s of gameplay, then a 1.5 s fade on black.

- **Eight shots,** cut on the beat across seven environments, set to bars
  79–117 of *Action*.
- **No text and no UI.**
- **Live and captured.** It plays live in the normal build with sound. Through
  the existing video capture (`--capture-video`), it also renders a
  frame-perfect 1080p60 video with mixed audio.

Every frame is real gameplay from the demo's own simulation, but each shot is
staged for it:

- **Its own short simulation.** Each shot has its own environment, horde
  formation, density and set of hero moves.
- **Everything always moves.** The hero runs on every beat and lands a
  weighty blow on the song's major beats, and the horde chases them in
  coloured blocks of one monster type.
- **Plausible survivors play.** The hero is never surrounded: they keep an
  open lane behind them and kite, as a player who is surrounded would be
  dead.
- **Every monster dies to one blow,** except the dragon. The climax is a
  boss fight: the dragon telegraphs attacks the hero dodges, takes the hero's
  blows, and dies to the final slam.

The trailer's job is to make a viewer want to play. That sets its goals:

- **Hook within three seconds.** The song's heaviest drop lands 2.48 s in,
  and the first cut blows a crater in a charging horde of hundreds on it.
- **Room to breathe.** After the hook, three long shots let each place and
  horde play out, before the cuts quicken into the climax.
- **Feeling over explanation.** Impact, speed, power, chaos and satisfaction,
  with nothing that explains mechanics.
- **Everything on the music.** Blows, kills, spells, cuts, flashes and the
  horde's steps all land on the song's beat grid. Blows land on its major
  beats, each with its full windup and follow-through.
- **Rising intensity.** Density, view width, cut pace and the hero's moves all
  escalate to a climax: a fight with the dragon that ends in a
  screen-clearing slam.
- **No dead air.** Every shot opens on a blow. Between blows the hero runs,
  and the horde steps on every beat.

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
- [Environment](environment.md): the seven environments, how each is baked and
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
- Epic ps-bop (closed): the lava, neon and blossom environments, and the
  crypt. They joined the trailer as data (ps-7mx.2).

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
  around the hero) and `slam` (a larger ring and a shockwave). The normal
  run's cleaves are combos too: `cleaveL` and `cleaveR` play the slashes'
  clips with a 360° ring for a hit shape, and `cleave`, the heavy cleave,
  plays a third slash with a wider ring. A cleave has a short windup of its
  own (0.32 s), so it lands two beats after any other blow.
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

**When the hero strikes (ps-7mx.1).** The hero's blows land on the song's
**major beats**: the strong beats (each bar's first and third, the `strong`
grid position), the accents, the cut and the final slam. Each clip plays
near its natural rate, at 0.8–1.5×, rather than overdrive's 2–3×, which
read as flailing. The hero runs on every beat between blows. At 155 BPM a
blow on every strong beat is 1.3 a second, about the normal run's pace. A
spin (1.43 s to its impact at full rate) or a slam (1.1 s) therefore takes
the place of the blow before it, so they are kept for the big moments.

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

The hero is drawn at their true size, about as tall as a skeleton (1.85 m
against 1.8 m), so the humanoid monsters look like a threat (ps-b5z.1).
Overdrive drew them 1.7× larger until then (`heroScale`), which made the horde
look small and harmless. Closer views keep the hero findable instead
(ps-b5z.6): most horde shots frame at 32–42 m rather than overdrive's 40–50 m,
each on-screen target scaled down with the view's area so the crowd is as
dense as before, and the dragon fight at 34 m (32 m in the crystal cut)
rather than 36–44 m. Two shots keep their old views, the default cut's
desert charge (45 m) and the crystal cut's lava streams (45→50 m), whose
first and last bars fell outside the density check's band at every closer
view tried. A glowing outline round the hero (ps-b5z.1's
`heroOutline`, cyan in the default cut and gold in the crystal cut) was
removed on the operator's review ("no yellow glow around the hero"; the
closer camera was their ask). The stills of ps-b5z.1's outline, beside the old
1.7× captures, are in
[evidence/trailer/hero-scale](../../demo3d/evidence/trailer/hero-scale/README.md).

The trailer uses overdrive as the base of every shot (see
[The trailer file](#the-trailer-file)). Everything above stays in force
unless this document changes it.

## What the Viewer Sees

The trailer runs 60.5 s from first frame to silence:

- **0 s: the cold open.** It opens already in motion, on a hero facing three
  giant brutes in a torchlit crypt. One falls on each of the music's stabs.
- **2.48 s: the drop.** The drop lands with a white flash, and the picture
  cuts to a golden-hour desert. There, the hero's slam blows a crater in a
  charging wall of monsters, and for eight bars the hero runs through it,
  turns back through the wave chasing them and turns again.
- **14.87 s: the lava fields.** On the song's re-entry, a flash cuts to a
  basalt hellscape lit by lava. Four streams of monsters, Big Orks and
  Trolls in their own masses among them, pour in from ahead and the flanks
  while lightning strikes on every downbeat. Another eight bars.
- **27.26 s: the blossom garden.** On the hottest phrase of the drop, a
  flash cuts to pale stone under blossom trees. The hero runs a wide loop and
  the horde spirals in after them, carved by spins and rings. Eight bars
  again.
- **39.64 s: the final peak.** The cuts quicken: four bars of a charge down
  a wet neon street, then two bars of a vortex in the snow and two of
  converging streams in the crypt, lightning on the strong beats.
- **52.03 s: the climax, a dragon fight.** A flash cuts to the night forest,
  where a dragon holds a clearing ringed by the horde. The hero's slam lands on
  it, and for four bars they fight: the dragon winds up three attacks on the
  beat, each a glowing zone on the ground under the hero (a cone of fire
  breath, a tail sweep round it), and each time the hero dashes clear on the
  beat before it lands and the blow misses, burning down the horde in the
  zone instead. Between attacks the hero closes in and cuts at it, and it
  flinches. The final slam, on the downbeat of bar 117 at 58.22 s, kills it
  and everything on screen.
- **58.22–59.00 s: the aftermath.** Two beats as the whole horde falls, with
  the shockwave and the embers. By the cut the field lies dead.
- **59.00–60.50 s: black.** The picture cuts to black, and the music fades out
  over 1.5 s.

Throughout, the horde moves in coloured blocks of one monster type, the hero
runs on every beat and swings on the strong ones, and the horde always
leaves them a lane to run. There is no title, text, logo, HUD or
letterboxing.

## The Music Window

The trailer plays the song from song second 121.40 to 181.897. The picture
ends at 180.397, the cut to black, and the music fades out over the last
1.5 s. **Trailer time** is song time minus 121.40.

*Action* has a constant 155 BPM grid in 4/4. Beat 0 falls at 0.00982 s, and a
beat lasts 0.387097 s. Bar `n`, beat `b` (written `n.b`) falls at:

```
songTime(n, b) = 0.00982 + ((n - 1) * 4 + (b - 1)) * 0.387097
```

The window covers three kinds of section (the song's form is in
[songs/action/form.md](../../songs/action/form.md)):

- **Break-2, bars 79–80.** A **free section**: the song map marks it as
  having no audible beat, because the kick and sub-bass drop out. The grid's
  beat times still exist there, since the tempo never changes, but they are
  silent. What the music does mark are three **anchors**, curated stab times.
  In a free section, monsters move smoothly instead of stepping on beats (see
  [Free sections](choreography.md#free-sections)).
- **Drop-2, bars 81–104.** A pulsed section with a steady, audible beat, in
  three 8-bar phrases: a light lead-in (81–88), then the full arrangement
  (89–96) and the hottest phrase (97–104).
- **Final-peak, bars 105–120.** The song's highest-energy section, the main
  drop's arrangement with hats on top. The trailer ends inside it, on 117.1,
  before bar 120's fill, the outro and the free ending.

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
| 27.255 | 148.655 | 97.1 | Re-entry into drop-2's hottest phrase |
| 33.736 | 155.136 | 101.2 | A stutter stop, then an off-beat stab |
| 38.868 | 160.268 | 104.3 | The sub-bass cuts out for beats 3–4 |
| 39.642 | 161.042 | 105.1 | Re-entry: final-peak, hats on top |
| 52.029 | 173.429 | 113.1 | Final-peak's second phrase |
| 58.223 | 179.623 | 117.1 | The final slam |
| 58.997 | 180.397 | 117.3 | The cut to black; the fade begins |
| 60.497 | 181.897 | | Silence; the trailer ends |

Some notes on the table:

- **Accents.** The song map marks 1.753, 2.481, 14.868, 27.255, 33.736,
  39.642 and 52.029 as accents. The stab at 33.736 falls 100 ms ahead of
  101.2's beat, so nothing is placed on it.
- **The extra hits.** The table gives the beat times; the hits themselves land
  31–33 ms earlier. They are not accents. The grid stays on the beat, so
  blows placed there land on the beat itself.
- **Drop-2's character.** Bars 81–88 carry the heaviest sub-bass but sparse
  mids. From bar 89 the full arrangement plays.

## The Shot List

The trailer has eight shots. A **shot** is a continuous stretch of picture
between two cuts, made by its own short simulation in its own environment.

Every cut lands on a beat, and every shot that follows a cut (shots 2–8)
opens on the hero's blow landing on its cut beat. Shot 1 starts the trailer
rather than following a cut. It opens 0.18 s before its first blow, on the
first anchor.

The cuts fall on the song's phrase boundaries: the drop (81) and the
re-entries (89, 97, 105, 113), with two more inside final-peak's first
phrase. The opening has the most room to breathe, and the pace tightens
toward the climax:

- one 2.5 s duel;
- three 8-bar shots (12.4 s each), one per phrase of drop-2;
- one 4-bar shot (6.2 s) on final-peak's re-entry;
- two 2-bar shots (3.1 s);
- one unbroken climax of 4 bars and the aftermath's 2 beats (7.0 s).

No shot is shorter than 2 bars.

| # | Trailer s | Bars | Environment | Formation | On screen | View | Heading |
|---|---|---|---|---|---|---|---|
| 1 | 0.000–2.481 | 79–80 | crypt | Duel: 3 elites | 3 | 20 m | 0° |
| 2 | 2.481–14.868 | 81–88 | desert | Charge | ~560 | 45 m | 0° |
| 3 | 14.868–27.255 | 89–96 | lava | Converge, 4 streams, with elites | ~390 | 42→47 m | 135° |
| 4 | 27.255–39.642 | 97–104 | blossom | Vortex | ~490 | 40 m | −45° |
| 5 | 39.642–45.836 | 105–108 | neon | Charge | ~290 | 34 m | −90° |
| 6 | 45.836–48.933 | 109–110 | snow | Vortex | ~290 | 32 m | 45° |
| 7 | 48.933–52.029 | 111–112 | crypt | Converge, 3 streams | ~350 | 36 m | 180° |
| 8 | 52.029–58.997 | 113–116, plus 117.1–117.2 | forest | The boss fight, the horde in a vortex round its arena | ~380 | 34 m | 0° |

The columns:

- **On screen:** the shot's target count of live monsters in view.
- **View:** the **view width**, the width of ground visible across the middle
  of the screen. It sets the game camera's height.
- **Heading:** the camera's turn around the vertical axis. It makes shots in
  the same environment show different sides of it.
- **Formation:** the horde's opening layout (see
  [Kiting and formations](#kiting-and-formations)).

Every one of the seven resident environments appears: the original four
(crypt twice, for the duel and a horde seen from the far side) and the three
newer ones, which carry the long middle of the trailer (lava and blossom) and
final-peak's re-entry (neon).

The shot list escalates on four tracks at once:

- **Moves.** Single blows in the duel; slams, cleaves and spins after the
  drop; lightning in the lava; spins and rings in the blossom vortex; then
  lightning on the strong beats, and finally the dragon fight and the final
  slam.
- **Density.** 3 monsters, then 290–560 in view (the closer views hold
  about the same crowd per square metre as the old 420–760). The climax
  holds its horde of about 380 in view back at the edge of the fight's
  arena, so the fight reads.
- **View.** Tight for the duel, 40–45 m for the long shots, 32–36 m for
  final-peak's short ones, and 34 m on the fight, so the true-size hero and
  the dragon fill the frame with the horde pressing at its edges.
- **Pace.** 8-bar shots through drop-2, then 4, 2 and 2 bars, until the
  climax plays unbroken.

The table's counts, views and headings are starting points. Implementers tune
counts, widths, headings, paths and moves against the checks and frame
sequences, but they keep each shot's bars, environment, formation and role.

### Shot by shot

Each shot is built around particular moves, listed here in order. Plain
combo entries land on the strong beats (each bar's beats 1 and 3, see
[The hero's moves](#the-heros-moves)); the hero runs on every beat between.

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
2. **The drop (desert, charge, 8 bars).** A white flash covers the cut, and
   the hero's slam lands on 81.1. It blows a crater in a charging wall of
   fodder, a patchwork of coloured blocks.
   - The hero runs straight through the wave, cleaving on the strong beats,
     with spins on drop-2's extra hits (82.3, 84.3 and 86.3).
   - The wave turns and streams after them. The path turns back through the
     chasing wave late in bar 83, and turns again on 87.1, so the long shot
     never runs out of monsters to hit.
   - Through bar 88's sub-bass cut, the hero strides into the release.
3. **The lava fields (lava, converge with elites, 8 bars).** A white flash
   covers the cut and the hero's cleave lands on 89.1 as four streams meet
   them.
   - The hero cuts across the streams, turning once just before 90.1,
     cleaving on the strong beats. Big Orks and Trolls in their own blocks
     fall to single cleaves, so their size sells each blow.
   - Lightning strikes on every downbeat. The view widens from 42 to 47 m
     across the shot.
4. **The blossom vortex (blossom, vortex, 8 bars).** A white flash covers
   the cut, and the hero's slam lands on 97.1, drop-2's hottest phrase.
   - The hero runs a wide loop, entered mid-side so the cut falls between
     corners, and the horde spirals in after them.
   - Spins on 98.3, 100.3 and 102.3 and rings on the downbeats carve the
     spiral.
5. **Final peak (neon, charge, 4 bars).** A white flash covers the cut, and
   the hero's slam on 105.1 flattens the oncoming wave's front rank. The hero
   runs down the street through the wave, cleaving on the strong beats, with
   lightning on the downbeats.
6. **The snow vortex (snow, vortex, 2 bars).** The heavy cleave on the cut,
   then cleaves as the hero circles inside the spiral closing on them, rings
   on the downbeats.
7. **Crypt streams (crypt, converge, 2 bars).** Seen from the crypt's far
   side (heading 180°), three streams meet the hero; lightning strikes on
   the strong beats while they cleave at the meeting point.
8. **Climax: the boss fight (forest, bar 113).** A white flash covers the cut
   and the hero's slam lands on 113.1, on the dragon itself, in a clearing
   the horde rings (see [The boss fight](#the-boss-fight)).
   - The dragon holds the middle of the forest's fairy ring. The hero circles
     it on a loop 5.5 m out, and the horde presses at the arena's edge 10 m
     out, round the frame's edges at the fight's 34 m view.
   - Three telegraphed attacks: a fire breath (113.2 to 114.1), a tail sweep
     (114.4 to 115.3) and a second breath (116.1 to 116.3). In the crystal
     cut the sweep comes first. Each zone glows under the hero from its
     windup; the hero dashes out of it on the beat before it lands, and the
     blow misses, burning down the monsters in it.
   - Between the attacks the hero's blows land on the dragon: the slam on
     the cut, cleaves on the strong beats and a spin on 114.3, five hits in
     all. No blow falls on an attack's impact, so each miss reads alone.
   - The view holds 34 m on the fight.
   - The final slam on 117.1 kills every monster in view, the dragon
     included. The aftermath's two beats show the horde falling: its death
     clips play faster where they must, so every body is down by three
     quarters of the aftermath. Refill stops at the final slam, and the hero
     plays out the slam's recovery.

**Outcome (ps-7mx.3).** The re-cut as shipped, from `trailer --checks`
(the same for every roster set). Its climax row is the converge and lasers
that ps-b5z.5 replaced with the boss fight (see
[The boss fight](#the-boss-fight) for its numbers). Evidence is in
[demo3d/evidence/trailer/recut](../../demo3d/evidence/trailer/recut/README.md).

| # | Bars | Environment | Formation | On screen / target | View | `shapeScale` | Swings/s | Kills per swing (mean, peak) | Kite | Blocks | Lane |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 79–80 | crypt | Duel: 3 elites | 3 | 20 m | 1 | 1.21 | 1, 1 | – | – | 103° |
| 2 | 81–88 | desert | Charge | 545 / 560 | 45 m | 2.4 | 1.05 | 67.5, 238 | 2.4 | 83% | 86° |
| 3 | 89–96 | lava | Converge, 4 streams, with elites | 431 / 440 | 45→50 m | 1.8 | 1.29 | 29.5, 77 | 2.4 | 81% | 87° |
| 4 | 97–104 | blossom | Vortex | 768 / 760 | 50 m | 2.1 | 1.05 | 73.9, 215 | 2.3 | 78% | 143° |
| 5 | 105–108 | neon | Charge | 410 / 420 | 40 m | 2.0 | 1.29 | 62.5, 213 | 2.5 | 81% | 88° |
| 6 | 109–110 | snow | Vortex | 450 / 450 | 40 m | 1.6 | 1.29 | 41.5, 82 | 2.4 | 78% | 105° |
| 7 | 111–112 | crypt | Converge, 3 streams | 526 / 550 | 45 m | 1.4 | 1.29 | 31.0, 38 | 2.3 | 77% | 73° |
| 8 | 113–117.2 | forest | Converge, 6 streams, plus the boss | 1,266 / 1,300 | 52→65 m | 2.0 | 1.00 | 238.7, 1,427 | 2.3 | 83% | 99° |

- **Every check passes,** with zero liveness violations, on the default
  seed and roster, and for `--roster forest` and `--roster heroic`.
- **Seeds.** Trailer seeds 3, 5, 7, 9, 11, 13 and 21 pass every check. Seed
  42 misses two bars' density by a few points (shot 2's bar 87 at 468 of
  560, shot 4's bar 97 at 628 of 760).
- **Pinned environments.** Pinned to desert, snow, forest, lava or blossom
  every check passes; pinned to crypt or neon, shot 3's last bar holds 371
  and 366 of 440, just under the 15% line. The 8-bar converge drains a
  little in its last bar on some seeds; the view's widening to 50 m keeps it
  up on the default.
- **Tuning against the long shots.** The 8-bar shots were the hard part:
  over 32 beats a formation's refill drifts, so every bar was tuned across
  seeds 3–42 and all seven pinned environments, not seed 7 alone:
  - the paths are long enough for 48 beats of 1.9 m strides (91 m for an
    8-bar shot, pre-roll included) and keep the view on open ground: shot 3
    runs east from the courtyard field so its stream sources stay on the
    arena, and shot 5 runs west down the street, away from the arena's edge;
  - shot 2's charge aims at 560 and shot 3's elite converge at 440, as the
    long shots drained below their first targets of 600 and 500;
  - shot 4's vortex aims at 760 at `shapeScale` 2.1, since at 700 its
    spiral piled up past the target in bar 103;
  - shot 5 swings cleaves at `shapeScale` 2.0 without a spin, since a spin's
    windup in bar 106 let the wave close the lane;
  - shot 3 dropped a slam wound up across its cut (`slam@97.1`, as the first
    cut's shot 9 did), which hemmed the hero in during bar 96 on some seeds;
  - the climax rings on 114.1 only, and its path turns at bar 116, so the
    boss stays in view on the hero's flank rather than meeting them.
- **Cut frames.** The player switches in 0.10–0.12 ms of CPU at every cut.
  A GPU probe puts each cut frame up to about 14 ms over the frames after
  it, but the same frames drawn with no cut (`--shot N --from-preroll`)
  cost as much or more: the cost is the opening blow, not the switch
  ([timing](../../demo3d/evidence/trailer/recut/timing.md)).
- **The mix.** `demo3d trailer --stand-in` passes every mix check over the
  60.5 s window and its seven cuts.
- **The boss prowls.** Over the climax's four bars the boss reaches its
  keep-out with beats to spare, where the horde packed behind it held it
  still; it now prowls round the hero instead (see [The boss](#the-boss)).
- **Carried over from the first cut (ps-hgs.7):** a shot's fixed entry is
  scheduled past a flurry's pending decision, so explicit-beat spins land
  (every `@bar.beat` entry lands, checked); and the final slam's deaths play
  their clips faster where they would outlast three quarters of the
  aftermath (1.4–3.5×), so the field lies dead at the cut to black. Lens
  hits keep `TrailerLook.MassKillWeight` at 0.5.

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
  - In the horde shots the hero strides on every beat, blow or not.
- **Blows land on the major beats, never flailing.** Every hero impact lands
  on a strong beat (4/4's beats 1 and 3), an accent, the shot's cut, the
  final slam or one of break-2's anchors. In a pulsed section, blows land at
  least two beats apart, each clip played near its natural rate (see
  [The hero's moves](#the-heros-moves)). This replaces the first cut's
  blow on every beat with flurries on the eighths.
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
- **The hero is never surrounded.** As in a survivors game, a player boxed in
  on every side would be dead. On every beat, the bodies of the monsters
  within 6 m of the hero leave a gap round them at least 60° wide: an escape
  lane. The horde closes from the front and the flanks and trails behind.
  - No monster stands where the hero is about to run, so the hero never
    walks through a body.

### Kills

Every monster dies to one blow, but the boss fights:

- **One hit kills every rank.** Fodder, elites and the boss all have one hit
  point. The fight's boss takes the hero's blows as hits it survives (see
  [The boss fight](#the-boss-fight)).
- **Every monster a blow touches dies.** The kill budget is off, so no death
  is held back.
- **Trampling kills too.** Small monsters the boss tramples die, as they do
  today.
- **Deaths pay off on the impact frame.** The death burst starts on the frame
  the blow lands, with no build-up. Under `--monster-style crystal`, dying
  monsters break into glass shards, and the shatter starts on that frame too.
- **The boss dies once, to the final slam** (see [The boss](#the-boss)).
- **The boss's attacks kill what they land on.** Every monster in a boss
  attack's zone dies on its impact; the hero has dodged out of it.

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

- **Eight simulations.** There are eight short simulations, one per shot,
  instead of one long one.
- **Seven environments.** Seven environments are loaded in one player,
  instead of one.

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
  "startS": 121.40, "blackAt": "117.3", "fadeOutS": 1.5, "seed": 7,
  "shots": [
    { "cut": "start", "environment": "crypt", "viewWidthM": 20,
      "hero": { "at": "camp", "holdGround": true },
      "duel": { "bigork": 2, "troll": 1 },
      "combo": ["slashR@anchor", "spin@anchor", "slashL@anchor"] },
    { "cut": "113.1", "environment": "forest", "viewWidthM": 34,
      "path": { "via": [[40.1, 36.1], [38.5, 40], [40.1, 43.9], [44, 45.5],
                        [47.9, 43.9], [49.5, 40], [47.9, 36.1], [44, 34.5]], "stepM": 1.9, "loop": true },
      "formation": { "kind": "vortex", "onScreen": 380,
                     "mix": { "skeleton": 4, "goblin": 3, "bat": 2, "demon": 2,
                              "bigork": 0.4, "troll": 0.3 } },
      "boss": "ahead",
      "bossFight": { "at": [44, 40], "attacks": [
        { "kind": "breath", "windup": "113.2", "impact": "114.1" },
        { "kind": "sweep", "windup": "114.4", "impact": "115.3" },
        { "kind": "breath", "windup": "116.1", "impact": "116.3" } ] },
      "shapeScale": 2.0, "densityCap": 1.8, "grade": { "contrast": 6, "saturation": 8 },
      "combo": ["slam@cut", "cleaveL", "cleaveR", "spin@114.3", "slam@final"] } ] }
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
- **`blowsOn`**: the grid position the combo's plain entries land on, as the
  level script's `comboOn` (default `strong`: each bar's beats 1 and 3, see
  [The hero's moves](#the-heros-moves)).
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
- **`bossFight`**: the climax's boss fight (needs `boss` and a final slam):
  `at`, the ground the dragon holds (metres or a named area), and `attacks`,
  each `{ "kind": "breath" | "sweep", "windup": bar.beat, "impact": bar.beat }`.
  A windup is a beat from the cut on; its impact lands on a strong beat or an
  accent at least two beats later, before the final slam and after the
  attack before has landed. See [The boss fight](#the-boss-fight).
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

New environments join the trailer by being named in the file, with no code
change, as lava, neon and blossom did (ps-7mx.2). An unknown
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
- **The boss rule** (see [The boss](#the-boss)), and its fight
  (`bossFight`, see [The boss fight](#the-boss-fight)).

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
- **`vortex`**: the horde in a ring round a looped path. As the hero circles,
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
  empty. Refill waits for that meeting (ps-7mx.1; until then at the cut,
  which left the bar after it thin once the hero's cleaves cut into the
  wave), then comes from within 75° of the hero's travel.
- **Vortex.** The target's worth of monsters spread over a ring round the
  loop, from its corners to 8 m past them (ps-7mx.1; a disk over the whole
  loop, until then, put the hero inside the horde). Refill comes from the
  whole edge, and monsters behind the hero stay, since the hero circles
  back.
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

The liveness check held: zero violations in all ten shots of the first
cut (bars 79–99; the shot numbers in this outcome and the next are its own,
the re-cut's are in [The Shot List](#the-shot-list)), for the
default roster and the forest and heroic sets. The density check passes
too, and both hold across other trailer seeds: seeds 3, 5, 9, 10, 11, 13,
17, 21, 27, 28, 29 and 42 pass every check.
Pinned to one environment, shot 2 or 5 could still miss a bar's density by
up to 20%; since the shot list's tuning (ps-hgs.7) every pinned environment
passes. Tuning changes from the first cut:

- shots 6 and 8 run at 1.6 and 1.3, since spins and lightning on every beat
  killed faster than their streams could fill;
- shot 5's elite charge aims at 540, since its big bodies jam a 600-strong
  wave.

The kite ratio is 2.3-2.5 in every horde shot, measured up to the final
slam. Frame sequences are in
[demo3d/evidence/trailer/kiting](../../demo3d/evidence/trailer/kiting/README.md).

**Outcome (ps-7mx.1): blocks, blows and the lane.** The first cut's hordes
were confetti, its hero flailed on every beat and eighth, and monsters
stood packed round the hero. Three changes, all in a formation's shot only:

- **Type blocks.** The ground is cut into square blocks 30% of the view
  wide, and at least 12 m (ps-b5z.6: a close view's smaller blocks broke
  up under the type-block check's 3 m neighbourhood), and each block is dealt one of the mix's types, from the block and
  the seed. Blocks are dealt by the ground each type's share of the count
  covers (its weight × its diameter²), as the song's clump arcs are. Every
  opening monster and every refill takes its spot's block's type, so
  waves walk in as coloured masses: a charge is a patchwork of blocks, a
  converge's streams change colour as their sources sweep across blocks.
  - A big monster's block fills sparser by count (the mix's mean diameter²
    over its own), so a block of trolls covers the ground a block of bats
    does with fewer bodies. The horde keeps the mix's proportions by count.
  - A refill tries up to 64 spots per monster, since a charge's spots must
    lie ahead and a big monster's block keeps fewer of them.
- **Blows on the major beats.** A shot's plain combo entries play on its
  `blowsOn` position, `strong` by default (the level script's `comboOn`),
  with the combo speed limits at 0.8–1.5× (see
  [The hero's moves](#the-heros-moves)). The shots swing cleaves, the
  normal run's 360° slashes, so each blow clears the ground round the hero.
- **Keeping out of the hero's way.** No formation monster steps into:
  - **the escape lane**, a wedge 45° either side of straight behind the
    hero, out to 8 m. The wedge is taken both against the hero's travel and
    back along its last two strides, so round a corner it holds both ways
    until the trail has turned too. A monster whose body reaches into it, or
    whose step would take it there, turns out of it to the nearer side and
    on along the hero's travel. The horde then trails behind in a wake;
  - **the hero's way**, the ground the hero's next two strides sweep. A
    monster there sidesteps out of it and plants no blow there, unless the
    hero's own blow lands by the next beat and cuts it down mid-windup.
  - A step out of the lane or the way leads away from the hero, so the
    monster's hold distance doesn't shorten it. Boxed in, it presses on as it
    chased instead. Cornered with the hero in reach, it swings. On its last
    try, rather than mark time, it shoves 70% of the smaller radius into its
    neighbours, not 40%.

Fewer blows kill fewer, so the first cut's ten shots were retuned (by their
own numbers; the re-cut's shots are in [The Shot List](#the-shot-list)):

- shot 2's charge swings at `shapeScale` 2.4, so each ring clears the two
  beats of ground the hero runs into between blows; shot 5's elite charge
  stays at 1.8, since its refill can't keep up with more;
- shot 5's elite charge and shot 7's charge aim at 420 on screen: with the
  lane open behind the hero, half the screen is the wake;
- shot 6's vortex loop is entered mid-side, so its cut falls between corners.
  It opens on the heavy cleave, since a spin's long windup cost it the blow
  before its cut, at `shapeScale` 1.4;
- shot 7 opens on its slam (on 94.4 it no longer fit), at `shapeScale` 1.8;
- shot 8's lightning strikes on the strong beats, not every beat.

### The boss

The boss, a dragon in every roster set, appears only in the climax. With
the boss rule (a shot's `boss`), it takes no spell and dies only to the
final slam:

- **The final slam kills it.** Nothing else fires on that beat. It dies on
  117.1 with everything else in view.
- **Spells skip it.** The rings, lightning and lasers ignore it.
- **It never stands.** It walks, prowls or plays a clip on every beat.

Both cuts' climaxes fight it (`bossFight`, below). A boss rule without a
fight, which no checked-in cut uses now, keeps the first cut's prowling
boss: it enters outside the view on the cut from the shot's `boss` side
(relative to the hero's travel), storms in trampling the small fry, keeps
beyond the shot's largest hit shape plus 1 m so no blow reaches it, prowls
round the hero when the horde packs behind it (ps-7mx.3), and the final
slam is the only hit it takes.

### The boss fight

The climax ends each cut on an obvious fight with the dragon, where
positioning matters (ps-b5z.5, the operator's note: "maybe the boss gets
off some attacks the player dodges"). The shot's `bossFight` makes it part
of the shot simulation: every attack, zone and dodge is a timeline event,
and the client only draws them.

- **The dragon holds its ground.** It stands at the fight's `at` from the
  pre-roll's start, so the cut reveals it there. Between attacks it steps
  toward the hero, up to 2.5 m from its ground and never onto the hero,
  faces them, and tramples whatever it steps through. On its mark it prowls
  round with the hero.
- **The hero circles it.** The climax's kite path is a loop 5.5 m round
  the dragon's ground, so the hero's cleaves (4.8 m at `shapeScale` 2.0)
  reach it.
- **The horde holds back at the arena's edge.** In a fight, formation
  monsters keep out of a 10 m circle round the dragon's ground: one inside
  walks out, and a step that would cross the edge ends on it, or prowls
  round it on the hero's side; one left no step at all swings at the air,
  snarling at the hero, rather than mark time. The vortex opens outside the
  arena with three quarters of a vortex's horde, since the edge holds the
  horde in view instead of feeding it to the hero's blows. Its refill makes
  up a quarter of the shortfall on screen each beat, from beyond the view
  (ps-b5z.6): most of the ring is off screen at the fight's close view, held
  at the arena's edge, so the usual refill (which counts that ring as
  arriving and feeds the breaths' bursts of kills back in) starved the view
  or flooded it. The fight reads in a clear arena whose edge rings the
  frame, about 380 in view at 34 m, and the hero's blows still cut the front
  rank.
- **Telegraphed attacks on the music.** Each attack winds up on a beat and
  lands on a later strong beat or accent, at least two beats on. On the
  windup the sim places its zone where the hero will be through the windup,
  plants the dragon and turns it to the zone:
  - a **breath** is a cone from the dragon's head, 15 m long and 26–40°
    either side, aimed at the middle of the hero's strafe, drawn with the
    dragon's first attack clip (its head strike);
  - a **sweep** is a disc round the dragon, at least 6.5 m and 1.5 m past the
    hero, drawn with its second attack clip (its tail whip).
  - The clip is fitted to land on the impact, slowed to at most half speed
    to fill the windup. The dragon stays planted until a beat after the
    blow, then steps on as the clip plays out.
- **Positioning matters.** While an attack winds up, the hero strafes at
  55% of its stride, so it stays in the zone. On the last beat before the
  blow it dodges: a dash that ends 0.06 s before the impact, sideways out of
  a breath's cone (on along the path's side first) or straight out of a
  sweep's disc, its body 1.4 m clear of the edge, to whichever landing the
  fewest monsters crowd. The blow lands on the empty zone, killing every
  monster it holds, which also clears ground for the fight. No hero blow
  falls on an attack's impact, so each miss reads alone.
- **The hero fights back.** Every hero blow that reaches the dragon is a hit
  it survives: a heavy spark and a flinch (its hit clip) and a half-metre
  recoil between attacks, a flash with no clip while it is mid-attack, so
  the attack plays on. The final slam kills it.
- **Payoffs on the impact frame.** Each attack's fire (bursts down the cone,
  growing with it) or sweep (a giant slash round the dragon, shocks round
  the disc's edge), its sound, the heaviest flash and shake a break gets,
  and a full-strength lens hit all land on the impact.

The client (`BossTelegraphs`) draws each zone on the ground from its windup
to its impact in the Demo3D/BossTelegraph shader: a bright rim, a dim fill
charging outward from the dragon to reach the rim on the impact, hazard
bands streaming outward, a throb on each beat, and a white-hot flash as the
blow lands. A breath's zone burns red, a sweep's magenta, both drawn
premultiplied so they read on a lit floor and in the dark. The hero's
dodge plays its run clip at 1.6× and kicks up dust where it starts and
lands.

**Outcome (ps-b5z.5, framing from ps-b5z.6).** Both cuts' climaxes as shipped, from
`trailer --checks` (the dodges are the hero's distance from the dodge's start
to the impact):

| Cut | Ground | Attacks (windup to impact) | Dodges | Hits on screen | On screen / target |
|---|---|---|---|---|---|
| action | (44, 40), the fairy ring | breath 113.2–114.1, sweep 114.4–115.3, breath 116.1–116.3 | 3.1, 5.8, 3.6 m | 5 | 404 / 380 |
| crystal | (36, 30) | sweep 113.2–114.1, breath 114.4–115.3, breath 116.1–116.3 | 4.0, 2.9, 3.5 m | 5 | 343 / 340 |

- The breaths kill 79 and 78 (action) and 67 and 64 (crystal) of the horde
  pressing at the arena's edge; the sweeps, inside it, clear the dragon's
  ground and the hero's dodge. The crystal cut's two breaths smash its two
  formations. Every check passes in both cuts.
- The action cut fights in the forest's fairy ring (its candles and stones
  ring the arena); the crystal cut on the open field south-west of it.
- The final review (ps-b5z.6) found the hero hard to find in the fight's
  frames: at the first cut's 36–44 m the true-size hero is about 30 px tall,
  and in the crystal cut its gold outline sat on the arena's warm-lit ground
  beside the gold dragon, so the dodges barely read. The outline is gone and
  the fight holds 34 m (32 m in the crystal cut): the arena's edge rings the
  frame and a 3 m dodge is a tenth of its width. Closer still, 30 m, read
  better, but its bars swung past the density band under one runtime or
  the other. The crystal cut's two formations stand 5 m tall so they break
  wholly in view.
- Evidence is in
  [demo3d/evidence/trailer/fight](../../demo3d/evidence/trailer/fight/README.md):
  each attack's telegraph, dodge and impact in both cuts.
- The lasers, the ring and the six converging streams of the first cut's
  climax are gone: the fight replaces them, and the horde is held back
  rather than flooding the frame.

### Environments in one player

The player loads every environment the trailer uses before the first frame:
crypt, desert, snow, forest, lava, neon and blossom. All seven stay resident,
and exactly one is active at a time. This is new: today a player loads one environment for its
whole run, and loading it changes global rendering state.

Each environment loads as its finished version. Its rendering is on the E2
rung, the top of the environment's measured optimization ladder, with baked
static lighting and a static shadow map. Its decoration is at density 2, the
authored prop density. That is the one baked variant every environment
has.

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
- **Each environment looks as it does alone.** With all seven resident, each
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
  all seven are resident.
- **Fallback.** If overlapping light probes or lightmaps fail the test, the
  environments are placed far apart in world space. Shot simulations keep
  local coordinates, and the client adds each environment's offset.
- **Outcome (ps-hgs.1, then ps-7mx.2 for seven): they overlap.** With all
  seven resident, every environment's stills, taken on the very frame that
  switches back to it, match its single-environment run within 0.01 L*, in
  all 28 look-set stills of each, lava's emission, neon's signs and wet street
  and blossom's pale stone included
  ([evidence](../../demo3d/evidence/trailer/residency/README.md)). No offset
  exists, and shot simulations use world coordinates as they are.

How it works (`ArenaSet`, `ArenaRuntime.Resident`, `Show` and `Apply`):

- **Lightmaps** need nothing: every loaded scene's lightmaps join one list,
  and each renderer indexes its own.
- **Light probes** are swapped. Unity keeps one scene's probes after
  additive loads (each scene's 882, never all seven), so a switch sets
  `LightmapSettings.lightProbes` to the new scene's own
  (`LightProbes.GetSharedLightProbesForScene`).
- **Visibility** is a layer per scene (8 upwards: 8 to 14 for seven, of the
  24 free from 8 to 31, which `ArenaSet` checks) and the camera's culling
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

The memory cost is accepted: seven scenes, seven lightmap sets and seven
64 MiB shadow maps. The trailer's player peaks at about 2.8 GiB of physical
footprint, against 1.4 GiB for a normal one-environment run and 2.3 GiB for
four resident. The trailer runs on a powerful machine.

**Outcome (ps-7mx.2).** Lava, neon and blossom joined as data: their baked
E2/density-2 scenes load like the others, the warm-up draws them (2 to 4 ms
to submit each), and the trailer's shots 5, 6 and 7 cut to them with grades of
their own. A switch costs 0.12 to 0.20 ms of CPU. In six probe runs of all
seven switches, 41 of 42 switch frames were within 2 ms of their neighbours;
the other, at +2.22 ms, was draw noise under a load average near 20, since
its switch took 0.13 ms and the same switch measured −0.20 to +1.54 ms in the
other runs.

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

**Flash cuts.** Five cuts land on the trailer's big musical steps: 81.1 (the
drop) and the re-entries 89.1, 97.1, 105.1 and 113.1. These cut under a
white flash.

- The flash is the **post feel**'s hit flash. The post feel is the client part
  that pulses exposure, bloom and vignette on beats, and flashes on accents
  and big hits.
- On these cuts, the flash peaks near-white on the cut frame and decays over
  an eighth note.
- The cut hides under the flash, and the new shot's opening blow bursts out of
  it.

The other cuts are plain hard cuts. The crystal cut turns both into its
transitions, palette flashes and impulse wipes (see
[The Crystal Cut, Transitions](#transitions)).

The trailer's start and end:

- **The first frame.** The screen stays black while the player loads,
  simulates and warms. Then the music and shot 1 start together, 0.18 s
  before the first kill.
- **The end.** On 117.3 the picture cuts to black, and the music and sounds
  fade to silence over `fadeOutS`.
- **Exit.** The live player then exits. A capture ends with the fade.

The timeline checks run offline. The live player plays whatever the file
produces, even if a check would fail. `--overlay` shows any liveness
violations live.

**Outcome (ps-hgs.6).** `--trailer` is DemoMain's trailer mode
(`DemoMain.Trailer.cs`). Some details of what shipped:

- **Load.** The first cut's ten shot simulations took about 0.6 s in the
  IL2CPP player.
  A global black volume (a colour filter of black, priority 1000) covers the
  screen until the warm-up ends and the clock starts, and again from the cut
  to black.
- **The cut.** One frame switches the environment, then swaps in the shot's
  own timeline, evaluator, effect driver, blow and camera feel and crystal
  breaks. The effect driver's seek clears particles and effect lights. The
  crystal cut's horde (`LookCrowd`) is retargeted, which clears its shards. The horde never
  poses before the shot's first frame, which clears the hit-stop. Crowd slots
  are keyed per shot, so no slot carries a monster's state across the cut.
  A cut costs 0.1–0.3 ms, plus up to about 1.2 ms of CPU on its frame while
  the new shot's live effects re-spawn
  ([evidence](../../demo3d/evidence/trailer/playback/README.md)).
- **Which cuts flash.** A cut flashes when it lands on one of the song map's
  accents, within 10 ms. In *Action*'s window those are 81.1, 89.1, 97.1,
  105.1 and 113.1, so the trailer file needs no field for it. The flash is
  +5 stops, −70 saturation and +4 bloom on the cut frame, decaying
  quadratically over an eighth note, with one post feel per environment
  holding every shot's big hits and the flash cuts. With `--shot`, only that shot's own cut and hits
  flash.
- **Sound.** One scheduler plays every shot's cues within its visible span.
  Its gain envelope fades a cue from before a cut over 60 ms from the cut, and
  every cue and the music from the cut to black. With `--shot n
  --from-preroll` the pre-roll's own sounds play too, since its whole span is
  what that run shows. `--dump-sounds` writes the merged cue list with the
  cut and end fades, for the capture's mix (see [Capture](#capture)).
- **Time flags.** `--start-at`, `--stills-at`, `--seconds`, `--freeze-at` and
  `--gpu-probe-at` take trailer seconds. With `--shot`, the run starts at the
  shot's cut (or its pre-roll) unless `--start-at` says otherwise, and ends at
  the shot's end.
- **Rejected too.** `--structure-breaks` (a trailer's breaks are its shots'
  own; see [Structure breaks in a shot](#structure-breaks-in-a-shot)),
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
- **The hero's size.** True size (ps-b5z.1); the shots frame closer, at
  32–45 m, to keep the hero readable (ps-b5z.6).

No shot adds a virtual zoom, an orbit, a punch-in or any camera move a player
wouldn't see. The crystal cut narrows this to one grammar for every shot: one
heading, one framing per shot from a ladder (see
[The Crystal Cut, The camera grammar](#the-camera-grammar)).

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

**The horde's beat pulse.** Every living monster squashes on each beat's own
frame, rebounds into a stretch and lights up in its own colours, harder on
downbeats and accents and scaled by the song map's energy; in break-2's cold
open only the three anchors hit. It is the normal run's pulse, in both cuts and
under every style ([crowd.md, The beat pulse](crowd.md#the-beat-pulse)); at the
cuts' view widths the throb carries it and the squash adds weight. Two-bar
sequences of both cuts:
[evidence/trailer/pulse](../../demo3d/evidence/trailer/pulse/README.md).

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
  after the post stack (`Hidden/Look/LensHit`; the feature and `LensDirt` live
  in the look package since ps-wnj.6). The pass draws only on frames
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

**Under a look (ps-wnj.6).** The crystal cut and the neon action cut draw with their look preset's post
stack ([neon-look.md, Post-processing stack](../design/neon-look.md#post-processing-stack)):
every environment's night volume takes the stack's one profile, so no shot
grade, section grade, style post look or trailer grain and dirt apply; the
option brings its own grain and dirt, its beat and flash amounts drive the
post feel, and lens hits and flash cuts are unchanged. Since ps-wnj.24 the
look is calm outside special moments: accents, anchors and the hero's hits
flash at the option's `calm` share, structure breaks and formation smashes in
full, and the formations' breaks, the grid's ripples and the horde's hit
flashes peak only in a special moment
([neon-look.md, Restraint and special moments](../design/neon-look.md#restraint-and-special-moments)).
The plain action cut (`--trailer`, no look flag) keeps everything above.

## The Crystal Cut

`--trailer crystal` plays an alternate cut, `trailer-crystal.json`, that
showcases the crystal horde and shattering: glowing crystalline monsters in
the look package's void world, and towering crystal formations that the
hero's blows smash on screen, again and again (ps-7i4, redesigned around the
formations by ps-b5z.4, re-staged in the void by ps-wnj.17). The default cut,
`--trailer` (`action`), has none.

```
just demo3d --trailer crystal --look edge --sound
just trailer --out captures/trailer-crystal-edge.mp4 -- --trailer crystal --look edge
```

- **The same song, window and pace.** Bars 79–117, eight shots cut where the
  action cut's are, 60.5 s with the fade. The gameplay rules hold too: blocks
  of one type, blows on the major beats, constant motion, an open lane, one-hit
  kills, the boss and the final slam.
- **One world, the void** ([neon-look.md, The Trailer](../design/neon-look.md#the-trailer)).
  Every shot plays on the void's open ground and is one **zone** of it, its
  hue the palette's zone tint under `hued-zones` (see
  [The void staging](#the-void-staging)).
- **Shattering is the centrepiece.** Fifteen formations burst over seven of
  the eight shots, one every three to five seconds from the drop to the
  climax, each to the hero's blow on a strong beat, but the climax's two,
  which the dragon's breaths smash as they miss the hero (see
  [Crystal formations](#crystal-formations) and
  [The boss fight](#the-boss-fight)).
- **Cuts are designed transitions** in one grammar, and **one camera
  grammar** frames every shot (see [Transitions](#transitions) and
  [The camera grammar](#the-camera-grammar)).
- **The look is a preset's.** `--look edge|solid|hologram|prism` picks it
  (neon-look.md, Look presets); without one, `edge` so it runs. The file's
  `look` still stands in for `--monster-style` and `--structure-style`, which
  only `--look classic` draws.

| # | Trailer s | Bars | Zone (old place) | View | Formation | Crystals smashed (trailer s, bar.beat) |
|---|---|---|---|---|---|---|
| 1 | 0.000–2.481 | 79–80 | 0 (neon) | 20 m | Duel: 3 elites | |
| 2 | 2.481–14.868 | 81–88 | 1 (crypt) | 44 m | Charge | 4.80 (82.3), 9.45 (85.3), 13.32 (88.1) |
| 3 | 14.868–27.255 | 89–96 | 2 (lava) | 44 m | Converge, 3 streams | 17.19 (90.3), 21.06 (93.1), 22.61 (94.1) |
| 4 | 27.255–39.642 | 97–104 | 3 (forest) | 42 m | Vortex | 29.58 (98.3), 34.22 (101.3), 38.09 (104.1) |
| 5 | 39.642–45.836 | 105–108 | 0 (neon) | 38 m | Converge, 4 streams (300) | 41.19 (106.1), 44.29 (108.1) |
| 6 | 45.836–48.932 | 109–110 | 1 (crypt) | 36 m | Vortex | 47.38 (110.1) |
| 7 | 48.932–52.029 | 111–112 | 2 (lava) | 36 m | Converge, 3 streams | 50.48 (112.1) |
| 8 | 52.029–59.0 | 113–117.2 | 3 (forest) | 32 m | The boss fight, a vortex round its arena | 55.90 (115.3), 57.45 (116.3), both by the dragon's breath |

**The mixes key the frame** (ps-wnj.29,
[evidence](../../demo3d/evidence/look/trailer/last-breaks/README.md)). The
style guide's key wants one hue family to hold most of a still's colour, and a
type's share of the colour is not its share of the mix: an elite took 40-48%
of shot 3's colourful pixels from 8% of its mix, and the accent goblins far
outdraw the key skeletons. So shot 3 is the key family with a few elites,
shot 4 all key family, shots 5 and 6 have fewer goblins and more demons, and
the finale no goblins. Every still keys at 0.55 or more in every preset.
Shot 2 lost a goblin too (3 to 2, ps-wnj.28): the line looks' outline hulls
take more of a finely cut goblin's light than a skeleton's, and with three
the accent and key families split shot 2's colour evenly (edge keyed 0.53 at
4 s; [evidence](../../demo3d/evidence/look/separation/README.md)).

### The void staging

The Synty cut (ps-b5z) stood each shot in a dark environment: neon, crypt,
lava and the night forest, each twice. The void world kit (ps-wnj.8) hid those
sets, but the shots still simulated on their layouts, so the horde parted
round trees, landmarks and walls the void no longer drew: big empty black
holes in the swarm ([before](../../demo3d/evidence/look/world/README.md)).
Re-staged (ps-wnj.17), the simulation plays in the void too:

- **The void's open ground** is `void-layout.json`: a 160 m arena layout
  like the forest's, with one named area (the camp, at the centre, where the
  hook's duel stands) and nothing that blocks. A shot's formations are its
  only obstacles, and its only set pieces: GW3's open arena, with crystal the
  one material, and the formations, the brightest solid things on screen,
  standing alone.
- **A shot's `layout`** names the layout its simulation plays on in place of
  its environment's (`"layout": "void"`); its areas, crystals and run inputs
  are that layout's (`TrailerShot.Ground`, `layout/void`). Its `environment`
  then only names the resident scene that lends render state: every shot
  names the forest, so one scene is resident and no cut switches
  environments.
- **The host scene shows nothing of its own.** An environment that hosts
  only own-layout shots (`VoidArena`'s host-only mode) hides its whole set,
  mist and flame lights under every world option, `classic` too (which then
  draws the forest's bare ground under the classic monsters), and stands no
  structure props, since nothing of its layout blocks those shots.
- **A shot's `zone`** (0 up) is the palette zone it takes on its cut under
  `hued-zones` (else its index). The old places become zones in turn, neon 0,
  crypt 1, lava 2 and the forest 3, so the cut keeps its rhythm of four places
  each shown twice, and no two neighbouring shots share one. `void-grid` and
  `pure-void` keep one key throughout.
- **The shots kept their composition.** Each shot's path, formations and
  boss ground were turned by its old heading into the one heading and
  centred on the void, so each frames as it did on its old ground, and no
  frame reaches the void's edge. Two formations moved to stay in view
  without an obstacle-bent path (shot 3's first, shot 5's first, which the
  cut's slam had reached), so all fifteen smash at the old cut's moments. The
  per-shot grades went with the environments they balanced.
- **What still passes.** `trailer --name crystal --checks` and
  `TrailerTests.TheCrystalCutPassesTheTrailersChecksAndSmashesItsCrystals`
  under `dotnet test` and Unity's EditMode run: open lane, liveness, density,
  every formation breaking on screen and in view, the camera grammar. Stills
  and view sheets:
  [evidence/look/restage](../../demo3d/evidence/look/restage/README.md).

### The camera grammar

Every shot uses the game camera (see [Camera](#camera)): its pitch, field of
view, follow, punch and shake. On top, the crystal cut frames by one set of
rules, which the `one camera grammar` check holds for every shot on its own
layout (`TrailerChecks.CameraGrammar`):

- **One heading.** Every shot looks the same way (yaw 0), so the floor's grid
  runs along and across the frame in every shot and the void reads as one
  place. The check takes the first void shot's heading, which must be a
  multiple of 90°. The Synty cut's diagonal headings (45°, 135°) put the grid
  aslant in one shot and square in the next.
- **One framing per shot**, from a ladder by the shot's role: the hook's duel
  close (20 m), the three long shots wide enough to read the horde's shape
  (42–44 m, by the horde each holds within its density cap), the quickening
  tighter (38 then 36 m) and the finale's fight tightest (32 m). The exact
  width within a rung is set so every bar's density sits well inside the
  check's 15% under both `dotnet` and Unity, whose floats differ (shot 5 at
  36 m held 251 of 300 in its first bar under Unity only). No zoom within a shot
  (shot 3's 45 to 50 m went).
- **The hero leads.** Paths carry the hero across or up the frame, so the
  frame's deeper half lies ahead, and the formations stand ahead of the
  hero's blows, 20 to 80° off the line ([Crystal formations](#crystal-formations)).

### Transitions

A cut in the crystal cut is one of two transitions, both on the beat (every
cut is a bar's downbeat) and both in the incoming shot's zone colour (its
zone tint under `hued-zones`, else the palette's `grid.pulse`):

- **A section's entry** (the cuts on the song map's accents: 81.1, the drop,
  and the re-entries 89.1, 97.1, 105.1 and 113.1) is a **palette flash** and a
  strong **impulse wipe**. The palette flash replaces the action cut's
  near-white flash: on the cut frame the colour filter leans 65% toward the
  zone's hue (brightened to full value, so it tints without darkening),
  exposure lifts 1.2 stops and bloom 1, decaying quadratically over an eighth
  note (`PostFeel.PaletteFlashes`). The frame floods with the hue it cuts to
  rather than washing out: a transition, never the brightest thing in the cut
  (the run's restraint, ps-wnj.24).
- **A cut within a section** (the first frame, 109.1 and 111.1) is a soft
  **impulse wipe** alone.
- **The impulse wipe** is one ring on the look's impulse field from where the
  hero stands on the cut's frame (`ImpulseField.Wipe`, `ShotImpulses.Wipe`):
  it runs at 64 m/s to 40 m, so its front sweeps the whole frame's floor in
  about a beat, lighting and shoving the grid in the zone's colour, the floor
  turning over into the new zone. A section's entry's is stronger (0.9 against
  0.35) and wider.
- **No whips.** A whip pan is a camera move the game camera never makes (the
  trailer's own rule), and its blur is noise against the run's restraint.
- The cut to black at 117.3 and the fade are unchanged.

The plain action cut keeps its near-white flash cuts and hard cuts; the neon
action cut takes the crystal cut's transitions.

### The crystal look

Under a look preset the monsters, formations, world, post and effects are the
preset's (neon-look.md, The Axes). The file's look below is what `--look
classic` (the look before the epic, for parity checks) draws in their place,
on the forest's bare ground:

```json
"look": {
  "monsterStyle": "crystal", "structureStyle": "crystal",
  "styleTune": "glow=1.0,exposure=0.5,contrast=26,saturation=30,bloom=2.0,threshold=1.0,vignette=0.42,chroma=0.05",
  "structureTune": "gain=0.3,glow=0.6"
}
```

- **Glow** is a new tune key, crystal's inner glow (`_InnerGlow` in
  `CrystalCommon.hlsl` for the structures, the look package's global
  `_SL_InnerGlow` in `MonsterCommon.hlsl` for the monsters and their shards).
  It adds the gem's colour after the grade's peak, so it reaches the bloom:
  each facet its own brightness, throbbing with the beat wave, a shard's fresh
  faces twice as bright. It is 0 unless a tune sets it, so every other run
  looks as before. Monsters, their shards and crystal structures (and so the
  structures' shards) all take it; the monsters only in the crystal, the
  `--look classic` monster look (ps-wnj.13: the other looks have their own
  light).
- **The horde is the look package's** (ps-wnj.22): `--monster-style crystal`
  draws through the package's crowd and GPU shatter, in the applied `--look`
  preset's monster look ([crowd.md](crowd.md#monster-styles), The crystal cut
  on the look package), so every monster look built in the package shows here.
  Its shards are the game's GPU chunks rather than PhysX bodies, and every
  death shatters.
- **Post.** The style's post keys darken the forest's reference look (exposure
  0.5 against the style's 0.8), deepen contrast and saturation, and lower the
  bloom threshold to 1.0 at intensity 2.0, so the gems bloom and the ground
  doesn't. The style post fix moves every environment by the same offsets.
- **No per-shot grades and no structures** since the void staging: the
  grades balanced the environments, and the void's open ground has no walls.
- **Formations** are their own warm white-gold (`CrystalFormations.Gem`,
  #ffe7b0), lit from inside past the bloom's knee (gain 0.55, peak 0.35, glow
  1.6): the horde's gems span every hue, so a formation stands out by being
  the largest and brightest solid thing on screen, not by its hue.

### Crystal formations

The operator's review of the first cut (epic ps-b5z, note 1): shattering is
"a core thing we are showing off", and it was invisible. Six low ruin-wall
runs broke to a distant bolt into knee-high piles among a horde of the same
blues. The cut now stages its breaks round **crystal formations**: clusters
of glowing spires as tall as a house, generated in code, standing on the
hero's path, that the hero's blows smash.

A shot's `crystals` field places them:

```json
"crystals": [ { "at": [22.0, 22.4] }, { "at": [30.5, 36.2], "radiusM": 2.2, "heightM": 10 } ]
```

- **`at`**: a named area or metres, on the shot's open ground (its layout's). One
  standing in a structure, or off the arena, fails at load and names it.
- **`radiusM`** (default 2.2, 0.8 to 2.5): the footprint's radius.
- **`heightM`** (default 10, 2 to 16): its tallest spire.
- Only a horde shot (one with a formation) takes them.

**In the simulation** (the level script's `crystals`,
[choreography.md, Shot fields](choreography.md#shot-fields)):

- **They block.** The shot runs on its layout (the void's) with a round
  blocking footprint for each formation after the layout's own placements
  (`GroundLayout.WithCrystals`). The hero's path routes round them, the horde
  parts round them, and refill never spawns on them.
- **The hero's blow smashes them.** The first hero blow whose hit shape
  reaches a formation (a cleave's or slam's ring at the shot's `shapeScale`,
  past its edge) breaks it on that blow's impact: a strong beat, the
  fight's own beat. Its break event has no projectile, from where the hero
  stands to the formation's nearest point (`Sim.SmashCrystals`).
- **So do the dragon's attacks.** In a boss fight, an attack whose zone
  reaches a formation smashes it on its impact, from the zone's origin (the
  dragon's head or body): the crystal cut's climax stands two in the horde
  ring, 14.5 m out from the dragon's ground and beyond the hero's reach,
  where its two breaths reach them.
- **The ground opens.** Its footprint leaves the obstacle map as a structure
  break's does, and the fields rebuild over the next beat, so the horde pours
  through where it stood.
- **Placement.** Each stands 20 to 80° off the hero's line at one of its
  blows, as far out as that blow's ring just reaches (the reach plus the
  radius, less 0.8 m) and beyond every earlier blow's, on the side where the
  frame holds it whole. They break 4 to 8 m from the hero, as monsters die
  at the same reach. On the line itself the hero's path bent round them and
  could miss its blow.
- **Height.** 10 m by default. The closer views (ps-b5z.6) cut a tall
  spire's top off the frame where it stands far up the screen, so shots 5
  and 7 stand theirs 8 m tall and the climax's two 5 m (the fight's 32 m
  view), each still towering over the horde.

**On screen** (the look package's `CrystalFormations`, demo3d's `StructureShatter`):

- **The formation.** A main spire of the formation's height and five to
  seven shorter ones leaning out round it, hexagonal and tapering to off-axis
  points, flat-shaded and sunk 0.4 m into the ground, seeded by its position.
  Each spire is cut at build time into chunky shards: slices of its shaft
  split round the axis into two or three wedges, and its tip, about fifty a
  formation, their cut faces flagged as a baked shard's are. They draw in
  crystal's structure shader, in the formations' gem (under a look with a
  structure look, its featured material in the palette's `shatter.gem`:
  neon-look.md, The Axes > World), and stand from the
  shot's first frame; a cut away hides them, and their shot's next showing
  puts them back whole.
- **The burst, on the impact frame.** On the blow's frame the formation is
  its shards, flashing white, and they fly: the front crosses a formation
  within three frames (120 m/s), each shard kicked out from the hero, away
  along the blow and up (7.5, 6 and 6.5 m/s at full share), spinning,
  under 1.8 g, its fresh inside faces glowing white-gold for 1.4 s as it
  rises and rains down. After 2.4 s they sink into the ground. Nothing
  cracks or glows before the blow.
- **The feel.** The blow weighs 1 whatever it kills (`BlowFeel`): the full
  hit-stop, punch and shake. The moment also takes the largest wall's post
  flash and shake (1.1 and 0.4 m), a lens hit at full strength at the strike
  (`TrailerLook`), crystal chips tinted the formations' gem where the blow
  lands and up the formation's height, a ground shock at its foot, and
  crystal's shatter sound at full gain.

The crystal manner of a structure break changed with it: breaks pay off on
the impact frame (the operator's rule), so a crystal wall's front no longer
creeps at 22 m/s opening glowing seams a moment before the burst. It bursts
at 120 m/s, harder and higher than stone
([environment.md, Breaks on screen](environment.md)).

**The checks** (see [Checks and Evidence](#checks-and-evidence)):

- `breaks on screen`: each formation stands wholly in view (its footprint
  inside the frame) when it breaks, and falls to a hero blow or a boss attack
  landing on that instant.
- `every crystal breaks on screen`: every formation of a shot breaks from its
  cut to its end, none unseen in the pre-roll, none left standing.

### Structure breaks in a shot

A shot's `breaks` field lists `target@bar.beat` entries, with targets named as
in a structure-breaks schedule (choreography.md, Structure breaks): an area's
name, or an area's part such as `courtyard/east-south` or `ruins-gate/1`.
The hero's bolt breaks them, as a schedule's do. The first crystal cut broke
six wall runs this way; its redesign replaced them with crystal formations,
so no checked-in cut uses the field now.

- **Parse** (`Trailer.ValidateBreaks`): every entry needs a bar.beat on the
  grid within the shot's visible span, from its cut to its end, and a target
  the shot's environment has. Anything else fails, naming the entry.
- **The shot's script** puts each entry in its song section's level-script
  section, and the simulation schedules a bar.beat break on that grid point
  (`Sim.ScheduleBreaks`). The bolt, the changing obstacle map and the room
  check are a schedule's. The room check is per environment: a wall run's
  neighbouring kit pieces differ, so a run that breaks in one environment may
  leave too narrow an opening in another. Among the dark environments the
  crypt breaks every courtyard and ruins-gate run, the forest every one but
  the courtyard's west-south, neon only the courtyard's south-west,
  north-east and west-north and both ruins-gate runs, and lava none.
- **On screen.** Each shot with breaks or crystal formations gets its own
  `BreakMoments` and `StructureShatter` on its own environment, with the
  moments' bursts, sounds, shakes and flashes in the shot's effect driver,
  cues, camera feel and post feel. A cut away from the shot rewinds its
  shatter (`StructureShatter.Rewind`), so the walls stand again for the next
  shot in that environment, whose simulation starts whole.
- **The check.** `breaks on screen` (`TrailerChecks.BreaksOnScreen`): each
  break strikes in view, and its bolt flies at most 14 m.
- **`--structure-breaks`** still conflicts with `--trailer`: a trailer's
  breaks are its shots' own. `--environment` pins the shots and their breaks
  together, so pinning a cut to an environment where one of its walls can't
  break (lava, say) fails at load, naming the wall.

**Outcome.** Every shot passes every check (`trailer --name crystal
--checks`, and `TrailerTests.TheCrystalCutPassesTheTrailersChecksAndSmashesItsCrystals`),
and the action cut's checks are unchanged. The test must pass under both
`dotnet test` and Unity's EditMode run (`demo3d test`): their floating point
differs, so a shot's density can pass on one and fail on the other. Shot 3's
lava converge is the fragile one: with four streams its last bar thinned to
372 against 440 under Unity only, so it runs three (ps-vr7). Stills:
[evidence/trailer/crystal](../../demo3d/evidence/trailer/crystal/README.md);
the formations: [evidence/trailer/shatter](../../demo3d/evidence/trailer/shatter/README.md).

## The Neon Action Cut

`--trailer --look PRESET` plays the action cut on the look package
(`trailer-action-neon.json`, ps-wnj.26): the crystal cut's world, crowd,
shatter, post stack, grid floor, transitions and restraint, on the action
cut's own shots. Plain `--trailer`, with no look flag, still plays the Synty
action cut unchanged, the epic's "before"
([neon-look.md, Decision 11](../design/neon-look.md#decisions)).

```
just demo3d --trailer --look edge --sound
just trailer --out captures/trailer-action-edge.mp4 -- --trailer --look edge
just trailer-looks action          # edge, solid, hologram and prism in turn
```

- **A file of its own.** Any look flag swaps `--trailer`'s default `action`
  for `action-neon` (`DemoMain.UseLook`); `--trailer action-neon` names it
  directly. The void staging changes every shot's ground, path, heading and
  framing, so the neon cut is a second file rather than per-look overrides in
  the first.
- **The action cut's shots.** The same cuts, beats, combos, spells, duel,
  formations (kinds, streams, mixes, on-screen counts), seed and dragon fight
  (breath, sweep, breath) as `trailer-action.json`;
  `TrailerTests.TheNeonActionCutIsTheActionCutInTheVoid` holds it to that
  shot by shot.
- **Re-staged the crystal cut's way** ([The void staging](#the-void-staging)):
  every shot on the void's open ground, hosted by the forest; each turned by
  its old heading into yaw 0 and centred (its path's and boss ground's box)
  on the void, so it frames as it did; zones 0 to 3 each twice. No crystal
  formations: those are the crystal cut's showcase.
- **The camera grammar** ([The camera grammar](#the-camera-grammar)): one
  heading and one framing per shot, the action cut's widths held for the
  whole shot (shot 3's 42→47 m zoom is 47 m throughout, shot 5's 34 m 36 m
  for its density).
- **Its look** is the crystal cut's `look` block and any preset's
  ([The crystal look](#the-crystal-look)).

| # | Trailer s | Bars | Zone (the action cut's place) | View | Formation |
|---|---|---|---|---|---|
| 1 | 0.000–2.481 | 79–80 | 0 (crypt) | 20 m | Duel: 3 elites |
| 2 | 2.481–14.868 | 81–88 | 1 (desert) | 44 m | Charge (560) |
| 3 | 14.868–27.255 | 89–96 | 2 (lava) | 47 m | Converge, 4 streams (390) |
| 4 | 27.255–39.642 | 97–104 | 3 (blossom) | 42 m | Vortex (490) |
| 5 | 39.642–45.836 | 105–108 | 0 (neon) | 36 m | Charge (290) |
| 6 | 45.836–48.932 | 109–110 | 1 (snow) | 34 m | Vortex (290) |
| 7 | 48.932–52.029 | 111–112 | 2 (crypt) | 36 m | Converge, 3 streams (350) |
| 8 | 52.029–59.0 | 113–117.2 | 3 (forest) | 34 m | The boss fight, a vortex round its arena (380) |

**Outcome.** Every shot passes every check (`trailer --name action-neon
--checks`) under dotnet and Unity's EditMode run. Evidence:
[demo3d/evidence/look/action-neon](../../demo3d/evidence/look/action-neon/README.md).

## Command Line

Trailer mode is one flag on the normal player:

```
just demo3d --trailer --sound
just demo3d --trailer --sound --palette neon --monster-style crystal
just demo3d --trailer --sound --roster heroic
```

`--trailer` takes an optional name, which defaults to `action`; `crystal`
plays [the crystal cut](#the-crystal-cut). With any look flag, `action`
plays [the neon action cut](#the-neon-action-cut) (`action-neon`) instead,
and says so in the log. A following argument that starts with `--` is the
next flag, not a name.

These work as in a normal run and apply to every shot:

- **`--palette <name>`:** recolours each environment's moon, ambient light,
  fog and ground. The name of one of the look package's palettes is the
  crystal cut's look palette instead ([neon-look.md](../design/neon-look.md),
  Flags).
- **The look flags** (`--look`, the axis flags, `--beat-ripple`): the crystal
  cut's and the neon action cut's look preset, logged at start (`Look: ...`).
  Any of them turns `--trailer` (the action cut) into the neon action cut.
- **`--roster <name>`:** picks the roster set.
- **`--monster-style <name>`:** picks the horde's style. As today, a roster
  set can't take a style other than `baseline`.
- **`--signature-monster-colors` and `--style-tune`.**
- **`--crowd`, `--aa` and `--sound`:** the crowd renderer, anti-aliasing,
  and sound (a built player starts muted).
- **The post overrides:** `--post-exposure`, `--bloom-threshold` and the
  others.
- **`--no-feel` and `--no-post-feel`.**

These change meaning in trailer mode:

- **`--environment <name>`:** pins every shot to one environment, for checks.
- **`--start-at`, `--stills-at` and `--seconds`:** take trailer seconds (0 to
  60.5), not song seconds. Still times round to the next 60 fps frame.
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

One tool command produces the finished, mixed video. `just trailer` builds
the player first and defaults `--out` to `captures/trailer.mp4`:

```
just trailer --out captures/trailer-neon-crystal.mp4 -- --palette neon --monster-style crystal
uv run --project demo3d/tools demo3d trailer --out captures/trailer.mp4   # no build
just trailer-looks             # the crystal cut in every look preset: captures/trailer-crystal-{edge,solid,hologram,prism}.mp4
just trailer-looks action --stand-in   # the neon action cut's cues and mix, no window
```

`just trailer-looks [CUT]` takes `demo3d trailer`'s flags (`--stand-in` too)
and player flags after `--`, for every preset
([neon-look.md, Flags](../design/neon-look.md#flags)).

It runs three steps:

1. **Render the video.** It launches the windowed player with `--trailer`,
   the given player flags, `--fixed-time`, `--capture-video` and
   `--dump-sounds` (and `--ffmpeg` with ffmpeg's full path, which IL2CPP
   needs on the Mac). The player renders each frame at 2× and downsamples it:
   `--supersample`'s URP render scale draws 3840×2160 into the capture's
   1920×1080 target, a 2×2 box per pixel. The result is a silent 1920×1080
   60 fps H.264 file.
2. **Write the cue list.** Trailer mode's `--dump-sounds` writes exactly the
   cues the live run would play, in song seconds, with its fades:
   - `trailerStart`, `start` and `end`: trailer time 0, the run's first frame
     (the video's) and its end;
   - `cutFadeS` (0.06), and on each cue that rings into the next shot,
     `fadeAt`, the cut it fades out from;
   - `fadeOut`: the cut to black (`at`) and the fade's `seconds` (1.5), which
     take the music and every sound down together.
3. **Mix and mux.** It mixes the song window from `start` under those cues
   with the existing mixer (`demo3d mix`), applies the fades as the live player
   does, and muxes the audio into the video. Then it checks the mix: the peak
   stays under the limiter's ceiling, every cue's clip was found, nothing from
   before a cut sounds 60 ms after it, and the end fade falls to silence.

The picture, the cue list and the player's log stay beside the output, as
`FILE.silent.mp4`, `FILE.sounds.json` and `FILE.player.log`, so the mix can be
redone with `demo3d mix` alone.

While the player renders, the command prints its progress from the log (the
player logs every 60 frames). A player whose log stops growing for 5 minutes
is stuck, so the command stops it and fails. A capture that can't start
ffmpeg or write a frame quits the player with exit 1.

The capture needs a windowed player. Agents never open player windows, so
recording it is a task for the **operator**, the developer who runs the demo
and reviews its evidence. Agents check the trailer with windowless stills and
frame sequences instead, and the mix with `--stand-in`, which runs every step
but the window: a batch-mode player dumps the same cues, and a black video of
the capture's length stands in for the picture.

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
- liveness violations: monster beat intervals that break the monster rule;
- blocks: the type blocks' coherence, the mean share of a visible monster's
  neighbours within 3 m that share its type (a random mix of the shots'
  scores about 25%, pure blocks 100%);
- lane: the narrowest the hero's open lane gets, in degrees;
- breaks: the structure and crystal formation breaks on screen.

These checks fail the run, naming the shot, the time and the monster:

- **Cuts on the grid.** Every cut lands exactly on a beat.
- **Opening blows.** Every shot after the first has a hero impact on its cut
  beat. Shot 1 has one on anchor 1.
- **Impacts on the grid.** Every impact lands on a grid position, or on an
  anchor in break-2.
- **Liveness.** The monster and hero rules in [Motion](#motion) hold in every
  beat interval of every visible span. Break-2's intervals are its silent
  beats.
- **Type blocks.** In every horde shot, up to any final slam, the blocks
  score at least 60%: on every beat each visible monster scores the share of
  the visible monsters within 3 m that share its type, and the shot's mean
  holds.
- **Blows on the major beats.** Every hero impact of the visible span lands
  on a strong beat, an accent, the cut or an anchor, and in a pulsed section
  at least two beats after the last.
- **An open lane.** On every beat of the visible span, up to any final slam,
  the bodies of the monsters within 6 m of the hero leave a gap round them
  at least 60° wide.
- **One hit kills.** No monster takes a hit and survives, but the fight's
  boss.
- **The boss.** It dies to the final slam on 117.1 and to nothing before.
  Without a fight that is the only hit it takes; in a fight it takes at
  least three of the hero's blows on screen first, and no spell's tick.
- **The boss fight.** In a shot with a `bossFight` the dragon lands every
  attack, at least two, each telegraphed and dodged: its windup on a beat
  and its blow, the dragon's attack clip, on a strong beat or an accent at
  least two beats later; the hero inside the zone as it winds up and still
  as the dodge begins; the dodge ending at most 0.15 s before the blow; the
  hero's body out of the zone as it lands; the hero, the dragon and the
  zone's origin on screen then. The dragon is on screen on every beat from
  the cut to the final slam. A shot without a fight has no attacks.
- **The final slam.** It kills every monster in view (one that left the run
  off screen before it isn't there to survive it).
- **Breaks on screen.** Every structure break strikes in view; every crystal
  formation stands wholly in view as the hero's blow (or the dragon's
  attack) smashes it.
- **Every crystal breaks on screen.** Each shot's formations all break from
  its cut to its end (see [Crystal formations](#crystal-formations)).
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
  Since every shot opens on a blow landing on its cut frame, a trailer
  cut frame carries that blow's cost too (up to about 14 ms in the re-cut);
  the switch is judged against the same frame drawn with no cut
  (`--shot N --from-preroll`), as in
  [the re-cut's timing](../../demo3d/evidence/trailer/recut/timing.md).

Visual evidence comes from windowless, fixed-time player runs:

- a still at every shot's first frame and mid-shot;
- frame sequences across the flash cuts and the final slam;
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
- **The cuts.** Eight shots, every cut on the beat and every shot opening on
  a blow, five of them under a flash.
- **Pacing.** The three 8-bar shots after the drop should breathe, not drag:
  judge whether 12.4 s of one place and horde holds the eye, especially the
  lava converge, and whether final-peak's 4, 2 and 2-bar shots then build
  into the climax. Judge too whether about a minute is the right length.
- **Motion.** No monster frozen anywhere, the hero acting on every beat, and
  the horde chasing and streaming.
- **The horde in blocks.** Waves of one monster type arrive together as
  coloured masses, never confetti; a charge reads as a patchwork, a
  converge's streams change colour as they pour in. Under the default
  palette skeletons, goblins and bats share tan tones, so judge whether the
  blocks read enough there, and compare `--signature-monster-colors`.
- **Blows with weight.** The hero's blows land on the music's strong beats,
  each with a visible windup and follow-through, and the hero runs between
  them. Nothing reads as flailing. Judge whether the pace (about one blow
  every two beats) still feels energetic enough for a trailer.
- **Never surrounded.** The horde closes from the front and the flanks and
  trails behind in a wake; the hero always has an open lane and never walks
  through a monster. Judge whether the monsters stepping aside from the
  hero's path reads as natural, not as a force field.
- **The new environments.** Shots 3, 4 and 5 are lava, blossom and neon,
  two of them the trailer's long middle. Judge whether their grades (warm
  and contrasty on the lava, soft on the blossom's pale stone, saturated and
  cool in the neon city) sit well beside the original four, and whether the horde's blocks read against the lava's
  glowing cracks and the neon's signs.
- **The end.** The final slam clears the screen, then two beats of aftermath,
  black and a 1.5 s fade.
- **Picture.** No text and no UI at any point.
- **Smoothness.** No hitch at any cut.

**Flags.** Run the live trailer, or windowless stills at 4.0 (desert), 20.0
(lava), 33.0 (blossom), 42.0 (neon), 47.0 (snow), 50.5 (crypt) and 57.5 (the
climax, before the final slam), with each of these:

- every `--palette`;
- `--monster-style crystal`, `mix` and `painted`;
- `--roster forest` and `--roster heroic`;
- `--signature-monster-colors`.

Each should play the same shots in its own look. The snow and desert shots
should keep their own exposure under a non-baseline style.

**Edge cases.** Each of these should behave as described:

- **`--environment snow`.** Every shot plays in snow, with paths intact.
- **`--intensity overdrive`.** Fails at start and names the conflict.
- **`--roster heroic --monster-style crystal`.** Fails at start, as in a
  normal run.
- **`--start-at 52.1`.** Opens just after the climax's cut, with the horde
  already pouring in.
- **`--shot 4 --from-preroll`.** Shows the blossom vortex forming before its
  cut.
- **`--overlay`.** Live mode shows the shot number and zero liveness
  violations. A capture with it warns and leaves it out.
- **A trailer file naming an unknown environment.** Fails at load and names
  the bad value.

**Capture.** This is an operator task:

```
just trailer
```

Expect a 60.5 s, 1920×1080, 60 fps video with mixed audio. It should match
the live run: the same shots, cuts and sounds, with no dropped frames.

**Debug surfaces.**

- **`trailer --checks`** in the choreography's command-line tool. It prints
  the per-shot table and every failed rule, with the shot, time and monster.
- **`--shot` and `--from-preroll`.** They isolate one shot, with or without
  its pre-roll.
- **`--overlay` in live mode.** It shows the shot number and liveness
  violations as they happen.

**The crystal cut.** `just demo3d --trailer crystal --sound`, then
`just trailer --out captures/trailer-crystal-cut.mp4 -- --trailer crystal`.
Judge:

- **The glow.** The gems should read as lit from inside against dark ground,
  their facets still visible, not as flat neon silhouettes; the effects should
  stay the brightest thing on screen.
- **The formations.** Each of the fifteen break moments (the shot list's
  table) should read as the hero's blow smashing a towering white-gold
  formation, bursting on the beat with no build-up, its shards raining down
  and the horde pouring through where it stood. Judge whether one every
  three to five seconds is the centrepiece without wearing thin.
- **Darkness.** The crypt's two shots should read as dark as the others.

**The neon action cut.** `just trailer-looks action` (or `just demo3d
--trailer --look edge --sound`, then `solid`, `hologram`, `prism`). Judge:

- **The same trailer.** Beside `captures/trailer.mp4` (the plain action cut),
  every shot should read as the same moment: the duel, the drop's charge, the
  lava streams, the vortices, the dragon's breath, sweep and breath, the final
  slam on 117.1.
- **One world.** No shot should show the void's edge or a hole in the horde;
  the cuts flash in the incoming zone's colour, the soft ones only wipe.
- **Restraint.** The hero reads first, the horde second, the grid last; only
  the drop's cuts, the shatters and the slams flash.
