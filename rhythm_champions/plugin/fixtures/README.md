# Test fixtures

Copied unchanged from the frozen tree, so the workspace's tests read nothing outside it:

- `house-128.ogg`: `public/reactive/house-128/house-128.ogg`, the corpus's synthetic 128 BPM
  house loop: the File source's tests decode and play it.
- `house-128-20s.jsonl`: `native/fixtures/entries/house-128-20s.jsonl`, the entries its first
  20 s produce, which the record and replay tests compare against.
- `streams/<track>/stream.json`: `public/reactive/<track>/stream.json` for a tempo ramp
  (accelerando-100-140), a steady loop (house-128) and a classical piece (orpheus-galop), which
  the stream crate's tests parse and replay.
