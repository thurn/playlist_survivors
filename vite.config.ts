import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { Plugin } from "vite";
import { defineConfig } from "vitest/config";
import { devPort, previewPort } from "./scripts/lib/port.ts";

/** A hash of the planner and runtime sources: cached plans from older code are never used. */
function buildVersion(): string {
  const h = createHash("sha256");
  const walk = (dir: string) => {
    for (const f of readdirSync(dir).sort()) {
      const p = join(dir, f);
      if (statSync(p).isDirectory()) walk(p);
      else h.update(p).update(readFileSync(p));
    }
  };
  walk("src");
  walk("content");
  return h.digest("hex").slice(0, 16);
}

/** Play mode's timing log: the dev server saves each session's log to logs/play/<id>.json (see src/live/log.ts). */
function playLog(): Plugin {
  return {
    name: "play-log",
    apply: "serve",
    configureServer(server) {
      server.middlewares.use("/__playlog", (req, res) => {
        const id = new URL(req.url ?? "", "http://x").searchParams.get("id") ?? "";
        if (req.method !== "POST" || !/^[\w-]{1,64}$/.test(id)) {
          res.statusCode = 400;
          res.end();
          return;
        }
        const chunks: Buffer[] = [];
        req.on("data", (c: Buffer) => chunks.push(c));
        req.on("end", () => {
          const dir = join(server.config.root, "logs", "play");
          mkdirSync(dir, { recursive: true });
          writeFileSync(join(dir, `${id}.json`), Buffer.concat(chunks));
          res.statusCode = 204;
          res.end();
        });
      });
    },
  };
}

export default defineConfig({
  plugins: [playLog()],
  base: "./",
  define: { __BUILD_VERSION__: JSON.stringify(buildVersion()) },
  // Tollgate worktrees live under .worktrees/; their files must not trigger reloads or dependency scans.
  server: { port: devPort(), strictPort: true, watch: { ignored: ["**/.worktrees/**", "**/logs/**"] } },
  optimizeDeps: { entries: ["index.html"] },
  preview: { port: previewPort(), strictPort: true },
  build: { target: "es2022", chunkSizeWarningLimit: 4000 },
  worker: { format: "es" },
  test: {
    include: ["tests/**/*.test.ts"],
    environment: "node",
    testTimeout: 60_000,
  },
});
