/**
 * Deep copies of simulation state, for rollback snapshots. Identity is kept:
 * two references to one object in the original are two references to one copy.
 * Immutable data is shared, not copied: functions, frozen objects, and
 * anything registered as shared (the song schedule, the content library).
 */

/** Mark an object graph as immutable for the life of a simulation: clones share it. */
export function registerShared(root: unknown, shared: WeakSet<object>): void {
  const stack: unknown[] = [root];
  while (stack.length > 0) {
    const v = stack.pop();
    if (v === null || typeof v !== "object" || shared.has(v)) continue;
    shared.add(v);
    if (ArrayBuffer.isView(v)) continue;
    if (Array.isArray(v)) {
      for (const x of v) if (x !== null && typeof x === "object") stack.push(x);
    } else if (v instanceof Map) {
      for (const [k, x] of v) stack.push(k, x);
    } else if (v instanceof Set) {
      for (const x of v) stack.push(x);
    } else {
      for (const k of Object.keys(v)) {
        const x = (v as Record<string, unknown>)[k];
        if (x !== null && typeof x === "object") stack.push(x);
      }
    }
  }
}

/** A deep copy of `v` that shares functions, frozen objects and registered shared objects. */
export function deepClone<T>(v: T, shared: WeakSet<object>, memo: Map<object, unknown> = new Map()): T {
  const copy = (x: unknown): unknown => {
    if (x === null || typeof x !== "object") return x;
    if (shared.has(x) || Object.isFrozen(x)) return x;
    const seen = memo.get(x);
    if (seen !== undefined) return seen;
    if (Array.isArray(x)) {
      const out: unknown[] = new Array(x.length);
      memo.set(x, out);
      for (let i = 0; i < x.length; i++) {
        const e = x[i];
        out[i] = e !== null && typeof e === "object" ? copy(e) : e;
      }
      return out;
    }
    if (ArrayBuffer.isView(x)) {
      const out = (x as unknown as Int32Array).slice();
      memo.set(x, out);
      return out;
    }
    if (x instanceof Set) {
      const out = new Set();
      memo.set(x, out);
      for (const e of x) out.add(copy(e));
      return out;
    }
    if (x instanceof Map) {
      const out = new Map();
      memo.set(x, out);
      for (const [k, e] of x) out.set(copy(k), copy(e));
      return out;
    }
    const out = Object.create(Object.getPrototypeOf(x)) as Record<string, unknown>;
    memo.set(x, out);
    for (const k of Object.keys(x)) {
      const e = (x as Record<string, unknown>)[k];
      out[k] = e !== null && typeof e === "object" ? copy(e) : e;
    }
    return out;
  };
  return copy(v) as T;
}
