# Structure shatter and regrowth (ps-msz.3.12)

The crystal structures breaking into PhysX shards on the impact frame, glinting, and erupting
from the ground on regrowth ([the design](../../../docs/design/unity-game.md), Crystal Structures
and Shatter). The code is game/Assets/Game/Client/Structures/: `StructureShatter` (the presenter:
breaks, regrowths, glints, the local physics scene) and `StructureShards` (the baked asset), cut
by game/Assets/Game/Editor/Structures/`StructureShardBaker` (`game unity BakeStructureShards`, or
`game build`). The static shadow map's regional redraw is `StaticShadowMap.Redraw`, and the
eruption's sink is `_SL_Sink` in StructureCommon.hlsl.

## How it was run

On the Mac (jarvis, Apple M5 Max), fixed time (frame n is song time n/60), no music: the events
come from synthetic feeds written by [feeds.py](feeds.py), on 128 BPM beats in the game's arena.
[moments.jsonl](moments.jsonl) walks the hero to the small site 7 and breaks it with a heavy spin
on beat 8 (4.75 s, on a frame), then takes the large site 6 (9 tiles, so three breaking hits):
heavy spins on beats 20 and 22 glint toward the break (off the frame grid), a light slash glints
at half, and the break lands on beat 24 (12.25 s). Site 7 regrows on beat 32 (16.0 s), the hero
3.2 m away. [bench.jsonl](bench.jsonl) breaks every site alone, 4 s apart, then regrows them all
at 153 s and breaks all 38 at once at 157 s. From the worktree's root:

```sh
python3 game/evidence/structures/feeds.py
E="$PWD/game/evidence/structures"; C="$PWD/captures/game/structures"
uv run --project demo3d/tools game look small-break --backend mono --at 4.65 --frames 12 --strip 6 --evidence structures --player-args "--events $E/moments.jsonl --view-width 16"
# likewise small-flight, large-break and large-impact (the --at lists below), and regrowth with --view-width 22 --crop 760,60,640,420
EXE="game/Builds/mac/il2cpp/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors"
"$EXE" -batchmode -force-metal -logFile $C/moments.log --fixed-time --events $E/moments.jsonl --seconds 18 --structures-out $C/moments.csv
python3 game/evidence/structures/feeds.py check $E/moments.jsonl $C/moments.csv
"$EXE" -batchmode -force-metal -logFile $C/bench.log --fixed-time --events $E/bench.jsonl --seconds 163 --structures-out $C/bench.csv
python3 game/evidence/structures/feeds.py bench $C/bench.csv
```

`--structures-out` writes each frame's breaks so far, live shards, shards in flight, the farthest
any has moved from where it stood, the physics steps and their milliseconds, and the shadow
redraw's milliseconds. The CSVs stay in the gitignored captures/game/structures/.

## Results

- **Shards move on the impact frame, and the whole structure is in flight within 50 ms.**
  `feeds.py check` on the IL2CPP run: on the frame before each break nothing has changed. On the
  impact frame the site's renderers are gone and its shards are out, some already moving: 4 of
  16 shards, 10.4 cm, for the small break at 4.75 s, and 11 of 27, 21.4 cm, for the large one at
  12.25 s. 50 ms later every shard is flying. The quarter of the structure nearest the strike
  launches on the strike and the farthest shard 40 ms later. The physics runs one frame ahead of
  the song clock, so the impact frame shows a frame of flight. A first version launched only the
  single nearest shard on the impact frame, 2 to 3 cm, which doesn't read.
- **The strips** (the hero's attacks and 3.11's effects are drawn too):
  - [small-break.jpg](small-break.jpg): 12 consecutive frames, the break at +6 (4.750). The near
    side flashes white and lifts on that frame, under the spin's wave.
  - [small-flight.jpg](small-flight.jpg): the same break over 3.5 s (4.733 to 8.3). The shards
    scatter away from the hero, clatter down and lie there, then sink by 8.2 s (2.4 s linger and
    1 s sink).
  - [large-break.jpg](large-break.jpg): the large site's two glints toward the break (10.383 and
    11.333: the whole site flashes, then fades within 0.3 s), then the break (12.233 to 13.5): on
    12.250 the struck side flashes and moves, by 12.300 all of it is flying, by 12.7 it lies on the
    ground.
  - [large-impact.jpg](large-impact.jpg): 12 consecutive frames, the break at +6 (12.250, under
    the spin's wave and the structure burst).
  - [regrowth.jpg](regrowth.jpg): site 7 erupting on the downbeat at 16.0 s. On the downbeat
    frame the burst of small shards is thrown up and the crystal flashes as it breaks the
    ground. By 16.067 it stands most of its height, it overshoots a little and settles by the
    beat's end (16.47), and the burst's shards fall and sink.
- **Bench** (IL2CPP, 9,780 frames, `feeds.py bench`): each of the 38 single breaks keeps its
  shards for 207 frames (3.45 s). Per break: 10 to 117 shards (median 34); physics 29 ms in all
  (median; at most 47 ms), 0.05 to 0.11 ms per 1/120 s step; its worst frame 0.28 ms (median; at
  most 0.91 ms). Regrowing all 38 sites at once throws 522 burst shards, at most 1.1 ms a frame.
  Breaking all 38 at once (1,493 shards, far past any real moment) peaks at 2.9 ms a frame.
- **The static shadow map** re-renders only where the changed site falls on it: 0.25 ms per break
  (median; at most 0.54), 2.9 ms for all 38 regions at once, 4.2 ms for the mass regrowth.
  A full redraw took 8.7 ms on the break's frame, and the first regional one 71 ms, until the
  setup moved to load (the map's render at load is 15 ms, 13 before). Patched and full redraws
  give the same stills: the moments run at 4.7, 9, 14 and 17 s, patched against a temporary
  full-redraw build, differ by at most 3 grey levels, JPEG noise (the frame before any break
  differs as much). With no redraw at all, the 14 s still differs on kit beside site 6, so the
  test can fail. The ground (URP Lit) takes the structures' shadows from the realtime cascades,
  so the static map shows mostly on the kit, the crystal, the horde and the hero.
- The break's flash, punch and shake are the look's (FeelMoments, from the same event), now
  scaled by the break's force against a heavy strike's 4.63 m throw (a cleave band's 1.85 m is
  0.4). Previously every break clamped to full. Its burst and glass are the effects' and the
  sound's (ps-msz.3.11); `StructureShatter.Erupted` hands them the regrowth's.

Still open for the operator: the breaks and eruptions in motion in a window
([operator-pending.md](../operator-pending.md)).
