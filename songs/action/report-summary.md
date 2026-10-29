# Action: song map report summary

Built by the beat-map workflow in `docs/design/tech-demo.md` (TD0). Form, the
evidence for it and the uncertain points: [form.md](form.md). Source and
playback conversion: [source.json](source.json). Alignment measurement:
`demo3d/evidence/td0/alignment.json`.

## Reproduce

The audio stays out of git. `public/audio/action.flac` matches
`/public/audio/action*` in `.gitignore`, and the work folder is ignored.

```sh
SRC="D:/vendor/songs/action/Alessia Labate, Conrad Taylor - Action [NCS Release].mp3"  # Mac: ~/Downloads/...
A="uv run --project tools/analysis ps-analysis"
W=tools/analysis/work
# On Cygnus only: no system Python 3.12 (see the TD0 notes in docs/tech-demo-3d/plan.md)
export UV_PYTHON_PREFERENCE=managed
# The map's audio: the MP3's gapless decode, stored losslessly.
ffmpeg -i "$SRC" -map 0:a -c:a flac -compression_level 8 public/audio/action.flac
$A analyze public/audio/action.flac --song action \
  --source "Alessia Labate, Conrad Taylor - Action [NCS Release].mp3 (see songs/action/source.json)" \
  --out $W/draft.json --analysis $W/analysis.json
$A edit $W/draft.json songs/action/edits.json \
  --analysis $W/analysis.json --out songs/action/songmap.json
$A report songs/action/songmap.json --analysis $W/analysis.json --out $W/report
$A check songs/action/songmap.json
$A click songs/action/songmap.json --out $W/action-click.wav
# Playback (the worktree setup command's conversion), and its alignment check:
ffmpeg -nostdin -y -i "$SRC" -map 0:a -map_metadata -1 -c:a libvorbis -q:a 6 $W/action.ogg
$A check songs/action/songmap.json --audio $W/action.ogg
uv run --project tools/analysis python demo3d/evidence/td0/align.py \
  public/audio/action.flac $W/action.ogg
```

## What the edits do (`edits.json`)

