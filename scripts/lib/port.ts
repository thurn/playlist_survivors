/**
 * Dev and preview ports for this checkout. The main checkout owns 5199 (dev) and
 * 5198 (preview) for the operator's own testing; each git worktree gets its own
 * stable pair in 5200–5999, derived from its path, so parallel worktrees never
 * share or steal a server. `PORT` overrides the dev port (preview is PORT + 1),
 * except that a worktree may never take the operator's ports.
 */
import { createHash } from "node:crypto";
import { statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const OPERATOR_DEV_PORT = 5199;
const OPERATOR_PREVIEW_PORT = 5198;
const WORKTREE_BASE = 5200;
const WORKTREE_PAIRS = 400;

/** A linked worktree has a `.git` file pointing at the main repository; the main checkout has a `.git` directory. */
function isWorktree(): boolean {
  try {
    return statSync(join(ROOT, ".git")).isFile();
  } catch {
    return false;
  }
}

export function devPort(): number {
  const worktree = isWorktree();
  const env = Number(process.env.PORT);
  if (Number.isInteger(env) && env > 0) {
    if (worktree && (env === OPERATOR_DEV_PORT || env === OPERATOR_PREVIEW_PORT)) {
      throw new Error(`Port ${env} is reserved for the main checkout; unset PORT to use this worktree's own port.`);
    }
    return env;
  }
  if (!worktree) return OPERATOR_DEV_PORT;
  const pair = createHash("sha256").update(ROOT).digest().readUInt32BE(0) % WORKTREE_PAIRS;
  return WORKTREE_BASE + 2 * pair;
}

export function previewPort(): number {
  const dev = devPort();
  return dev === OPERATOR_DEV_PORT ? OPERATOR_PREVIEW_PORT : dev + 1;
}
