# W cold review and disposition

Fresh read-only same-host subagent /root/sw7_warden, warden skill, no inherited author conversation, no edits/builds/window. Scope is the recorded six W patches plus SW7 diff in review-index.md; self-check recorded before dispatch in native Beads and self-review.md. No confirmation review.

One P2: engine/weapons.rs burst offsets omitted source Weapon.PSpeedRepeatInterval division by effective projectile Speed (docs/survivors/appendix-feel.md:77–78). Holy Wand Speed2 used100ms instead of50ms; Duplicator2 produced500ms rather than250ms burst. Confirmed and fixed using captured shot.speed.max(0.001). Regression checks six shots at source Speed2 and non-default Speed4, and fractional pause preserves the remaining25ms. All12 evolution tests pass. Existing base Wand Speed1 is unchanged. No data/cooldown/throughput rule retuned.

Reviewer reported no other confirmed defects across W/current offers/assertions/additive chest-report repair. Final full-run/replay evidence was pending at review; author must complete it before landing.

The initial full CI found two test-fixture assumptions about the old offer order (Lightning acquired later; Garlic8 pushes/kills targets before Bible contacts). Their replacement remains actual XP/cards/keys and normal record projection, with a bounded synthetic schedule: XP carriers plus a durable large knockback-immune target, authored before Engine construction. This is mechanism coverage, never balance/pacing evidence. All test writers must settle before final candidate.
