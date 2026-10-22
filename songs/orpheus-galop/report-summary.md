# Orpheus galop: song map report summary

Built by the beat-map workflow in `docs/design/tech-demo.md`. Form and bar
counts: [form.md](form.md). Source recording: [source.json](source.json).

## Reproduce

```sh
A="uv run --project tools/analysis ps-analysis"
W=tools/analysis/work
$A crop $W/orpheus-overture.ogg --start 415.2 --end 559.8 \
  --out public/audio/orpheus-galop.flac --info $W/crop.json \
  --source-url https://upload.wikimedia.org/wikipedia/commons/a/a8/Offenbach_-_Orpheus_in_the_Underworld_-_Overture.ogg
$A analyze public/audio/orpheus-galop.flac --song orpheus-galop \
  --crop-info $W/crop.json --out $W/draft.json --analysis $W/analysis.json
$A edit $W/draft.json songs/orpheus-galop/edits.json \
  --analysis $W/analysis.json --out songs/orpheus-galop/songmap.json
$A report songs/orpheus-galop/songmap.json --analysis $W/analysis.json --out $W/report
$A check songs/orpheus-galop/songmap.json
$A click songs/orpheus-galop/songmap.json --out $W/orpheus-galop-click.wav
```

## What the edits do (`edits.json`)