1. **A constant grid.** The draft's beats are madmom's DBN beats, quantized
   to 10 ms, so gaps alternate between 380 and 390 ms and four pairs break
   the 8% smoothness limit. The edits replace them with one exact grid at
   155 BPM:
   - beat 0 is moved to 0.00982 s and the last beat to 198.59047 s;
   - `respace` puts 513 equal gaps of 0.387097 s between them, giving 514
     beats.
   The tempo and phase come from a least-squares fit of the strong onset
   peaks: 155.0002 BPM, residual spread 3.2 ms, no drift from start to end
   (form.md, How this was worked out). The draft and every tracker agreed on
   the tempo (153.8 BPM median at madmom's 10 ms resolution); none of them
   had it this exact.
2. **Meter and phase.** 4/4 throughout, with the first downbeat on beat 0.
   madmom's downbeat tracker drifts to beat 4 and then beat 3 of the bar
   after the breaks. The map keeps the phase on which every sub-bass entry
   and cut lands on a downbeat (form.md, uncertain point 2).
3. **Twelve sections** with roles for choreography (form.md, Form):
   - nine `pulsed`: intro, groove, drop-1, breakdown, groove-2, build,
     drop-2, final-peak, outro;
   - three `free`: the two 2-bar breaks without a kick, and the kickless
     ending.
   The 8-bar phrases (shorter in the short sections) give 20 phrases.
4. **No tempo changes.**
5. **Anchors for the free sections,** each snapped to an onset peak within
   30 ms:
   - break-1: 34.580, 35.645, 36.445 and 37.095 s;
   - break-2: 121.580, 123.153 and 123.807 s;
   - ending: 195.898 and 197.446 s.
6. **Accents.** The analyzer's 60 candidates are replaced by 26 curated
   accents:
   - Accents on the beat sit exactly on their beat's time, so an accent on a
     section's first downbeat belongs to that section. Each is 0–3 ms from
     its onset peak; `check` allows 20 ms.
   - Accents off the beat are snapped to their onset peak within 20 ms.
   - Labels: `drop`, a full arrival after silence (3); `re-entry`, the bass
     returning after a one-bar or half-bar cut (5); `stab`, a short
     broadband hit in a break or the ending (5).
   - Unlabelled accents mark phrase starts and smaller hits.

## Accents

| Time (s) | Bar.beat | Section | Strength | Label |
|---|---|---|---|---|
| 1.558 | 2.1 | intro | 0.55 |  |
| 4.623 | 4.1 (−32 ms, noise layer enters) | intro | 0.60 |  |
| 7.752 | 6.1 | intro | 0.55 |  |
| 10.848 | 8.1 | intro | 0.55 |  |
| 12.397 | 9.1 | groove | 1.00 | drop |
| 24.784 | 17.1 | groove | 0.60 |  |
| 35.645 | 24.1 (+22 ms) | break-1 | 0.60 |  |
| 36.445 | 24.3 (+48 ms) | break-1 | 0.85 | stab |
| 37.171 | 25.1 | drop-1 | 1.00 | drop |
| 49.558 | 33.1 | drop-1 | 0.90 | re-entry |
| 61.945 | 41.1 | drop-1 | 0.60 |  |
| 80.526 | 53.1 | breakdown | 0.60 |  |
| 86.719 | 57.1 | groove-2 | 0.85 | re-entry |
| 99.107 | 65.1 | groove-2 | 0.60 |  |
| 105.300 | 69.1 | groove-2 | 0.55 |  |
| 111.494 | 73.1 | build | 0.60 |  |
| 123.153 | 80.3 (+46 ms) | break-2 | 0.90 | stab |
| 123.881 | 81.1 | drop-2 | 1.00 | drop |
| 136.268 | 89.1 | drop-2 | 0.90 | re-entry |
| 148.655 | 97.1 | drop-2 | 0.95 | re-entry |
| 155.136 | 101.2 (−99 ms, after a stutter stop) | drop-2 | 0.75 | stab |
| 161.042 | 105.1 | final-peak | 0.95 | re-entry |
| 173.429 | 113.1 | final-peak | 0.65 |  |
| 185.816 | 121.1 | outro | 0.60 |  |
| 195.898 | 127.3 (+17 ms) | ending | 0.80 | stab |
| 197.446 | 128.3 (+17 ms) | ending | 0.90 | stab |

Lookups:
- `{ accent: drop, fromEnd: 1 }` is the second drop (123.881 s).
- `{ accent: stab, fromEnd: 1 }` is the song's last hit (197.446 s).
- `{ section: final-peak, point: start }` is the final peak's arrival
  (161.042 s).

- Audio: `action.flac` (198.968 s, 44100 Hz, stereo, 8774479 frames), the
  whole recording (no crop)
- Beats: 514; bars: 129 (the last has 2 beats); meter: 4/4 from bar 1;
  155.0 BPM throughout
- Tempo changes: none
- Accents: 26 (`drop` 3, `re-entry` 5, `stab` 5)
- Beats flagged in the disagreement table: 24

## Sections

| Section | Bars | Feel | Start (s) | End (s) | Beats | BPM | Onset alignment | Max gap change | Anchors |
|---|---|---|---|---|---|---|---|---|---|
| intro | 1–8 | pulsed | 0.010 | 12.397 | 32 | 155.0 | 96.9% (31/32, median 0.7 ms) | 0.0% |  |
| groove | 9–22 | pulsed | 12.397 | 34.074 | 56 | 155.0 | 98.2% (55/56, median 1.1 ms) | 0.0% |  |
| break-1 | 23–24 | free | 34.074 | 37.171 | 8 | 155.0 | 71.4% (5/7, median 21.9 ms) | 0.0% | 4 (max gap 1.06 s) |
| drop-1 | 25–48 | pulsed | 37.171 | 74.332 | 96 | 155.0 | 99.0% (95/96, median 0.8 ms) | 0.0% |  |
| breakdown | 49–56 | pulsed | 74.332 | 86.719 | 32 | 155.0 | 100.0% (32/32, median 1.0 ms) | 0.0% |  |
| groove-2 | 57–72 | pulsed | 86.719 | 111.494 | 64 | 155.0 | 100.0% (64/64, median 1.2 ms) | 0.0% |  |
| build | 73–78 | pulsed | 111.494 | 120.784 | 24 | 155.0 | 100.0% (24/24, median 0.7 ms) | 0.0% |  |
| break-2 | 79–80 | free | 120.784 | 123.881 | 8 | 155.0 | 25.0% (2/8, median 43.4 ms) | 0.0% | 3 (max gap 1.57 s) |
| drop-2 | 81–104 | pulsed | 123.881 | 161.042 | 96 | 155.0 | 93.7% (89/95, median 0.9 ms) | 0.0% |  |
| final-peak | 105–120 | pulsed | 161.042 | 185.816 | 64 | 155.0 | 100.0% (64/64, median 0.6 ms) | 0.0% |  |
| outro | 121–126 | pulsed | 185.816 | 195.107 | 24 | 155.0 | 100.0% (24/24, median 1.1 ms) | 0.0% |  |
| ending | 127–129 | free | 195.107 | 198.968 | 10 | 155.0 | 60.0% (6/10, median 16.7 ms) | 0.0% | 2 (max gap 1.55 s) |

## Final `check` output

```text
$ uv run --project tools/analysis ps-analysis check songs/action/songmap.json
intro            bars   1-8   pulsed    0.010-  12.397 s   155.0 bpm  align 96.9% (31/32)  max gap change 0.0%
groove           bars   9-22  pulsed   12.397-  34.074 s   155.0 bpm  align 98.2% (55/56)  max gap change 0.0%
break-1          bars  23-24  free     34.074-  37.171 s   155.0 bpm  anchors 4 max gap 1.06 s
drop-1           bars  25-48  pulsed   37.171-  74.332 s   155.0 bpm  align 99.0% (95/96)  max gap change 0.0%
breakdown        bars  49-56  pulsed   74.332-  86.719 s   155.0 bpm  align 100.0% (32/32)  max gap change 0.0%
groove-2         bars  57-72  pulsed   86.719- 111.494 s   155.0 bpm  align 100.0% (64/64)  max gap change 0.0%
build            bars  73-78  pulsed  111.494- 120.784 s   155.0 bpm  align 100.0% (24/24)  max gap change 0.0%
break-2          bars  79-80  free    120.784- 123.881 s   155.0 bpm  anchors 3 max gap 1.57 s
drop-2           bars  81-104 pulsed  123.881- 161.042 s   155.0 bpm  align 93.7% (89/95)  max gap change 0.0%
final-peak       bars 105-120 pulsed  161.042- 185.816 s   155.0 bpm  align 100.0% (64/64)  max gap change 0.0%
outro            bars 121-126 pulsed  185.816- 195.107 s   155.0 bpm  align 100.0% (24/24)  max gap change 0.0%
ending           bars 127-129 free    195.107- 198.968 s   155.0 bpm  anchors 2 max gap 1.55 s
accents: 26 {'drop': 3, 'stab': 5, 're-entry': 5}
WARNING: last bar 129 is incomplete (extend the beats to its end)
check: OK
```

`check --audio tools/analysis/work/action.ogg` (the converted playback file)
prints the same lines and also passes.

The warning is expected. The file ends 0.77 s into bar 129, and a beat after
the end of the file would be an error (form.md, uncertain point 6).

The disagreement table (`report/disagreements.md`, not committed) flags 24
beats:

- 15 are in the free sections (break-1 4, break-2 7, ending 4). No kick
  plays there, and the nearest onsets are off-beat synth figures and stabs.
- 6 are in drop-2, and they are the 6 misses `check` counts there:
  - the pre-beat hits on beat 3 of bars 82, 84 and 86 (31–33 ms early);
  - bar 88's sub cut (beats 1, 2 and 4).
- 3 are early layers or fills:
  - the noise entry at bar 4 (32 ms early);
  - an early layer at bar 12 (28 ms);
  - bar 32 beat 4, inside drop-1's one-bar sub cut.

The trackers' columns differ from the map partly through madmom's 10 ms
frame quantization. The grid itself rests on the 483-peak fit, not on these
tables.

## Human gate

Click track: `tools/analysis/work/action-click.wav` (not committed, never
shared: it contains the NCS audio). It has a 1 kHz click on each beat, a
louder 1.6 kHz click on each downbeat and a 2.4 kHz click on the first beat
of each section. The operator's listening check is in the plan's
Pending on the operator table.
