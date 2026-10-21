/**
 * Audio playback, the audio clock, and sample-accurate sound-effect
 * scheduling. Song time comes from the audio hardware clock, so picture and
 * sound cannot drift apart.
 */
import type { SoundManifest } from "../assets/types.ts";
import type { Plan, SoundEvent } from "../plan/types.ts";

const SCHEDULE_EVERY_MS = 25;
const LOOKAHEAD_S = 0.15;
const MAX_CORRECTION_S = 0.002;
const JUMP_S = 0.05;
/** Sound effects sit about 6 dB below the music's peak. */
const SFX_LEVEL = 0.5;
const DUCK_GAIN = 0.66; // about −3.5 dB
const DUCK_S = 0.12;

/** Keeps the song-time estimate monotonic and slew-limited, except after a glitch. */
export class Smoother {
  private value = NaN;
  private lastAt = 0;

  update(raw: number, presentAt: number): number {
    if (Number.isNaN(this.value)) {
      this.value = raw;
      this.lastAt = presentAt;
      return raw;
    }
    const predicted = this.value + Math.max(0, presentAt - this.lastAt) / 1000;
    const err = raw - predicted;
    let next: number;
    if (Math.abs(err) > JUMP_S) next = raw; // after an audio glitch or a hidden tab
    else next = Math.max(this.value, predicted + Math.max(-MAX_CORRECTION_S, Math.min(MAX_CORRECTION_S, err)));
    this.value = next;
    this.lastAt = presentAt;
    return next;
  }

  reset(): void {
    this.value = NaN;
  }
}

export interface SongClock {
  /** Song time at the moment this animation frame will be presented. */
  songTimeAtPresentation(rafTimestamp: number, frameDurationMs: number): number;
}

export class VirtualClock implements SongClock {
  s = 0;
  songTimeAtPresentation(): number {
    return this.s;
  }
}

export class AudioEngine implements SongClock {
  readonly ctx: AudioContext;
  private music?: AudioBuffer;
  private source?: AudioBufferSourceNode;
  private musicGain: GainNode;
  private sfxBus: GainNode;
  private buffers = new Map<string, AudioBuffer>();
  private t0 = 0;
  private started = false;
  private smoother = new Smoother();
  private schedIdx = 0;
  private timer?: ReturnType<typeof setInterval>;
  private clickBuffer?: AudioBuffer;
  private clickIdx = 0;

  constructor(
    readonly avOffsetMs = 0,
    readonly metronome = false,
  ) {
    this.ctx = new AudioContext({ latencyHint: "interactive" });
    const limiter = this.ctx.createDynamicsCompressor();
    limiter.threshold.value = -1.5;
    limiter.knee.value = 0;
    limiter.ratio.value = 20;
    limiter.attack.value = 0.002;
    limiter.release.value = 0.08;
    limiter.connect(this.ctx.destination);
    this.musicGain = this.ctx.createGain();
    this.musicGain.connect(limiter);
    const comp = this.ctx.createDynamicsCompressor();
    comp.threshold.value = -18;
    comp.knee.value = 8;
    comp.ratio.value = 4;
    comp.attack.value = 0.003;
    comp.release.value = 0.12;
    this.sfxBus = this.ctx.createGain();
    this.sfxBus.gain.value = SFX_LEVEL;
    this.sfxBus.connect(comp);
    comp.connect(limiter);
  }

