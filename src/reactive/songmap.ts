/**
 * The reactive song map, built in the browser from a belief stream: the
 * committed beats in 2-beat bars counted from the first, one keyless pulsed
 * section over every bar, and each beat's energy as known when it was
 * committed. Nothing else: no phrases, accents or tempo changes. See
 * docs/design/reactive-audio.md, "The reactive song map".
 */
import type { SongMap } from "../core/songmap.ts";
import { energyAt, type ReactiveStream } from "./stream.ts";

export const REACTIVE_SONG = "reactive";
export const REACTIVE_SECTION = "reactive";
export const REACTIVE_BEATS_PER_BAR = 2;

export function reactiveSongMap(stream: ReactiveStream): SongMap {
  const beats = stream.beats.map((b) => b.t);
  const bars = Math.ceil(beats.length / REACTIVE_BEATS_PER_BAR);
  return {
    version: 1,
    song: REACTIVE_SONG,
    audio: { file: stream.audio.file, source: "", cropStart: 0, cropEnd: stream.audio.duration, sampleRate: stream.audio.sampleRate, duration: stream.audio.duration },
    meter: [{ fromBar: 1, beatsPerBar: REACTIVE_BEATS_PER_BAR }],
    beats,
    firstDownbeatIndex: 0,
    sections: [{ label: REACTIVE_SECTION, bars: [1, bars], feel: "pulsed" }],
    phrases: [],
    tempoChanges: [],
    energy: stream.beats.map((b) => energyAt(stream, b.knownAt)),
    accents: [],
  };
}
