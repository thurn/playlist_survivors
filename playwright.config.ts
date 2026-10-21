import { defineConfig } from "@playwright/test";
import { devPort } from "./scripts/lib/port.ts";

const PORT = devPort();

export default defineConfig({
  testDir: "tests/e2e",
  timeout: 120_000,
  use: { viewport: { width: 1920, height: 1080 }, launchOptions: { args: ["--use-angle=metal", "--enable-gpu", "--ignore-gpu-blocklist"] } },
  webServer: {
    command: `npx vite --port ${PORT} --strictPort`,
    url: `http://localhost:${PORT}`,
    reuseExistingServer: true,
  },
});
