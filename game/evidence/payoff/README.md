# The payoff tuned: the mass kill's sound, the finale's hits, shatter and breaks at the peak (ps-msz.8.6)

On jarvis (M-series Mac), 2026-10-31, IL2CPP players built from this bead's tree: "before" with
only the measuring change (per-category counts, fixed-time sound books), "after" with the fixes.

## Sounds dropped and cut off

ps-msz.6.14's real-time full run dropped 2,220 of 44,072 sounds (5%). A fixed-time run used to drop
every sound (no music plays in time with its frames), so it couldn't say which. Now the frames' clock
keeps the sounds' books on its own mixer position (`FrameClock.TryDspAt`, a DSP buffer past
`SongAt(frame)`, the live rule), silently. It drops what a live run with perfect frame pacing would
drop. The run summary now counts each category's scheduled, dropped and cut-off sounds (cut off:
a sound still ringing whose voice went to a later one). The 30-minute fixed-time File run, windowless
with a device so every weapon's view runs (headless leaves out the instanced views: the wand, the
bible, the chests and the evolutions):

```sh
"PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode -force-metal -logFile "$PWD/player.log" \
  --fixed-time --file "$PWD/native/playlists/survivors-30-twice.m3u" --run --bot positioning
```

Both runs win at 30:00 with the same hash, A20A5CA751C13A54 (15,228 killed, 148 breaks, 145 regrowths).

| | before | after |
|---|---|---|
| scheduled | 50,715 | 52,550 (1,737 started into their clip) |
| dropped | 1,979 (3.9%) | **0** |
| cut off | 16,043 (32%) | 1,898 (3.6%) |

By category, scheduled / dropped / cut off:

| category | before | after |
|---|---|---|
| swing | 5599 / 0 / 1931 | 5599 / 0 / **0** |
| swing-heavy | 150 / 0 / 126 | 150 / 0 / **0** |
| slam | 76 / 0 / 23 | 76 / 0 / **0** |
| hurt | 1 / 0 / 0 | 1 / 0 / 0 |
| chest-open, chest-tick, chest-land | 15, 120, 15 / 0 / 0, 0, 1 | 15, 120, 15 / 0 / **0** |
| evolve | 0 / **3** / 0 | 3 / 0 / 0 |
| garlic-pulse | 0 / **1734** / 0 | 1734 / 0 / 9 |
| shatter-body | 5451 / **121** / 3764 | 5500 / 0 / 860 |
| shatter-glass | 5451 / **121** / 3441 | 5500 / 0 / 594 |
| shatter-tinkle | 5564 / 0 / 1242 | 5564 / 0 / 28 |
| death | 5423 / 0 / 1624 | 5423 / 0 / 30 |
| hit | 5643 / 0 / 3005 | 5643 / 0 / 196 |
| hit-heavy | 363 / 0 / 246 | 363 / 0 / 13 |
| lightning-strike | 562 / 0 / 484 | 562 / 0 / 168 |
| chime, garlic-hit, wand-fire, wand-hit, bible-fire, bible-hit, lightning-hit | 21,282 / 0 / 156 | 21,282 / 0 / 0 |

Retaken after rebasing onto ps-msz.8.5 (staging 1fd9fcac; its press rules move the run, hash
AC0F5686B95E04BE, 14,129 killed): 53,112 scheduled (1,801 into their clip), 0 dropped, 1,782 cut off
(3.4%), none of them a swing, the slam, a chest's or an evolution's.

The positioning bot is hit once in the whole run. The idle bot's run (`--bot idle`, falling at 1:59 of
run clock) schedules 7 hurts, none dropped or cut off.

What dropped, and the fixes:

- **Every Garlic pulse (1,734) and every evolution's sound (3)**: their clips' lead-ins (54 and 94 ms
  before the attack) are longer than the slack an event has (about a frame plus a DSP buffer). Garlic's
  pulse settles on its own impact, so it was always too late. Now, when the mixer can make the attack
  but not all of the lead-in, the voice starts into the clip at the earliest it can, so the attack still
  lands on its time (`SoundScheduler.Trimmed`). Only an attack the mixer is already past is dropped.
- **Every regrowth's shatter (121 of each layer)**: `StructureShatter` raised it on the frame that
  reached the eruption, a lead after the mixer had played it. The sound presenter now shatters it from
  the Regrowth event, as a break's, a lead ahead (ps-msz.8.5's audit found it too, and landed the same fix).
- **Cut off**: with 32 voices a third of all sounds lost their voice while still ringing. The pool is now 64 (the project's real
  voices 128, so the music and a full pool never make Unity virtualize either), and a full pool takes
  the oldest voice that isn't the hero's own (swings, the slam, the hurt), a chest's or an evolution's.
  What still goes is the long tails of ice breaks and thunderclaps.

