# The effect schedule leaves Unity (ps-tfn.4.3, 2026-10-30)

`EffectLayer` held two things: the CFXR pool, and the engine-free schedule that decides which
effects are live, where they are, how they turn and how bright their lights are. The schedule is
now `Demo2D.Choreography.EffectSchedule`, so `dotnet test` runs it. What changed:

- **No more hand-rolled union.** Each live effect is an `EventEffect` or a `FlightEffect`.
  `Yaw` and `BeamAlpha` take an `EventEffect`, so the compiler stops a flight from asking for a
  yaw. The pool keys instances by the effect object, not by a signed index. `LiveEffect.Order` is
  only the draw order and the light pool's tie-break. It is the same as the old key: flights
  first, newest first, then events in order.
- **The beam's sweep comes from the parsed `Roster`.** It uses `Move.Bars` as a float, as the sim
  does. `EffectLibrary.SweepSeconds` and its second parse of `roster.json` are gone. The schedule
  throws if two line spells share an effect with different bars, or if a sweeping entry has no
  line spell.

Every run was on the operator's Mac, windowless, in the bead's worktree. "Before" is the base
`3f30c8da`, built and captured in the same worktree before the change.

## Old against new, frame by frame

A scratch harness ported the old `Gather`, `Position`, `Yaw`, sweep and beam fade, with
`UnityEngine.Vector2` swapped for `System.Numerics`. It ran them against the new schedule on
every 1/60 s frame of the song, with the camera's view and a varied punch. At each frame it
compared:

- the live set and its order;
- the keys, seeds and scales;
- the position, yaw and beam alpha at every 1/30 s step from birth (what a still simulates);
- the light intensity and radius.

| Frames | Live effects | Differing |
|---|---|---|
| 11,999 | 311,701 | 0 |

## Tests

- `dotnet test demo2d/dotnet/Demo2D.Choreography.Tests`: 40 passed. The new `EffectScheduleTests`:
  - **The beam passes over every monster it ticks.** At each of the 152 ticks, the newest beam is
    live and whole. Its yaw puts the monster ahead of the hero and within the widest line's
    half-width, inflated as the sim's query inflates it. The farthest monster is 0.71 m from
    the axis. Stretching the sweep by 5 % fails the test.
  - **No kind exceeds its max** in the camera's view, sampled every 1/30 s. hit-spark (48),
    death-burst (16), parry-spark (12) and bolt-launch (16) reach their caps.
  - **Only an event has a yaw.**
- `demo2d test` (EditMode): 97 of 97 passed. This includes `PlaybackTests`' seek still, run after
  the build's BakePlayback.

## Stills

The IL2CPP player was built before and after the change. Each build captured `--effect-stills`
and `--stills-at 3,60,116,134,172.5` twice.

**The base player isn't byte-stable from run to run.** Ten of the 16 stills differ between two
runs of the same build, by a mean of up to 0.82 per channel. These are the ones where particles
or the beam are on screen. So "byte-identical" can only hold for the other six:

- **Byte-identical, old against new:** 63.257, 73.708, 92.676 and 99.108 s (effect stills), and
  3 and 134 s (night).
- **The other ten:** old against new differs by a mean of 0.000 to 0.383 per channel, with at
  most 0.34 % of pixels off by over 24. Old against old differs by 0.006 to 0.822, and new against
  new by 0.003 to 0.707. Every pair is within the same range.

`effect-stills.txt` picks the same events before and after. No still is committed here, since
they match the base.
