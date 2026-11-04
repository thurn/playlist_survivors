# Boss Rush fight 04: the Rootwarden (ps-8z4.12)

It is boss 4 of act 1, *Echo eruptions* ([boss-rush.md](../../../../docs/champions/boss-rush.md), The roster). Its
module is `Sim/Rush/RootwardenModule.cs`, its arena is `rootHollow` in `Content/arenas.json`, and its effects are in
`Client/Bosses/RootwardenPresenter.cs`.

## Stills

Picked from `champions boss review rootwarden --motion` (2026-11-04, the vendor Treant: its mesh bead ps-8z4.13 hadn't
landed). The review plays the fight alone in its hollow: 120 BPM, a hero with six weapons wandering, 6,000 health over
60 s. That is too short to reach its phase two, so phase two shows only in the sim tests. The rest of the 268 stills
and the motion frames stay out of the tree; rerun the review to get them.

The stills were rendered with the roots in violet; it is cyan since (the Bloom landed violet first), and the
mesh bead's review shows the colour as it is now.

- `entrance.jpg`: it arrives, roots in its hue crawling in to its feet from round the hollow.
- `attack-01-echo.jpg`: the signature, taught alone. Each beat marks the hero's spot; the earlier marks fill behind the
  hero, a root stroke crawling from the boss to each.
- `attack-03-line.jpg`: the line, eruptions racing out from it toward the hero's spot, one a half beat.
- `attack-05-seeds.jpg`: five seeds lobbed round the hero's spot, sprouting together.
- `attack-06-burrow.jpg`: it has sunk under the floor and travels to the hero's marked spot; the burst and the ring
  that spreads a beat after it.
- `attack-08-quake-dash-wave.jpg`: the quake's telegraph at its feet, the ring of roots that outruns the walk.
- `t-30.jpg`, `t-50.jpg`: mid-fight frames.
- `motion-sheet.jpg`: every 20th motion frame, 4 to 12 s: the entrance and the first echoes erupting in spikes.

## The fight

- **Hollow**: a round clearing 17 m from centre to wall, its middle open, its props (two broken oaks, logs, rune
  stones, stumps, mushrooms, four lanterns) standing back by the wall. The camera holds it whole and still
  (`TheCameraHoldsItsHollowWhole`).
- **Order**: echo, echo (taught alone), then line, echo, seeds, burrow, echo, quake in turn. Below half health it draws
  back to the centre, untouchable for 4 beats. Phase two cycles the chain, a fan of three lines, the chain under seeds,
  the burrow, the quake (two rings), the chain under a line, seeds (seven) and the chain. It rests 1 beat instead of 2.
- **The echo**: 6 marks a beat apart (8 in phase two), each erupting 3 beats after it was marked, 1.5 m across, its
  roots up half a beat. Chained, each eruption after the first is a root 1 m either side of the hero's path from the
  last marked spot to this one, so the chain joins end to end along the hero's trail.
- **Damage**: echo 10, line 12, seeds 12, burrow burst 18 and its ring 10, quake 16 (hero health 100).
- **Health**: the slot's 1,441 (targetS 127.5 s × the measured sustained damage). The pacing bead re-measures it.
- **Bursts**: gems strewn along the hero's last six beats' spots, where the roots have been.

## Verification

- dotnet `RootwardenTests`, 12 tests, all pass:
  - **Fairness** at 90 and 180 BPM in its hollow (the arena formed, the props blocking). At 90 BPM: 139 hazards, 19
    attack sequences, 2 dash waves, each needing the dash. At 180 BPM: 293 hazards, 37 sequences, 4 dash waves, each
    needing the dash. Both fights finish.
  - **The echo**: each eruption on the hero's spot when it was marked, 3 beats later, one a beat; a hero standing still
    through the taught echoes is hurt, one walking a wide circle never is.
  - **The rest**: the teaching order; the burrow (buried as it travels, risen at its burst, on the marked spot, its
    ring); phase two's untouchable four beats, the chain joined end to end, its move order; the beatless fallback
    (3 × 0.5 s warnings, and it can still be beaten); same seed, same fight; its hollow's framing and open middle; its
    slot is a landed fight in its arena; its bursts along the trail.
- **The whole suite**: 602 dotnet tests pass; Unity EditMode 153/153.
- **Review**: the player ran the fight with no exceptions (after the animator fix below).
- **Shaders**: no new shader. It uses BossEdge's floats (Flow 0.55, FlowSpeed 0.6, Ember 0.35) and the world's Glow
  material for the roots and spikes, so no new variants.
- **Sound**: its bank in `sounds.json` uses Universal Sound FX wood (snaps, cracking trees, wood destruction), added to
  the manifest. Each telegraph tick plays 10% higher than the last (`TickRise`).

## Notes

- This fight registered the source (the Green Treant prefab, its Root and Projectile Seed prefabs, the clips, in
  manifest.json; the `rootwarden` type in monsters.json with impacts measured in Blender from the arms' motion; the
  boss cast row and its hue, first violet, then cyan on rebase, since the Bloom landed violet just before it). Clip roles: smash (Root Attack, the line), stomp (Stomp Attack, the quake), throw
  (Projectile Up Attack, the seeds), roar (Cast Spell, the entrance).
- Framework additions: `BossModule.Sunk` (a body's share under the floor; `BossPresenter` lowers it). The fairness
  harness now judges a half beat landing after the last beat it heard (the fight died within that beat).
- Fixed: `BossAnimator.Landed` threw on a move with no clip (`BossMove.Hazard`: these echoes, the goblin's bombs),
  faulting the boss presenter for the rest of the fight.
- While burrowed its body can still be struck; the window is three beats and it keeps its health bar honest.
