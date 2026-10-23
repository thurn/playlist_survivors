/**
 * Reactive mode's track indexes: `public/reactive/index.json` for the shipped
 * tracks and `reactive-local/index.json` for the user's own (written by
 * `npm run reactive:add`, served by the dev server at /reactive-local/). Paths
 * are relative to the index file; each stream names its audio relative to
 * itself. See docs/design/reactive-audio.md, "Tracks".
 */
import { z } from "zod";

export const TRACK_GROUPS = ["classical", "synthetic", "local"] as const;

export const ReactiveTrackSchema = z.object({
  id: z.string().regex(/^[a-z0-9][a-z0-9-]{0,63}$/),
  title: z.string().min(1),
  artist: z.string(),
  group: z.enum(TRACK_GROUPS),
  stream: z.string().min(1),
  audio: z.string().min(1),
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
