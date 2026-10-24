//! The playable slice (docs/design/native-app.md, "Phase V: The Playable Slice"): a rough game
//! on live audio, deleted by C3 when the real runner lands. The tap (or a `--file` played through
//! the output) feeds the analyzer on a thread; committed beats arrive with the host time they are
//! heard. The game (`game`): move with the arrows or WASD, enemies step toward you on each beat,
//! and Space (or J, K) attacks around you, landing only on a beat. Five touches end the run, as
//! do 3 s of silence; Space or Enter starts the next (and after silence, the music returning).
//!
//!     playlist-survivors               # System mode: whatever the Mac is playing
//!     playlist-survivors --file <path> # plays the file and listens to it
//!     playlist-survivors --click       # also clicks on each committed beat
//!     playlist-survivors --file <path> --volume 0.3  # the file quieter (0: silent, still heard)

// The tap and output are the phase 0 spike's (57e846a^:native/spike), kept as they were.
#[allow(clippy::pedantic, unused_qualifications, dead_code)]
mod coreaudio;
mod game;
mod gfx;
mod live;
#[allow(
    clippy::pedantic,
    clippy::explicit_counter_loop,
    unused_qualifications,
    deprecated,
    dead_code
)]
mod output;
#[allow(
    clippy::pedantic,
    clippy::explicit_counter_loop,
    unused_qualifications,
    deprecated,
    dead_code
)]
mod tap;

use std::collections::VecDeque;
use std::path::PathBuf;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc;

use winit::application::ApplicationHandler;
use winit::event::{ElementState, WindowEvent};
use winit::event_loop::{ActiveEventLoop, ControlFlow, EventLoop};
use winit::keyboard::{KeyCode, PhysicalKey};
use winit::window::{Window, WindowId};

use coreaudio::now_s;
use game::{End, Game, Input, Judgement};
use gfx::{Gfx, Rect};
use live::{Msg, Source};
use output::{Click, Output};

/// The tap's timeline runs this far behind the output's render timeline (phase 0, built-in
/// speakers); `PS_TAP_MINUS_RENDER_MS` overrides it.
const TAP_MINUS_RENDER_S: f64 = 0.023;
/// A captured buffer this loud is music, not silence.
const SOUND_PEAK: f32 = 0.01;
/// This long without sound ends the run.
const SILENCE_S: f64 = 3.0;

struct Options {
    file: Option<PathBuf>,
    volume: f32,
    click: bool,
}

fn options(args: &[String]) -> Result<Options, String> {
    let mut o = Options {
        file: None,
        volume: 1.0,
        click: false,
    };
    let mut it = args.iter();
    while let Some(a) = it.next() {
        match a.as_str() {
            "--file" => o.file = Some(it.next().ok_or("--file needs a path")?.into()),
            "--click" => o.click = true,
            "--volume" => {
                o.volume = it
                    .next()
                    .and_then(|v| v.parse().ok())
                    .ok_or("--volume needs a number")?;
            }
            // (Finder passes a process serial number to a bundled app.)
            s if s.starts_with("-psn_") => {}
            other => return Err(format!("unknown argument {other}")),
        }
    }
    Ok(o)
}

