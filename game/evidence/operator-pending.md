# Checks waiting on the operator

Checks an agent can't finish alone for the Unity game: they need a window, a click, a device, the
speakers on, or an ear. Clear them in one sitting; the consuming bead re-runs them and records the
result, then removes the row. Add a row whenever a game bead records something as pending on the
operator (epic ps-msz; native's own list is native/evidence/operator-pending.md).

| Check | What the operator does | Command | Recorded by | Consumed by |
|---|---|---|---|---|
| The sync spike's clicks by ear (ps-msz.1.6) | With the speakers on, listen to house-128 and then dnb-174 for a minute each: a click on every beat, on the music's own beat, never flamming or drifting late (Cmd-Q quits; the track ends the run). Optional acoustic loopback: hold the Mac's microphone to the speaker, record with QuickTime while it plays, and send the recording, so an agent can measure the clicks against the music in what the air carried | `just backend=mono game --file "$PWD/public/reactive/house-128/house-128.ogg"` (then `.../dnb-174/dnb-174.ogg`; the player needs an absolute path when launched with `open`) | ps-msz.1.6 | ps-msz.3.13 |
