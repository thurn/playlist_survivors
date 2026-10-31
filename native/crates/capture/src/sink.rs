//! Where a capture's buffers go, whatever the platform captured them with: the mono mixdown into
//! a lock-free ring with each buffer's [`Meta`], the first two channels into Delayed listening's
//! delay line, and the latest buffer's host time for the watchdog. The platform's capture
//! callback hands each buffer to a [`Sink`]; the reader drains the [`Stream`].

use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};

use audio::delay::Writer;

use crate::Meta;
use crate::analysis::Source;
use crate::clock::{self, HostTime};

/// The capture callback's end. Real time: nothing allocates, locks or logs.
pub struct Sink {
    samples: rtrb::Producer<f32>,
    meta: rtrb::Producer<Meta>,
    /// The next buffer's first frame, and the frames dropped so far because the ring was full.
    received: u64,
    dropped: u64,
    delay: Option<Writer>,
    alive: Arc<AtomicU64>,
}

/// The reader's end: the mono samples at `rate` and their buffers' [`Meta`]s.
pub struct Stream {
    pub rate: f64,
    pub samples: rtrb::Consumer<f32>,
    pub meta: rtrb::Consumer<Meta>,
    /// The host time of the latest buffer captured (s, an f64's bits; 0 before the first).
    pub alive: Arc<AtomicU64>,
}

/// A capture at `rate` into a ring of `ring_s` seconds (long enough to ride out a stalled
/// reader), its stereo also into `delay` when given, numbering its frames from `first` (a capture
/// that continues another's timeline).
pub fn sink(rate: f64, ring_s: f64, delay: Option<Writer>, first: u64) -> (Sink, Stream) {
    // (The clock's units looked up here, not on the capture's thread.)
    clock::tick_s();
    let (sp, sc) = rtrb::RingBuffer::new((rate * ring_s) as usize);
    let (mp, mc) = rtrb::RingBuffer::new(4096);
    let alive = Arc::new(AtomicU64::new(0));
    (
        Sink {
            samples: sp,
            meta: mp,
            received: first,
            dropped: 0,
            delay,
            alive: Arc::clone(&alive),
        },
        Stream {
            rate,
            samples: sc,
            meta: mc,
            alive,
        },
    )
}

impl Sink {
    /// One captured buffer of `frames` frames of `channels` (at least one) channels, `at(i, c)`
    /// reading frame `i`'s channel `c`, its first frame captured at host time `input` and handed
    /// over at `now`. When the ring is full the buffer is dropped, its frames still counted, so
    /// the next buffer's frames keep their place on the timeline.
    pub fn push(
        &mut self,
        frames: usize,
        channels: usize,
        at: impl Fn(usize, usize) -> f32,
        input: HostTime,
        now: HostTime,
    ) {
        let first = self.received;
        self.received += frames as u64;
        let Ok(mut chunk) = self.samples.write_chunk_uninit(frames) else {
            self.dropped += frames as u64;
            return;
        };
        let gain = 1.0 / channels as f32;
        let mut peak = 0.0f32;
        let (a, b) = chunk.as_mut_slices();
        for (i, slot) in a.iter_mut().chain(b.iter_mut()).enumerate() {
            let s = (0..channels).map(|c| at(i, c)).sum::<f32>() * gain;
            peak = peak.max(s.abs());
            slot.write(s);
        }
        // SAFETY: every slot of the chunk was written above.
        unsafe { chunk.commit_all() };
        // (Delayed listening: the stereo frames too, the first two channels (mono doubled), by
        // capture time.)
        let host = input.secs();
        if let Some(w) = self.delay.as_mut() {
            w.write(first, frames, host, |a, b| {
                for (k, slot) in a.iter_mut().chain(b.iter_mut()).enumerate() {
                    *slot = at(k / 2, (k % 2).min(channels - 1));
                }
            });
        }
        self.alive.store(host.to_bits(), Ordering::Relaxed);
        let _ = self.meta.push(Meta {
            first,
            frames: frames as u32,
            input_host: input,
            now_host: now,
            peak,
            dropped: self.dropped,
        });
    }

    /// Takes back the delay line's writer it captured into, if any (for a capture that continues
    /// the line), once the capture has stopped calling `push`.
    pub fn take_writer(&mut self) -> Option<Writer> {
        self.delay.take()
    }
}

impl Stream {
    /// Drain everything captured so far.
    pub fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>) {
        while let Ok(m) = self.meta.pop() {
            metas.push(m);
        }
        let n = self.samples.slots();
        if n > 0 {
            let c = self.samples.read_chunk(n).unwrap();
            let (a, b) = c.as_slices();
            samples.extend_from_slice(a);
            samples.extend_from_slice(b);
            c.commit_all();
        }
    }
}

