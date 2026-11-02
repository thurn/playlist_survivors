# A press's blow at once, live (ps-w9p.10)

A free-mode press ([the design](../../../docs/archive/design/game-modes.md#attacking-on-the-beat)) lands its blow 1/30 s after
its key. Live, the key reaches the sim about the lead after it was heard (the output latency plus the 52 ms margin:
about 90 ms on the built-in speakers, 0.3 s on Bluetooth), past that impact. Before this bead the blow then landed a
tick after the sim took the key, so the strike was seen and heard a lead late: on the operator's play-test (speakers),
swings felt "barely connected to the button press". Now the press resolves in heard time as the sim takes it, against
the world as it was ([The sim lead](../../../docs/archive/design/unity-game.md#the-sim-lead)), and its sounds, already past
the mixer, start as soon as it can ([Sound timing](../../../docs/archive/design/unity-game.md#sound-timing)).

## How it was measured

`LoopTests.AFreePressIsSeenAndHeardAtOnceWhateverTheLead`, an EditMode test, drives the client's real frame loop
(`SimDriver`, `MusicTimes` on a `MusicClock`, `SoundPresenter` and `SoundScheduler`) in simulated real time: 60 fps
with ±2 ms of jitter, the DSP clock in 256-frame buffers, a free-mode sim with the sword, and Z going down every 0.4 s
at 24 phases across a frame, each taken at the next frame's start as `KeyInput` takes keys. **Seen**: the first frame
whose frameSong reaches the impact, a display latency (a frame) after it starts. **Heard**: the swing's attack, a DSP
time, leaving the speakers the output latency after the mixer plays it. The same test was run on the tree before the
bead (8b1495d6, the test alone added). On jarvis, from the worktree:

```sh
uv run --project demo3d/tools game setup
uv run --project demo3d/tools game test   # the numbers: grep "output latency" game/Logs/editmode-results.xml
```

## What it shows

Key down to the blow seen and to its swing heard, over 24 presses (min / median / max, ms):

| Output | Lead | Seen before | Seen after | Heard before | Heard after |
|---|---|---|---|---|---|
| Built-in speakers, 38 ms | 90 | 82.3 / 92.2 / 100.9 | 35.0 / 40.5 / 50.5 | 79.7 / 88.7 / 97.0 | 44.1 / 55.9 / 64.7 |
| Bluetooth-like, 250 ms | 302 | 301.7 / 309.8 / 317.3 | 35.0 / 40.5 / 50.5 | 291.7 / 300.7 / 309.0 | 256.1 / 267.9 / 276.7 |

- **Seen** is now 1/30 s plus where in its frame the key fell, whatever the output: the frame after the key settles
  the press while the screen is a display latency past the key, so the blow is on that frame or the next. Before it
  was the lead.
- **Heard** is now the output latency plus at most a frame (the key waits for the next frame) and two DSP buffers
  (the mixer's earliest start): the margin no longer sits in front of the sound. On Bluetooth the headphones' own
  quarter second stays; nothing the game schedules can start before the mixer.
- No swing was dropped: every press's sound started late as soon as the mixer could (`SoundScheduler.Late`).

The sim's side is tested in .NET (`FreeModeTests`): a press the sim takes 90 ms or 302 ms late settles its swing, blow
and kill stamped at its impact as the sim takes it; it strikes from where the walking hero was at the impact (an
enemy it has walked away from is hit, one it has walked up to is not); and an enemy killed mid-step dies where it was
on its step. The free-mode session replay (`SessionTests.AFreeModeSessionReplaysToItsHash`, keys heard 90 ms before
the frontier) replays to its hash, and a grid-mode run's events hash as before the bead. The crowd's side
(`CrowdTests`): the hit shows from its time and its knock replaces the step the sim had settled after it.

How it feels by hand, on the speakers and on Bluetooth, is on [the operator's list](../operator-pending.md).
