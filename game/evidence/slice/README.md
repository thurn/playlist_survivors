# The cinematic slice (ps-msz.3.13)

The assembled slice ([unity-game.md, Build Order step 2](../../../docs/design/unity-game.md#build-order)):
on File audio, the sim plays the slice's level (`SliceLevel`: the arena's level data, a horde of five
early-run roles and the starting sword) and every presenter draws it: the grid hero and its sword on
beats, the stepping crystal horde with GPU shatter, crystal structures that break and regrow, the night
post with its beat feel, the camera and the scheduled sound. Windowless fixed-time stills and filmstrips
on house-128, judged against the 3D demo's reference command
([arena/reference](../arena/reference/), rendered for ps-msz.3.6).

## How they were made

On jarvis (Apple silicon), IL2CPP, from the worktree's root:

```sh
uv run --project demo3d/tools game build
F="$PWD/public/reactive/house-128/house-128.ogg"
P="--file $F --debug accent@30,change@44,break@47"
uv run --project demo3d/tools game look slice --evidence slice --readability \
  --at 8.05,12.25,20,26.3,30.55,38,45.1,49.75,57.3 --player-args "$P"
uv run --project demo3d/tools game look slice-heavy --evidence slice --at 30.45 --frames 12 --strip 6 --player-args "$P"
uv run --project demo3d/tools game look slice-regrowth --evidence slice --at 44.983333 --frames 12 --strip 6 --player-args "$P"
uv run --project demo3d/tools game look slice-break --evidence slice --at 46.966667 --frames 12 --strip 6 --player-args "$P"
uv run --project demo3d/tools game look slice-kill --evidence slice --at 49.683333 --frames 12 --strip 6 --player-args "$P"
```

The debug hooks force an accent at 30 s, a section change at 44 s and a break of the structures near the
hero at 47 s (F6, F5 and F7 in a window). The hero stands at its start, since nobody presses a key. The
moments' times come from a headless fixed-time run's `--events-out` CSV, which gives the same events:

| time | what |
|---|---|
| 30.50 s | the forced accent's heavy strike (wound up at 30.18 s): a spin, its cleave wave breaking site 6 at 30.735 s |
| 44.09 s | the forced section change lands on its downbeat (zone 4, lava's grade, eased over two bars) |
| 45.03 s | the slam on the next downbeat, and seven sites regrow on it |
| 47.00 s | the debug break: seven sites within 15 m break at once, large ones too |
| 49.72 s | an ordinary strike kills 37 monsters on one impact |

## Stills

| still | what it shows |
|---|---|
| [8.05 s](still-008.050.jpg) | the first heavy strike's mass kill: 34 monsters shatter in their gem colours, the horde streaming in from every side |
| [12.25 s](still-012.250.jpg) | a heavy strike's wave with 29 kills, the post's flash on it |
| [20 s](still-020.000.jpg) | an ordinary strike's ring sweeping out through the horde, green goblins shattering in its path; lanes of cyan skeletons, goblins, pink slimes and red demons stepping in |
| [26.3 s](still-026.300.jpg) | 27 kills on one impact, the strike's ring white-hot at its start |
| [30.55 s](still-030.550.jpg) | the forced heavy strike's ring and cleave a frame after impact |
| [38 s](still-038.000.jpg) | a strike's ring at the rune circle's edge, the white blade raised, shards scattered around the crowd held between steps |
| [45.1 s](still-045.100.jpg) | the slam's burst at the hero, the regrown structures in crystal |
| [49.75 s](still-049.750.jpg) | the 37-kill impact's shards on the next frames |
| [57.3 s](still-057.300.jpg) | a slam on a natural zone change: 49 kills, the largest shatter of the run |

Every still passes the readability floor ([readability.json](readability.json): monsters at L* 57 to 86
against a ground ring of 6 to 18, dL* +47 to +70, the floor 17).

## Filmstrips (consecutive 60 fps frames)

- [slice-heavy.jpg](slice-heavy.jpg): the forced heavy strike. Nothing anticipates it; the ring and the
  hits' flashes appear on the impact frame (30.500 s, +3) and the cleave wave follows it outward.
- [slice-regrowth.jpg](slice-regrowth.jpg): the slam lands on 45.033 s (+3), the first frame at or after
  the downbeat, with its ice burst at the hero; the structures erupt back on the same frame.
- [slice-break.jpg](slice-break.jpg): the debug break on 47.000 s (+1): the structures' launch glow and
  bursts on that frame, shards after. Seven breaks at once max out the feel's flash and aberration.
- [slice-kill.jpg](slice-kill.jpg): 37 monsters break on 49.733 s (+3), the first frame at or after their
  death at 49.72 s, from their poses on that frame: no build-up, nothing blinking.

## Against the reference

CIE L* and C*ab over the whole frame (demo3d_tools.look's `still_stats`) and the median hue, on the
960 px stills:

| set | L* p50 | L* p98 | contrast | chroma | near-black (L* < 10) | clipped | hue |
|---|---|---|---|---|---|---|---|
| reference, horde shots (4) | 23.5 - 44.1 | 62 - 66 | 20.4 - 21.5 | 28.6 - 31.6 | 16 - 33% | 0% | 203 - 228° |
| reference, song at 25 s (1) | 12.5 | 70 | 20.2 | 24.2 | 43% | 0% | 231° |
| reference, empty shots (4) | 3.5 - 8.0 | 28 - 58 | 7.4 - 13.6 | 23.3 - 25.9 | 56 - 79% | 0% | 243 - 257° |
| the slice (9) | 4.7 - 12.2 | 73 - 100 | 19.3 - 28.2 | 28.6 - 41.0 | 45 - 66% | 0.1 - 3.0% | 248 - 254° |

**It reads as the reference look.** The world is the same dark violet (the ground's hue and value, the
magenta mist), lit by the hero's cyan pool and the crystal. Monsters are rainbow crystal in their roles'
gem colours, the blade white, and bursts are coherent shatters in each monster's colour. The contrast and
chroma match the reference's horde shots. The slice is darker overall (p50 5 to 12 against 12 to 44),
because its crowd is a few hundred, not the stress scene's thousands that fill the reference's frame.

The differences, and why:

- **Clipping on big moments.** Four stills clip 2 to 3% of the frame in one blob: the strike's ring
  at its white-hot start under the post's flash, which is full from 24 kills on one impact (FeelMoments).
  The dense slice reaches that on every heavy strike and slam. The reference stills were rendered with
  `--no-post-feel`, so they have no flash to compare with. Whether the flash reads as too strong in
  motion is the operator's beat-feel row.
- **A sparser crowd**, as above: the reference is demo3d's stress scene.
- **A strike's ring in every still.** The sword strikes on every beat (0.47 s at 128 BPM), and each
  strike's ring sweeps out and fades over most of that beat, so none of the nine stills, chosen
  between the big moments or not, catches the slice without one. The reference has no sword. The
  ring is the brightest thing on screen most of the time; whether it reads as too insistent in motion
  is the operator's first assembled check.
- **Rainbow structures** where the reference's are one sapphire, by design (ps-msz.3.6).

## The slice level

- **Roster**: bat1, zombie, ghost, skeleton and mudman1 (violet bats, pink slimes, red demons, cyan
  skeletons, green goblins). The first try had the werewolf and the flower too. The werewolf has 18
  health against the rank-1 sword's 1 per hit, so the trolls stalled at the edge of its knockback and
  filled the frame in one blue. The flower walks at 0.29 m/s and never arrived.
- **Density**: a roster a second, and whole rosters while fewer than 220 are alive. On house-128, 214
  to 245 are alive and the sword kills about 23 a second (1,437 in 62 s), with ~100 monsters within
  17 m of the hero.
- **The evidence gate**: the sim's default gate was 0.5. house-128's beat evidence sits at 0.48 to
  0.51, so its beat flickered below the gate and the horde held still for 50 of the song's 62 s. It is
  native's 0.2 now (native/models/manifest.json's `gate`), and the beat is reliable from 4 s to 58 s.
- The hero stands (`HeroStands`): a fall needs the run's card, which comes with M3. The spawn ring stays
  out of the camera's loud view at 16:9 or wider (`CameraRig.Footprint`).

## The headless File run

The bead's check: house-128 at real time, batch mode, no graphics, muted (the song ends the run at
62.5 s, before `--seconds 120`):

```sh
EXE="game/Builds/mac/il2cpp/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors"
"$EXE" -batchmode -logFile "$PWD/captures/game/slice/headless-120.log" --file "$PWD/public/reactive/house-128/house-128.ogg" \
  --headless --seconds 120 --loop-out "$PWD/captures/game/slice/headless-120.csv" --debug "accent@20,break@30,change@40,regrow@50"
```

- **The lead** stays at output latency plus margin: median 90.3 ms, p5 to p95 90.3 to 90.3, max 91.4
  (38.0 ms output latency + 52.3 ms margin). frameSong never went backwards, no frame showed the
  frontier, and no entry was late.
- **The sim**: step median 0.008 ms, p99 0.32, max 0.81. 1,437 killed, 11 heavy strikes, 38 structures
  broken, 31 regrown, 5 zone changes; 1,707 sounds scheduled, none dropped.
- **No errors**: no exception. The only error lines are Unity's own for the shaders a -nographics player
  can't load. A headless player no longer renders the static shadow map
  (`ArenaRuntime.ShadowMap` is null there): before, each break's regional redraw logged a failed
  render target, 69 times.

## Slow CI's File run

Slow CI's game section runs the slice for 30 s of house-128 in a Mono player, with every scheduled sound
played as a click (`game spike ci --sounds`), measured from Unity's own mix. On jarvis: 786 sounds, of
which the 131 at least 60 ms from any other are measured (closer ones merge into one onset in the mix).
130 were found, from -0.44 to +0.33 ms of their music (median 0.000). The music sat exactly where its
anchor claimed (lag 13056 frames in every window), drift was +0.11 ms/min, and frameSong was within
-0.30 ms (median) of the audio thread's clock. There were no stalls and no capture gaps. A player
exception fails the run, and so does a run with fewer than 20 sounds apart to measure (a denser
slice whose sounds all crowd each other would otherwise pass on a handful).
