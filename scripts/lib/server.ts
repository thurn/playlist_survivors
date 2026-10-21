/** Runs a Vite dev server for scripts (or reuses one already on the port). */
import { createServer } from "vite";
import { devPort } from "./port.ts";

const PORT = devPort();

async function alive(url: string): Promise<boolean> {
  try {
    const r = await fetch(url);
    return r.ok;
  } catch {
    return false;
  }
}

export async function withDevServer<T>(fn: (base: string) => Promise<T>): Promise<T> {
  const base = `http://localhost:${PORT}`;
  if (await alive(base)) return fn(base);
  const server = await createServer({ server: { port: PORT, strictPort: true }, logLevel: "error" });
  await server.listen();
  try {
    return await fn(base);
  } finally {
    await server.close();
  }
}
