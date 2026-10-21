import { createHash } from "node:crypto";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { defineConfig } from "vitest/config";

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

export default defineConfig({
  base: "./",
  define: { __BUILD_VERSION__: JSON.stringify(buildVersion()) },
  server: { port: 5199, strictPort: true },
  preview: { port: 5198, strictPort: true },
  build: { target: "es2022", chunkSizeWarningLimit: 4000 },
  worker: { format: "es" },
  test: {
    include: ["tests/**/*.test.ts"],
    environment: "node",
    testTimeout: 60_000,
  },
});