impl Source for Stream {
    fn rate(&self) -> f64 {
        self.rate
    }

    fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>) {
        Stream::drain(self, samples, metas);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use audio::delay::{DelayLine, Read};

    /// A buffer of `frames` stereo frames, frame `i`'s left `first + i` and right its negative.
    fn stereo(first: u64, frames: usize) -> Vec<[f32; 2]> {
        (0..frames)
            .map(|i| {
                let l = (first as usize + i) as f32;
                [l, -l]
            })
            .collect()
    }

    /// The line's frames from `from` to `to`, pulled.
    fn frames(line: &mut DelayLine, from: u64, to: u64) -> Vec<Read> {
        line.pull();
        (from..to).map(|k| line.frame(k as f64)).collect()
    }

    fn drained(stream: &mut Stream) -> (Vec<f32>, Vec<Meta>) {
        let (mut samples, mut metas) = (Vec::new(), Vec::new());
        stream.drain(&mut samples, &mut metas);
        (samples, metas)
    }

    #[test]
    fn interleaved_and_planar_buffers_give_the_mean_and_the_first_two_channels() {
        let (writer, mut line) = audio::delay::delay_line(1000.0, 0.0, 1.0);
        let (mut sink, mut stream) = sink(1000.0, 1.0, Some(writer), 0);
        // Interleaved: three channels, the third 3.0 throughout.
        let buf: Vec<f32> = stereo(0, 4)
            .iter()
            .flat_map(|&[l, r]| [l, r, 3.0])
            .collect();
        sink.push(4, 3, |i, c| buf[i * 3 + c], HostTime(7), HostTime(9));
        // Planar: two channels, one buffer each.
        let planar: [Vec<f32>; 2] = [0, 1].map(|c| stereo(4, 4).iter().map(|f| f[c]).collect());
        sink.push(4, 2, |i, c| planar[c][i], HostTime(8), HostTime(10));
        let (samples, metas) = drained(&mut stream);
        assert_eq!(samples, [1.0, 1.0, 1.0, 1.0, 0.0, 0.0, 0.0, 0.0]);
        assert_eq!(metas.len(), 2);
        let m = metas[0];
        assert_eq!(
            (m.first, m.frames, m.input_host, m.now_host),
            (0, 4, HostTime(7), HostTime(9))
        );
        assert_eq!((m.peak, m.dropped), (1.0, 0));
        assert_eq!((metas[1].first, metas[1].peak), (4, 0.0));
        // (The line ends one frame short of its newest: it interpolates up to it.)
        let want: Vec<Read> = stereo(0, 7)
            .iter()
            .map(|&[l, r]| Read::Frame(l, r))
            .collect();
        assert_eq!(frames(&mut line, 0, 7), want);
    }

    #[test]
    fn mono_is_doubled_into_the_line() {
        let (writer, mut line) = audio::delay::delay_line(1000.0, 0.0, 1.0);
        let (mut sink, mut stream) = sink(1000.0, 1.0, Some(writer), 0);
        sink.push(3, 1, |i, _| i as f32 + 0.5, HostTime(1), HostTime(1));
        assert_eq!(drained(&mut stream).0, [0.5, 1.5, 2.5]);
        let want = [Read::Frame(0.5, 0.5), Read::Frame(1.5, 1.5)];
        assert_eq!(frames(&mut line, 0, 2), want);
    }

    #[test]
    fn a_full_ring_drops_the_buffer_and_the_next_keeps_its_place() {
        // (A ring of 6 frames: the second buffer of 4 doesn't fit until the first is drained.)
        let (mut sink, mut stream) = sink(6.0, 1.0, None, 100);
        sink.push(4, 1, |_, _| 0.25, HostTime(1), HostTime(1));
        sink.push(4, 1, |_, _| 0.5, HostTime(2), HostTime(2));
        let (samples, metas) = drained(&mut stream);
        assert_eq!(samples, [0.25; 4]);
        assert_eq!(metas.len(), 1);
        assert_eq!((metas[0].first, metas[0].dropped), (100, 0));
        sink.push(2, 1, |_, _| 0.75, HostTime(3), HostTime(3));
        let (samples, metas) = drained(&mut stream);
        assert_eq!(samples, [0.75; 2]);
        assert_eq!((metas[0].first, metas[0].dropped), (108, 4));
    }

    #[test]
    fn each_buffer_kept_updates_alive_with_its_capture_time() {
        let (mut sink, stream) = sink(1000.0, 1.0, None, 0);
        let alive = || f64::from_bits(stream.alive.load(Ordering::Relaxed));
        assert_eq!(alive(), 0.0);
        let input = HostTime::now();
        sink.push(2, 2, |_, _| 0.0, input, HostTime::now());
        assert_eq!(alive(), input.secs());
        assert!(alive() > 0.0);
    }
}