#[allow(clippy::single_match_else)]
pub fn run(args: &[String]) -> Result<(), String> {
    let opts = options(args)?;
    let music = match &opts.file {
        Some(p) => Some(Arc::<[f32]>::from(live::decode(p, Output::device_rate())?)),
        None => None,
    };
    // Output first: our process object must exist before the tap can exclude it.
    let mut out = Output::start(music.clone(), opts.volume)?;
    let l_out = Output::reported_latency_s();
    let (source, heard_after, mode) = match music {
        Some(samples) => (
            Source::File {
                samples,
                metas: out.music_meta.take().ok_or("no music timing")?,
                rate: out.rate,
            },
            l_out,
            "file",
        ),
        None => {
            let tap = tap::Tap::start(true)?;
            let offset = std::env::var("PS_TAP_MINUS_RENDER_MS")
                .ok()
                .and_then(|s| s.parse::<f64>().ok())
                .map_or(TAP_MINUS_RENDER_S, |ms| ms / 1e3);
            (Source::Tap(tap), l_out - offset, "system")
        }
    };
    eprintln!(
        "slice: {mode} mode, output {} Hz, L_out {:.1} ms, heard {:.1} ms after capture",
        out.rate,
        l_out * 1e3,
        heard_after * 1e3
    );
    let event_loop = EventLoop::new().map_err(|e| e.to_string())?;
    let stop = Arc::new(AtomicBool::new(false));
    let (tx, rx) = mpsc::channel();
    let analyzer = live::spawn(
        source,
        heard_after,
        tx,
        event_loop.create_proxy(),
        stop.clone(),
    )?;
    let mut app = App {
        gfx: None,
        rx,
        out,
        l_out,
        click: opts.click,
        beats: VecDeque::new(),
        peak: 0.0,
        committed: 0,
        title_at: 0.0,
        game: Game::new(now_s().to_bits()),
        input: Input::default(),
        last_frame: now_s(),
        last_sound: f64::NEG_INFINITY,
        hurt_seen: 0.0,
        logged_over: None,
    };
    let result = event_loop.run_app(&mut app).map_err(|e| e.to_string());
    stop.store(true, Ordering::Relaxed);
    let _ = analyzer.join();
    result
}

struct Beat {
    heard: f64,
    period: f64,
}

struct App {
    gfx: Option<Gfx>,
    rx: mpsc::Receiver<Msg>,
    out: Output,
    l_out: f64,
    click: bool,
    /// Recent and upcoming committed beats, by heard time.
    beats: VecDeque<Beat>,
    /// The loudest sample heard in the last moments.
    peak: f32,
    committed: usize,
    title_at: f64,
    game: Game,
    input: Input,
    last_frame: f64,
    /// When sound was last heard (host s).
    last_sound: f64,
    /// The last time the hurt sound played for.
    hurt_seen: f64,
    logged_over: Option<f64>,
}

impl App {
    fn drain(&mut self) {
        let now = now_s();
        while let Ok(m) = self.rx.try_recv() {
            match m {
                Msg::Beat { commit, heard } => {
                    if self.click {
                        let _ = self.out.clicks.push(Click {
                            at: heard - self.l_out,
                            freq: 1800.0,
                        });
                    }
                    self.committed += 1;
                    if std::env::var_os("PS_SLICE_LOG").is_some() {
                        eprintln!(
                            "beat t {:.3} known {:.3} period {:.3} heard in {:+.0} ms",
                            commit.t,
                            commit.known_at,
                            commit.period,
                            (heard - now) * 1e3
                        );
                    }
                    self.beats.push_back(Beat {
                        heard,
                        period: commit.period,
                    });
                    self.game.beat(heard);
                }
                Msg::Level { peak, heard } => {
                    if heard <= now + 0.5 {
                        self.peak = self.peak.max(peak);
                    }
                    if peak >= SOUND_PEAK {
                        self.last_sound = self.last_sound.max(heard);
                    }
                }
            }
        }
        while self.beats.front().is_some_and(|b| b.heard < now - 2.0) {
            self.beats.pop_front();
        }
        while self.out.events.pop().is_ok() {}
    }

    fn render(&mut self) {
        let now = now_s();
        self.peak *= 0.97;
        if now > self.title_at {
            self.title_at = now + 0.5;
            if let Some(g) = &self.gfx {
                let bpm = self.beats.back().map_or(0.0, |b| 60.0 / b.period);
                g.window.set_title(&format!(
                    "Playlist Survivors (slice) - {} beats, {bpm:.0} bpm",
                    self.committed
                ));
            }
        }
        // Shown at about the next vsync.
        let shown = now + 1.0 / 60.0;
        let dt = (now - self.last_frame).clamp(0.0, 0.1) as f32;
        self.last_frame = now;
        self.game.update(shown, dt, self.input);
        if self.game.hurt_since(self.hurt_seen) {
            self.hurt_seen = shown;
            self.sound(220.0);
        }
        match self.game.over {
            None if self.last_sound.is_finite() && shown - self.last_sound > SILENCE_S => {
                self.game.end(shown, End::Silence);
            }
            Some((t, why)) if self.logged_over != Some(t) => {
                self.logged_over = Some(t);
                eprintln!("slice: run over ({why:?}), score {}", self.game.score);
            }
            Some((t, End::Silence)) if self.last_sound > t && shown > t + 1.0 => self.restart(),
            _ => {}
        }
        let Some(g) = self.gfx.as_mut() else { return };
        let (w, h) = g.logical_size();
        let mut rects = Vec::new();
        self.game.draw(shown, w, h, &mut rects);
        // The level meter along the bottom.
        rects.push(Rect::new(
            0.0,
            h - 4.0,
            w * self.peak.min(1.0),
            4.0,
            [0.3, 0.8, 0.4, 0.8],
        ));
        g.draw([0.02, 0.02, 0.03], &rects);
    }

