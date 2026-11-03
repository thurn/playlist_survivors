# Walking F sword cut

Measured on November 3, 2026 from staging `2d580bad` (before) and this change. All recordings are local. The black space behind an F cut now comes from killing the bodies its blade reaches. In the same moving encounter at song 5.08 s, the old F killed two enemies and the new F killed eight. The nearest surviving body's edge was about 9 cm beyond the new footprint at the next settled tick.

## What changed

The old F entered the tail of the 1.43 s HeavyFlourish wind-up and left the running legs almost entirely out of the cut. The held crystal was 1.15 m long. A separate two-layer ring and six bursts began on damage and took another 0.18 s to sweep around the impact point. Its reach was that of Space. The trailer reference alternates body cuts, keeps the grip fixed to the right fist and times a local blade flare and crescent to contact. I used the supplied neon action cut around 24–28 s and read the frozen choreography, HeroBlade and sound baker.

F now starts an authored forehand or backhand at key-down, blending in over 32 ms. The legs carry part of the cut while they run. The chest coils and follows through, the blade lengthens and widens about the posed grip, and the original blade returns as the cut recovers over 0.22 s. Damage lands once at the hero's actual position 0.15 s after acceptance. Base radius is 4.2 m, up from 3 m. The timed area bonus produces a 5.94 m cut. Sword and Landing Blast upgrades still apply. Space keeps its movement, landing spin and original radius. The cooldown, held-key and buffer rules, shield and aura use the existing paths.

A single directional arc crosses the measured contact radius. Its head starts 75 ms before impact where the live simulation has notice. Body, sound, damage, shatter and surviving-target hit response use the same impact time. The first revised capture showed an oversized luminous rim around a thin blade. The final pass widened the crystal during extension and reduced the ring's gain, width and linger.

## Listening to the actual mix

The old path requested both a sword sound containing an armor hit and a heavy punch on *every* landing, including empty ground. It produced no blade-air sound on the accepted F press. The impact also shared the per-beat aggregation with other sounds. A single old spin's heavy cue began at only 0.6 gain multiplied by the aggregation, bank level, effects bus and music follower. Absence of audio stalls in the operator's old log could not establish which cue was audible.

Each accepted F now plays one pitched air sound. Real contact chooses a body cut or a lower punch for three or more contacts. Both use the existing music-following bus directly. There is at most one contact voice per accepted F, and heard-time inputs that arrive behind the DSP scheduler play as soon as the mixer allows. The timed reward retains its accent. Empty ground has no contact cue. Kill sounds still aggregate independently.

To check what reached the speakers' mix, I aligned each recording's captured application audio to the exact input soundtrack. Over the 100 ms starting at 5.08 s, the RMS of the remaining audio relative to the music rose from −9.8 to −0.9 dB. At 8.43 s it rose from −17.9 to −0.2 dB. These are whole-mix measurements including concurrent shatters, other effects and AAC residual. Matching the actual pitched punch against the residual at 8.43 s gave correlation 0.87 and onset within 1 ms of the damage timestamp. The individual body cut at 9.31 s matched at contact, and the two-body cut at 16.00 s matched within 2 ms. Press-to-air timing is less exact. This device reported about 100 ms of output latency, and the noise clips have weaker waveform matches. The body starts before that heard-time sound can reach the output.

## Gameplay-speed evidence

The untracked directory `~/Library/Caches/playlist-survivors/evidence/ps-oli1/` contains the actual recordings, sessions, explicit player logs and local analysis recipes. `take.py` launches the Mono player with windowed 1280×720 Medium, seed 31, reference card picks, the trailer soundtrack through the music plugin and timed key transitions. ScreenCaptureKit records the app's own window and 48 kHz mix. The reference and the before/final motion were played back at 1×. The cropped frame sequences and waveform checks supplement those movies.

- `before.mov` has 617 frames and 1,059 captured audio buffers. `after1.mov` is the initial revised cut. `final.mov` has 612 frames and 1,052 buffers. For the final, subtract approximately 0.551 s from song times to locate the movie time. For the before, subtract 0.421 s.
- `walking-white.mov` is a second run without prior empowerment. At song 9.16 s the hero presses F while walking. The 4.2 m white cut lands at 9.31 s and hits nothing. The nearest body's edge was about 0.33 m outside at settlement. A later buffered F kills four. Subtract 0.746 s to locate the movie time.
- The scripts used separate explicit log paths. These were short automated runs. The surviving excerpts of the operator's display-freeze run contained no per-swing evidence, and none of the experimental worktree's play was used for this comparison.

| Final, song seconds | Observed action |
| --- | --- |
| 1.720 → 1.870 | Empowered walking whiff. The sword sweeps and recovers without a body hit. |
| 4.930 → 5.080 | Moving backhand reaches eight bodies. The bodies break at contact. |
| 8.280 → 8.430 | An automatic attack is due at the press. F takes the body and cuts five. The extra press at 8.410 falls in cooldown and produces no second cut. |
| 9.160 → 9.310 | Off-beat walking F, with the prior aura still active. One body is hit. |
| 10.220 → 10.370 | Space travels 4 m and uses its own 4.24 m empowered sweep. |
| 10.740 → 10.820 → 10.970 | F buffers during the Space cooldown, starts at readiness, then hits one body. |
| 15.850 → 16.000 | Stationary F, aura expired. The 4.2 m ordinary cut kills two. |
| 16.650 → 16.800 | Timed stationary F, 5.94 m radius, empty ground. A surviving body's edge is roughly 0.59 m outside at settlement. |

The before recording replays on its original code at `14E700BE43D496C0`, 914/914 events. The final recording replays identically in .NET and Mono at `AEA6C0E9548D2E27`, 980/980 events, with no presenter faults. The walking-white recording matches in .NET at `6AE29FDAC5B10569`, 603/603 events. The larger F footprint changes historical F outcomes. Input codes and the session file format are unchanged.

All 506 .NET tests and 143 Unity EditMode tests passed. They exercise the footprint boundary, surviving strong targets, actual contact count, held/repeated keys, shared buffering, pause/resume, shield expiry, the posed grip, both hands of the cut, subsequent automatic attacks, and distinct empty/single/crowd sound routing. The recordings cover short opening encounters on one Mac and audio route. Operator hand-play, late-run balance, other speakers and controllers remain unmeasured.
