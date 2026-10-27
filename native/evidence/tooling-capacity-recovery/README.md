# hv-n7fn.37 — work survives model-capacity interruptions

Two monitor-reported external model-service occurrences are recorded in the native tooling bead: assets/app executor `01a17841-b0f1-7cd3-85e6-f659e8ba5ad1` during SF11, then sim executor `01a1765f-cbc2-75b2-8fd6-fee82750b02c` during SH6. The exact reported host message was `Selected model is at capacity. Please try a different model.` This was not a shell command or application failure. `capacity-native-record.json` retains the source reports. No new recurrence was observed while handling this bead; provider capacity was not experimentally reproduced, queried or changed.

The native reports record that worktrees and claims survived and that the original check processes were collected without launching duplicates. SF11 resumed its full checks, fixed an independent asset-test assumption and completed; SH6 resumed its app/Live checks and fixed independent new lunge-fixture failures. Those implementation failures are not attributed to provider capacity.

Current native status and exact candidate certificates corroborate that the owned work was preserved and delivered:

| Bead | Source | Tested/landed | Final candidate |
| --- | --- | --- | --- |
| SF11 `hv-n7fn.11` | `57d2622f773213014deb8750f66c28aa3ad3f6a5` | `de2ea4f0532c6ac6a5e1a9c71cd83ad3601049f5` | `01a1785a-73bf-7e90-b34e-bbddbae28484` |
| SH6 `hv-n7fn.17` | `b99d7eeceb9d89f35151e791847d019faf26c91a` | same | `01a178b4-00d7-7b23-be1a-f06f471f663c` |

Both have native `hive_resolution=completed`, terminal promoted candidates and tested OIDs verified as staging ancestors. Their retained evidence files are SHA-256 indexed in `summary.json`, and the native notes/comments plus exact candidate receipts are kept here. These records do not prove absence of every other attempt or provide provider telemetry; the no-duplicate-check statement is the contemporaneous executor report. There is no reason to repeat those settled checks or deliveries now.

AGENTS.md now makes the local recovery order explicit: inspect claim/worktree/check handles/candidate, collect surviving jobs, reconcile uncertain outcomes, then continue the same owned assignment. Record checkpoint and handles while retaining ownership; add a later exact host message to the matching tooling bead. This preserves the existing executor workflow rather than changing game code or building a substitute model service. Future unrelated recurrence can be filed as its own follow-up after this bead closes.

Inspection commands were native `bd show`/`bd comments` for the two beads with the configured Beads routing, `tg status <candidate> --json`, and `git merge-base --is-ancestor <tested-oid> staging`. There is no command that safely forces a real capacity outage, so no synthetic provider failure or artificial process test is represented as incident proof. No operator-only check, app window, new gameplay measurement, cold review or full local CI is required for this guidance/evidence change. Its final `scripts/check-fast`, source SHA and staging promotion are recorded in the tooling bead notes.
