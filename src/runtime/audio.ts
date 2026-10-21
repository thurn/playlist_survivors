/**
 * Audio playback, the audio clock, and sample-accurate sound-effect
 * scheduling. Song time comes from the audio hardware clock, so picture and
 * sound cannot drift apart.
 */
import type { SoundManifest } from "../assets/types.ts";
import type { Plan, SoundEvent } from "../plan/types.ts";

const SCHEDULE_EVERY_MS = 25;
const LOOKAHEAD_S = 0.15;
/** Matches the planner's MAX_SOUND_LEAD: samples with slow attacks start up to this early. */
const MAX_LEAD_S = 0.25;
const MAX_CORRECTION_S = 0.002;
const JUMP_S = 0.05;
/** Sound effects sit about 6 dB below the music's peak. */
const SFX_LEVEL = 0.32;
const DUCK_GAIN = 0.66; // about −3.5 dB
const DUCK_S = 0.12;
/** The limiter overshoots by a fraction of a dB on the sharpest transients; this keeps peaks under 0 dBFS. */
const OUTPUT_TRIM = 0.89; // −1 dB

/** The effects follow the music's loudness: they move this fraction of the music's dB change. */
const FOLLOW = 0.75;
const FOLLOW_MAX_CUT_DB = 10;
const LEVEL_RATE = 10; // envelope points per second

/**
 * The effects bus gain over the song, as scheduled automation: when the music is
 * quieter than its loud passages, the effects come down with it so the music
 * stays in front. Computed once from the decoded music, so it is a pure function
 * of song time like everything else.
 */
export function followCurve(music: AudioBuffer): Float32Array {
  const sr = music.sampleRate;
  const win = Math.floor(0.4 * sr);
  const hop = Math.floor(sr / LEVEL_RATE);
  const chans = Array.from({ length: music.numberOfChannels }, (_, c) => music.getChannelData(c));
  const n = Math.max(1, Math.floor(music.length / hop));
  const level = new Float32Array(n);
  for (let k = 0; k < n; k++) {
    const i0 = Math.max(0, k * hop - win / 2);
    const i1 = Math.min(music.length, i0 + win);
    let sum = 0;
    for (const ch of chans) for (let i = i0; i < i1; i++) sum += ch[i] * ch[i];
    level[k] = 10 * Math.log10(Math.max(sum / Math.max(1, (i1 - i0) * chans.length), 1e-12));
  }
  // Smooth over a second so the effects do not pump with the music's own rhythm.
  const smooth = new Float32Array(n);
  const half = LEVEL_RATE / 2;
  for (let k = 0; k < n; k++) {
    let sum = 0;
    let cnt = 0;
    for (let j = Math.max(0, k - half); j <= Math.min(n - 1, k + half); j++) {
      sum += level[j];
      cnt++;
    }
    smooth[k] = sum / cnt;
  }
  const sorted = [...smooth].sort((a, b) => a - b);
  const ref = sorted[Math.floor(0.9 * (sorted.length - 1))];
  const curve = new Float32Array(n);
  for (let k = 0; k < n; k++) curve[k] = Math.pow(10, Math.max(-FOLLOW_MAX_CUT_DB, Math.min(0, FOLLOW * (smooth[k] - ref))) / 20);
  return curve;
}

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
  private mix: Mix;
  private buffers = new Map<string, AudioBuffer>();
  private t0 = 0;
  private started = false;
  private smoother = new Smoother();
  private schedIdx = 0;
  private handled = new Uint8Array(0);
  private timer?: ReturnType<typeof setInterval>;
  private clickBuffer?: AudioBuffer;
  private clickIdx = 0;
  /** For the QA hook: sounds started, sounds skipped because they were already late, clicks. */
  readonly stats = { scheduled: 0, skippedLate: 0, startedLate: 0, clicks: 0, ducks: 0, ticks: 0 };

  constructor(
    readonly avOffsetMs = 0,
    readonly metronome = false,
  ) {
    this.ctx = new AudioContext({ latencyHint: "interactive" });
    this.mix = new Mix(this.ctx);
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

  private curve?: Float32Array;
  private followCurve(): Float32Array | undefined {
    if (!this.curve && this.music) this.curve = followCurve(this.music);
    return this.curve;
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
    src.connect(this.mix.musicGain);
    const when = this.ctx.currentTime + 0.1;
    src.start(when);
    this.mix.follow(this.followCurve(), when);
    this.source = src;
    this.t0 = when;
    this.started = true;
    this.schedIdx = 0;
    this.handled = new Uint8Array(plan.events.sounds.length);
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
  contextSongTime(): number {
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
    this.stats.ticks++;
    const now = this.contextSongTime();
    const horizon = now + LOOKAHEAD_S;
    const sounds = plan.events.sounds;
    // Events are sorted by their heard time s, but each starts at s − lead, so a later
    // event with a long lead can be due first: scan the window and mark what is handled.
    if (this.handled.length !== sounds.length) this.handled = new Uint8Array(sounds.length);
    for (let i = this.schedIdx; i < sounds.length && sounds[i].s <= horizon + MAX_LEAD_S; i++) {
      if (this.handled[i]) continue;
      const ev = sounds[i];
      const start = ev.s - (ev.lead ?? 0);
      if (start > horizon) continue;
      this.handled[i] = 1;
      // Never late: events already in the past (a hidden tab) are skipped.
      if (start < now) {
        this.stats.skippedLate++;
        continue;
      }
      this.stats.scheduled++;
      this.play(ev);
    }
    while (this.schedIdx < sounds.length && this.handled[this.schedIdx]) this.schedIdx++;
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
        src.connect(g).connect(this.mix.musicGain);
        src.start(this.t0 + beats[i]);
        this.stats.clicks++;
      }
    }
  }

  private play(ev: SoundEvent): void {
    const buf = this.buffers.get(ev.sample);
    if (!buf) return;
    const when = this.t0 + ev.s - (ev.lead ?? 0);
    if (when < this.ctx.currentTime) this.stats.startedLate++;
    if (this.mix.play(buf, when, ev)) this.stats.ducks++;
  }

  /**
   * Renders the whole level's mix offline through the same graph, for the QA
   * listening pass: music and sound effects together or either alone.
   */
  async renderOffline(plan: Plan, parts: { music: boolean; sfx: boolean }): Promise<AudioBuffer> {
    const sr = this.ctx.sampleRate;
    const length = Math.ceil((this.duration + 2) * sr);
    const ctx = new OfflineAudioContext({ numberOfChannels: 2, length, sampleRate: sr });
    const mix = new Mix(ctx);
    mix.follow(this.followCurve(), 0);
    if (parts.music && this.music) {
      const src = ctx.createBufferSource();
      src.buffer = this.music;
      src.connect(mix.musicGain);
      src.start(0);
    }
    for (const ev of plan.events.sounds) {
      const buf = this.buffers.get(ev.sample);
      if (!buf) continue;
      // The music ducks whenever the effects are present, whether or not the music is rendered.
      if (parts.sfx) mix.play(buf, Math.max(0, ev.s - (ev.lead ?? 0)), ev);
    }
    return ctx.startRendering();
  }
}

