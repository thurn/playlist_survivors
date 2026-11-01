# The dash (ps-zz4)

`--mode zen --dash` ([the design](../../../docs/design/game-modes.md#the-dash)): Space on the beat is a long lunge that
strikes what it passes and makes the sword's next beat strike a spin; off the beat a short hop; the hero can't be hurt
through either. The questions: does a press on the beat lunge and one off it hop, does the spin follow on the next beat,
and does the dash show at once, on the key-down frame, whatever the lead?

## In the player

In the player (the Mono build, fixed time, windowless), house-128 on the default level with a scripted walk (up from
8 s), Space at 9.40 s (on the 9.40 downbeat) and at 10.10 s (between the 9.87 and 10.34 beats):

```sh
uv run --project demo3d/tools game look zen-dash --backend mono --build --at 9.38,9.42,9.46,9.50,9.56,9.88,10.10,10.14,10.18,10.24 \
  --strip 5 --evidence zen-dash --player-args "--mode zen --dash --file $PWD/public/reactive/house-128/house-128.ogg \
  --walk right@6-8,up@8-11,space@9.40-9.45,space@10.10-10.15,left@11-12 --view-width 14 --events-out /tmp/dash-events.csv"
```

Its events (`--events-out`, 2026-11-01, this change):

| Space heard | Event | Dash | From | Then |
|---|---|---|---|---|
| 9.400 (on the beat) | `HeroDash` on beat, 9.40 to 9.55 s, up | the lunge, 4.5 m in 0.15 s | (11.53, 6.75) | the next beat strike (wound up 9.635, impact 9.87) is a heavy `Spin`; the one after (10.34) a plain slash |
| 10.100 (off it) | `HeroDash` off beat, 10.10 to 10.20 s, up | the hop, 1.5 m in 0.1 s | (11.53, 14.76): the lunge's 4.5 m plus 0.55 s of walk | no spin: 10.34 slashes as ever |

- **The spin is the dash's**: the same run without `--dash` strikes the 9.87 beat with a plain right slash (kind 1, not
  heavy).
- **The lunge struck nothing here**, and nothing in three more tries (`--run`, and `--start-minute 10` and `20` with
  `--no-cards`): in zen the sword's beat strikes reach 7-8.8 m and kill or throw the crowd before any of it comes within
  the lunge's 1.2 m, and a 4.5 m lunge from the middle of that cleared ring doesn't reach its edge. Its blows (a heavy
  strike's, thrown aside) are covered by the sim tests; whether they ever matter in play is on the design's open
  questions. (On `--run` the 10.10 press answered a level-up's card instead, as the cards take Space while open.)
- **[zen-dash.jpg](zen-dash.jpg)**, 9.38 to 10.24 s: the knight before the press (9.38), then the lunge carrying it from
  the bottom of the view to the top in 0.14 s, faster than the camera follows (9.42 to 9.56), skimming with its blade
  alight under the beat's loud follow-up slash; the spin on the next beat (9.88), its wide gold ring; then the hop at
  10.10 (10.14, 10.18), a tile up under the beat's slash, and the walk again (10.24).

## Key to dash seen, live

`LoopTests.ADashIsSeenOnTheFrameAfterItsKeyWhateverTheLead`, an EditMode test beside the press's
([press-latency](../press-latency/README.md)): the client's real frame loop in simulated real time (60 fps with ±2 ms
of jitter, the DSP clock in 256-frame buffers), a zen sim with `--dash` and no music (every dash the lunge), Space every
0.4 s at 24 phases across a frame, each taken at the next frame's start as `KeyInput` takes keys. The drawn hero is
`HeroMotion` evaluated each frame from the events that frame had. **Seen**: the first frame whose pose has left the
dash's start reaches the eye a display latency after it starts.

```sh
uv run --project demo3d/tools game setup
uv run --project demo3d/tools game test   # the numbers: grep "key to the dash" game/Logs/editmode-results.xml
```

| Output | Lead | Key to the dash seen (min / median / max) |
|---|---|---|
| Built-in speakers, 38 ms | 90 ms | 16.7 / 25.5 / 35.5 ms |
| Bluetooth-like, 250 ms | 302 ms | 16.7 / 25.5 / 35.5 ms |

- The same whatever the lead: the sim starts the dash when the key was heard and walks the hero's path again from
  there, so the frame after the key already draws it moving: at most a frame of waiting for the next frame and the
  display latency. A press's blow is 1/30 s later than that (35-50 ms), its impact two frames after its key.

## In the sim

[DashTests](../../Assets/Game/Tests/Sim/DashTests.cs): on the beat the hero lunges 4.5 m along the held arrow,
striking the enemy on its path once (a heavy strike's damage) and throwing it square to the path, not the one beside
it, and the next beat strike spins, once; off the beat (on the half beat) it hops 1.5 m, striking nothing, no spin; no
reliable beat, every dash is the lunge; the direction (target, then the walk, then the facing); both can't be hurt
through them and 0.1 s after; a wall and the arena's edge stop them; mashing at 120, 170 and 70 BPM dashes no faster
than the strike pulse (less twice the window) and the last press still dashes; two presses each on its beat both lunge;
paused or fallen, no dash, and a pause drops a waiting press; a key taken 90 or 302 ms late dashes from when it was
heard, the same path and blows as taken at once. With `--finishers` too, Space in a ring's window is the finisher and
not a dash ([FinisherTests](../../Assets/Game/Tests/Sim/FinisherTests.cs)); a zen dash session replays to its hash
([SessionTests](../../Assets/Game/Tests/Sim/SessionTests.cs)), and a zen run dashing on and off the beat is causal
([ZenModeTests](../../Assets/Game/Tests/Sim/ZenModeTests.cs)).

How it feels by hand is on [the operator's list](../operator-pending.md).
