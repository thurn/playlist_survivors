# W cold review index

Each row is the landed/tested SHA recorded in that bead’s native notes/comments. Review each patch against its own parent, not the combined chronological range, which also contains H/tooling work.

| Key | Bead | Landed SHA | Parent |
|---|---|---|---|
| SW1 | hv-n7fn.23 | `5d26c7dda3505819382024c265e9564068c54fa7` | `2ce6daf5447c95d4f6e9c96f9bada636d3502efa` |
| SW2 | hv-n7fn.24 | `8e5320736e06e954f10435b74a68c9cccb8327e5` | `5d26c7dda3505819382024c265e9564068c54fa7` |
| SW3 | hv-n7fn.25 | `c720ce8d25761d0ede095cd4b16acdd4defed27e` | `8e5320736e06e954f10435b74a68c9cccb8327e5` |
| SW4 | hv-n7fn.26 | `fba3ff99418424866a6f4d8a3d813590d860c2e4` | `12d4e39b75d03dc4a9b83b9fc62fa21eddf3cb67` |
| SW5 | hv-n7fn.27 | `4b26d5429c68454ef2b3f0e6bf008573dc30827d` | `c5162345fb14a5e2834836e7e98c18338a384446` |
| SW6 | hv-n7fn.28 | `0f1d9ebf85d1aa2049a92da9fd431c29f0907533` | `4b26d5429c68454ef2b3f0e6bf008573dc30827d` |

SW5 source d76ab3fabaa5474dd37f53b49993828b7ab9e40c was rebased by Tollgate; review landed4b26d542 as the bead’s clarification records. SW6 source e8a4b11393ccc862bc2ee0620d7a85776d25ea6a was rebased atop SW5. Context: curated swarm source c5162345fb14a5e2834836e7e98c18338a384446, SH10 chests12db4a0a34ad38ed77672862799165510c28767b, SH11 corrected offers12d4e39b75d03dc4a9b83b9fc62fa21eddf3cb67.

SW7 current uncommitted source diff against0f1d9ebf: content/offers.yaml, existing offer-capacity tests, and scripts/ci-full enabling --assert-survival. Original easy knobs restored unchanged; no runtime/rollback-runner edit. Full local CI is running, its final matrix/replay evidence will be added before landing.

Post-review fixes: one source-cadence line in engine/weapons.rs plus speed/pause regressions, the chest-report identity/origin helper, and bounded actual-card live fixtures adapted to the new acquisition order. See review.md; no confirmation review.
