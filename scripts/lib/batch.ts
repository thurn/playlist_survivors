/**
 * Long local batch jobs run below Tollgate's gate and release steps (hv-t8o8.55). On macOS the job
 * re-runs itself under `taskpolicy -b` (background QoS: efficiency cores, throttled I/O), which
 * its child processes inherit. A gate measured 43 s with a 10-job oracle export running under it
 * against 65 s with the export at normal priority (43 s idle); `nice -n 10` made no difference.
 */
import { spawnSync } from "node:child_process";

export const BATCH_ENV = "PS_BATCH";

/** Re-run this process in the background QoS class and exit with its status; a no-op elsewhere, or once applied. */
export function runAsBatch(): void {
  if (process.platform !== "darwin" || process.env[BATCH_ENV]) return;
  const r = spawnSync("/usr/sbin/taskpolicy", ["-b", process.execPath, ...process.execArgv, ...process.argv.slice(1)], { stdio: "inherit", env: { ...process.env, [BATCH_ENV]: "1" } });
  // (Without taskpolicy, run at normal priority.)
  if (r.error) return;
  process.exit(r.status ?? 1);
}
