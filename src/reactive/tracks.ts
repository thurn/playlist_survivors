/**
 * Reactive mode's track indexes: `public/reactive/index.json` for the shipped
 * tracks and `reactive-local/index.json` for the user's own (written by
 * `npm run reactive:add`, served by the dev server at /reactive-local/). Paths
 * are relative to the index file; each stream names its audio relative to
 * itself. See docs/design/reactive-audio.md, "Tracks". The reactive feel corpus
 * (docs/design/reactive-feel.md, "The Corpus") adds the notjam and cc groups, each
 * track with its split, licence and credit from corpus/manifest.yaml.
 */
import { z } from "zod";

export const TRACK_GROUPS = ["classical", "synthetic", "notjam", "cc", "local"] as const;
/** The corpus's splits: tracks to tune on, and tracks only to test on. */
export const TRACK_SPLITS = ["iteration", "held-out"] as const;

export const ReactiveTrackSchema = z.object({
  id: z.string().regex(/^[a-z0-9][a-z0-9-]{0,63}$/),
  title: z.string().min(1),
  artist: z.string(),
  group: z.enum(TRACK_GROUPS),
  stream: z.string().min(1),
  audio: z.string().min(1),
  split: z.enum(TRACK_SPLITS).optional(),
  /** An SPDX licence id, such as CC0-1.0 or CC-BY-4.0. */
  licence: z.string().min(1).optional(),
  /** The attribution a CC-BY track requires. */
  credit: z.string().min(1).optional(),
});

export const ReactiveIndexSchema = z.object({
  version: z.literal(1),
  tracks: z.array(ReactiveTrackSchema),
});

export type ReactiveTrack = z.infer<typeof ReactiveTrackSchema>;
export type ReactiveIndex = z.infer<typeof ReactiveIndexSchema>;

export function parseReactiveIndex(json: unknown): ReactiveIndex {
  return ReactiveIndexSchema.parse(json);
}
