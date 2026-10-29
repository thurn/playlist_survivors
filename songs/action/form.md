# Action: expected form

Recording: *Action* by Alessia Labate and Conrad Taylor (NCS Release), the
full supplied MP3 (198.968 s), identified in [source.json](source.json). The
song map's audio is `public/audio/action.flac` (gitignored), the MP3's gapless
decode, so all times below are seconds from the MP3's first gapless sample,
which is also the Ogg's first sample (see source.json, `playback`).

## How this was worked out

No score exists, so the form comes from the recording. The beat-map agent
cannot listen; it read these instead:

- **The beat grid.** A least-squares fit of the 483 strong onset peaks (peak
  strength above 0.25) that fall within 25 ms of a constant grid gives
  155.0002 BPM, with a residual spread of 3.2 ms. Fixing the tempo at exactly
  155 BPM, the median residual in each 20 s window stays within ±1.3 ms from
  start to end, so there is no tempo ramp and no drift: one constant grid,
  beat 0 at 0.00982 s, 0.387097 s per beat.
- **Band energies per bar and per beat** on that grid: sub-bass (20–100 Hz),
  low (100–400 Hz), mid (0.4–4 kHz) and high (4–16 kHz). The arrangement
  changes in whole bars: the sub-bass enters, cuts or fades, and hats come in,
  always on the grid's downbeats with bar 1 at beat 0.
- **Spectrogram strips and onsets** from `report` (8 s strips, plus 2–4 s
  zooms at the breaks, bar 82 and 155 s) for the stabs, cuts and pickups.
- **The map's energy curve** (`edit` recomputes it per beat) for the peaks.
- **Chroma** (CQT on the harmonic part) for keys. It is weak here; see
  uncertain point 4.

## Form

4/4 throughout, 155 BPM, 129 bars (the last has 2 beats). Phrases are 8 bars,
with shorter ones where a section is shorter.

