/**
 * Sound effects: decode with ffmpeg, trim silence, loudness-normalize, and
 * encode to mono Ogg Vorbis (libvorbis compiled to WebAssembly, with a fixed
 * Ogg serial number so output is byte-identical between bakes).
 *
 * Normalization: each sample is brought to the target loudness (-18 LUFS by
 * default) measured as integrated loudness, or as the maximum momentary
 * loudness for clips too short for an integrated measurement, then limited so
 * its true peak stays at or below -1 dBTP. The resulting loudness is measured
 * on the encoded file and recorded in sounds.json.
 */
import { execFile } from "node:child_process";
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { promisify } from "node:util";
import { createOggEncoder } from "wasm-media-encoders";
import type { SoundEntry, SoundManifest } from "../../../src/assets/types.ts";
import { ASSETS_SRC, CACHE, resolveSource, SFX_OUT } from "./paths.ts";

const run = promisify(execFile);
const FFMPEG = process.env.FFMPEG ?? (process.platform === "darwin" ? "/opt/homebrew/bin/ffmpeg" : "ffmpeg");
const RATE = 44100;

interface SoundDef { id: string; src: string; category: string; desc: string }
interface SoundsDoc { target: { lufs: number; truePeakDb: number }; sounds: SoundDef[] }

interface Loudness { integrated: number; momentaryMax: number; truePeak: number }

async function measure(args: string[]): Promise<Loudness> {
  const { stderr } = await run(FFMPEG, ["-hide_banner", "-nostats", ...args, "-af", "apad=whole_dur=1,ebur128=peak=true", "-f", "null", "-"], { maxBuffer: 1 << 26 });
  const integrated = Number(/I:\s+(-?[\d.]+|-inf) LUFS/.exec(stderr.slice(stderr.lastIndexOf("Summary:")))?.[1] ?? -70);
  const truePeak = Number(/Peak:\s+(-?[\d.]+|-inf) dBFS/.exec(stderr.slice(stderr.lastIndexOf("Summary:")))?.[1] ?? -100);
  let momentaryMax = -120;
  for (const m of stderr.matchAll(/ M:\s*(-?[\d.]+)/g)) momentaryMax = Math.max(momentaryMax, Number(m[1]));
  return { integrated: isFinite(integrated) ? integrated : -70, momentaryMax, truePeak: isFinite(truePeak) ? truePeak : -100 };
}

/** The loudness figure we normalize and report: integrated when measurable, else the momentary maximum. */
function loudnessOf(l: Loudness, durationS: number): number {
  return durationS >= 0.6 && l.integrated > -69 ? l.integrated : l.momentaryMax;
}

/**
 * The perceived onset: when the sound's 5 ms RMS envelope first reaches half its
 * maximum. The planner starts the sample this much early so a slow attack is
 * heard on the beat. (scripts/lib/listen.ts measures it the same way in QA.)
 */
export function onsetSeconds(pcm: Float32Array, rate: number): number {
  const win = Math.max(1, Math.round(rate * 0.005));
  const env = new Float32Array(Math.max(1, pcm.length - win + 1));
  let sum = 0;
  for (let i = 0; i < pcm.length; i++) {
    sum += pcm[i] * pcm[i];
    if (i >= win) sum -= pcm[i - win] * pcm[i - win];
    if (i >= win - 1) env[i - win + 1] = Math.sqrt(Math.max(0, sum) / win);
  }
  let max = 0;
  for (const v of env) max = Math.max(max, v);
  let i = 0;
  while (i < env.length && env[i] < 0.5 * max) i++;
  return i / rate;
}

async function encodeOgg(pcm: Float32Array): Promise<Uint8Array> {
  const enc = await createOggEncoder();
  enc.configure({ channels: 1, sampleRate: RATE, vbrQuality: 4, oggSerialNo: 0x50534658 });
  const parts: Uint8Array[] = [];
  for (let i = 0; i < pcm.length; i += 8192) parts.push(enc.encode([pcm.subarray(i, i + 8192)]).slice());
  parts.push(enc.finalize().slice());
  const out = new Uint8Array(parts.reduce((s, p) => s + p.length, 0));
  let o = 0;
  for (const p of parts) {
    out.set(p, o);
    o += p.length;
  }
  return out;
}

