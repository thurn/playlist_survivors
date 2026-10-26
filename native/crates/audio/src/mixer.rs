//! The output's mixer (docs/design/native-app.md, "Sound (G4)"; `src/runtime/audio.ts` and
//! `live/sounds.ts` as a reference): voices scheduled on the render timestamps' host-time
//! timeline and started on their exact sample, movable or cancellable until they start, never
//! started after their onset; the played file under a duck envelope; the effects through a
//! compressor and a level follower; everything through a limiter. It runs on the render thread,
//! so it never allocates or locks once built.

#![allow(clippy::many_single_char_names)]

use std::sync::Arc;

use crate::delay::{DelayLine, Read};

/// Voices waiting to start, and voices playing, at most.
pub const MAX_PENDING: usize = 64;
pub const MAX_PLAYING: usize = 32;
/// A voice whose onset has passed by more than this is skipped, never started late.
pub const ONSET_TOLERANCE_S: f64 = 0.001;

/// The effects sit about 6 dB below the music's peak.
const SFX_LEVEL: f32 = 0.32;
/// A ducking sound takes the music down about 3.5 dB for 120 ms, ramping in over 10 and back
/// over 80.
const DUCK_GAIN: f32 = 0.66;
const DUCK_IN_S: f64 = 0.01;
const DUCK_S: f64 = 0.12;
const DUCK_OUT_S: f64 = 0.08;
/// The effects' compressor: -18 dB threshold, 4:1, 3 ms attack, 120 ms release.
const COMP_THRESHOLD_DB: f32 = -18.0;
const COMP_RATIO: f32 = 4.0;
const COMP_ATTACK_S: f32 = 0.003;
const COMP_RELEASE_S: f32 = 0.12;
/// The limiter: -1.5 dB, 2 ms attack, 80 ms release, then a 1 dB trim for its overshoot.
const LIMIT: f32 = 0.841;
const LIMIT_ATTACK_S: f32 = 0.002;
const LIMIT_RELEASE_S: f32 = 0.08;
const OUTPUT_TRIM: f32 = 0.89;
/// The level follower's gain glides over this long, so a new level never clicks.
const FOLLOW_GLIDE_S: f32 = 0.05;
/// A synthesized click: 30 ms of a decaying sine.
const CLICK_S: f64 = 0.03;
/// Leaving Delayed listening fades the delay line out over this long (s).
const DELAY_FADE_S: f64 = 0.05;

/// What a voice plays: a sample from the bank, or a click at a frequency.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Source {
    Sample(usize),
    Click(f32),
}

/// A sound to play.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Voice {
    /// Its identity, for moving or cancelling it.
    pub id: u64,
    pub source: Source,
    /// When its first sample plays (host s on the render timestamps' timeline).
    pub at: f64,
    /// Where in the sample it starts (samples at the output's rate).
    pub from: f64,
    /// From its first sample to its onset (s): a voice due before now starts partway in while its
    /// onset is still ahead, and is skipped once the onset has passed.
    pub lead: f64,
    pub gain: f32,
    /// -1 (left) to 1 (right).
    pub pan: f32,
    /// Playback rate (1: as recorded).
    pub rate: f32,
    /// It ducks the music.
    pub duck: bool,
}

impl Voice {
    /// A sample at `at`, centred, at its own rate, from its start.
    pub fn sample(id: u64, sound: usize, at: f64, gain: f32) -> Voice {
        Voice {
            id,
            source: Source::Sample(sound),
            at,
            from: 0.0,
            lead: 0.0,
            gain,
            pan: 0.0,
            rate: 1.0,
            duck: false,
        }
    }
}

/// What the render thread is asked to do.
#[derive(Clone, Debug, PartialEq)]
pub enum Command {
    Play(Voice),
    /// Plays `samples` (mono, at the output's rate) as music from host time `at`, beside the file:
    /// a run's own music under its replay. It replaces any replay playing. (The caller keeps a
    /// clone, so the render thread never frees it.)
    Replay {
        samples: Arc<[f32]>,
        at: f64,
    },
    /// Stops the replay's music.
    StopReplay,
    /// Fades the delay line's music out over 50 ms and drops it (leaving Delayed listening).
    FadeDelayLine,
    /// The played file's gain from now on.
    MusicGain(f32),
    /// Moves a voice that hasn't started.
    Move {
        id: u64,
        at: f64,
    },
    /// Drops a voice that hasn't started.
    Cancel(u64),
    /// Drops every voice that hasn't started (a restart).
    CancelAll,
    /// The effects' level (a gain, from the level follower).
    Follow(f32),
    /// Survivors effects have a -12 dB peak ceiling against the heard music's 400 ms RMS.
    SurvivorsMix(bool),
    /// Absolute capture RMS for Direct System mode, where music bypasses this mixer.
    MusicLevel(f32),
}