/** The mixing graph: music bus with ducking, effects bus compressor, final limiter. */
class Mix {
  readonly musicGain: GainNode;
  private sfxBus: GainNode;
  private sfxFollow: GainNode;
  private duckEnd = -Infinity;

  constructor(private ctx: BaseAudioContext) {
    const limiter = ctx.createDynamicsCompressor();
    limiter.threshold.value = -1.5;
    limiter.knee.value = 0;
    limiter.ratio.value = 20;
    limiter.attack.value = 0.002;
    limiter.release.value = 0.08;
    const trim = ctx.createGain();
    trim.gain.value = OUTPUT_TRIM;
    limiter.connect(trim).connect(ctx.destination);
    this.musicGain = ctx.createGain();
    this.musicGain.connect(limiter);
    const comp = ctx.createDynamicsCompressor();
    comp.threshold.value = -18;
    comp.knee.value = 8;
    comp.ratio.value = 4;
    comp.attack.value = 0.003;
    comp.release.value = 0.12;
    this.sfxBus = ctx.createGain();
    this.sfxBus.gain.value = SFX_LEVEL;
    this.sfxBus.connect(comp);
    // After the compressor, so that it does not undo the follow.
    this.sfxFollow = ctx.createGain();
    comp.connect(this.sfxFollow).connect(limiter);
  }

  /** Schedules the effects level over the song, starting at context time t0. */
  follow(curve: Float32Array | undefined, t0: number): void {
    if (!curve || curve.length < 2) return;
    this.sfxFollow.gain.setValueAtTime(curve[0], t0);
    this.sfxFollow.gain.setValueCurveAtTime(curve, t0, curve.length / LEVEL_RATE);
  }

  /** Starts one sound at context time `when`; returns whether it ducked the music. */
  play(buf: AudioBuffer, when: number, ev: SoundEvent): boolean {
    const src = this.ctx.createBufferSource();
    src.buffer = buf;
    src.playbackRate.value = ev.rate;
    const gain = this.ctx.createGain();
    gain.gain.value = ev.gain;
    const pan = this.ctx.createStereoPanner();
    pan.pan.value = ev.pan;
    src.connect(gain).connect(pan).connect(this.sfxBus);
    src.start(when);
    if (!ev.duck) return false;
    // Scheduled gain automation: the music dips under heavy impacts. Overlapping dips
    // extend the hold rather than restarting from full volume.
    const g = this.musicGain.gain;
    const ducked = when <= this.duckEnd;
    g.cancelScheduledValues(when);
    if (ducked) g.setValueAtTime(DUCK_GAIN, when);
    else {
      g.setValueAtTime(1, when);
      g.linearRampToValueAtTime(DUCK_GAIN, when + 0.01);
    }
    g.setValueAtTime(DUCK_GAIN, when + DUCK_S);
    g.linearRampToValueAtTime(1, when + DUCK_S + 0.08);
    this.duckEnd = when + DUCK_S + 0.08;
    return true;
  }
}
