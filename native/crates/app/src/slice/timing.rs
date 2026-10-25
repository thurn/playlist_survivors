//! The timing log (docs/design/native-app.md, "Timing logs (D4)"; `src/live/log.ts` extended with
//! the latency chain): one JSON line per event in the session's folder, `timing.jsonl`. Each has
//! its kind `k`, and the session song time `s` where there is one:
//!
//! - `cap`: a captured buffer, the host time it is heard (`h`);
//! - `arr`: an entry's arrival, its group `g` and how long after its content time it came (`lag`);
//! - `key`: a key event, from the window server's timestamp to the main thread (`lag`);
//! - `rb`: a frame's rollbacks (`n`, of them `keys` for a key), the song re-simulated
//!   (`replayed`), the wall time that took (`resim`, ms) and its depth (`deepest`);
//! - `frame`: a frame's own work, sim and evaluate (`build`) and encode (`encode`), in ms;
//! - `snd`: a voice scheduled, how long before the output must render its onset (`margin`, ms);
//!   and
//!   `cancel`;
//! - `dev`: the output device (`name`), its latency (`lOut`, ms) and when a captured buffer is
//!   heard after its host time (`heardAfter`, ms); `run` and `end`: a run's bounds.
//!
//! `ps log` summarises one; F8 flushes it and says where it is.

use std::fs::File;
use std::io::{BufWriter, Write};
use std::path::{Path, PathBuf};

use serde_json::Value;

pub struct Timing {
    out: BufWriter<File>,
    path: PathBuf,
}

impl Timing {
    /// Opens the log in the session folder `dir` (none, logged, if it can't be).
    pub fn open(dir: &Path) -> Option<Timing> {
        let path = dir.join("timing.jsonl");
        match File::create(&path) {
            Ok(f) => Some(Timing {
                out: BufWriter::new(f),
                path,
            }),
            Err(e) => {
                eprintln!("slice: no timing log ({}): {e}", path.display());
                None
            }
        }
    }

    pub fn event(&mut self, v: &Value) {
        let _ = writeln!(self.out, "{v}");
    }

    /// Writes out what is buffered; the log's path.
    pub fn flush(&mut self) -> &Path {
        let _ = self.out.flush();
        &self.path
    }
}