1. **Octave fix.** The draft (madmom's DBN, 55–215 BPM) locked onto half
   tempo in the galop (one beat per 2/4 bar, about 92 BPM). From 43.0 s the
   beats are replaced with the `madmom-fast` tracker (150–240 BPM), giving
   quarter notes at about 176–193 BPM.
2. **Prelude grid.** The madmom beats are kept for the slow passage. The
   sparse transition (beats 41–56) is evenly spaced so the prelude holds
   exactly 14 bars of 4/4 before the vamp's first downbeat at 43.21 s. The
   first beat is moved to the phrase's first onset (0.149 s).
3. **Coda.** The fast tracker drifted by half a bar in the closing bars. The
   downbeats of bars 159–167 are pinned to the strong onsets (the final chord
   hits are 138.079, 138.551, 139.041 and 139.575 s). The beats in between are
   evenly spaced. After the final chord the beats continue at 0.2405 s through
   the held chord and the decay, so the last bar (177) is complete.
4. **Meter and structure.** 4/4 from bar 1 and 2/4 from bar 15. Nine sections
   and the phrases from `form.md`. The quiet galop vamp (bars 15–30) is its own
   pulsed section, `galop-vamp`: it already has the galop's steady pulse, so the
   game steps to it rather than walking. A tempo change covers bars 141–168: the coda
   stringendo (about 186 to 250 BPM) and the closing hits.
5. **`fitOnsets` (beats 58–381).** Moves galop beats, from the vamp's third
   beat on, onto onset peaks within 45 ms wherever every gap stays within 7.5%
   of the previous one. This raised onset alignment from 68–91% to 90–98% per
   section without breaking smoothness. (It starts two beats into the vamp
   because the gap before the vamp's first beat is a slow prelude beat.)
6. **Section starts on their hits.** Beat 88 (bar 31 beat 1, the start of
   `galop-opening`) sits exactly on the first tutti onset (54.0656 s), and
   beat 242 (bar 108, the start of `climax`) sits on the onset at 104.5476 s.
   Accent lookups by time then land in the right section.
7. **Anchors and accents.** 11 prelude anchors, each snapped to an onset peak
   within 30 ms (the four that fell in the vamp went with it: anchors mark
   strong moments only where there is no steady beat). The accent candidates are replaced with 30 curated accents,
   each snapped to an onset peak within 20 ms. The prelude's quiet transition
   has high *relative* onset scores but is about 25 dB below the galop, so it
   gets anchors, not accents.

## Accents

8 `tutti-chord` accents: the first galop chord, the three flourish chords that
follow it, and the four closing hits. The coda has four of these. The last
`tutti-chord` is the final chord (139.575 s) and the second-to-last is
139.041 s (`{ accent: tutti-chord, fromEnd: 2 }`).

| Time (s) | Bar.beat | Section | Strength | Label |
|---|---|---|---|---|
| 54.066 | 31.1 | galop-opening | 1.00 | tutti-chord |
| 54.423 | 31.2 | galop-opening | 0.82 |  |
| 56.779 | 35.1 | galop-opening | 0.98 | tutti-chord |
| 57.178 | 35.2 | galop-opening | 0.81 |  |
| 59.529 | 39.1 | galop-opening | 0.99 | tutti-chord |
| 59.905 | 39.2 | galop-opening | 0.78 |  |
| 62.301 | 43.1 | galop-opening | 1.00 | tutti-chord |
| 62.648 | 43.2 | galop-opening | 0.74 |  |
| 64.352 | 46.1 | galop-opening | 0.84 |  |
| 70.685 | 55.2 | theme-a | 0.78 |  |
| 76.328 | 64.1 | theme-a | 0.77 |  |
| 80.236 | 70.1 | theme-a-repeat | 0.82 |  |
| 87.013 | 80.2 | theme-a-repeat | 0.79 |  |
| 90.168 | 85.2 | theme-a-repeat | 0.84 |  |
| 98.070 | 98.1 | kick-line | 0.77 |  |
| 99.376 | 100.1 | kick-line | 0.76 |  |
| 104.548 | 108.1 | climax | 0.76 |  |
| 107.097 | 112.1 | climax | 0.74 |  |
| 116.232 | 126.2 | climax | 0.80 |  |
| 117.168 | 128.1 | climax | 0.81 |  |
| 120.873 | 134.1 | climax | 0.75 |  |
| 126.148 | 142.2 | coda | 0.83 |  |
| 126.731 | 143.2 | coda | 0.77 |  |
| 132.985 | 154.2 | coda | 0.83 |  |
| 133.781 | 156.1 | coda | 0.76 |  |
| 135.005 | 158.2 | coda | 0.76 |  |
| 138.079 | 165.1 | coda | 0.92 | tutti-chord |
| 138.551 | 166.1 | coda | 0.94 | tutti-chord |
| 139.041 | 167.1 | coda | 0.95 | tutti-chord |
| 139.575 | 168.1 | coda | 1.00 | tutti-chord |

- Audio: `orpheus-galop.flac` (144.600 s, 44100 Hz), cut from 415.200–559.800 s of the source
- Beats: 382; bars: 177; meter: 4/4 from bar 1, 2/4 from bar 15
- Tempo changes: bars 141–168
- Accents: 30 {'tutti-chord': 8}
- Beats flagged in the disagreement table: 95

## Sections

| Section | Bars | Feel | Key | Start (s) | End (s) | Beats | BPM | Onset alignment | Max gap change | Anchors |
|---|---|---|---|---|---|---|---|---|---|---|
| prelude | 1–14 | free | E major | 0.149 | 43.210 | 56 | 81.1 | 45.5% (25/55, median 28.9 ms) | 17.9% | 11 (max gap 7.57 s) |
| galop-vamp | 15–30 | pulsed | D major | 43.210 | 54.066 | 32 | 176.5 | 96.9% (31/32, median 9.3 ms) | 5.9% |  |
| galop-opening | 31–53 | pulsed | D major | 54.066 | 69.820 | 46 | 176.5 | 95.7% (44/46, median 10.2 ms) | 7.3% |  |
| theme-a | 54–69 | pulsed | G major | 69.820 | 80.230 | 32 | 184.9 | 96.9% (31/32, median 11.6 ms) | 7.5% |  |
| theme-a-repeat | 70–85 | pulsed | G major | 80.230 | 90.528 | 32 | 187.5 | 96.8% (30/31, median 10.6 ms) | 6.8% |  |
| kick-line | 86–101 | pulsed | D major | 90.528 | 100.650 | 32 | 188.6 | 93.8% (30/32, median 11.8 ms) | 6.2% |  |
| bridge | 102–107 | pulsed | D major | 100.650 | 104.548 | 12 | 185.4 | 91.7% (11/12, median 11.3 ms) | 6.3% |  |
| climax | 108–139 | pulsed | D major | 104.548 | 124.630 | 64 | 192.8 | 98.4% (62/63, median 10.3 ms) | 7.2% |  |
| coda | 140–177 | pulsed | D major | 124.630 | 144.386 | 76 | 240.0 | 90.4% (66/73, median 8.3 ms) | 0.0% |  |

## Final `check` output

```text
$ npm run check:songmap
prelude          bars   1-14  free      0.149-  43.210 s    81.1 bpm  anchors 11 max gap 7.57 s
galop-vamp       bars  15-30  pulsed   43.210-  54.066 s   176.5 bpm  align 96.9% (31/32)  max gap change 5.9%
galop-opening    bars  31-53  pulsed   54.066-  69.820 s   176.5 bpm  align 95.7% (44/46)  max gap change 7.3%
theme-a          bars  54-69  pulsed   69.820-  80.230 s   184.9 bpm  align 96.9% (31/32)  max gap change 7.5%
theme-a-repeat   bars  70-85  pulsed   80.230-  90.528 s   187.5 bpm  align 96.8% (30/31)  max gap change 6.8%
kick-line        bars  86-101 pulsed   90.528- 100.650 s   188.6 bpm  align 93.8% (30/32)  max gap change 6.2%
bridge           bars 102-107 pulsed  100.650- 104.548 s   185.4 bpm  align 91.7% (11/12)  max gap change 6.3%
climax           bars 108-139 pulsed  104.548- 124.630 s   192.8 bpm  align 98.4% (62/63)  max gap change 7.2%
coda             bars 140-177 pulsed  124.630- 144.386 s   240.0 bpm  align 90.4% (66/73)  max gap change 0.0%
accents: 30 {'tutti-chord': 8}
check: OK
```

The disagreement table (`report/disagreements.md`, not committed) flags 92
beats (this breakdown predates the vamp's split, when it flagged 95):

- 51 are prelude beats, where the trackers run at different metrical levels
  in free time (57 when the prelude still included the vamp).
- 23 are galop beats that sit on an onset peak, but one tracker is 40–110 ms
  away (median about 60 ms).
- 15 are galop beats 25–80 ms from the nearest peak. These are the misses
  counted by `check`: flourish downbeats where the chord comes early (bar 35,
  41 ms), the transition into the closing hits, and beats during the held
  final chord.

## Human gate

Click track: `tools/analysis/work/orpheus-galop-click.wav` (not committed). It
has a 1 kHz click on each beat, a louder 1.6 kHz click on each downbeat, and a
2.4 kHz click on the first beat of each section.