| Section role | Label | Bars | Bar count | Time (s) | Feel | Energy (mean) | Evidence |
|---|---|---|---|---|---|---|---|
| Intro | `intro` | 1–8 | 8 | 0.010–12.397 | pulsed | 0.12 | Kick and a bass hit on every downbeat, no sub-bass (about 50 dB below the drops), about 17 dB quieter overall. A noise layer enters at bar 4 (4.623 s, 32 ms ahead of the beat). Riser fill in bar 8. |
| First drop: the groove | `groove` | 9–22 | 14 | 12.397–34.074 | pulsed | 0.86 | Full kick and sub-bass land on bar 9's downbeat (+19.8 dB). Sparse mids. |
| Break | `break-1` | 23–24 | 2 | 34.074–37.171 | free | 0.35 | Kick and sub cut out. A syncopated synth figure plays off the beat, then a stab on bar 24 beat 3 (36.445 s, 48 ms after the beat) and a pickup at 37.095 s. |
| Main drop | `drop-1` | 25–48 | 24 | 37.171–74.332 | pulsed | 0.85 | Full arrangement, mids 4 dB above the groove. Bar 32 cuts the sub on beats 3–4. Hats come in at bar 33 (high band +4.5 dB, the biggest jump in the first half). Phrases 25–32, 33–40, 41–48. |
| Breakdown | `breakdown` | 49–56 | 8 | 74.332–86.719 | pulsed | 0.71 | The sub is pulled down 9 dB while the kick continues, about 5 dB quieter overall. Bar 56 cuts the sub on beats 2–4. |
| Second groove | `groove-2` | 57–72 | 16 | 86.719–111.494 | pulsed | 0.89 | Sub back on bar 57 (−5.7 dB), mids sparser than in the groove (−16 to −20 dB), filling in from bar 65. |
| Build | `build` | 73–78 | 6 | 111.494–120.784 | pulsed | 0.77 | The sub fades out bar by bar (−6.8 to −21 dB) under a rising noise riser, as mids and highs climb. |
| Pre-drop break | `break-2` | 79–80 | 2 | 120.784–123.881 | free | 0.30 | A mirror of break-1: no kick or sub, and the strongest stab of the song on bar 80 beat 3 (123.153 s), then a pickup at 123.807 s. |
| Second drop | `drop-2` | 81–104 | 24 | 123.881–161.042 | pulsed | 0.88 | The heaviest sub of the song lands at 123.881 s. Bars 81–88 are narrow and light in the mids. A hit lands 31–33 ms ahead of beat 3 in bars 82, 84 and 86 (the beat stays on the kick: uncertain point 3). Bar 88 cuts the sub on beats 2–4. From bar 89 the arrangement is full. Bars 96 and 104 cut the sub on beats 3–4. A stutter stop (155.05–155.13 s) is followed by an off-beat stab at 155.136 s (bar 101). Phrases 81–88, 89–96, 97–104. |
| Final peak | `final-peak` | 105–120 | 16 | 161.042–185.816 | pulsed | 0.92 | The main drop's full arrangement with the hats on top (high band 5 dB above drop-2). It is the highest-energy section (phrases 0.92 and 0.93; only drop-2's last phrase, 97–104, matches it at 0.93). Bar 120 is a fill. |
| Outro | `outro` | 121–126 | 6 | 185.816–195.107 | pulsed | 0.88 | Kick and sub continue while mids and highs drop back (by 3–4 dB). The last kick is bar 126 beat 4 (194.72 s). |
| Ending | `ending` | 127–129 | 2.5 | 195.107–198.968 | free | 0.17 | No kick and no sub. Two stabs fall on beat 3 of bars 127 and 128 (195.898 and 197.446 s; the second is the song's last hit), then they decay. The file ends 0.77 s into bar 129 with the tail still audible, about 26 dB below the drops. |

**The shape for choreography.** There are three arrivals after silence: bar 9
(the first drop, from the intro), bar 25 (the main drop, after break-1) and
bar 81 (the second drop, after the build and break-2). Smaller re-entries,
where the bass returns after a one-bar or half-bar cut, come at bars 33, 57,
89, 97 and 105. The final energy peak is `final-peak`, bars 105–120
(161.0–185.8 s). The song ends with six bars of outro groove and the two
closing stabs. It has no tempo ramp, no ritardando and no held closing chord.

## Uncertain points

1. **The section granularity.** `drop-1` (25–48) and `drop-2` (81–104) each
   cover an 8-bar lead-in and a fuller 16 bars. They are single sections with
   8-bar phrases because:
   - the lead-in and the full part are one drop musically;
   - drop-2's lead-in has the pre-beat hits (point 3), which put bars 81–88
     alone at 81% onset alignment, under `check`'s 90%.
   The phrases keep the split available to the level script.
2. **The bar phase.** madmom's downbeat tracker agrees from 0 to 34 s. From
   about 36.8 s it puts downbeats one beat early (on the map's beat 4), and
   from 123.1 s two beats off (on beat 3). This was rejected, because every
   arrangement change falls on the map's downbeats at the per-beat level:
   - sub entries at bars 9, 25, 33, 57, 81, 89 and 105;
   - cuts at bars 23, 32, 56, 79, 88, 96, 104 and 127;
   - the fade over 73–78.
3. **The pre-beat hits in drop-2** (bars 82, 84 and 86, beat 3). A broadband
   onset 31–33 ms before the beat is stronger than the kick, so the onset
   peak picker keeps it and drops the kick. The mid and high attack of the
   kick itself sits on the grid, as on every other beat. The beat was not
   moved: moving it would break the constant grid (an 8% gap change).
4. **Keys are not in the map.** Chroma is weak in the drops: a G♯ bass drone
   gives a best key-profile correlation of only 0.40–0.47 (G♯ major). The
   intro leans E minor (0.77) and the breaks E major (0.50–0.69). These are
   too uncertain to tune pitched effects to.
5. **What the stabs are.** "Stab" describes the spectrogram: a broadband
   attack with harmonics at 1–4 kHz and a short tail. They may be chopped
   vocals (the track features a singer), but that wasn't checked by ear. The
   operator's click-track check (in the plan's Pending on the operator table)
   covers it.
6. **The incomplete last bar.** Beats stop at 198.590 s, the last grid beat
   before the end of the file, so bar 129 has 2 of its 4 beats and `check`
   warns. Extending to a full bar would put beats after the end of the file,
   which `check` rejects.
