/**
 * Drives the installed Google Chrome over the DevTools protocol, without
 * Playwright's launch flags and focus emulation, so that a background tab is
 * really hidden and throttled the way a user's would be.
 */
import { spawn } from "node:child_process";
import { existsSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

export const CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
const PORT = 9333;
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

export class Cdp {
  private ws: WebSocket;
  private id = 0;
  private pending = new Map<number, (v: { result?: { value?: unknown } } & Record<string, unknown>) => void>();
  private constructor(url: string) {
    this.ws = new WebSocket(url);
    this.ws.onmessage = (m) => {
      const d = JSON.parse(String(m.data)) as { id?: number; result?: Record<string, unknown> };
      const done = d.id !== undefined ? this.pending.get(d.id) : undefined;
      if (done) {
        this.pending.delete(d.id!);
        done((d.result ?? d) as never);
      }
    };
  }
  static async connect(url: string): Promise<Cdp> {
    const c = new Cdp(url);
    await new Promise((r) => (c.ws.onopen = r));
    return c;
  }
  send(method: string, params: object = {}): Promise<Record<string, unknown> & { result?: { value?: unknown } }> {
    const id = ++this.id;
    this.ws.send(JSON.stringify({ id, method, params }));
    return new Promise((r) => this.pending.set(id, r));
  }
  async eval<T>(expression: string): Promise<T> {
    const r = await this.send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true, userGesture: true });
    return r.result?.value as T;
  }
  /** A trusted mouse click at page coordinates. */
  async click(x: number, y: number): Promise<void> {
    for (const type of ["mousePressed", "mouseReleased"]) await this.send("Input.dispatchMouseEvent", { type, x, y, button: "left", clickCount: 1 });
  }
  close(): void {
    this.ws.close();
  }
}

export interface ChromeSession {
  page: Cdp;
  pageId: string;
  browser: Cdp;
}

/** Opens url in a fresh Chrome profile; returns undefined when Chrome is not installed. */
export async function withChrome<T>(url: string, fn: (s: ChromeSession) => Promise<T>): Promise<T | undefined> {
  if (!existsSync(CHROME)) return undefined;
  const profile = mkdtempSync(join(tmpdir(), "qa-chrome-"));
  const chrome = spawn(CHROME, [`--user-data-dir=${profile}`, `--remote-debugging-port=${PORT}`, "--no-first-run", "--no-default-browser-check", "--autoplay-policy=no-user-gesture-required", "--window-size=1920,1180", url], { stdio: "ignore" });
  try {
    interface Target {
      id: string;
      url: string;
      webSocketDebuggerUrl: string;
    }
    let target: Target | undefined;
    for (let i = 0; i < 100 && !target; i++) {
      try {
        const list = (await (await fetch(`http://127.0.0.1:${PORT}/json`)).json()) as Target[];
        target = list.find((t) => t.url.startsWith(url.split("?")[0]) && t.url.includes("?"));
      } catch {
        // not up yet
      }
      if (!target) await sleep(200);
    }
    if (!target) throw new Error("Chrome did not open the page");
    const version = (await (await fetch(`http://127.0.0.1:${PORT}/json/version`)).json()) as { webSocketDebuggerUrl: string };
    const page = await Cdp.connect(target.webSocketDebuggerUrl);
    const browser = await Cdp.connect(version.webSocketDebuggerUrl);
    try {
      return await fn({ page, pageId: target.id, browser });
    } finally {
      page.close();
      browser.close();
    }
  } finally {
    chrome.kill();
    await sleep(500);
    rmSync(profile, { recursive: true, force: true });
  }
}
