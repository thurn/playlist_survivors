import { existsSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { parseReactiveIndex, type ReactiveTrack } from "../src/reactive/tracks.ts";
import { ROOT } from "../scripts/lib/files.ts";
import { contentType, resolveLocalPath, slugify, upsertTrack } from "../scripts/lib/reactive-local.ts";

const REACTIVE = join(ROOT, "public/reactive");

interface StreamFile {
  version: number;
  audio: { file: string; duration: number; sampleRate: number };
  params: { toolVersion: number; gate: number };
  beats: { t: number; knownAt: number; period: number; evidence: number }[];
  ticks: { t: number; evidence: number; energy: number }[];
  onsets: { t: number; knownAt: number; strength: number; strong: boolean }[];
  reference?: { source: string; beats: number[] };
}

function readStream(rel: string): StreamFile {
  return JSON.parse(readFileSync(join(REACTIVE, rel), "utf8")) as StreamFile;
}

describe("shipped reactive tracks", () => {
  const index = parseReactiveIndex(JSON.parse(readFileSync(join(REACTIVE, "index.json"), "utf8")));

  it("lists the ten playable tracks, without the check-only causality-mix", () => {
    expect(index.tracks.map((t) => t.id).sort()).toEqual(
      [
        "accelerando-100-140",
        "beethoven-fifth",
        "bumblebee",
        "dnb-174",
        "hiphop-92-swing",
        "house-128",
        "mountain-king",
        "orpheus-galop",
        "pop-arrangement",
        "william-tell-finale",
      ].sort(),
    );
    expect(existsSync(join(REACTIVE, "causality-mix/stream.json"))).toBe(true);
    expect(existsSync(join(REACTIVE, "causality-mix/causality-mix.ogg"))).toBe(true);
  });

  it.each([...index.tracks.map((t) => [t.id, t] as const), ["causality-mix", undefined] as const])(
    "%s has a well-formed stream whose audio exists",
    (id, track?: ReactiveTrack) => {
      const rel = track?.stream ?? `${id}/stream.json`;
      const s = readStream(rel);
      expect(s.version).toBe(1);
      const audio = resolve(dirname(join(REACTIVE, rel)), s.audio.file);
      expect(existsSync(audio)).toBe(true);
      if (track) expect(resolve(REACTIVE, track.audio)).toBe(audio);
      expect(s.beats.length).toBeGreaterThan(20);
      for (let i = 1; i < s.beats.length; i++) expect(s.beats[i].t).toBeGreaterThan(s.beats[i - 1].t);
      for (const b of s.beats) expect(b.knownAt).toBeLessThan(b.t);
      expect(s.ticks.length).toBe(Math.floor(s.audio.duration / 0.05 + 1e-9));
      expect(s.onsets.some((o) => o.strong)).toBe(true);
      expect(s.reference?.beats.length).toBeGreaterThan(20);
    },
  );

  it("shares one tool version", () => {
    const versions = new Set([...index.tracks.map((t) => t.stream), "causality-mix/stream.json"].map((r) => readStream(r).params.toolVersion));
    expect(versions.size).toBe(1);
  });

  it("reuses the Galop's audio and takes its reference from the song map", () => {
    const s = readStream("orpheus-galop/stream.json");
    expect(s.audio.file).toBe("../../audio/orpheus-galop.flac");
    expect(s.reference?.source).toBe("songmap");
  });
});

describe("local reactive tracks", () => {
  it("slugifies file names into track ids", () => {
    expect(slugify("My Song (Live) - Ünïcode!.mp3")).toBe("my-song-live-unicode-mp3");
    expect(slugify("***")).toBe("track");
    expect(slugify("a".repeat(80))).toHaveLength(64);
  });

  it("replaces a track with the same id", () => {
    const t = (id: string, title: string): ReactiveTrack => ({ id, title, artist: "", group: "local", stream: `${id}/stream.json`, audio: `${id}/audio.mp3` });
    const index = upsertTrack(upsertTrack({ version: 1, tracks: [t("a", "A")] }, t("b", "B")), t("a", "A2"));
    expect(index.tracks.map((x) => [x.id, x.title])).toEqual([
      ["b", "B"],
      ["a", "A2"],
    ]);
    expect(parseReactiveIndex(index)).toEqual(index);
  });

  it("serves only files inside the local folder", () => {
    const dir = "/repo/reactive-local";
    expect(resolveLocalPath(dir, "/song/stream.json")).toBe("/repo/reactive-local/song/stream.json");
    expect(resolveLocalPath(dir, "/song/a%20b.mp3?x=1")).toBe("/repo/reactive-local/song/a b.mp3");
    expect(resolveLocalPath(dir, "/../secret.json")).toBeUndefined();
    expect(resolveLocalPath(dir, "/%2e%2e/secret.json")).toBeUndefined();
    expect(resolveLocalPath(dir, "/song/../../x")).toBeUndefined();
    expect(resolveLocalPath(dir, "/")).toBeUndefined();
    expect(resolveLocalPath(dir, "/%E0%A4%A")).toBeUndefined();
  });

  it("names audio and stream content types", () => {
    expect(contentType("a/stream.json")).toBe("application/json");
    expect(contentType("a/audio.MP3")).toBe("audio/mpeg");
    expect(contentType("a/audio.flac")).toBe("audio/flac");
    expect(contentType("a/x.bin")).toBe("application/octet-stream");
  });
});
