import { afterEach, describe, expect, it } from "vitest";
import { BATCH_ENV, runAsBatch } from "../scripts/lib/batch.ts";

describe("runAsBatch", () => {
  const before = process.env[BATCH_ENV];
  afterEach(() => {
    if (before === undefined) delete process.env[BATCH_ENV];
    else process.env[BATCH_ENV] = before;
  });

  it("does nothing once the background policy is applied", () => {
    process.env[BATCH_ENV] = "1";
    expect(() => runAsBatch()).not.toThrow();
  });
});