    /// A short blip now (hits and hurts).
    fn sound(&mut self, freq: f32) {
        let _ = self.out.clicks.push(Click { at: now_s(), freq });
    }

    /// A new run, fed the committed beats still to come.
    fn restart(&mut self) {
        let now = now_s();
        self.game = Game::new(now.to_bits());
        for b in self.beats.iter().filter(|b| b.heard > now) {
            self.game.beat(b.heard);
        }
        self.hurt_seen = now;
    }

    fn key(&mut self, code: KeyCode, pressed: bool, repeat: bool) {
        match code {
            KeyCode::ArrowLeft | KeyCode::KeyA => self.input.left = pressed,
            KeyCode::ArrowRight | KeyCode::KeyD => self.input.right = pressed,
            KeyCode::ArrowUp | KeyCode::KeyW => self.input.up = pressed,
            KeyCode::ArrowDown | KeyCode::KeyS => self.input.down = pressed,
            KeyCode::Space | KeyCode::Enter
                if pressed && self.game.over.is_some_and(|(t, _)| now_s() > t + 1.0) =>
            {
                self.restart();
            }
            KeyCode::Space | KeyCode::KeyJ | KeyCode::KeyK if pressed && !repeat => {
                let j = self.game.attack(now_s());
                if let Judgement::Hit { killed, .. } = j {
                    self.sound(if killed > 0 { 1500.0 } else { 1000.0 });
                }
                if std::env::var_os("PS_SLICE_LOG").is_some() {
                    eprintln!("attack {j:?}");
                }
            }
            _ => {}
        }
    }
}

impl ApplicationHandler for App {
    fn resumed(&mut self, el: &ActiveEventLoop) {
        if self.gfx.is_some() {
            return;
        }
        let window = el.create_window(
            Window::default_attributes()
                .with_title("Playlist Survivors (slice)")
                .with_inner_size(winit::dpi::LogicalSize::new(960, 640)),
        );
        match window
            .map_err(|e| e.to_string())
            .and_then(|w| Gfx::new(Arc::new(w)))
        {
            Ok(g) => self.gfx = Some(g),
            Err(e) => {
                eprintln!("slice: no window: {e}");
                el.exit();
            }
        }
    }

    fn user_event(&mut self, _el: &ActiveEventLoop, (): ()) {
        self.drain();
    }

    fn window_event(&mut self, el: &ActiveEventLoop, _id: WindowId, event: WindowEvent) {
        match event {
            WindowEvent::CloseRequested => el.exit(),
            WindowEvent::Resized(s) => {
                if let Some(g) = self.gfx.as_mut() {
                    g.resize(s.width, s.height);
                }
            }
            WindowEvent::KeyboardInput { event, .. } => {
                if let PhysicalKey::Code(code) = event.physical_key {
                    self.key(code, event.state == ElementState::Pressed, event.repeat);
                }
            }
            WindowEvent::RedrawRequested => {
                self.drain();
                self.render();
            }
            _ => {}
        }
    }

    fn about_to_wait(&mut self, el: &ActiveEventLoop) {
        if let Some(g) = &self.gfx {
            g.window.request_redraw();
        }
        el.set_control_flow(ControlFlow::Poll);
    }
}
