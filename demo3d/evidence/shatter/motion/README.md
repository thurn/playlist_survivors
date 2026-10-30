# Crystal shatter: cracks, the release and the shards' motion (ps-2iq.2)

The break itself, tuned on filmstrips at the showcase preset (epic ps-2iq; the foundation is in
[../foundation](../foundation/README.md), the design in
[crowd.md](../../../../docs/tech-demo-3d/crowd.md#monster-styles)). Every strip was rendered by the
Mac's batch-mode IL2CPP player in fixed time, 2026-10-30, every 2nd 1/60 s frame (each row is
0.2 s), without the effect library so the shards read on their own.

| file | what | command (`demo3d filmstrip ...`) |
|---|---|---|
| [skeleton.jpg](skeleton.jpg) | fodder: a lone skeleton from its hold (41.233) through the release (41.617) to its settled pile, 2 s | `skeleton --at 41.2 --frames 60 --every 2 --columns 6 --scale 0.8 --crop 760,0,400,300 --player-args "--monster-style crystal --envelope 24 --view-width 12 --no-effects"` |
| [troll.jpg](troll.jpg) | an elite: a troll's hold (163.733) and release (164.117), among other deaths | `troll --at 163.7 --frames 60 --every 2 --columns 6 --scale 0.8 --crop 1090,300,640,540 --player-args "<the same>"` |
| [crowd.jpg](crowd.jpg) | a crowded beat: the song's last bars at a 48 cap, seven skeletons released on 197.25 and seven goblins on 197.483 | `crowd --at 196.85 --frames 48 --every 2 --columns 4 --scale 1.25 --crop 0,140,1920,940 --player-args "--monster-style crystal --envelope 48 --view-width 12 --no-effects"` |

## What happens

- **Cracks over the hold.** Seams glow along the cuts between the baked chunks, and hairlines on a
  fifth of the edges inside each chunk (at 0.3 of the brightness), spreading from where the blow
  landed: the pose's edge toward the hero. They cover the body by 80% of the hold at a steady
  pace, thin (2 mm) at the front and 8 mm behind it, and their heat climbs to the beat. From 55%
  of the hold the shards swell 3% apart and tremble (1.2% of the height at the beat).
- **The release on the beat.** The shards pop a few millimetres apart, the inside faces open and
  the cracks flare, and they hold for 4 frames (hit-stop on the dying monster only: its
  neighbours and the hero keep moving), then burst.
- **Impulse.** Outward from the hit point (harder near it), along the blow, and up. A quarter of
  the shards slump (0.1 to 0.35 of the kick), one in eight flies far (1.8 to 2.7), the rest 0.55
  to 1.15; the largest chunks get 0.55 of the smallest's share. Spin scales with the share and
  inversely with size, so slivers whirl and big chunks turn over slowly.
- **Weight.** Gravity 2.2 g, bounciness 0.35, friction 0.4 dynamic and 0.7 static: shards clack
  down, bounce once or twice, skitter a little and stop. The cracks' heat cools over about half a
  second of flight, so facets flash white as they tumble, then the pile settles to the gem colour.
- **Debris.** 5 s on the floor, then a 1.2 s sink.

## Tuning, and why

- **Most death poses lie on the floor.** The first pass kicked and swelled from the standing
  body's centre (0.9 m up for a skeleton), which pushed a lying body's shards down: on the pop
  the shell sank under the floor and vanished for the hit-stop's frames. The swell, the hit point
  and the kick now work from the held pose's own centre (the shards' centres by volume), and
  never point into the floor.
- **Cracks: cuts alone were too sparse.** Many bodies are separate shells (armour over a body), so
  most cuts share no edge; edges with both ends within 1.5 cm of another chunk count as cuts too,
  but the torso still showed only a few seams. Hairlines inside the chunks (first a quarter of the
  edges at 0.35, then a fifth at 0.3, which was less busy) give the crazing the Superhot look.
- **Crack spread.** An ease-out spread covered the body in the hold's first fifth, so the build
  didn't read; a steady spread over 80% of it shows the cracks racing across in about a dozen
  frames.
- **The flare.** Inside faces at 0.6 of the heat and a flare of 3.5 washed the whole body white on
  the pop; 0.3 and 2.5 keep the flash and the shapes.
- **The kick.** The foundation's 1.0 / 1.3 / 1.6 m/s, with doubled gravity, left a pile in the
  body's outline; 2.4 / 3.0 / 3.0 m/s (outward / blow / up) and the shares above scatter it into a
  field around the death, with a few slivers skidding a metre or two. Big chunks flying as far as
  slivers looked weightless, hence the size factor.
- **Linger.** 2.5 s gave the pile no time to read before it sank; 5 s holds it about three bars.
  At the song's own density the floor carpets with shards (`--monster-style crystal --view-width
  12` around 113 s), but scale is the next epic's.

## Checks

- **Deterministic.** `skeleton-repeat ... --check skeleton`: 60 of 60 frames match pixel for pixel.
- **The full song** at its own density, `--monster-style crystal`, windowless in fixed time: no
  errors, 2,336 deaths, at most 12,051 shards live (11,609 with the foundation's 2.5 s linger), in
  63 s of wall time for 200 s of song (39 s before).
- Styles other than crystal are untouched: the shatter, its gravity and its shards exist only
  when a type is drawn by Crystal.
