# The sim's test vectors

The `gen-*` generators import the TS game, which D6 removed: they run on the `ts-freeze` tag.
From a checkout of it (`git worktree add ../ts ts-freeze`, then `npm ci` there), copy the
generator into the same path and run it as its header says; the vectors it writes are reference
data (AGENTS.md, "Prototype, not specification"), rewritten or dropped when a deliberate change
moves them.
