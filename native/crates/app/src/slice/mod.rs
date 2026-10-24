//! The playable slice (docs/design/native-app.md, "Phase V: The Playable Slice"): a rough game
//! on live audio, deleted by C3 when the real runner lands. The tap (or a `--file` played through
//! the output) feeds the analyzer on a thread; committed beats arrive with the host time they are
//! heard, and the window flashes on them.
//!
//!     playlist-survivors               # System mode: whatever the Mac is playing
//!     playlist-survivors --file <path> # plays the file and listens to it
//!     playlist-survivors --click       # also clicks on each committed beat
//!     playlist-survivors --file <path> --volume 0.3  # the file quieter (0: silent, still heard)

// The tap and output are the phase 0 spike's (57e846a^:native/spike), kept as they were.
#[allow(clippy::pedantic, unused_qualifications, dead_code)]
mod coreaudio;
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
use winit::event::WindowEvent;
use winit::event_loop::{ActiveEventLoop, ControlFlow, EventLoop};
use winit::window::{Window, WindowId};

use coreaudio::now_s;
use gfx::{Gfx, Rect};
use live::{Msg, Source};
use output::{Click, Output};

/// The tap's timeline runs this far behind the output's render timeline (phase 0, built-in
/// speakers); `PS_TAP_MINUS_RENDER_MS` overrides it.
const TAP_MINUS_RENDER_S: f64 = 0.023;

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
                }
                Msg::Level { peak, heard } => {
                    if heard <= now + 0.5 {
                        self.peak = self.peak.max(peak);
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
        let Some(g) = self.gfx.as_mut() else { return };
        // Shown at about the next vsync.
        let shown = now + 1.0 / 60.0;
        let mut flash = 0.0f64;
        for b in &self.beats {
            let d = shown - b.heard;
            if (0.0..0.12).contains(&d) {
                flash = flash.max(1.0 - d / 0.12);
            }
        }
        let (w, h) = g.logical_size();
        let mut rects = Vec::new();
        // The level meter, and the next beats approaching a line.
        rects.push(Rect::new(
            0.0,
            h - 6.0,
            w * self.peak.min(1.0),
            6.0,
            [0.3, 0.8, 0.4, 1.0],
        ));
        let line = w * 0.2;
        rects.push(Rect::new(line - 1.0, 0.0, 2.0, 24.0, [1.0, 1.0, 1.0, 0.8]));
        for b in &self.beats {
            let x = line + (b.heard - shown) as f32 * w * 0.4;
            if (0.0..w).contains(&x) {
                rects.push(Rect::centered(x, 12.0, 10.0, [1.0, 0.8, 0.3, 1.0]));
            }
        }
        let c = 0.05 + 0.9 * flash;
        g.draw([c, c * 0.8, c * 0.3], &rects);
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