async function bakeOne(def: SoundDef, target: SoundsDoc["target"], tmp: string): Promise<SoundEntry> {
  const src = resolveSource(def.src);
  const raw = join(tmp, `${def.id.replace("/", "__")}.f32`);
  // Decode to mono, trimming leading silence (so the sound starts on its scheduled time) and trailing silence.
  const trim = "silenceremove=start_periods=1:start_threshold=-55dB,areverse,silenceremove=start_periods=1:start_threshold=-65dB,areverse";
  await run(FFMPEG, ["-v", "error", "-y", "-i", src, "-ac", "1", "-ar", String(RATE), "-af", trim, "-f", "f32le", raw]);
  const buf = readFileSync(raw);
  const pcm = new Float32Array(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.length));
  if (pcm.length < RATE * 0.02) throw new Error(`sound ${def.id} is empty after trimming`);
  const durationS = pcm.length / RATE;
  const before = await measure(["-f", "f32le", "-ar", String(RATE), "-ac", "1", "-i", raw]);
  let gainDb = target.lufs - loudnessOf(before, durationS);
  gainDb = Math.min(gainDb, target.truePeakDb - 0.3 - before.truePeak);
  const g = Math.pow(10, gainDb / 20);
  // A short fade-out avoids clicks where trailing silence was cut.
  const fade = Math.min(pcm.length, Math.round(RATE * 0.01));
  for (let i = 0; i < pcm.length; i++) {
    let v = pcm[i] * g;
    if (i >= pcm.length - fade) v *= (pcm.length - i) / fade;
    pcm[i] = Math.max(-1, Math.min(1, v));
  }
  const ogg = await encodeOgg(pcm);
  const file = `${def.id}.ogg`;
  const out = join(SFX_OUT, file);
  mkdirSync(dirname(out), { recursive: true });
  writeFileSync(out, ogg);
  const after = await measure(["-i", out]);
  // The onset is measured on the encoded file, as it will be played.
  await run(FFMPEG, ["-v", "error", "-y", "-i", out, "-ac", "1", "-ar", String(RATE), "-f", "f32le", raw]);
  const dec = readFileSync(raw);
  const onset = onsetSeconds(new Float32Array(dec.buffer.slice(dec.byteOffset, dec.byteOffset + dec.length)), RATE);
  rmSync(raw);
  return { file, durationS: Math.round(durationS * 1000) / 1000, lufs: Math.round(loudnessOf(after, durationS) * 10) / 10, onsetS: Math.round(onset * 10000) / 10000 };
}

export async function bakeSounds(use: (ref: string, usedFor: string) => void, log: (s: string) => void): Promise<SoundManifest> {
  const doc = JSON.parse(readFileSync(join(ASSETS_SRC, "sounds.json"), "utf8")) as SoundsDoc;
  const ids = new Set<string>();
  for (const s of doc.sounds) {
    if (!/^(kenney|rpg80|rpgpack|fantasy|sot)\/[A-Za-z0-9_.-]+$/.test(s.id)) throw new Error(`bad sound id ${s.id}`);
    if (ids.has(s.id)) throw new Error(`duplicate sound id ${s.id}`);
    ids.add(s.id);
    use(s.src, `sfx:${s.id}`);
  }
  // Remove stale outputs.
  rmSync(SFX_OUT, { recursive: true, force: true });
  mkdirSync(SFX_OUT, { recursive: true });
  const tmp = join(CACHE, "sfx-tmp");
  rmSync(tmp, { recursive: true, force: true });
  mkdirSync(tmp, { recursive: true });
  const results = new Map<string, SoundEntry>();
  const queue = [...doc.sounds];
  const workers = Array.from({ length: 8 }, async () => {
    for (let s = queue.shift(); s; s = queue.shift()) results.set(s.id, await bakeOne(s, doc.target, tmp));
  });
  await Promise.all(workers);
  log(`  baked ${results.size} sounds`);
  const sounds: Record<string, SoundEntry> = {};
  for (const s of [...doc.sounds].sort((a, b) => a.id.localeCompare(b.id))) sounds[s.id] = results.get(s.id)!;
  writeCatalogue(doc, sounds);
  return { sounds };
}

/** assets-src/sfx-catalogue.md, generated so it never drifts from the selection. */
function writeCatalogue(doc: SoundsDoc, sounds: Record<string, SoundEntry>): void {
  const order = ["impact-light", "impact-medium", "impact-heavy", "whoosh", "magic", "bow", "boom", "chime", "fanfare", "creature", "pickup", "footstep", "ambience", "ui", "misc"];
  const byCat = new Map<string, SoundDef[]>();
  for (const s of doc.sounds) byCat.set(s.category, [...(byCat.get(s.category) ?? []), s]);
  const cats = [...byCat.keys()].sort((a, b) => (order.indexOf(a) + 100) % 100 - (order.indexOf(b) + 100) % 100 || a.localeCompare(b));
  const lines = [
    "# Sound effect catalogue",
    "",
    "Generated by `npm run bake` from `assets-src/sounds.json`; edit that file, not this one.",
    "",
    `Every sample is mono Ogg Vorbis, trimmed of leading and trailing silence and normalized to about ${doc.target.lufs} LUFS (integrated, or the momentary maximum for clips under 0.6 s), with true peaks limited to ${doc.target.truePeakDb} dBTP. Quiet clips may sit below the target because of the peak limit. Sample ids are \`<pack>/<name>\`; use them in sound palettes (content/sounds.yaml).`,
    "",
    "Packs: `kenney` (Kenney Impact Sounds, RPG Audio and Interface Sounds; CC0), `rpg80` (80 CC0 RPG SFX), `rpgpack` (RPG Sound Pack; CC0), `fantasy` (Fantasy Sound Effects Library; CC-BY 3.0, credit Little Robot Sound Factory), `sot` (Source of Tales: bomb CC0, fire and metal bowl CC-BY-SA 3.0).",
    "",
    "Descriptions come from the packs' file names and pages, not from listening; audition before relying on one.",
    "",
  ];
  for (const c of cats) {
    lines.push(`## ${c}`, "", "| id | seconds | LUFS | description |", "|---|---|---|---|");
    for (const s of byCat.get(c)!.sort((a, b) => a.id.localeCompare(b.id, "en", { numeric: true }))) {
      const e = sounds[s.id];
      lines.push(`| \`${s.id}\` | ${e.durationS.toFixed(2)} | ${e.lufs.toFixed(1)} | ${s.desc} |`);
    }
    lines.push("");
  }
  writeFileSync(join(ASSETS_SRC, "sfx-catalogue.md"), lines.join("\n"));
}
