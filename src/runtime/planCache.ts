/** Caches plans in IndexedDB, keyed by a hash of the planner inputs and the build version. */
import type { Plan } from "../plan/types.ts";

const DB = "playlist-survivors";
const STORE = "plans";

function open(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB, 1);
    req.onupgradeneeded = () => req.result.createObjectStore(STORE);
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

export async function cacheKey(parts: unknown[]): Promise<string> {
  const json = JSON.stringify(parts, (_k, v) => (v instanceof Map ? Object.fromEntries(v) : v));
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(json));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

export async function loadCachedPlan(key: string): Promise<Plan | undefined> {
  try {
    const db = await open();
    return await new Promise((resolve) => {
      const req = db.transaction(STORE).objectStore(STORE).get(key);
      req.onsuccess = () => resolve(req.result as Plan | undefined);
      req.onerror = () => resolve(undefined);
    });
  } catch {
    return undefined;
  }
}

export async function storePlan(key: string, plan: Plan): Promise<void> {
  try {
    const db = await open();
    await new Promise<void>((resolve) => {
      const tx = db.transaction(STORE, "readwrite");
      const store = tx.objectStore(STORE);
      // Keep only the latest few plans.
      store.clear();
      store.put(plan, key);
      tx.oncomplete = () => resolve();
      tx.onerror = () => resolve();
    });
  } catch {
    // Caching is an optimization only.
  }
}