The aggregation is unchanged: per beat and category at most three voices, their gain growing with the
count, so the finale's 290 deaths are three layered shatters at full gain, a crash of glass.

Real time (no `--fixed-time`), headless, 75 s from minute 25 with the finale at 68 s (SONG and EV as under Shatter at the peak):

```sh
"PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode -logFile "$PWD/player.log" --headless --file "$SONG" \
  --start-minute 25 --bot positioning --debug "$EV,finale@68" --seconds 75
```

| | before | after |
|---|---|---|
| scheduled | 1,962 | 1,890 (71 started into their clip) |
| dropped | 77: garlic-pulse 67, shatter-body and -glass 4 each, hit 2 | **0** |
| cut off | 868 (44%), swings 99 of 204 | 7 (0.4%), no swing |

(Real-time runs aren't repeatable, so the two runs' events differ a little; the hit drops are frame jitter
the fixed-time books can't see.)

## The finale's blast hits

`Horde.Blast` now goes through `Horde.Damage`: each enemy in its disc gets a Hit event
(`WeaponHit.Blast`, weapon `Horde.NoWeapon`, tallying no weapon) before its death or knock, so a
survivor flashes and shows its number (`HordeTests.TheBlastHitsEverySurvivorAndVictimOnItsTimeAndTalliesNoWeapon`,
and the finale's run test). `Kill` and `Leave` share one removal (`Horde.Remove`).

[finale-survivor.jpg](finale-survivor.jpg): the blue crystal boss at minute 25, level-scaled past the
blast's 1,000, flashing white on the blast's frame (68.383) and knocked out on the next. Before this
bead it took the blast with no flash.

## Shatter at the peak

From 25:00 on cc-kevin-macleod-go-cart with the bot and three evolutions (the full run's peak stills'
setup):

```sh
SONG="$PWD/public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg"
EV="evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5"
uv run --project demo3d/tools game look payoff-kill --evidence payoff --at 61.05 --frames 8 --strip 4 \
  --crop 300,0,1300,760 --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV"
uv run --project demo3d/tools game look payoff-linger --evidence payoff --at 61.083,61.4,61.8,62.3,62.9,63.5 --strip 3 \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV"
uv run --project demo3d/tools game look payoff-finale --evidence payoff --at 68.30 --frames 8 --strip 4 \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,finale@68"
uv run --project demo3d/tools game look payoff-finale-linger --evidence payoff --at 68.4,68.8,69.3,69.9,70.4,71.0 --strip 3 \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,finale@68"
uv run --project demo3d/tools game look payoff-regrowth --evidence payoff --at 38.05,38.117,38.2,38.3,38.45,38.65,38.9,39.4 \
  --strip 4 --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV"
```

- [payoff-kill.jpg](payoff-kill.jpg): the peak's busiest frame, a heavy strike at 61.07 s that kills
  22 at once and breaks four structures. Frames +0 and +1 stand still; on +2 (61.083), the first frame
  past the impact, the bodies are glass and the structure on the left splits; nothing moves before it.
- [payoff-linger.jpg](payoff-linger.jpg): the next 2.4 s. The chips scatter and sink in their 1.8 s
  while the crowd keeps dying around the hero. Scattered glass, not a carpet.
- [payoff-finale.jpg](payoff-finale.jpg): the finale at 68.37 s. Frames +0 to +4 stand still; on +5
  (68.383) every monster on screen is glass at once, flashing white where its body stood, and the kill
  count jumps from 814 to 1,098 on the next frame.
- [payoff-finale-linger.jpg](payoff-finale-linger.jpg): the blast's glass and the structures' shards
  thrown wide by 68.8, sinking by 69.9, gone by 70.4, leaving the gems. The boss stands.
- [payoff-regrowth.jpg](payoff-regrowth.jpg): the four structures broken at 16.2 s regrowing (the
  Regrowth event at 38.11 s), their shards bursting up from the footprint (38.65 to 38.9).

Live monster shards over a peak minute (`--shatter-out`, new: each frame's alive, deaths broken, live
deaths and their shards), 10 to 70 s of song, 300 alive throughout:

```sh
"PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode -force-metal -logFile "$PWD/player.log" \
  --fixed-time --file "$SONG" --start-minute 25 --bot positioning --debug "$EV" --seconds 70 --shatter-out "$PWD/shatter.csv"
```

| | median | p95 | max |
|---|---|---|---|
| live shards | 802 | 1,800 | 2,050 |
| live deaths | 24 | 53 | 62 |

797 deaths in the minute (13 a second), the most in one frame 22. Nothing showed a carpet of glass or
a shard moving before its impact frame, so the shard counts, kick and linger are unchanged (no
before/after strips needed). The flash on the break is the look bead's (ps-msz.8.3).

By ear: [operator-pending.md](../operator-pending.md), "The payoff's sound by ear (ps-msz.8.6)".