/// What happened to a voice.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Report {
    /// It started: when it was due, and when its first sample played (host s).
    Started {
        id: u64,
        at: f64,
        actual: f64,
    },
    /// Its onset had passed before it could start.
    Skipped {
        id: u64,
        at: f64,
        buffer: f64,
    },
    Cancelled {
        id: u64,
    },
    /// No room for it.
    Dropped {
        id: u64,
    },
    /// The replay's music started: when it was due, and when its first sample played (host s).
    ReplayStarted {
        at: f64,
        actual: f64,
    },
    /// The delay line, about every half second and on each buffer it underran: how far the
    /// capture is ahead of what plays (s; negative: underrun), and the frames that had nothing
    /// captured to play since the last report.
    Delay {
        fill: f64,
        underrun: u32,
    },
}

#[derive(Clone, Copy, Debug)]
struct Playing {
    voice: Voice,
    /// The frame of this buffer it starts on (0 once it has).
    start: usize,
    pos: f64,
}

/// One-pole smoothing coefficient for a time constant at `rate`.
fn coeff(seconds: f32, rate: f64) -> f32 {
    (-1.0 / (seconds * rate as f32)).exp()
}

/// The mixer: its bank, its voices, the played file and the buses' state.
pub struct Mixer {
    rate: f64,
    bank: Vec<Arc<[f32]>>,
    pending: [Option<Voice>; MAX_PENDING],
    playing: [Option<Playing>; MAX_PLAYING],
    music: Option<Arc<[f32]>>,
    music_gain: f32,
    music_pos: u64,
    /// How far the heard file trails `music_pos`, the position the analyzer reads (frames:
    /// Delayed listening's `M`).
    music_delay: u64,
    /// A replay's music, when it is due (host s), and whether it has started.
    replay: Option<(Arc<[f32]>, f64, bool)>,
    /// Delayed listening's delay line (System mode), the frames it has underrun since its last
    /// report, the frame of that report, and its fade-out's start (frames) once leaving.
    delay_line: Option<DelayLine>,
    delay_underrun: u32,
    delay_reported: u64,
    delay_fade: Option<u64>,
    /// Frames rendered so far.
    clock: u64,
    /// The duck: when it started ramping in (frames), whether it was already down then, when it
    /// starts ramping out and when it ends.
    duck_start: u64,
    duck_from_down: bool,
    duck_hold: u64,
    duck_end: u64,
    survivors: bool,
    music_ms: f32,
    external_music_rms: f32,
    k_music: f32,
    follow: f32,
    follow_target: f32,
    comp_env: f32,
    limit_env: f32,
    k_follow: f32,
    k_comp_attack: f32,
    k_comp_release: f32,
    k_limit_attack: f32,
    k_limit_release: f32,
}

impl Mixer {
    /// A mixer at `rate` over `bank` (mono samples at that rate), playing `music` from its start.
    pub fn new(
        rate: f64,
        bank: Vec<Arc<[f32]>>,
        music: Option<Arc<[f32]>>,
        music_gain: f32,
    ) -> Mixer {
        Mixer {
            rate,
            bank,
            pending: [None; MAX_PENDING],
            playing: [None; MAX_PLAYING],
            music,
            music_gain,
            music_pos: 0,
            music_delay: 0,
            replay: None,
            delay_line: None,
            delay_underrun: 0,
            delay_reported: 0,
            delay_fade: None,
            clock: 0,
            duck_start: 0,
            duck_from_down: false,
            duck_hold: 0,
            duck_end: 0,
            survivors: false,
            music_ms: 0.0,
            external_music_rms: 0.0,
            k_music: coeff(0.4, rate),
            follow: 1.0,
            follow_target: 1.0,
            comp_env: 0.0,
            limit_env: 0.0,
            k_follow: coeff(FOLLOW_GLIDE_S, rate),
            k_comp_attack: coeff(COMP_ATTACK_S, rate),
            k_comp_release: coeff(COMP_RELEASE_S, rate),
            k_limit_attack: coeff(LIMIT_ATTACK_S, rate),
            k_limit_release: coeff(LIMIT_RELEASE_S, rate),
        }
    }

