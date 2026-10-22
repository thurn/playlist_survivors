# Working on Playlist Survivors

Implement every prompt in its own Tollgate worktree, never by committing on master. Create one with `tg worktree create <name>`, run `npm ci` in it, make the change, validate with `scripts/ci`, and immediately commit using Conventional Commits syntax. Then deliver it without asking: submit with `tg candidate HEAD` in the worktree, then from the main checkout run `tg approve <candidate-id> --wait`, which validates the candidate and promotes it to remote master. Tollgate owns pushes to remote master.

## Features

Executor sessions in Hive project `playlist_survivors` work from beads.

- **Delivery.** Every change, including feature beads, delivers through an independent Tollgate worktree (`tg candidate`, then `tg approve --wait`). Never commit on master or use `tg push-master`.
- **Worktrees.** Use a fresh worktree per prompt or bead. Run `npm ci` in each new worktree before running checks. Run Tollgate waits from the main checkout, since promotion removes the clean worktree.
- **Ports.** Port 5199 (and preview port 5198) belongs to the operator's own testing in the main checkout: never start, stop, reuse or browse a server on it. In a worktree, `npm run dev`, `npm run preview`, Playwright and `scripts/lib/server.ts` pick that worktree's own port automatically; `npm run port` prints it. Never pass `--port` to Vite, and don't use the `demo` launch config from a worktree, since it expects port 5199.
- **No subagents.** Never dispatch a subagent for implementation, review, QA or research, and never shell out to another agent CLI.
- **Play logs.** When the operator reports how play mode felt (timing, presses that did nothing), read their timing logs before guessing. Their dev server saves them to `logs/play/<session>.json` in the main checkout (`/Users/dthurn/playlist_survivors/logs/play/`, gitignored, so a worktree has none). Summarize one with `npm run log:play -- <path>` (newest log if no path; `--from`/`--to` in song seconds, `--no-table` for the summary only). A log the operator downloaded with F8 works the same way. See `src/live/log.ts` for what it records.
