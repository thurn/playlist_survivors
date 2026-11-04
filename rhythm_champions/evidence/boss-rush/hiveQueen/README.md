# Boss Rush fight 02: the Hive Queen (ps-8z4.8)

She is boss 2 of act 1, *Swarm formations* ([boss-rush.md](../../../../docs/champions/boss-rush.md), The roster). Her
module is `Sim/Rush/HiveQueenModule.cs`, her arena is `hiveClearing` in `Content/arenas.json`, and her effects are in
`Client/Bosses/HiveSwarmPresenter.cs`.

## Stills

Picked from `champions boss review hiveQueen --motion` (2026-11-04, with the mesh bead ps-8z4.9's rebuilt bee). The review plays the fight alone in her clearing: 120 BPM, a hero with six weapons wandering, 6,000 health over
60 s. That is too short to reach her phase two, so phase two shows only in the sim tests. The rest of the 268 stills,
and the motion frames, stay out of the tree; rerun the review to get them. An MP4 of the motion frames (4–12 s) is at
`captures/hiveQueen-review-motion.mp4` in the main checkout. It isn't committed.

- `entrance.jpg`: she arrives, a ring of drones spiralling into her over the entrance's four beats.
- `attack-01-sweep.jpg`: the signature, taught alone. A row of drones, its telegraph filling along the wall behind her,
  its ends bent along the walls beside it, with walking-width gaps.
- `attack-03-chevron.jpg`: the chevron, its point leading, with a gap at the point.
- `attack-04-dive.jpg`: she flies to the hero's marked spot and stings it on the downbeat.
- `attack-06-spiral.jpg`: rings of five drones out of her body, one a beat, each turned on from the last.
- `attack-08-wall-dash-wave.jpg`: the dash wave, a gapless row on a 4-beat telegraph, faster than walking.
- `attack-10-net.jpg`: the net round the hero's spot. Next to a wall, only the sides inside the clearing are laid.
- `t-20.jpg`, `t-40.jpg`: mid-fight frames, the honeycomb between pulses.
- `motion-sheet.jpg`: 12 of the motion frames, 4 to 12 s: the entrance, the first sweeps flying and the dive.

## The fight

- **Clearing**: a flat-topped hexagon, 17 m from centre to corner, with a lantern or stump in each corner. The camera
  holds it whole and still (`TheCameraHoldsHerClearingWhole`). Her drones' honeycomb is drawn on the floor in her magenta
  and pulses on every beat.
- **Order**: sweep, sweep, chevron, dive, then sweep, spiral, dive, wall, chevron, net, dive, sweep in turn. Below half
  health she swarms to the centre, untouchable for 4 beats. Phase two cycles a sweep under a dive, two crossing sweeps,
  the wall, the spiral (7 rings), the net round a dive, the chevron, the wall, the sweep under a dive and a dive. Rows
  cross in 5 beats instead of 6, and she rests 1 beat instead of 2.
- **Rows**: 3.4 m gaps every 8.5 m along the row, placed fresh each time. A row starts just inside the walls it
  launches from: along the near wall, and bent forward along the two walls beside it. It is laid in pieces of at most
  2 m, and each piece breaks off once it is wholly past the walls, so no drone fills or flies far outside the clearing.
- **Damage**: drones 12, dive 18, spiral 10, net 12, wall 15 (hero health 100).
- **Health**: the slot's 735 (targetS 122.5 s × the measured sustained damage). The pacing bead re-measures it against
  the real fights.

## Verification

- dotnet `HiveQueenTests`, 10 tests, all pass:
  - **Fairness** at 90 and 180 BPM in her clearing. At 90 BPM: 115 hazards, 16 attack sequences, 3 dash waves, every
    one needing the dash. At 180 BPM: 222 hazards, 31 sequences, 5 dash waves. Both fights finish.
  - **The rest**: the teaching order; the gaps; phase two's untouchable four beats and combinations; the beatless
    fallback (3 × 0.5 s warnings, and she can still be beaten); same seed, same fight; her clearing's framing and
    geometry; her slot is a landed fight in her arena; her XP bursts.
- **The whole suite**: 576 tests pass. BossRushTests now kills either a landed fight or a stand-in, since boss 2 is a
  real fight.
- **Shaders**: no new shader. She uses BossEdge's floats (Flow 0.7, FlowSpeed 1.6, Scan 0.25) and the world's Glow
  material for the drones and cells, so no new variants.
- **Sound**: her bank in `sounds.json` uses the Universal Sound FX ALIENS insects, added to the manifest. Each
  telegraph tick plays 15% higher in pitch than the last (`HiveQueenModule.TickRise`).

## Notes

- The source registration landed first with the mesh bead (ps-8z4.9): its measured impacts, its size and magenta.
  This fight renamed its clip roles to the ones the boss animator plays: smash (Sting Attack, the dive),
  attack (Cast Spell, the formations), stomp (Claw Attack), roar (Defend, the entrance).
