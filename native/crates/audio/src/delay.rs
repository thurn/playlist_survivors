//! Delayed listening's delay line (docs/design/native-app.md, "Playback"): the tapped app's
//! stereo capture, played `M` late through the output.
//!
//! The capture's IOProc writes each buffer through a [`Writer`]: its frames (interleaved stereo)
//! and an [`Anchor`] (its first frame's index and the host time it was captured). The output's
//! render thread owns the [`DelayLine`]: it pulls what has been captured into a ring of the last
//! `keep` seconds, and plays at render time `r` the frame captured at host time `r - delay`, by
//! the newest anchor at the capture's rate, linearly interpolated. (`delay` is `M` less the tap's
//! lead over the render timeline, so the music is heard exactly `M` late: ES, hv-t8o8.45.) The
//! tap and the output share a clock when they are the same device; a second device's drift shows
//! as the newest anchor moving, so each buffer re-anchors.
//!
//! Real time on both sides: the rings are preallocated, and nothing allocates, locks or frees.

/// A captured buffer's first frame (its index on the capture's timeline, dropped frames
/// included), its length in frames, and when its first frame was captured (host s).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Anchor {
    pub frame: u64,
    pub frames: u32,
    pub host: f64,
}

/// What the line holds for a host time.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Read {
    /// The stereo frame captured then.
    Frame(f32, f32),
    /// Before what it holds: before the capture began (the delay's silence), or too long ago.
    Before,
    /// Not captured yet: an underrun.
    After,
}

/// The capture's end of the delay line.
pub struct Writer {
    samples: rtrb::Producer<f32>,
    anchors: rtrb::Producer<Anchor>,
}

impl Writer {
    /// Writes a buffer of `frames` frames, captured at host time `host` and starting at frame
    /// `frame`, with `fill` writing its interleaved stereo samples (2 x frames of them). When the
    /// ring is full the buffer is dropped (the line reads silence there) and false is returned.
    pub fn write(
        &mut self,
        frame: u64,
        frames: usize,
        host: f64,
        fill: impl FnOnce(&mut [f32], &mut [f32]),
    ) -> bool {
        if self.anchors.slots() == 0 {
            return false;
        }
        let Ok(mut chunk) = self.samples.write_chunk(2 * frames) else {
            return false;
        };
        let (a, b) = chunk.as_mut_slices();
        fill(a, b);
        chunk.commit_all();
        let _ = self.anchors.push(Anchor {
            frame,
            frames: frames as u32,
            host,
        });
        true
    }
}

/// The render thread's end: the last `keep` seconds captured, read by host time.
pub struct DelayLine {
    samples: rtrb::Consumer<f32>,
    anchors: rtrb::Consumer<Anchor>,
    /// The ring of frames (interleaved stereo), indexed by capture frame modulo its length.
    ring: Box<[f32]>,
    /// The newest frame index written, plus 1, and the oldest still held.
    end: u64,
    start: u64,
    /// The newest anchor.
    anchor: Option<Anchor>,
    rate: f64,
    /// How long after capture a frame plays (s, on the render timeline).
    pub delay: f64,
}

/// A delay line at the capture's `rate`, holding `keep` seconds, playing `delay` s late: the
/// writer for the capture and the line for the render thread.
pub fn delay_line(rate: f64, delay: f64, keep: f64) -> (Writer, DelayLine) {
    let frames = (rate * keep).ceil() as usize;
    let (sp, sc) = rtrb::RingBuffer::new(2 * frames);
    let (ap, ac) = rtrb::RingBuffer::new(4096);
    (
        Writer {
            samples: sp,
            anchors: ap,
        },
        DelayLine {
            samples: sc,
            anchors: ac,
            ring: vec![0.0; 2 * frames].into_boxed_slice(),
            end: 0,
            start: 0,
            anchor: None,
            rate,
            delay,
        },
    )
}

impl DelayLine {
    fn frames(&self) -> u64 {
        (self.ring.len() / 2) as u64
    }

    /// Takes in everything captured so far.
    pub fn pull(&mut self) {
        let len = self.frames();
        while let Ok(a) = self.anchors.pop() {
            let n = 2 * a.frames as usize;
            let Ok(chunk) = self.samples.read_chunk(n.min(self.samples.slots())) else {
                break;
            };
            let (x, y) = chunk.as_slices();
            let sample = |i: usize| if i < x.len() { x[i] } else { y[i - x.len()] };
            let pairs = chunk.len() / 2;
            for k in 0..pairs {
                let at = ((a.frame + k as u64) % len) as usize;
                self.ring[2 * at] = sample(2 * k);
                self.ring[2 * at + 1] = sample(2 * k + 1);
            }
            chunk.commit_all();
            // (A gap from a dropped buffer reads silence.)
            if a.frame > self.end {
                for f in self.end..a.frame.min(self.end + len) {
                    let at = (f % len) as usize;
                    self.ring[2 * at] = 0.0;
                    self.ring[2 * at + 1] = 0.0;
                }
            }
            self.end = self.end.max(a.frame + u64::from(a.frames));
            self.start = self.end.saturating_sub(len);
            self.anchor = Some(a);
        }
    }

