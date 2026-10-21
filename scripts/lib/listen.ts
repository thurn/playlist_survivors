/**
 * In-page analysis for the QA listening pass. Runs inside the demo page (passed
 * to page.evaluate), renders the level's mix offline through the live mixing
 * graph, and measures it.
 */
export interface ListenResult {
  peakDb: number;
  clipped: number;
  musicPeakDb: number;
  sfxP95Db: number;
  sections: { label: string; musicDb: number; sfxDb: number; mixDb: number; sfxOverMusic: number; sfxP10Db: number; sfxP90Db: number; musicSpreadDb: number; mixSpreadDb: number }[];
  attacks: { sample: string; ms: number; uses: number }[];
  /** Per event: how late its perceived onset is heard after its scheduled time, once its lead is applied. */
  lateness: { maxMs: number; worst: string; over10ms: number; events: number };
}

export async function listenInPage(): Promise<ListenResult> {
  interface Hook {
    plan: { duration: number; sections: { label: string; s0: number; s1?: number }[]; events: { sounds: { sample: string; s: number; rate: number; lead?: number }[] } };
    renderMix: (p: { music: boolean; sfx: boolean }) => Promise<AudioBuffer>;
  }
  const d = (window as unknown as { __demo: Hook }).__demo;
  const db = (x: number) => 20 * Math.log10(Math.max(x, 1e-9));
  const mono = (b: AudioBuffer) => {
    const l = b.getChannelData(0);
    const r = b.getChannelData(1);
    const m = new Float32Array(l.length);
    for (let i = 0; i < l.length; i++) m[i] = 0.5 * (l[i] + r[i]);
    return m;
  };
  const rmsWindows = (x: Float32Array, sr: number, win: number, hop: number) => {
    const n = Math.floor(win * sr);
    const h = Math.floor(hop * sr);
    const out: number[] = [];
    for (let i = 0; i + n <= x.length; i += h) {
      let s = 0;
      for (let j = i; j < i + n; j++) s += x[j] * x[j];
      out.push(Math.sqrt(s / n));
    }
    return out;
  };
  const pct = (xs: number[], p: number) => {
    const a = [...xs].sort((u, v) => u - v);
    return a[Math.min(a.length - 1, Math.floor(p * a.length))];
  };

  const mixBuf = await d.renderMix({ music: true, sfx: true });
  const sr = mixBuf.sampleRate;
  let peak = 0;
  let clipped = 0;
  for (let c = 0; c < 2; c++) {
    const ch = mixBuf.getChannelData(c);
    for (let i = 0; i < ch.length; i++) {
      const a = Math.abs(ch[i]);
      if (a > peak) peak = a;
      if (a >= 1) clipped++;
    }
  }
  const mix = mono(mixBuf);
  const music = mono(await d.renderMix({ music: true, sfx: false }));
  const sfx = mono(await d.renderMix({ music: false, sfx: true }));
  const HOP = 0.1;
  const mMus = rmsWindows(music, sr, 0.4, HOP);
  const mSfx = rmsWindows(sfx, sr, 0.4, HOP);
  const mMix = rmsWindows(mix, sr, 0.4, HOP);
  const fSfx = rmsWindows(sfx, sr, 0.05, 0.025);
  const fMus = rmsWindows(music, sr, 0.05, 0.025);
  const fMix = rmsWindows(mix, sr, 0.05, 0.025);
  // The p10–p90 spread of 50 ms levels: how much the sound rises and falls between hits.
  const spread = (xs: number[]) => {
    const v = xs.filter((x) => x > 1e-5);
    return v.length ? db(pct(v, 0.9)) - db(pct(v, 0.1)) : 0;
  };
  const secs = d.plan.sections;
  const sections = secs.map((s, i) => {
    const s1 = secs[i + 1]?.s0 ?? d.plan.duration;
    const k0 = Math.floor(s.s0 / HOP);
    const k1 = Math.min(mMus.length, Math.floor((s1 - 0.4) / HOP));
    const range = (xs: number[]) => xs.slice(k0, Math.max(k0 + 1, k1));
    const mean = (xs: number[]) => Math.sqrt(xs.reduce((a, x) => a + x * x, 0) / xs.length);
    const mm = range(mMus);
    const ms = range(mSfx);
    let over = 0;
    for (let k = 0; k < mm.length; k++) if (ms[k] > mm[k]) over++;
    const f0 = Math.floor(s.s0 / 0.025);
    const f1 = Math.floor(s1 / 0.025);
    const fine = fSfx.slice(f0, f1).filter((x) => x > 1e-5);
    return {
      label: s.label,
      musicDb: db(mean(mm)),
      sfxDb: db(mean(ms)),
      mixDb: db(mean(range(mMix))),
      sfxOverMusic: over / mm.length,
      sfxP10Db: fine.length ? db(pct(fine, 0.1)) : -180,
      sfxP90Db: fine.length ? db(pct(fine, 0.9)) : -180,
      musicSpreadDb: spread(fMus.slice(f0, f1)),
      mixSpreadDb: spread(fMix.slice(f0, f1)),
    };
  });

  // How long each sample takes to reach half its peak: a slow attack lands audibly late.
  const manifest = (await (await fetch("assets/sfx/sounds.json")).json()) as { sounds: Record<string, { file: string }> };
  const uses = new Map<string, number>();
  for (const ev of d.plan.events.sounds) uses.set(ev.sample, (uses.get(ev.sample) ?? 0) + 1);
  const dec = new OfflineAudioContext(1, 1, sr);
  const attacks: ListenResult["attacks"] = [];
  for (const [id, n] of uses) {
    const entry = manifest.sounds[id];
    if (!entry) continue;
    const buf = await dec.decodeAudioData(await (await fetch("assets/sfx/" + entry.file)).arrayBuffer());
    // Same definition as the baker's onsetSeconds: the 5 ms RMS envelope first reaching half its maximum.
    const x = buf.getChannelData(0);
    const win = Math.max(1, Math.round(buf.sampleRate * 0.005));
    const env = new Float32Array(Math.max(1, x.length - win + 1));
    let sum = 0;
    for (let j = 0; j < x.length; j++) {
      sum += x[j] * x[j];
      if (j >= win) sum -= x[j - win] * x[j - win];
      if (j >= win - 1) env[j - win + 1] = Math.sqrt(Math.max(0, sum) / win);
    }
    let pk = 0;
    for (const v of env) pk = Math.max(pk, v);
    let i = 0;
    while (i < env.length && env[i] < 0.5 * pk) i++;
    attacks.push({ sample: id, ms: (i / buf.sampleRate) * 1000, uses: n });
  }
  attacks.sort((a, b) => b.ms - a.ms);
  const onsetMs = new Map(attacks.map((a) => [a.sample, a.ms]));
  const lateness = { maxMs: 0, worst: "", over10ms: 0, events: 0 };
  for (const ev of d.plan.events.sounds) {
    const heard = (onsetMs.get(ev.sample) ?? 0) / ev.rate - (ev.lead ?? 0) * 1000;
    lateness.events++;
    if (Math.abs(heard) > 10) lateness.over10ms++;
    if (Math.abs(heard) > Math.abs(lateness.maxMs)) {
      lateness.maxMs = heard;
      lateness.worst = ev.sample;
    }
  }
  return {
    lateness, peakDb: db(peak), clipped, musicPeakDb: db(Math.max(...mMus)), sfxP95Db: db(pct(mSfx.filter((x) => x > 1e-5), 0.95)), sections, attacks };
}
