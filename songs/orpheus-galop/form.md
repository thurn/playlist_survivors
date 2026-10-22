# Orpheus galop: expected form

Recording: Musopen, *Orpheus in the Underworld* overture (Carl Binder
arrangement), Wikimedia Commons. Excerpt: 415.200–559.800 s of the source
(`public/audio/orpheus-galop.flac`, 144.6 s). All times below are seconds in
the excerpt; add 415.2 for the source.

## How this was worked out

**Not from the score.** The IMSLP page for the Binder overture
(`Overture_to_Offenbach's_opera_'Orphée_aux_Enfers'_(Binder,_Carl)`, full
score #18572 and parts #50047–50058) is reachable, but every PDF download goes
through a JavaScript "friendly redirect" page that acts as bot detection. The
analysis agent does not get around bot checks, so it could not read the score.
Someone with a browser can download the score and confirm the bar counts below
(the parts, for example Violin I #50056, are the quickest to count).

**From structural analysis of the audio instead:**

- Bar-synchronous chroma self-similarity (with and without transposition) to
  find repeats: theme A repeats after exactly 16 bars, and its return is the
  same 32 bars transposed up a fifth (mean bar similarity 0.97 at an offset of
  54 bars with a +7 semitone shift, against 0.86 untransposed).
- Bass chroma per bar and per beat for the harmony and the keys (tonic and
  dominant alternate bar by bar in both theme statements).
- RMS loudness per bar for the dynamic blocks (the quiet contrasting theme is
  about 12 dB below the themes around it).
- Constant-Q spectrogram strips and onset strength for the attacks (the four
  opening flourishes, the final chord hits).
- madmom's downbeat tracker as an independent check of the bar phase. It
  agrees with every downbeat of the final map from bar 15 to the end. From
  bar 156 it tracks one beat per bar, and those beats land on the map's
  downbeats, including the closing hits.

## Form

| Section role | Label | Bars | Bar count | Time (s) | Key | Evidence |
|---|---|---|---|---|---|---|
| Slow lyrical passage | `prelude` | 1–14 | 14 (4/4) | 0.15–43.2 | E major | sustained strings at ~85 BPM; phrase starts at 0.15, ~11.0, ~25.8; it fades out at ~30.3 s, then a sparse, quiet rubato transition (31.6–43.2) |
| Galop vamp (quiet introduction) | `galop-vamp` | 15–30 | 16 (2/4) | 43.2–54.07 | D major | soft bass-and-chord accompaniment with a crescendo; two 8-bar phrases (chroma repeats after 8 bars) |
| Galop opening chords | `galop-opening` | 31–53 | 23 | 54.07–69.82 | D major | first fortissimo tutti at 54.066; four chord + descending-run flourishes at bars 31, 35, 39, 43 (54.07, 56.78, 59.53, 62.30); the fourth is cut short and runs into 4 bars on A (46–49) and 4 bars on D (50–53), a dominant preparation for G |
| Theme A | `theme-a` | 54–69 | 16 | 69.82–80.23 | G major | bass alternates G/D by bar; tonic arrives at bar 54 |
| Theme A repeat | `theme-a-repeat` | 70–85 | 16 | 80.23–90.53 | G major | bar-for-bar repeat of bars 54–69 (chroma similarity 0.98) |
| Contrasting theme | `kick-line` | 86–101 | 16 | 90.53–100.65 | D major | sudden drop to about −29 dB; an 8-bar idea (86–93) repeated (94–101); the repeat gains a crescendo over an A pedal from bar 98 |
| Bridge (retransition) | `bridge` | 102–107 | 6 | 100.65–104.55 | D major (on its dominant) | continuing crescendo on the A pedal into the return |
| Theme A return (climax) | `climax` | 108–139 | 32 | 104.55–124.63 | D major | theme A plus its repeat, transposed up a fifth |
| Coda | `coda` | 140–177 | 38 | 124.63–144.39 | D major | 2-bar sequence (140–146), A pedal (146–153) with a stringendo from about 186 to 250 BPM, descending run (154–161), cadence, four tutti hits on the downbeats of bars 165–168 (138.08, 138.55, 139.04, 139.58); the last is the held final chord (released at about 142.3 s), then about 2 s of decay |

Galop tempo: about 176 BPM in the opening, 185–189 BPM in the themes,
193 BPM in the climax, then accelerating to about 250 BPM in the coda
(quarter notes, 2/4).

Phrases are recorded in `songmap.json`. They are 8 bars in the themes and 4
bars in the opening flourishes. The prelude's phrases are approximate, because
its beat grid is approximate.

## Uncertain points (confirm against the score)

1. **`galop-opening` is 23 bars.** The fourth flourish seems truncated to 3
   bars (43–45) before the A/D preparation (46–53). An odd count like this
   could also come from a written bar that is held longer (for example a
   fermata) without the tracker noticing. The downbeat tracker agrees with the
   phase on both sides, so any such error would add or remove a whole bar.
2. **Where `kick-line` ends and `bridge` begins.** The audio supports either
   16 + 6 bars (86–101, 102–107: the 8-bar periodicity, used here) or 12 + 10
   bars (86–97 quiet, then 98–107 crescendo, where the texture changes).
3. **The keys of theme A (G) and the return (D)** come from the bass line. A
   key-profile match leans a fifth higher in both, which is the usual bias
   when tonic and dominant alternate.
4. **Coda bar phase near the end.** The beat tracker drifted by half a bar in
   bars 158–164 (its tempo was too slow during the descending run). The map
   pins bars 158–168 to the strong onsets and assumes the four closing tutti
   hits fall on downbeats. That keeps the tempo steady (about 0.48 s per bar)
   and agrees with the downbeat tracker. The other reading, hits on beat 2,
   would need a sudden held bar that the audio does not show.
5. **The prelude meter (4/4) and its beat grid** are approximate by design (a
   free section). The beats in the quiet transition (31.6–43.2) are evenly
   spaced at about 0.86 s, because the music there is sparse and rubato.
6. **LP clicks.** The excerpt contains vinyl clicks, for example at about
   36.97 s, which show as isolated onset spikes. No anchor or accent was put
   on them.
