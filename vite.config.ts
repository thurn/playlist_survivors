import { createHash } from "node:crypto";
import { createReadStream, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { Plugin } from "vite";
import { defineConfig } from "vitest/config";
import { devPort, previewPort } from "./scripts/lib/port.ts";
import { LOCAL_DIR, contentType, resolveLocalPath } from "./scripts/lib/reactive-local.ts";

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

/** Reactive mode's local tracks: serves reactive-local/ (from `npm run reactive:add`) at /reactive-local/, never in builds. */
function reactiveLocal(): Plugin {
  return {
    name: "reactive-local",
    apply: "serve",
    configureServer(server) {
      server.middlewares.use(`/${LOCAL_DIR}`, (req, res) => {
        const path = resolveLocalPath(join(server.config.root, LOCAL_DIR), req.url ?? "");
        let size = -1;
        try {
          const stat = path ? statSync(path) : undefined;
          if (stat?.isFile()) size = stat.size;
        } catch {
          // A missing file is a 404.
        }
        if (!path || size < 0 || (req.method !== "GET" && req.method !== "HEAD")) {
          res.statusCode = 404;
          res.end();
          return;
        }
        res.setHeader("Content-Type", contentType(path));
        res.setHeader("Content-Length", size);
        res.setHeader("Cache-Control", "no-cache");
        if (req.method === "HEAD") res.end();
        else createReadStream(path).pipe(res);
      });
    },
  };
}

export default defineConfig({
  plugins: [playLog(), reactiveLocal()],
  base: "./",
  define: { __BUILD_VERSION__: JSON.stringify(buildVersion()) },
  // Tollgate worktrees live under .worktrees/; their files must not trigger reloads or dependency scans.
  server: { port: devPort(), strictPort: true, watch: { ignored: ["**/.worktrees/**", "**/logs/**", "**/reactive-local/**"] } },
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
