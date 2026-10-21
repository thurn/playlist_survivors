import { defineConfig } from "vitest/config";

export default defineConfig({
  base: "./",
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
