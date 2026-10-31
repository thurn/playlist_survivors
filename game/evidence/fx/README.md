# Effects and sound (ps-msz.3.11)

The sword's slashes and crystal waves, the crystal death bursts and the DSP-scheduled mix
([the design](../../../docs/design/unity-game.md): Weapon and Ability Effects, Sound Timing,
Audio). The code is game/Assets/Game/Client/Effects/ (demo3d's compute particle runtime, copied
and owned, with `EffectCues`, `EffectsPresenter` and `CrystalWaves`) and Client/Sound/
(`SoundScheduler`, `SoundAggregator`, `MusicalGrid`, `SoundPresenter`); the bakes are
Editor/Effects/EffectConverter (game/Content/effects.json) and Editor/Sound/SoundBaker
(game/Content/sounds.json), both into the gitignored Assets/Baked/.

The sim doesn't wire the sword and the horde into the client yet (ps-msz.3.13), so these runs play
synthetic feeds shaped as the sim's events, written by [feeds.py](feeds.py) on house-128's beats:
[slice.jsonl](slice.jsonl) (slashes on the beats with hits on five of 40 enemies, a heavy spin on
the accent at 7.53 s killing all 40, its cleave wave through 30 more, a slam at 10.34 s) and
[sounds.jsonl](sounds.jsonl) (the sync probe's feed). The strips show the crystal horde (ps-msz.3.8;
the feed's roles 0 to 3 are bats) and the knight (ps-msz.3.10); a death leaves no shards yet
(ps-msz.3.9), so the bursts are the shatter's companions, and the role tints wait for the slice's
content (ps-msz.3.13): every burst is ice blue.

## How it was run

On the Mac (jarvis), the Mono player, from the worktree's root:

```sh
python3 game/evidence/fx/feeds.py
S="$PWD/public/reactive/house-128/house-128.ogg"; P="--file $S --events $PWD/game/evidence/fx/slice.jsonl"
uv run --project demo3d/tools game look slash --backend mono --at 3.28 --frames 12 --strip 6 --evidence fx --player-args "$P"
uv run --project demo3d/tools game look deaths --backend mono --at 7.50 --frames 12 --strip 6 --evidence fx --player-args "$P"
uv run --project demo3d/tools game look cleave --backend mono --at 7.53,7.6,7.7,7.8,7.9,8.0,8.1,8.2,8.3,8.4,8.5,8.6 --strip 6 --evidence fx --player-args "$P"
uv run --project demo3d/tools game spike sounds-house-128 --file public/reactive/house-128/house-128.ogg --sounds --seconds 21 --backend mono --evidence fx --player-args "--events $PWD/game/evidence/fx/sounds.jsonl"
```

## Sounds land on their samples

[sounds-house-128.json](sounds-house-128.json): `game spike --sounds` runs the game's frame loop
headless in real time with `--sound-probe`, so every sound the events schedule (swings and hits on
the beats, a death's cry and its crystal shatter's three layers, hurts snapped to the sixteenth,
gem chimes on the grid) plays the spike's click from its first sample instead of its clip, through
the same aggregation and scheduler. The spike's in-process mix capture then finds each click in
Unity's final mix against the music, cross-correlated with the source:

| measure | n | median | p5 | p95 | min | max |
|---|---|---|---|---|---|---|
| music to sound (ms) | 108 | +0.000 | -0.021 | +0.000 | -0.021 | +0.000 |
| sound to its scheduled DSP time (ms) | 108 | -0.000 | -0.021 | +0.000 | -0.021 | +0.000 |

All 108 sounds scheduled were found, on their song time's sample (one sample is 0.021 ms at 48 kHz),
none dropped and none late; no stalls, no capture gaps, 256 x 4 DSP buffers, output latency
38.0 ms. The lead stayed 90.6 to 93.2 ms (output latency + margin), so every event settles in time
to be scheduled. A plain headless run of the slice feed with the real clips (`--headless --seconds
30`) scheduled 86 sounds and dropped none. A headless player has no compute, so the crowd and
the effects sit those runs out (the crowd's CrowdView threw there and stopped the loop's setup).

## Filmstrips

- [slash.jpg](slash.jpg), 12 consecutive frames from 3.283 s (the beat at 3.30 s): the white
  slash (Hovl's Sword Slash 2 through an ice-to-white heat ramp, sized to the 7.04 m reach) appears
  on the impact frame with its cool light cue, and the crystal wave rings out from the hero to the
  reach over 0.12 s (7 frames), its white front exactly at the radius the sim hits, then fades.
- [deaths.jpg](deaths.jpg), 12 consecutive frames from 7.500 s (the heavy spin at 7.53 s): on the
  7.533 frame all 40 bats go at once, each in one coherent burst of crystal chips and dust thrown
  outward along its blow and fading within 0.7 s, nothing blinking; the 40 share their light
  (each 1/sqrt(40)), so the frame flashes without washing out. The spin's slash, oversized for the
  heavy strike, sweeps the 9.15 m reach while its ring reaches it.
- [cleave.jpg](cleave.jpg), 7.53 to 8.6 s every 0.1 s: the cleave wave picks up where the heavy
  strike's ring stops, at 9.15 m, and follows the seven bands out to 15.07 m on their sixteenths,
  the outer bats bursting as their band passes, then fades by 8.6 s.

The EditMode tests (`EffectsSoundTests`) check the rest without a GPU or speakers: effects wait for
the frame that reaches their song time (a slash its impact, not its wind-up; a cleave band's hit its
own frame), an instant's deaths share their light, a killing hit shows no flash, the waves' front is
exactly the strike's and the cleave bands' radius, slots on the beat and the sixteenth, the chimes'
pentatonic climb, a sound the mixer can't make dropped rather than started late, a clip's onset
started early so its attack lands on time, at most three voices per beat and category with the gain
growing, and the events' sounds at their times.

Not checked here (on [the operator's list](../operator-pending.md)): how the mix sounds, the
effects' level under the music with the follower, and the clips' choice.