    /// What the line holds for host time `t`.
    pub fn at(&self, t: f64) -> Read {
        let Some(a) = self.anchor else {
            return Read::Before;
        };
        self.frame(a.frame as f64 + (t - a.host) * self.rate)
    }

    /// What the line holds at frame `x` of the capture's timeline (fractional: interpolated).
    #[allow(clippy::many_single_char_names)]
    pub fn frame(&self, x: f64) -> Read {
        if self.anchor.is_none() {
            return Read::Before;
        }
        let k = x.floor();
        if k < self.start as f64 {
            return Read::Before;
        }
        if k + 1.0 >= self.end as f64 {
            return Read::After;
        }
        let f = (x - k) as f32;
        let len = self.frames();
        let (i, j) = (((k as u64) % len) as usize, ((k as u64 + 1) % len) as usize);
        let lerp =
            |c: usize| self.ring[2 * i + c] + (self.ring[2 * j + c] - self.ring[2 * i + c]) * f;
        Read::Frame(lerp(0), lerp(1))
    }

    /// The newest frame captured, plus 1 (0 before the first).
    pub fn end(&self) -> u64 {
        self.end
    }

    /// The capture's rate.
    pub fn rate(&self) -> f64 {
        self.rate
    }

    /// How far ahead of host time `t`'s frame the capture is (s): the line's fill as the render
    /// thread reads it at `t + delay`. Negative is an underrun.
    pub fn fill(&self, t: f64) -> Option<f64> {
        let a = self.anchor?;
        let newest = a.host + (self.end as f64 - a.frame as f64) / self.rate;
        Some(newest - t)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const RATE: f64 = 48_000.0;

    /// Writes `frames` frames from `frame` captured at `host`, left frame index, right its negative.
    fn capture(w: &mut Writer, frame: u64, frames: usize, host: f64) -> bool {
        w.write(frame, frames, host, |a, b| {
            for (k, s) in a.iter_mut().chain(b.iter_mut()).enumerate() {
                let f = (frame + (k / 2) as u64) as f32;
                *s = if k % 2 == 0 { f } else { -f };
            }
        })
    }

    #[test]
    fn a_frame_plays_by_the_host_time_it_was_captured() {
        let (mut w, mut line) = delay_line(RATE, 3.0, 5.0);
        for b in 0..20u64 {
            capture(&mut w, b * 512, 512, 100.0 + (b * 512) as f64 / RATE);
        }
        line.pull();
        // (Frame 1000's capture time, read back exactly; halfway to the next, interpolated.)
        assert_eq!(line.at(100.0 + 1000.0 / RATE), Read::Frame(1000.0, -1000.0));
        assert_eq!(line.at(100.0 + 1000.5 / RATE), Read::Frame(1000.5, -1000.5));
        // Before the capture began: the delay's silence; past it: an underrun.
        assert_eq!(line.at(99.0), Read::Before);
        assert_eq!(line.at(101.0), Read::After);
        let fill = line.fill(100.0 + 9000.0 / RATE).unwrap();
        assert!((fill - (20.0 * 512.0 - 9000.0) / RATE).abs() < 1e-9);
    }

    #[test]
    fn a_frame_reads_by_its_index_whatever_its_capture_time() {
        // (A second capture continuing the first's timeline at another host time.)
        let (mut w, mut line) = delay_line(RATE, 3.0, 5.0);
        capture(&mut w, 0, 512, 100.0);
        capture(&mut w, 4096, 512, 500.0);
        line.pull();
        assert_eq!(line.frame(300.25), Read::Frame(300.25, -300.25));
        assert_eq!(line.frame(2000.0), Read::Frame(0.0, 0.0));
        assert_eq!(line.frame(4100.0), Read::Frame(4100.0, -4100.0));
        assert_eq!(line.frame(4607.0), Read::After);
        assert_eq!(line.end(), 4608);
    }

    #[test]
    fn a_dropped_buffer_reads_silence_and_the_ring_forgets_the_oldest() {
        let (mut w, mut line) = delay_line(RATE, 3.0, 0.05);
        capture(&mut w, 0, 512, 0.0);
        // (Frames 512-1023 dropped by the capture.)
        capture(&mut w, 1024, 512, 1024.0 / RATE);
        line.pull();
        assert_eq!(line.at(700.0 / RATE), Read::Frame(0.0, 0.0));
        assert_eq!(line.at(1100.0 / RATE), Read::Frame(1100.0, -1100.0));
        // Holding 0.05 s (2400 frames): after 4800 frames, the first are gone.
        // (The render thread pulls every buffer.)
        for b in 3..10u64 {
            assert!(capture(&mut w, b * 512, 512, (b * 512) as f64 / RATE));
            line.pull();
        }
        assert_eq!(line.at(100.0 / RATE), Read::Before);
        assert_eq!(line.at(4000.0 / RATE), Read::Frame(4000.0, -4000.0));
    }

    #[test]
    fn a_full_ring_drops_the_buffer() {
        let (mut w, _line) = delay_line(RATE, 3.0, 0.01);
        assert!(capture(&mut w, 0, 480, 0.0));
        assert!(!capture(&mut w, 480, 480, 0.01));
    }
}
