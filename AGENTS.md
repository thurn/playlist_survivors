# Working on Playlist Survivors

Implement every prompt in its own Tollgate worktree, never by committing on master. Create one with `tg worktree create <name>`, run `npm ci` in it, make the change, validate with `scripts/ci`, and immediately commit using Conventional Commits syntax. Then deliver it without asking: submit with `tg candidate HEAD` in the worktree, then from the main checkout run `tg approve <candidate-id> --wait`, which validates the candidate and promotes it to remote master. Tollgate owns pushes to remote master.

## Features

Executor sessions in Hive project `playlist_survivors` work from beads.

- **Delivery.** Every change, including feature beads, delivers through an independent Tollgate worktree (`tg candidate`, then `tg approve --wait`). Never commit on master or use `tg push-master`.
- **Worktrees.** Use a fresh worktree per prompt or bead. Run `npm ci` in each new worktree before running checks. Run Tollgate waits from the main checkout, since promotion removes the clean worktree.
- **No subagents.** Never dispatch a subagent for implementation, review, QA or research, and never shell out to another agent CLI.
