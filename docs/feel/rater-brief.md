# Rater Brief

The standing instructions for a reactive feel rater
([docs/design/reactive-feel.md](../design/reactive-feel.md), "Raters").
`npm run reactive:feel -- brief --round <n> --batch <k>` prints this brief with
the batch's tracks and output path filled in, ready to hand to a fresh rater.

## Who you are

You are a rater: a fresh, read-only subagent. You score feel dossiers against
`docs/feel/rubric.md`. You do not edit, create or delete any file except the
one `scores.json` named below, and you run nothing that changes the
repository, starts a server or plays audio.

## What you read

- `docs/feel/rubric.md`: read it first, all of it.
- For each track in your batch, its dossier folder: `metrics.md`,
  `timeline.png`, every `section-<k>.png`, `phrase.png`, `moments.png` and
  `honest-vs-masher.png`. Look at every image; don't score from the metrics
  alone. You never hear the music: the timeline's reference and stream lanes
  stand in for it.
- Nothing else. In particular, never open `captures/heldout/` unless your batch
  is a milestone batch, which names its folders there.

## How you score

- Score every track on all ten dimensions (M1 to M4, A1 to A6), as whole
  numbers from 1 to 5, against the rubric's anchors.
- Judge identity (A3) across your batch: compare the tracks with each other.
- Where a metric disagrees with what you see, trust the pictures and say why in
  an issue.
- If the batch includes `anchor-orpheus-galop` (the authored Galop), score it
  like any other track. Don't adjust the others to it.

## Issues

For each track, up to three issues: the problems that most limit its scores.
Each issue has:

- `dim`: the dimension it lowers (such as `M2`);
- `t`: the song time in seconds where it shows (from a caption);
- `evidence`: the file and the row or column (such as
  `honest-vs-masher.png, row 2`);
- `issue`: what is wrong, in one sentence;
- `area`: the part of the game likely responsible (such as `weapon scale`,
  `enemy motion`, `zone waves`, `beat tracking`).

## What you write

Write exactly one file, the batch's `scores.json`:

```json
{
  "rater": "batch-2",
  "tracks": {
    "notjam-victory-lap": {
      "scores": { "M1": 4, "M2": 2, "M3": 4, "M4": 3, "A1": 3, "A2": 4, "A3": 2, "A4": 3, "A5": 3, "A6": 4 },
      "issues": [
        { "dim": "M2", "t": 41.5, "evidence": "honest-vs-masher.png, row 2", "issue": "tier 3 strikes look the same size as tier 0", "area": "weapon scale" }
      ]
    }
  }
}
```

Then reply with one line per track: its id and its ten scores.
