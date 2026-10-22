/**
 * The song map: one recording's musical structure. It is the only place where
 * seconds appear; everything downstream refers to bars and beats and converts
 * through these functions.
 */

export type Feel = "pulsed" | "free";

export interface Anchor {
  /** Seconds from the start of the cropped file. */
  time: number;
  /** 0–1. */
  strength: number;
}

export interface Accent {
  time: number;
  strength: number;
  label?: string;
}

export interface SongSection {
  label: string;
  /** Inclusive 1-based bar range. */
  bars: [number, number];
  feel: Feel;
  key?: string;
  anchors?: Anchor[];
}

export interface MeterRegion {
  fromBar: number;
  beatsPerBar: number;
}

export interface SongMap {
  version: 1;
  song: string;
  audio: {
    /** File name under public/audio/. */
    file: string;
    /** Source recording the excerpt was cut from. */
    source: string;
    cropStart: number;
    cropEnd: number;
    /**
     * Source ranges joined to make the excerpt, when it skips part of the
     * source. cropStart and cropEnd are then the outer bounds.
     */
    segments?: [number, number][];
    sampleRate: number;
    /** Duration of the cropped file in seconds. */
    duration: number;
  };
  meter: MeterRegion[];
  /** One time per beat, strictly increasing. */
  beats: number[];
  /** Which beat is beat 1 of bar 1. Earlier beats are a pickup. */
  firstDownbeatIndex: number;
  sections: SongSection[];
  /** Inclusive bar ranges of musical phrases. */
  phrases: [number, number][];
  tempoChanges: { bars: [number, number] }[];
  /** 0–1 per beat. */
  energy: number[];
  accents: Accent[];
}

/** 1-based musical position. */
export interface BarBeat {
  bar: number;
  beat: number;
}

/** Index structures derived once from a song map. */
export interface SongIndex {
  map: SongMap;
  /** Beat index of the first beat of each bar; barStart[bar] for 1-based bars. */
  barStart: number[];
  beatsPerBar: number[];
  barCount: number;
}

export function indexSong(map: SongMap): SongIndex {
  const barStart: number[] = [NaN];
  const beatsPerBar: number[] = [NaN];
  let beat = map.firstDownbeatIndex;
  let bar = 1;
  const meters = [...map.meter].sort((a, b) => a.fromBar - b.fromBar);
  while (beat < map.beats.length) {
    let bpb = meters[0].beatsPerBar;
    for (const m of meters) if (m.fromBar <= bar) bpb = m.beatsPerBar;
    barStart[bar] = beat;
    beatsPerBar[bar] = bpb;
    beat += bpb;
    bar++;
  }
  return { map, barStart, beatsPerBar, barCount: bar - 1 };
}

/** Fractional 0-based beat index at song time s (piecewise-linear, extrapolated). */
export function beatIndexAt(map: SongMap, s: number): number {
  const b = map.beats;
  const n = b.length;
  if (s <= b[0]) return (s - b[0]) / (b[1] - b[0]);
  if (s >= b[n - 1]) return n - 1 + (s - b[n - 1]) / (b[n - 1] - b[n - 2]);
  let lo = 0;
  let hi = n - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (b[mid] <= s) lo = mid;
    else hi = mid;
  }
  return lo + (s - b[lo]) / (b[hi] - b[lo]);
}

/** Song time of a fractional beat index (piecewise-linear, extrapolated). */
export function timeOfBeatIndex(map: SongMap, i: number): number {
  const b = map.beats;
  const n = b.length;
  if (i <= 0) return b[0] + i * (b[1] - b[0]);
  if (i >= n - 1) return b[n - 1] + (i - (n - 1)) * (b[n - 1] - b[n - 2]);
  const k = Math.floor(i);
  return b[k] + (i - k) * (b[k + 1] - b[k]);
}

/** Beat index of a 1-based bar and (possibly fractional) 1-based beat. */
export function beatIndexOf(idx: SongIndex, pos: BarBeat): number {
  if (pos.bar < 1) return idx.map.firstDownbeatIndex + (pos.bar - 1) * idx.beatsPerBar[1] + (pos.beat - 1);
  if (pos.bar > idx.barCount) {
    const last = idx.barCount;
    return idx.barStart[last] + (pos.bar - last) * idx.beatsPerBar[last] + (pos.beat - 1);
  }
  return idx.barStart[pos.bar] + (pos.beat - 1);
}

export function timeOf(idx: SongIndex, pos: BarBeat): number {
  return timeOfBeatIndex(idx.map, beatIndexOf(idx, pos));
}

/** 1-based bar containing beat index i (floored). */
export function barOfBeatIndex(idx: SongIndex, i: number): number {
  const k = Math.floor(i + 1e-9);
  let lo = 1;
  let hi = idx.barCount;
  if (k < idx.barStart[1]) return 0;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (idx.barStart[mid] <= k) lo = mid;
    else hi = mid - 1;
  }
  return lo;
}

/** Energy (0–1) at song time s, interpolated between beats. */
export function energyAt(map: SongMap, s: number): number {
  const i = beatIndexAt(map, s);
  const e = map.energy;
  if (i <= 0) return e[0];
  if (i >= e.length - 1) return e[e.length - 1];
  const k = Math.floor(i);
  return e[k] + (i - k) * (e[k + 1] - e[k]);
}

export function sectionAtBar(map: SongMap, bar: number): SongSection | undefined {
  return map.sections.find((sec) => bar >= sec.bars[0] && bar <= sec.bars[1]);
}

/** Song time of the start of a section's first bar and the end of its last bar. */
export function sectionTimes(idx: SongIndex, sec: SongSection): [number, number] {
  return [timeOf(idx, { bar: sec.bars[0], beat: 1 }), timeOf(idx, { bar: sec.bars[1] + 1, beat: 1 })];
}

export function sectionAtTime(idx: SongIndex, s: number): SongSection {
  const bar = barOfBeatIndex(idx, beatIndexAt(idx.map, s));
  const secs = idx.map.sections;
  if (bar < secs[0].bars[0]) return secs[0];
  return sectionAtBar(idx.map, bar) ?? secs[secs.length - 1];
}