    /// The same mixer with the heard file `frames` behind the position the analyzer reads
    /// (Delayed listening): silence first, then the file `frames` late.
    #[must_use]
    pub fn with_music_delay(self, frames: u64) -> Mixer {
        Mixer {
            music_delay: frames,
            ..self
        }
    }

    /// The same mixer playing `line`'s capture as the music (Delayed listening on the system's
    /// audio).
    #[must_use]
    pub fn with_delay_line(self, line: DelayLine) -> Mixer {
        Mixer {
            delay_line: Some(line),
            ..self
        }
    }

    /// The sample rate.
    pub fn rate(&self) -> f64 {
        self.rate
    }

    /// Where the played file is (its next frame).
    pub fn music_pos(&self) -> u64 {
        self.music_pos
    }

    /// Applies `c`, reporting what it did to `report`.
    pub fn apply(&mut self, c: Command, report: &mut impl FnMut(Report)) {
        match c {
            Command::Play(v) => match self.pending.iter_mut().find(|p| p.is_none()) {
                Some(slot) => *slot = Some(v),
                None => report(Report::Dropped { id: v.id }),
            },
            Command::Move { id, at } => {
                for v in self.pending.iter_mut().flatten() {
                    if v.id == id {
                        v.at = at;
                    }
                }
            }
            Command::Cancel(id) => {
                for slot in &mut self.pending {
                    if slot.is_some_and(|v| v.id == id) {
                        *slot = None;
                        report(Report::Cancelled { id });
                    }
                }
            }
            Command::CancelAll => {
                for slot in &mut self.pending {
                    if let Some(v) = slot.take() {
                        report(Report::Cancelled { id: v.id });
                    }
                }
            }
            Command::Follow(g) => self.follow_target = g.max(0.0),
            Command::SurvivorsMix(on) => self.survivors = on,
            Command::MusicLevel(rms) => {
                self.external_music_rms = if rms.is_finite() { rms.max(0.0) } else { 0.0 }
            }
            Command::Replay { samples, at } => self.replay = Some((samples, at, false)),
            Command::StopReplay => self.replay = None,
            Command::MusicGain(g) => self.music_gain = g,
            Command::FadeDelayLine => {
                self.delay_fade.get_or_insert(self.clock);
            }
        }
    }

    fn length(&self, source: Source) -> f64 {
        match source {
            Source::Sample(k) => self.bank.get(k).map_or(0, |s| s.len()) as f64,
            Source::Click(_) => CLICK_S * self.rate,
        }
    }

    fn at(&self, source: Source, pos: f64) -> f32 {
        match source {
            Source::Sample(k) => {
                let s = &self.bank[k];
                let i = pos.floor();
                let f = (pos - i) as f32;
                let i = i as usize;
                let a = s.get(i).copied().unwrap_or(0.0);
                let b = s.get(i + 1).copied().unwrap_or(0.0);
                a + (b - a) * f
            }
            Source::Click(freq) => {
                let x = (pos / self.rate) as f32;
                0.5 * (-x / 0.006).exp() * (std::f32::consts::TAU * freq * x).sin()
            }
        }
    }

    fn duck_gain(&self, frame: u64) -> f32 {
        if frame < self.duck_start || frame >= self.duck_end {
            return 1.0;
        }
        let ramp_in = (DUCK_IN_S * self.rate) as u64;
        if frame < self.duck_start + ramp_in && !self.duck_from_down {
            let u = (frame - self.duck_start) as f32 / ramp_in.max(1) as f32;
            return 1.0 + (DUCK_GAIN - 1.0) * u;
        }
        if frame < self.duck_hold {
            return DUCK_GAIN;
        }
        let u = (frame - self.duck_hold) as f32 / (self.duck_end - self.duck_hold).max(1) as f32;
        DUCK_GAIN + (1.0 - DUCK_GAIN) * u
    }