  async loadMusic(url: string, onProgress?: (f: number) => void): Promise<void> {
    const res = await fetch(url);
    if (!res.ok) throw new Error(`failed to load ${url}: ${res.status}`);
    const total = Number(res.headers.get("content-length") ?? 0);
    const reader = res.body?.getReader();
    let data: ArrayBuffer;
    if (reader && total > 0) {
      const chunks: Uint8Array[] = [];
      let got = 0;
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        chunks.push(value);
        got += value.length;
        onProgress?.(got / total);
      }
      const all = new Uint8Array(got);
      let off = 0;
      for (const c of chunks) {
        all.set(c, off);
        off += c.length;
      }
      data = all.buffer;
    } else data = await res.arrayBuffer();
    this.music = await this.ctx.decodeAudioData(data);
  }

  async loadSounds(manifest: SoundManifest, ids: Set<string>, base: string): Promise<void> {
    await Promise.all(
      [...ids].map(async (id) => {
        const entry = manifest.sounds[id];
        if (!entry) return;
        try {
          const res = await fetch(base + entry.file);
          const buf = await this.ctx.decodeAudioData(await res.arrayBuffer());
          this.buffers.set(id, buf);
        } catch {
          // A missing sample is not fatal; it just stays silent.
        }
      }),
    );
    // A short synthesized click for the metronome.
    const sr = this.ctx.sampleRate;
    const click = this.ctx.createBuffer(1, Math.floor(sr * 0.03), sr);
    const d = click.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = Math.sin((i / sr) * 2 * Math.PI * 1800) * Math.exp(-i / (sr * 0.004));
    this.clickBuffer = click;
  }

  get duration(): number {
    return this.music?.duration ?? 0;
  }

  get isStarted(): boolean {
    return this.started;
  }

  /** Start playback (from a user gesture). */
  async start(plan: Plan): Promise<void> {
    await this.ctx.resume();
    if (!this.music) throw new Error("music not loaded");
    const src = this.ctx.createBufferSource();
    src.buffer = this.music;
    src.connect(this.musicGain);
    const when = this.ctx.currentTime + 0.1;
    src.start(when);
    this.source = src;
    this.t0 = when;
    this.started = true;
    this.schedIdx = 0;
    this.clickIdx = 0;
    this.smoother.reset();
    this.timer = setInterval(() => this.schedule(plan), SCHEDULE_EVERY_MS);
    this.schedule(plan);
  }

  stop(): void {
    if (this.timer) clearInterval(this.timer);
    this.source?.stop();
    this.started = false;
  }

  /** Song time the audio thread is producing right now (ahead of what is heard). */
  private contextSongTime(): number {
    return this.ctx.currentTime - this.t0;
  }

  songTimeAtPresentation(rafTimestamp: number, frameDurationMs: number): number {
    if (!this.started) return 0;
    const presentAt = rafTimestamp + frameDurationMs + this.avOffsetMs;
    let raw: number;
    const ts = typeof this.ctx.getOutputTimestamp === "function" ? this.ctx.getOutputTimestamp() : undefined;
    if (ts && ts.contextTime !== undefined && ts.performanceTime !== undefined && ts.performanceTime > 0) {
      raw = ts.contextTime + (presentAt - ts.performanceTime) / 1000 - this.t0;
    } else {
      const lat = this.ctx.outputLatency || this.ctx.baseLatency || 0;
      raw = this.ctx.currentTime - lat + (presentAt - performance.now()) / 1000 - this.t0;
    }
    return this.smoother.update(raw, presentAt);
  }

  private schedule(plan: Plan): void {
    const now = this.contextSongTime();
    const horizon = now + LOOKAHEAD_S;
    const sounds = plan.events.sounds;
    while (this.schedIdx < sounds.length && sounds[this.schedIdx].s <= horizon) {
      const ev = sounds[this.schedIdx++];
      // Never late: events already in the past (a hidden tab) are skipped.
      if (ev.s < now) continue;
      this.play(ev);
    }
    if (this.metronome && this.clickBuffer) {
      const beats = plan.songBeats;
      const bars = new Set(plan.barStart);
      while (this.clickIdx < beats.length && beats[this.clickIdx] <= horizon) {
        const i = this.clickIdx++;
        if (beats[i] < now) continue;
        const src = this.ctx.createBufferSource();
        src.buffer = this.clickBuffer;
        const g = this.ctx.createGain();
        g.gain.value = bars.has(i) ? 0.9 : 0.45;
        src.playbackRate.value = bars.has(i) ? 1.5 : 1;
        src.connect(g).connect(this.musicGain);
        src.start(this.t0 + beats[i]);
      }
    }
  }

  private play(ev: SoundEvent): void {
    const buf = this.buffers.get(ev.sample);
    if (!buf) return;
    const when = this.t0 + ev.s;
    const src = this.ctx.createBufferSource();
    src.buffer = buf;
    src.playbackRate.value = ev.rate;
    const gain = this.ctx.createGain();
    gain.gain.value = ev.gain;
    const pan = this.ctx.createStereoPanner();
    pan.pan.value = ev.pan;
    src.connect(gain).connect(pan).connect(this.sfxBus);
    src.start(when);
    if (ev.duck) {
      // Scheduled gain automation: the music dips under heavy impacts.
      const g = this.musicGain.gain;
      g.cancelScheduledValues(when);
      g.setValueAtTime(g.value, when);
      g.linearRampToValueAtTime(DUCK_GAIN, when + 0.01);
      g.setValueAtTime(DUCK_GAIN, when + DUCK_S);
      g.linearRampToValueAtTime(1, when + DUCK_S + 0.08);
    }
  }
}
