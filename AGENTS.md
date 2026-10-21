# Working on Playlist Survivors

Validate with `scripts/ci`. Immediately commit all changes using Conventional Commits syntax, then deliver them through Tollgate: on master run `tg push-master --wait`; in a Tollgate worktree submit with `tg candidate HEAD`, approve, and wait for promotion. Tollgate owns pushes to remote master.

## Features

Executor sessions in Hive project `playlist_survivors` work from beads.

- **Delivery.** Feature beads deliver through Tollgate worktrees (`tg candidate`, then `tg approve --wait`), never by committing on master. `tg push-master` is for the operator's own direct changes.
- **Worktrees.** Run `npm ci` in each new worktree before running checks. Run Tollgate waits from the main checkout, since promotion removes the clean worktree.
- **No subagents.** Never dispatch a subagent for implementation, review, QA or research, and never shell out to another agent CLI.