    fn duck(&mut self, frame: u64) {
        let down = frame >= self.duck_start && frame < self.duck_end;
        if !down || frame >= self.duck_hold {
            self.duck_from_down = down;
            self.duck_start = frame;
        }
        self.duck_hold = frame + (DUCK_S * self.rate) as u64;
        self.duck_end = self.duck_hold + (DUCK_OUT_S * self.rate) as u64;
    }

    /// Renders the buffer whose first frame plays at host time `host` into `out` (interleaved
    /// stereo), starting the voices due in it. Returns the played file's peak in it.
    pub fn render(&mut self, host: f64, out: &mut [f32], report: &mut impl FnMut(Report)) -> f32 {
        let frames = out.len() / 2;
        let rate = self.rate;
        // Start what is due in this buffer.
        for k in 0..MAX_PENDING {
            let Some(v) = self.pending[k] else { continue };
            let offset = (v.at - host) * rate;
            if offset >= frames as f64 - 0.5 {
                continue;
            }
            self.pending[k] = None;
            if v.at + v.lead < host - ONSET_TOLERANCE_S {
                report(Report::Skipped {
                    id: v.id,
                    at: v.at,
                    buffer: host,
                });
                continue;
            }
            let (start, from) = if offset > 0.0 {
                (offset.round() as usize, v.from)
            } else {
                (0, v.from - offset * f64::from(v.rate))
            };
            match self.playing.iter_mut().find(|p| p.is_none()) {
                Some(slot) => {
                    *slot = Some(Playing {
                        voice: v,
                        start,
                        pos: from,
                    });
                    if v.duck && !self.survivors {
                        self.duck(self.clock + start as u64);
                    }
                    report(Report::Started {
                        id: v.id,
                        at: v.at,
                        actual: host + start as f64 / rate,
                    });
                }
                None => report(Report::Dropped { id: v.id }),
            }
        }
        if let Some(line) = &mut self.delay_line {
            line.pull();
        }
        // The replay's music: where in it this buffer's first frame is (negative before it).
        let replay_from = self.replay.as_mut().map(|(samples, at, started)| {
            let from = ((host - *at) * rate).round() as i64;
            if !*started && from + frames as i64 > 0 {
                *started = true;
                report(Report::ReplayStarted {
                    at: *at,
                    actual: host + (-from).max(0) as f64 / rate,
                });
            }
            (Arc::clone(samples), from)
        });
        let mut peak = 0.0f32;
        for i in 0..frames {
            let (mut l, mut r) = (0.0f32, 0.0f32);
            let (mut reference_l, mut reference_r) = (0.0f32, 0.0f32);
            for k in 0..MAX_PLAYING {
                let Some(mut p) = self.playing[k] else {
                    continue;
                };
                if i < p.start {
                    continue;
                }
                let len = self.length(p.voice.source);
                if p.pos >= len {
                    self.playing[k] = None;
                    continue;
                }
                let x = self.at(p.voice.source, p.pos) * p.voice.gain;
                // (Equal power, as a stereo panner places a mono source.)
                let a = (p.voice.pan.clamp(-1.0, 1.0) + 1.0) * std::f32::consts::FRAC_PI_4;
                if self.survivors && matches!(p.voice.source, Source::Click(_)) {
                    // Calibration/reference clicks must be audible without external music.
                    reference_l += x * a.cos();
                    reference_r += x * a.sin();
                } else {
                    l += x * a.cos();
                    r += x * a.sin();
                }
                p.pos += f64::from(p.voice.rate);
                self.playing[k] = if p.pos >= len { None } else { Some(p) };
            }
            // The effects' bus: level, compressor, follower.
            l *= SFX_LEVEL;
            r *= SFX_LEVEL;
            let level = l.abs().max(r.abs());
            let k = if level > self.comp_env {
                self.k_comp_attack
            } else {
                self.k_comp_release
            };
            self.comp_env = level + k * (self.comp_env - level);
            let env_db = 20.0 * self.comp_env.max(1e-9).log10();
            let over = env_db - COMP_THRESHOLD_DB;
            let comp = if over > 0.0 {
                10f32.powf(-over * (1.0 - 1.0 / COMP_RATIO) / 20.0)
            } else {
                1.0
            };
            self.follow = self.follow_target + self.k_follow * (self.follow - self.follow_target);
            let g = comp * self.follow;
            l *= g;
            r *= g;
            let effects = (l, r);
            l = 0.0;
            r = 0.0;
            // The played file under its duck: the analyzer reads it at `music_pos`, the player
            // hears it `music_delay` later.
            if let Some(music) = &self.music {
                let read = self.music_pos + i as u64;
                let s = music.get(read as usize).copied().unwrap_or(0.0);
                peak = peak.max(s.abs());
                let heard = read
                    .checked_sub(self.music_delay)
                    .and_then(|k| music.get(k as usize))
                    .copied()
                    .unwrap_or(0.0);
                let m = heard
                    * self.music_gain
                    * if self.survivors {
                        1.0
                    } else {
                        self.duck_gain(self.clock + i as u64)
                    };
                l += m;
                r += m;
            }
            // The delay line's music: the frame captured `delay` before this one plays.
            if let Some(line) = &self.delay_line {
                let fade = self.delay_fade.map_or(1.0, |f| {
                    1.0 - ((self.clock + i as u64 - f) as f64 / (DELAY_FADE_S * rate)).min(1.0)
                }) as f32;
                match line.at(host + i as f64 / rate - line.delay) {
                    Read::Frame(a, b) => {
                        l += a * self.music_gain * fade;
                        r += b * self.music_gain * fade;
                    }
                    Read::After => self.delay_underrun += 1,
                    Read::Before => {}
                }
            }
            if let Some((samples, from)) = &replay_from {
                let k = from + i as i64;
                if k >= 0
                    && let Some(&x) = samples.get(k as usize)
                {
                    let m = x * self.music_gain;
                    l += m;
                    r += m;
                }
            }
            if self.survivors {
                let rms =
                    if self.music.is_some() || self.delay_line.is_some() || self.replay.is_some() {
                        let ms = f32::midpoint(l * l, r * r);
                        self.music_ms = ms + self.k_music * (self.music_ms - ms);
                        self.music_ms.max(0.0).sqrt()
                    } else {
                        self.external_music_rms
                    };
                let ceiling = rms * 10f32.powf(-12.0 / 20.0);
                l += effects.0.clamp(-ceiling, ceiling);
                r += effects.1.clamp(-ceiling, ceiling);
                l += reference_l * SFX_LEVEL * g;
                r += reference_r * SFX_LEVEL * g;
            } else {
                l += effects.0;
                r += effects.1;
            }
            // The limiter.
            let level = l.abs().max(r.abs());
            let k = if level > self.limit_env {
                self.k_limit_attack
            } else {
                self.k_limit_release
            };
            self.limit_env = level + k * (self.limit_env - level);
            let lim = if self.limit_env > LIMIT {
                LIMIT / self.limit_env
            } else {
                1.0
            };
            out[2 * i] = l * lim * OUTPUT_TRIM;
            out[2 * i + 1] = r * lim * OUTPUT_TRIM;
        }
        // Voices start at the top of the next buffer from here on.
        for p in self.playing.iter_mut().flatten() {
            p.start = 0;
        }
        self.clock += frames as u64;
        if self.music.is_some() {
            self.music_pos += frames as u64;
        }
        // The delay line's report, and its end once faded out.
        if let Some(line) = &self.delay_line {
            let due = self.clock - self.delay_reported >= (0.5 * rate) as u64;
            if (due || self.delay_underrun > 0)
                && let Some(fill) = line.fill(host + frames as f64 / rate - line.delay)
            {
                report(Report::Delay {
                    fill,
                    underrun: self.delay_underrun,
                });
                self.delay_underrun = 0;
                self.delay_reported = self.clock;
            }
        }
        if self
            .delay_fade
            .is_some_and(|f| self.clock - f >= (DELAY_FADE_S * rate) as u64)
        {
            self.delay_line = None;
        }
        // (A replay played out is let go; the caller's clone frees it.)
        if replay_from.is_some_and(|(samples, from)| from + frames as i64 >= samples.len() as i64) {
            self.replay = None;
        }
        peak
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const RATE: f64 = 48_000.0;

    /// A bank of one sample: an impulse at frame 0 then silence.
    fn impulse() -> Vec<Arc<[f32]>> {
        let mut s = vec![0.0f32; 480];
        s[0] = 1.0;
        vec![s.into()]
    }

    fn first_sound(out: &[f32]) -> Option<usize> {
        out.chunks(2).position(|f| f[0].abs() > 1e-6)
    }

    fn quiet(_: Report) {}

    #[test]
    fn the_delay_line_plays_the_capture_late_reports_underruns_and_fades_out() {
        let (mut w, line) = crate::delay::delay_line(RATE, 0.01, 1.0);
        // A click captured at host 10.0 (frame 0 of a 2048-frame buffer), played 0.01 s late.
        w.write(0, 2048, 10.0, |a, b| {
            for s in a.iter_mut().chain(b.iter_mut()) {
                *s = 0.0;
            }
            a[0] = 0.5;
            a[1] = 0.5;
        });
        let mut m = Mixer::new(RATE, impulse(), None, 1.0).with_delay_line(line);
        let mut reports = Vec::new();
        let mut out = vec![0.0; 1024];
        m.render(10.0, &mut out, &mut |r| reports.push(r));
        assert_eq!(first_sound(&out), Some(480));
        // Past the capture (2048 frames, read 480 late): an underrun, reported.
        m.render(10.0 + 2048.0 / RATE, &mut out, &mut |r| reports.push(r));
        assert!(reports.iter().any(
            |r| matches!(r, Report::Delay { underrun, fill } if *underrun > 0 && *fill < 0.0)
        ));
        // Leaving: 50 ms of fade, then no line.
        m.apply(Command::FadeDelayLine, &mut quiet);
        for k in 0..6 {
            m.render(10.1 + f64::from(k) * 512.0 / RATE, &mut out, &mut quiet);
        }
        assert!(m.delay_line.is_none());
    }

    #[test]
    fn a_delayed_file_is_heard_late_and_read_on_time() {
        let mut music = vec![0.0f32; 1024];
        music[10] = 1.0;
        let mut m = Mixer::new(RATE, impulse(), Some(music.into()), 1.0).with_music_delay(300);
        let mut out = vec![0.0; 1024];
        // (The analyzer's peak is of the file as read, the output the file as heard.)
        let peak = m.render(1.0, &mut out, &mut quiet);
        assert!((peak - 1.0).abs() < 1e-6);
        assert_eq!(first_sound(&out), Some(310));
    }

    #[test]
    fn a_replay_plays_its_music_from_its_time_and_stops() {
        let mut m = Mixer::new(RATE, impulse(), None, 1.0);
        let mut reports = Vec::new();
        let host = 10.0;
        // A replay of a click every 100 frames, due 300 frames in: the second buffer's 44th frame.
        let mut music = vec![0.0f32; 1000];
        for k in (0..1000).step_by(100) {
            music[k] = 0.5;
        }
        let samples: Arc<[f32]> = music.into();
        let at = host + 300.0 / RATE;
        m.apply(
            Command::Replay {
                samples: Arc::clone(&samples),
                at,
            },
            &mut quiet,
        );
        let mut out = vec![0.0; 512];
        m.render(host, &mut out, &mut |r| reports.push(r));
        assert_eq!(first_sound(&out), None);
        m.render(host + 256.0 / RATE, &mut out, &mut |r| reports.push(r));
        assert_eq!(first_sound(&out), Some(300 - 256));
        assert!(matches!(
            reports[..],
            [Report::ReplayStarted { at: a, actual }] if a == at && (actual - at).abs() < 1e-9
        ));
        m.apply(Command::StopReplay, &mut quiet);
        m.render(host + 512.0 / RATE, &mut out, &mut quiet);
        assert_eq!(first_sound(&out), None);
        // (The render thread kept none of it.)
        assert_eq!(Arc::strong_count(&samples), 1);
    }

    #[test]
    fn a_voice_lands_on_its_sample() {
        let mut m = Mixer::new(RATE, impulse(), None, 1.0);
        let mut reports = Vec::new();
        // Due 100.5 frames into the second buffer of 256.
        let host = 10.0;
        let at = host + (256.0 + 100.0) / RATE;
        m.apply(Command::Play(Voice::sample(1, 0, at, 1.0)), &mut |r| {
            reports.push(r);
        });
        let mut out = vec![0.0; 512];
        m.render(host, &mut out, &mut |r| reports.push(r));
        assert_eq!(first_sound(&out), None, "not in the first buffer");
        m.render(host + 256.0 / RATE, &mut out, &mut |r| reports.push(r));
        assert_eq!(first_sound(&out), Some(100));
        assert!(
            matches!(reports[..], [Report::Started { id: 1, actual, .. }] if (actual - at).abs() < 0.6 / RATE)
        );
    }

    #[test]
    fn a_voice_moves_or_cancels_until_it_starts() {
        let mut m = Mixer::new(RATE, impulse(), None, 1.0);
        let mut out = vec![0.0; 512];
        let host = 1.0;
        m.apply(
            Command::Play(Voice::sample(1, 0, host + 0.5, 1.0)),
            &mut quiet,
        );
        m.apply(
            Command::Move {
                id: 1,
                at: host + 10.0 / RATE,
            },
            &mut quiet,
        );
        m.render(host, &mut out, &mut quiet);
        assert_eq!(first_sound(&out), Some(10), "moved");
        m.apply(
            Command::Play(Voice::sample(2, 0, host + 0.1, 1.0)),
            &mut quiet,
        );
        let mut cancelled = Vec::new();
        m.apply(Command::Cancel(2), &mut |r| cancelled.push(r));
        assert_eq!(cancelled, [Report::Cancelled { id: 2 }]);
        for k in 1..20 {
            m.render(host + f64::from(k) * 256.0 / RATE, &mut out, &mut quiet);
            assert_eq!(first_sound(&out), None);
        }
    }

    #[test]
    fn a_late_voice_starts_partway_while_its_onset_is_ahead_and_is_skipped_after() {
        // A ramp, so where it starts shows.
        let ramp: Vec<f32> = (0..4800).map(|i| i as f32 / 4800.0).collect();
        let mut m = Mixer::new(RATE, vec![ramp.into()], None, 1.0);
        let host = 2.0;
        // Due 100 frames ago with its onset 200 frames in: it starts 100 frames in, at once.
        let mut v = Voice::sample(1, 0, host - 100.0 / RATE, 1.0);
        v.lead = 200.0 / RATE;
        m.apply(Command::Play(v), &mut quiet);
        let mut out = vec![0.0; 512];
        let mut reports = Vec::new();
        m.render(host, &mut out, &mut |r| reports.push(r));
        let expected = 100.0 / 4800.0 * SFX_LEVEL * OUTPUT_TRIM * std::f32::consts::FRAC_1_SQRT_2;
        assert!((out[0] - expected).abs() < 1e-4, "{} vs {expected}", out[0]);
        assert!(matches!(reports[..], [Report::Started { .. }]));
        // Its onset already past: skipped, silent.
        let mut v = Voice::sample(2, 0, host - 300.0 / RATE, 1.0);
        v.lead = 200.0 / RATE;
        let mut m = Mixer::new(RATE, impulse(), None, 1.0);
        m.apply(Command::Play(v), &mut quiet);
        reports.clear();
        m.render(host, &mut out, &mut |r| reports.push(r));
        assert_eq!(first_sound(&out), None);
        assert!(matches!(reports[..], [Report::Skipped { id: 2, .. }]));
    }

    #[test]
    fn a_ducking_voice_takes_the_music_down_and_back() {
        let music: Arc<[f32]> = vec![0.5f32; 48_000].into();
        let mut m = Mixer::new(RATE, vec![vec![0.0f32; 10].into()], Some(music), 1.0);
        let mut v = Voice::sample(1, 0, 0.01, 0.0);
        v.duck = true;
        m.apply(Command::Play(v), &mut quiet);
        let mut out = vec![0.0; 2 * 24_000];
        m.render(0.0, &mut out, &mut quiet);
        let at = |s: f64| out[2 * (s * RATE) as usize] / OUTPUT_TRIM;
        assert!((at(0.005) - 0.5).abs() < 1e-3, "before");
        assert!((at(0.05) - 0.5 * DUCK_GAIN).abs() < 1e-3, "ducked");
        assert!((at(0.4) - 0.5).abs() < 1e-3, "back");
    }

    #[test]
    fn the_limiter_holds_a_loud_mix_near_full_scale() {
        let music: Arc<[f32]> = vec![0.99f32; 9600].into();
        let mut m = Mixer::new(RATE, Vec::new(), Some(music), 2.0);
        let mut out = vec![0.0; 2 * 9600];
        m.render(0.0, &mut out, &mut quiet);
        let tail = out[2 * 4800..].iter().fold(0.0f32, |a, &x| a.max(x.abs()));
        assert!(tail <= LIMIT * OUTPUT_TRIM + 1e-3, "{tail}");
    }

    #[test]
    fn clicks_play_without_a_bank_and_a_full_queue_drops() {
        let mut m = Mixer::new(RATE, Vec::new(), None, 1.0);
        let mut v = Voice::sample(1, 0, 0.0, 1.0);
        v.source = Source::Click(1000.0);
        v.at = 10.0 / RATE;
        m.apply(Command::Play(v), &mut quiet);
        let mut out = vec![0.0; 512];
        m.render(0.0, &mut out, &mut quiet);
        assert!(first_sound(&out).is_some_and(|f| (10..14).contains(&f)));
        let mut dropped = 0;
        for id in 0..=MAX_PENDING as u64 {
            m.apply(Command::Play(Voice::sample(id, 0, 99.0, 1.0)), &mut |r| {
                if matches!(r, Report::Dropped { .. }) {
                    dropped += 1;
                }
            });
        }
        assert_eq!(dropped, 1);
    }
}

#[cfg(test)]
mod survivors_mix_tests {
    use super::*;
    #[test]
    fn survivors_never_ducks_and_bounds_the_effects_below_the_heard_music() {
        let rate = 1000.0;
        let music: Arc<[f32]> = vec![0.1; 2000].into();
        let bank: Vec<Arc<[f32]>> = vec![vec![1.0; 2000].into()];
        let mut base = Mixer::new(rate, bank.clone(), Some(music.clone()), 1.0);
        let mut mixed = Mixer::new(rate, bank, Some(music), 1.0);
        mixed.apply(Command::SurvivorsMix(true), &mut |_| {});
        let mut voice = Voice::sample(1, 0, 0.0, 10.0);
        voice.duck = true;
        mixed.apply(Command::Play(voice), &mut |_| {});
        let mut a = vec![0.0; 2000];
        let mut b = a.clone();
        base.render(0.0, &mut a, &mut |_| {});
        mixed.render(0.0, &mut b, &mut |_| {});
        let cap = 0.1 * 10f32.powf(-12.0 / 20.0) * OUTPUT_TRIM;
        for (x, y) in a.iter().zip(&b) {
            assert!(*y >= *x - 1e-6, "music must not duck");
            assert!(*y - *x <= cap + 1e-6);
        }
        assert!(b[1999] > a[1999], "effects remain audible");
        assert_eq!(
            mixed.duck_end, 0,
            "even a duck-marked voice cannot duck survivors music"
        );
    }
    #[test]
    fn direct_system_uses_absolute_capture_rms_and_silence_has_no_effects() {
        let mut m = Mixer::new(1000.0, vec![vec![1.0; 1000].into()], None, 1.0);
        m.apply(Command::SurvivorsMix(true), &mut |_| {});
        m.apply(Command::Play(Voice::sample(1, 0, 0.0, 10.0)), &mut |_| {});
        let mut out = vec![0.0; 200];
        m.render(0.0, &mut out, &mut |_| {});
        assert!(out.iter().all(|x| *x == 0.0));
        m.apply(Command::MusicLevel(0.05), &mut |_| {});
        m.render(0.1, &mut out, &mut |_| {});
        let cap = 0.05 * 10f32.powf(-12.0 / 20.0) * OUTPUT_TRIM;
        assert!(out.iter().all(|x| x.abs() <= cap + 1e-6));
        assert!(out.iter().any(|x| *x > 0.0));
    }
}

#[cfg(test)]
mod calibration_ceiling_tests {
    use super::*;
    #[test]
    fn reference_clicks_remain_audible_when_direct_system_music_is_silent() {
        let mut mixer = Mixer::new(8000.0, Vec::new(), None, 1.0);
        mixer.apply(Command::SurvivorsMix(true), &mut |_| {});
        mixer.apply(Command::MusicLevel(0.0), &mut |_| {});
        mixer.apply(
            Command::Play(Voice {
                source: Source::Click(1000.0),
                ..Voice::sample(1, 0, 0.0, 1.0)
            }),
            &mut |_| {},
        );
        let mut out = vec![0.0; 480];
        mixer.render(0.0, &mut out, &mut |_| {});
        assert!(
            out.iter().any(|x| x.abs() > 0.01),
            "calibration cannot depend on music that excludes our own click"
        );
    }
}
