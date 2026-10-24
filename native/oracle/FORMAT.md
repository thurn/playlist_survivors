# Oracle fixture format

The TS game's reactive runs at `ts-freeze`, exported by `scripts/oracle-export.ts`
for the Rust sim to match bit for bit ([plan](../../docs/design/native-app.md#oracle-fixtures)).
This file is the contract: the Rust harness (`ps oracle`) implements every
procedure below exactly as written. Format version 1.

## Layout

```text
native/oracle/
  FORMAT.md, README.md
  fixtures/<track>/<profile>/
    meta.json           every run
    keys.jsonl.gz       every run
    events.jsonl.gz     full runs only
    frames.bin.gz       full runs only
    dense.json.gz       full runs only
    chains.json         chain runs only
  math-functions.json, math-vectors.jsonl.gz
  content.json, content.json.gz
  truncation.json
  stills/<track>-<profile>-<t>.png, stills/index.json
```

## Runs

Every run is the reactive level (`levels/reactive.yaml`) at its own seed, on a
shipped track's `public/reactive/<track>/stream.json`, played by one virtual
player profile.

- **The core set:** the Galop (`orpheus-galop`), the synthetic tracks
  (`house-128`, `dnb-174`, `hiphop-92-swing`, `accelerando-100-140`,
  `pop-arrangement`), the check-only `causality-mix`, and six corpus tracks
  (`notjam-breakbeat-chips`, `notjam-waltz-of-the-lich-king`,
  `notjam-chill-menu`, `cc-kimiko-ishizaka-aria`,
  `cc-hairy-larry-and-the-flying-hungarians-ride-me-to-heaven`,
  `cc-kevin-macleod-black-vortex`), each with every profile the TS reactive
  checks use: `reactive-moderate`, `reactive-human`, `reactive-half`,
  `reactive-offbeat`, `reactive-skilled`, `mash2`, `mash4`, `mash8`, `holder`.
- **Every other shipped track** (the rest of the classical group and of the
  corpus): `reactive-moderate` and `mash4`.
- **Full runs** keep the whole event log, every frame hash and dense frames:
  every core track with `reactive-moderate`, plus the Galop and
  `causality-mix` with `mash4`.
  **Chain runs** (every other run) keep per-second hash chains instead. `meta.json`'s
  `full` says which.

### How a run was made

1. The virtual player plays the track inside the TS sim (its `decide` events
   share the queue), as the TS checks run it.
2. Its recording's key events (`plan.inputs`: `s`, `kind`, `key`) are taken.
3. The sim is run again, fed only those keys, by the frame procedure below:
   that is the exported run.
4. The exporter asserts that the exported run's key events equal the
   player's, and that the same keys run **without** taking frames give an
   identical recording (the whole recorded plan, serialized, its timing stats
   aside). Taking a frame does move the sim's clock to `s + SPECULATE_S`
   between its own stops (`Sim.advanceTo` runs every stop before the target,
   then `moveClock`s to it when it is more than `CLOCK_EPS` clear of the last
   and the next stop); this assertion shows those clock moves change no
   recording on any run. The Rust sim must make the same clock moves.
5. It also compares the player's own run with the exported one. They match
   on almost every run. Where they don't, `meta.json` has
   `playerRunIdentical: false` and the first difference: the player's
   `decide` events are extra sim stops, and a few TS reads depend on where
   the sim stops (a sound's pan reads the camera at the sound's time, clamped
   to how far the camera track has been recorded, `emit.ts:287-290`; one warp
   break lands on a different stop). The exported keys-only run is the
   reference: the Rust sim, replaying keys, has the same stops.

So the Rust harness needs no TS player: it replays `keys.jsonl.gz`.

## Frames

**A frame is defined as the TS checks define it** (`liveRun` in
`src/planner/reactive-checks.ts`), so it does not depend on how keys and
arrivals are grouped into app frames:

1. Build the sim on the whole stream (Replay with `StreamEnd`: every entry is
   present, and each becomes visible to the sim's readers when the sim's clock
   reaches its `knownAt`), and queue **every** key event up front, at its time.
2. For `k = 0, 1, 2, ...` while `s = k / 60 < end`, where `end` is the sim's
   end (`Sim.endS`: `max(ctx.duration + 1, finale end)`):
   1. advance the sim to `min(end, s + SPECULATE_S)`, with `SPECULATE_S = 0.2`
      (`Sim.advanceTo`: every stop before the target, then a clock move to the
      target itself, as step 4 above describes; at or past `end` it runs to
      the end);
   2. refresh the live recording;
   3. evaluate the frame at `s` (`Evaluator.evaluate(s)`, no options).
3. Run the sim to its end and refresh the recording: that is the run's
   recording, whose event log is exported.

`meta.json`'s `frames` is the frame count. The frame is hashed whole,
including its `section` record. Its shape is the TS `Frame` interface
(`src/plan/evaluate.ts`) with its property names as written there (camelCase,
optional fields absent when undefined); every number, from an integer typed
array too, is hashed as an f64. The loaded level's hash (`levelHash`) is the
same serialization of the TS `LevelScript` that `loadLevel` returns.

## Canonical serialization

Frames, the loaded content and the loaded level are hashed over this byte
serialization, fed to **FNV-1a 64** (offset basis `0xcbf29ce484222325`, prime
`0x100000001b3`, one byte at a time: `h ^= byte; h *= prime` mod 2^64):

| Value | Bytes |
|---|---|
| null (and an `undefined` array item) | `0x00` |
| false, true | `0x01`, `0x02` |
| number | `0x03`, then its IEEE-754 f64 bits, 8 bytes little-endian; every NaN as `0x7ff8000000000000`; `-0` keeps its sign bit |
| string | `0x04`, then its UTF-8 length as u32 LE, then the UTF-8 bytes |
| array (and a typed array) | `0x05`, then its length as u32 LE, then each item |
| object | `0x06`, then the count of its fields whose value is not `undefined` (nor a function), as u32 LE, then for each such field in ascending order of its key (JS `sort()`: UTF-16 code units; every key is ASCII), the key as a string (u32 length and UTF-8 bytes, no tag) and then its value |

A field whose value is `undefined` is skipped, as `JSON.stringify` skips it.
In Rust that is an `Option` that is `None`.

Test vectors (`tests/oracle-export.test.ts`): FNV-1a 64 of the empty input is
`cbf29ce484222325`, of `a` `af63dc4c8601ec8c`, of `foobar` `85944171f73967e8`.
Hashes are written as 16 lowercase hex digits.

## Files

### `meta.json`

`format` (1), `tag` (`ts-freeze`), `track`, `profile`, `core`, `full`,
`seed`, `frameRate` (60), `speculateS` (0.2), `frames`, `events` (the event
log's line count), `keys`, `planHash` (the TS `planHash` of the recording),
`lastFrameHash` (FNV-1a 64, from the offset basis, over every frame hash in
order as 8 LE bytes), `stats` (the recording's `stats` without `planMs` and
`simMs`), `playerRunIdentical` (and `playerRunDifference` when false: see "How a run
was made"), `levelHash` (the loaded level script's canonical hash, hex) and
`inputs`:

- `stream`: the track's `stream.json` path and SHA-256;
- `content`, `assets`: the tree hash of `content/` and `public/assets/`;
- `level`: `levels/reactive.yaml`'s SHA-256.

A **tree hash** is the SHA-256 of the concatenation, over every file under the
directory sorted by path, of `<path relative to the repository root, with />\0<the
file's SHA-256 in lowercase hex>\n`. The harness fails, naming the file, when
an input no longer matches.

### `keys.jsonl.gz`

One key event per line, in order: `{"s": <song time>, "kind": "press" |
"release", "key": "up" | "down" | "left" | "right" | "space"}`.

### `events.jsonl.gz`

The recording's canonical event log, one JSON object per line:
`{"t": <song time>, "k": <category>, "i": <index in the category>, "v": <the record>}`.

- `v` is the recorded plan's entry as `JSON.stringify` writes it, typed arrays
  as arrays. Compare numbers by value after parsing (the TS writer gives the
  shortest round-trip form, so parsing restores the exact f64; it can't carry
  `-0`, `NaN` or infinities, which become `0`, `null` and `null`).
- Lines are sorted by `t`, then by category in the table's order, then by `i`.
- Within a category, `i` numbers the entries 0, 1, 2, ... in the order the
  table gives (the recorded plan's order), so `(t, category, i)` is unique.

| `k` | Source in the recorded plan | `t` |
|---|---|---|
| `beat` | `[songBeats[i], worldBeats[i], beatsKnownAt[i]]` (`null` where an array is shorter or absent) | `songBeats[i]` |
| `input` | `inputs` | `s` |
| `settle` | `settles` | `s` |
| `swing` | `swings` | `s` |
| `cleave` | `cleaves` | `launch`, else `max(s, press)` |
| `groove`, `tier`, `health` | the series: `v` is `[s[i], v[i]]` | `s[i]` |
| `enemy` | `enemies`, without `samples` and `sampleOffset` | song time of `spawnW` |
| `hit` | `hits` | `s` |
| `bite`, `heal`, `upgrade`, `levelUpDue`, `chicken`, `vacuum`, `weapon` | `bites`, `heals`, `upgrades`, `levelUpsDue`, `chickens`, `vacuums`, `weapons` | `s` |
| `formation` | `formations` | `sRelease` |
| `wave` | `open.waves` | `s` |
| `landmark` | `open.landmarks` | `s`, else song time of `w` |
| `ui`, `effect`, `sound` | `events.ui`, `events.effects`, `events.sounds` | `s` |
| `gem` | `events.gems` | song time of `w0` |
| `projectile`, `slam` | `projectiles`, `slams` | song time of `w0` |
| `chain` | `chains` | song time of the first point's `w` |
| `nearMiss`, `threat`, `accent`, `cleaveAccent` | `nearMisses`, `threats`, `accents`, `cleaveAccents` | `s` |
| `hud.kill`, `hud.perfect`, `hud.levelUp` | `hud.kills`, `hud.perfect`, `hud.levelUps`: `v` is the time | the time |
| `hud.xp` | `[hud.xpTimes[i], hud.xpFill[i]]` | `xpTimes[i]` |
| `hud.fire` | `[weapon, time]`: every fire of the first weapon in ascending name order, then the next weapon's, ...; `i` runs across all of them | the time |
| `summary` | one line, the object below | the latest finite `t` of any other line, or the recording's `duration` if later |

The summary's `v`, field for field (`hex` is an FNV-1a 64 as 16 hex digits,
over the array's length as u32 LE, then each value as f64 bits LE):

```text
{
  stats:        the recording's stats without planMs and simMs (the live recording leaves
                its counters at zero; they are kept for the shape, not as a check),
  boss:         the recording's boss, or null,
  warp:         the recording's warp breaks [{s, w, rate}],
  sections:     the recording's sections (PlanSection),
  pickupRadius: the recording's pickupRadius,
  grid:         hex(grid),
  strides:      {speed, durationS, hop, freePeriodS, steps: hex(steps), periods: hex(periods),
                 per: hex(per), only when per is present},
  hero:         {x, y, z, facing, anim, frame, trail}: hex of each hero track,
  heroAnims:    hero.animNames,
  camera:       {x, y, zoom}: hex of each camera track,
  enemySamples: hex(enemySamples),
  energy:       hex(energy),
  barStart:     the recording's barStart,
  map:          the recording's map {zones, hash},
  progressS:    the recording's progressS, or null
}
```

"Song time of `w`" is the recording's warp mapping world time to song time
(`Warp.toSong`).

### `frames.bin.gz`

The frame hashes, `frames` u64 values, little-endian, frame 0 first.

### `dense.json.gz`

A JSON object mapping frame numbers (as strings; frames at about 15, 60 and
120 s and 5 s before the song's end, where the song is long enough) to the
whole frame, keys sorted: for reading a mismatch, never compared.

### `chains.json`

`{"events": [...], "frames": [...]}`, each a list of hex FNV-1a 64 values,
one per second of song:

- **events:** `chain[n] = FNV-1a64(offset; chain[n-1] as 8 LE bytes, then
  the canonical serialization of each event-log entry {t, k, i, v} with
  n <= t < n + 1, in log order)`, with `chain[-1] = 0xcbf29ce484222325`; the
  last second also takes every later entry (a `t` that is infinite or past
  the end). The number of seconds is the larger of `ceil(stream.audio.duration)`
  (the track's `stream.json`) and `floor(t) + 1` for the latest finite `t`. The entries are those
  `events.jsonl.gz` would hold, but serialized canonically (so `-0`, NaN and
  infinities keep their bits, and no JSON number formatting is involved).
- **frames:** `chain[n] = FNV-1a64(offset; chain[n-1] as 8 LE bytes, then the
  hashes of frames 60n to 60n + 59 as 8 LE bytes each)`.

The first differing second locates a mismatch; a full run of the same track
gives the context.

### `math-functions.json`, `math-vectors.jsonl.gz`

Every `Math` function the ported TS code calls (`src/core`, `sim`,
`planner`, `reactive`, `plan`, `live`, `mapgen`, `content`), and 1000 vectors
for each from V8: `{"fn", "args": [<f64 bits, hex>], "out": <f64 bits, hex>}`.
Each starts with special values (zeros, ±1, π, e, tiny and huge magnitudes,
infinities, NaN; every pair of them for two-argument functions), known edge
cases (`pow(1, NaN)`, `pow(-1, ±Infinity)`, `atan2` of infinities and signed
zeros, `hypot` with an infinity and NaN, `round` at halves and
0.49999999999999994), for `pow` the exponents the code uses (2, 3, 1.5, 0.5,
...) and bases 0.5, 2, 10 and e, then seeded values over the ranges the game
uses and random bit patterns: at least 1000 per function. The `**` operator
has `Math.pow`'s semantics, so `jsmath::pow` serves it too. `jsmath` must
return the same bits for every vector.

### `content.json`, `content.json.gz`

The canonical hash of `loadContent(content/*.yaml)` (Maps as objects keyed by
their keys as strings, Sets as arrays in insertion order), and that value as
JSON (keys sorted), for reading a mismatch.

### `truncation.json`

The truncation check's result on TS (see README.md): per case, the track,
level, players, number of cuts and each failing cut's first difference; the
third case is the negative control, which must fail.

### `stills/`

PNGs from the TS renderer (`npm run capture`, 1920 × 1080) at chosen moments,
with `index.json` listing each one's track, profile, song time and capture
query, for the renderer's perceptual comparison.
