import { describe, expect, it } from "vitest";
import { FNV_OFFSET, Hasher, canonicalHash, fnv64Bytes, hex64, truncateStream } from "../scripts/oracle-export.ts";
import type { ReactiveStream } from "../src/reactive/stream.ts";

const fnv = (s: string) => hex64(fnv64Bytes(FNV_OFFSET, new TextEncoder().encode(s)));

describe("the oracle's FNV-1a 64", () => {
  it("matches the reference vectors", () => {
    expect(fnv("")).toBe("cbf29ce484222325");
    expect(fnv("a")).toBe("af63dc4c8601ec8c");
    expect(fnv("foobar")).toBe("85944171f73967e8");
  });
});

describe("the canonical serialization", () => {
  const bytes = (feed: (h: Hasher) => void) => {
    const h = new Hasher();
    feed(h);
    return hex64(h.value());
  };

  it("hashes an object by its sorted keys, skipping undefined fields", () => {
    expect(canonicalHash({ b: 1, a: "x" })).toBe(canonicalHash({ a: "x", b: 1, c: undefined }));
    expect(hex64(canonicalHash({ a: "x", b: 1 }))).toBe(
      bytes((h) => {
        h.byte(6);
        h.u32(2);
        h.str("a");
        h.byte(4);
        h.str("x");
        h.str("b");
        h.byte(3);
        h.f64(1);
      }),
    );
  });

  it("keeps -0's sign, folds every NaN to one, and hashes typed arrays as arrays", () => {
    expect(canonicalHash(-0)).not.toBe(canonicalHash(0));
    expect(canonicalHash(NaN)).toBe(canonicalHash(-NaN));
    expect(canonicalHash(new Float64Array([1, 2]))).toBe(canonicalHash([1, 2]));
    expect(canonicalHash([undefined])).toBe(canonicalHash([null]));
  });
});

describe("the truncation check's stream", () => {
  it("keeps only the entries known by the cut", () => {
    const stream = {
      beats: [{ t: 1, knownAt: 0.5 }, { t: 2, knownAt: 1.5 }],
      ticks: [{ t: 0.95 }, { t: 1.05 }],
      onsets: [{ t: 1, knownAt: 1.1 }],
      structure: { changes: [], jumps: [{ t: 0.5, knownAt: 0.9 }], ends: [] },
    } as unknown as ReactiveStream;
    const cut = truncateStream(stream, 1);
    expect(cut.beats.map((b) => b.t)).toEqual([1]);
    expect(cut.ticks.map((k) => k.t)).toEqual([0.95]);
    expect(cut.onsets).toEqual([]);
    expect(cut.structure?.jumps).toHaveLength(1);
  });
});
