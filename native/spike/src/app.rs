//! The live spike: tap -> analyzer thread (+ hindsight worker) -> committed
//! beats -> a winit + wgpu window flashing at their heard times, and an AUHAL
//! click on each. Logs the latency chain and prints a summary on exit.

use std::collections::VecDeque;
use std::ptr::NonNull;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use block2::RcBlock;
use objc2_app_kit::{NSEvent, NSEventMask};
use objc2_core_video::{CVDisplayLink, CVTimeStamp};
use winit::application::ApplicationHandler;
use winit::event::WindowEvent;
use winit::event_loop::{ActiveEventLoop, ControlFlow, EventLoop, EventLoopProxy};
use winit::window::{Window, WindowId};

use crate::analyzer::{Analyzer, Hindsight};
use crate::coreaudio::{host_s, now_s};
use crate::dbn::Beat;
use crate::models::{Tensors, models_dir};
use crate::output::{self, Output};
use crate::stats::summarize;
use crate::tap::{Meta, Tap};

const SR: f64 = 44100.0;

/// Measured offsets (built-in speakers, loopback test): the tap's timeline is
/// 23.0 ms behind the render timeline; heard = render + reported output latency.
fn l_tap_s(l_out: f64) -> f64 {
    let tap_minus_render = std::env::var("PS_TAP_MINUS_RENDER_MS")
        .ok()
        .and_then(|s| s.parse::<f64>().ok())
        .unwrap_or(23.0)
        / 1e3;
    l_out - tap_minus_render
}

#[derive(Debug, Clone, Copy)]
struct BeatMsg {
    beat: Beat,
    /// Heard time of the beat (host seconds).
    heard: f64,
    arrival: f64,
}

#[derive(Default)]
struct Log {
    /// capture -> arrival per activation frame (ms).
    frame_latency: Vec<f64>,
    /// capture -> arrival per committed beat (ms).
    beat_latency: Vec<f64>,
    /// Lead of each committed beat's heard time over its arrival (ms).
    beat_lead: Vec<f64>,
    buffer_frames: Vec<f64>,
    buffer_interval: Vec<f64>,
    ioproc_lag: Vec<f64>,
    analyzer_busy_s: f64,
    audio_s: f64,
    hindsight_ms: Vec<f64>,
    hindsight_ready_after_window_ms: Vec<f64>,
    key_to_main: Vec<f64>,
    key_to_winit: Vec<f64>,
    synthetic_key_to_main: Vec<f64>,
    main_to_target: Vec<f64>,
    main_to_vsync_after_present: Vec<f64>,
    frame_cpu: Vec<f64>,
    /// First vsync after the present minus the vsync the flash was timed for.
    shown_vs_after: Vec<f64>,
    /// The app's own share of capture -> arrival: IOProc run -> published, per frame.
    app_share: Vec<f64>,
    dropped: u64,
    flash_offset: Vec<f64>,
    click_late: usize,
    clicks: usize,
    beats: Vec<BeatMsg>,
    peak: f32,
    /// The analyzer's 44.1 kHz stream, for offline alignment.
    stream: Vec<f32>,
    /// (tap sample index, tap input host time) per buffer.
    anchors: Vec<(u64, f64)>,
    /// (requested render time, actual render time) per click.
    played: Vec<(f64, f64)>,
}

/// Linear resampler from the tap's rate to 44.1 kHz (the spike's stand-in for rubato).
struct Resampler {
    ratio: f64,
    /// Input position (in input samples) of the next output sample.
    pos: f64,
    /// Input samples consumed before `buf[0]`.
    base: u64,
    buf: Vec<f32>,
}

impl Resampler {
    fn push(&mut self, input: &[f32], out: &mut Vec<f32>) {
        self.buf.extend_from_slice(input);
        loop {
            let i = (self.pos - self.base as f64).floor() as usize;
            if i + 1 >= self.buf.len() {
                break;
            }
            let frac = (self.pos - self.base as f64 - i as f64) as f32;
            out.push(self.buf[i] * (1.0 - frac) + self.buf[i + 1] * frac);
            self.pos += self.ratio;
        }
        let keep_from = ((self.pos - self.base as f64).floor() as usize).min(self.buf.len());
        self.buf.drain(..keep_from);
        self.base += keep_from as u64;
    }
}

/// Host time of input sample `i` from the recent buffer metas (extrapolating at the nominal rate).
fn host_of(metas: &VecDeque<Meta>, rate: f64, i: f64) -> Option<f64> {
    let m = metas
        .iter()
        .rev()
        .find(|m| (m.first as f64) <= i)
        .or_else(|| metas.front())?;
    Some(host_s(m.input_host) + (i - m.first as f64) / rate)
}

fn analyzer_thread(
    mut tap: Tap,
    l_tap: f64,
    tx: mpsc::Sender<BeatMsg>,
    proxy: EventLoopProxy<()>,
    log: Arc<Mutex<Log>>,
    stop: Arc<AtomicBool>,
    hindsight_tx: mpsc::Sender<(Vec<f32>, f64)>,
) {
    let t = Tensors::load(&models_dir().join("models.bin"));
    let mut an = Analyzer::new(&t);
    let rate = tap.rate;
    let mut rs = Resampler {
        ratio: rate / SR,
        pos: 0.0,
        base: 0,
        buf: Vec::new(),
    };
    let mut metas: VecDeque<Meta> = VecDeque::new();
    let mut raw = Vec::new();
    let mut y = Vec::new();
    let mut beats = Vec::new();
    let mut history: Vec<f32> = Vec::new();
    let mut next_hindsight = SR as usize;
    // Absolute 44.1 kHz index of history[0] (history is trimmed as it grows).
    let mut hist_base = 0usize;
    let mut last_now = None;
    while !stop.load(Ordering::Relaxed) {
        let mut new_metas = Vec::new();
        raw.clear();
        tap.drain(&mut raw, &mut new_metas);
        {
            let mut l = log.lock().unwrap();
            for m in &new_metas {
                l.buffer_frames.push(m.frames as f64);
                if let Some(prev) = last_now {
                    l.buffer_interval
                        .push((host_s(m.now_host) - host_s(prev)) * 1e3);
                }
                last_now = Some(m.now_host);
                l.ioproc_lag
                    .push((host_s(m.now_host) - host_s(m.input_host)) * 1e3);
                l.anchors.push((m.first, host_s(m.input_host)));
                l.peak = l.peak.max(m.peak);
                l.dropped = m.dropped;
            }
        }
        metas.extend(new_metas);
        while metas.len() > 6000 {
            metas.pop_front();
        }
        if raw.is_empty() {
            std::thread::sleep(Duration::from_micros(500));
            continue;
        }
        let busy = std::time::Instant::now();
        y.clear();
        rs.push(&raw, &mut y);
        let frames_before = an.frames();
        beats.clear();
        an.push(&y, &mut beats);
        let arrival = now_s();
        let frames_after = an.frames();
        history.extend_from_slice(&y);
        let mut l = log.lock().unwrap();
        l.analyzer_busy_s += busy.elapsed().as_secs_f64();
        l.stream.extend_from_slice(&y);
        l.audio_s += y.len() as f64 / SR;
        // capture -> arrival for every frame this block finished: its last sample's capture time.
        for n in frames_before..frames_after {
            let last44 = (n as f64 * SR / 100.0).floor();
            let i = (last44 * rs.ratio).ceil();
            if let Some(h) = host_of(&metas, rate, i) {
                l.frame_latency.push((arrival - h) * 1e3);
            }
            if let Some(m) = metas.iter().rev().find(|m| (m.first as f64) <= i) {
                l.app_share.push((arrival - host_s(m.now_host)) * 1e3);
            }
        }
        for b in &beats {
            let n = (b.known_at * 100.0).round();
            let last44 = (n * SR / 100.0).floor();
            let Some(h_known) = host_of(&metas, rate, (last44 * rs.ratio).ceil()) else {
                continue;
            };
            let Some(h_beat) = host_of(&metas, rate, b.t * SR * rs.ratio) else {
                continue;
            };
            let heard = h_beat + l_tap;
            l.beat_latency.push((arrival - h_known) * 1e3);
            l.beat_lead.push((heard - arrival) * 1e3);
            let msg = BeatMsg {
                beat: *b,
                heard,
                arrival,
            };
            l.beats.push(msg);
            let _ = tx.send(msg);
            let _ = proxy.send_event(());
        }
        drop(l);
        // Hindsight: every 1 s of audio, the last 20 s, to the worker.
        while history.len() >= next_hindsight {
            let end = next_hindsight;
            let start = end.saturating_sub(20 * SR as usize);
            let window = history[start..end].to_vec();
            let end_host =
                host_of(&metas, rate, (hist_base + end) as f64 * rs.ratio).unwrap_or(arrival);
            let _ = hindsight_tx.send((window, end_host));
            next_hindsight += SR as usize;
        }
        if history.len() > 40 * SR as usize {
            let cut = history.len() - 25 * SR as usize;
            history.drain(..cut);
            next_hindsight -= cut;
            hist_base += cut;
        }
    }
}

fn hindsight_thread(rx: mpsc::Receiver<(Vec<f32>, f64)>, log: Arc<Mutex<Log>>) {
    let t = Tensors::load(&models_dir().join("models.bin"));
    let mut h = Hindsight::new(&t);
    while let Ok((window, end_host)) = rx.recv() {
        let s = std::time::Instant::now();
        let _d = h.decode(&window);
        let ms = s.elapsed().as_secs_f64() * 1e3;
        let mut l = log.lock().unwrap();
        // Only full 20 s windows count for the decode time.
        if window.len() >= 20 * SR as usize {
            l.hindsight_ms.push(ms);
        }
        l.hindsight_ready_after_window_ms
            .push((now_s() - end_host) * 1e3);
    }
}

/// The display link's latest (output time, period), written from its thread.
#[derive(Default, Clone, Copy)]
struct Vsync {
    output: f64,
    period: f64,
}

unsafe extern "C-unwind" fn display_link_cb(
    _link: NonNull<CVDisplayLink>,
    _now: NonNull<CVTimeStamp>,
    out: NonNull<CVTimeStamp>,
    _flags: u64,
    _flags_out: NonNull<u64>,
    ctx: *mut std::ffi::c_void,
) -> i32 {
    let v = unsafe { &*(ctx as *const Mutex<Vsync>) };
    let o = unsafe { out.as_ref() };
    let period = if o.videoTimeScale > 0 && o.videoRefreshPeriod > 0 {
        o.videoRefreshPeriod as f64 / o.videoTimeScale as f64
    } else {
        1.0 / 60.0
    };
    *v.lock().unwrap() = Vsync {
        output: host_s(o.hostTime),
        period,
    };
    0
}

struct Gfx {
    window: Arc<Window>,
    surface: wgpu::Surface<'static>,
    device: wgpu::Device,
    queue: wgpu::Queue,
    config: wgpu::SurfaceConfiguration,
}

struct App {
    gfx: Option<Gfx>,
    rx: mpsc::Receiver<BeatMsg>,
    beats: VecDeque<BeatMsg>,
    out: Output,
    l_out: f64,
    log: Arc<Mutex<Log>>,
    vsync: Arc<Mutex<Vsync>>,
    end_at: f64,
    stop: Arc<AtomicBool>,
    key_times: Arc<Mutex<VecDeque<(f64, f64)>>>,
    mute: bool,
    last_frame_s: f64,
}

impl App {
    fn render(&mut self) {
        let Some(g) = self.gfx.as_mut() else { return };
        let t0 = now_s();
        let vs = *self.vsync.lock().unwrap();
        // The display link's next vsync after now (logged as main -> display-link target).
        let mut target = vs.output;
        if vs.period > 0.0 {
            while target < t0 {
                target += vs.period;
            }
        }
        // The vsync this frame can actually reach: Fifo with frame latency 1 blocks in
        // the present, so a frame shows at the first vsync after the previous frame's
        // present-blocking time from now (review finding: `target` is already missed).
        let mut shown = vs.output;
        if vs.period > 0.0 {
            while shown < t0 + self.last_frame_s {
                shown += vs.period;
            }
        }
        // Flash: brightest at a beat's heard time, decaying over 80 ms.
        let mut flash = 0.0f64;
        for b in &self.beats {
            let d = shown - b.heard;
            if (0.0..0.08).contains(&d) {
                flash = flash.max(1.0 - d / 0.08);
            }
        }
        // The first frame whose target passes a beat's heard time shows it: log the offset.
        {
            let mut l = self.log.lock().unwrap();
            for b in self.beats.iter_mut() {
                if b.arrival.is_finite() && shown >= b.heard {
                    l.flash_offset.push((shown - b.heard) * 1e3);
                    b.arrival = f64::INFINITY; // logged
                }
            }
        }
        let frame = match g.surface.get_current_texture() {
            wgpu::CurrentSurfaceTexture::Success(f)
            | wgpu::CurrentSurfaceTexture::Suboptimal(f) => f,
            _ => {
                g.surface.configure(&g.device, &g.config);
                return;
            }
        };
        let view = frame.texture.create_view(&Default::default());
        let mut enc = g.device.create_command_encoder(&Default::default());
        {
            let c = 0.05 + 0.95 * flash;
            let _pass = enc.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: None,
                color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                    view: &view,
                    depth_slice: None,
                    resolve_target: None,
                    ops: wgpu::Operations {
                        load: wgpu::LoadOp::Clear(wgpu::Color {
                            r: c,
                            g: c * 0.8,
                            b: c * 0.3,
                            a: 1.0,
                        }),
                        store: wgpu::StoreOp::Store,
                    },
                })],
                depth_stencil_attachment: None,
                timestamp_writes: None,
                occlusion_query_set: None,
                multiview_mask: None,
            });
        }
        g.queue.submit([enc.finish()]);
        g.window.pre_present_notify();
        g.queue.present(frame);
        let presented = now_s();
        let mut after = vs.output;
        if vs.period > 0.0 {
            while after < presented {
                after += vs.period;
            }
        }
        let mut l = self.log.lock().unwrap();
        l.main_to_target.push((target - t0) * 1e3);
        l.main_to_vsync_after_present.push((after - t0) * 1e3);
        l.frame_cpu.push((presented - t0) * 1e3);
        l.shown_vs_after.push((after - shown) * 1e3);
        self.last_frame_s = presented - t0;
    }

    fn drain(&mut self) {
        while let Ok(b) = self.rx.try_recv() {
            // Click at the heard time: render at heard - L_out.
            if !self.mute {
                let _ = self.out.clicks.push(output::Click {
                    at: b.heard - self.l_out,
                    freq: 1800.0,
                });
            }
            self.beats.push_back(b);
        }
        let now = now_s();
        while self.beats.front().is_some_and(|b| b.heard < now - 1.0) {
            self.beats.pop_front();
        }
        let mut l = self.log.lock().unwrap();
        while let Ok(e) = self.out.events.pop() {
            match e {
                output::Event::Played { at, actual } => {
                    l.clicks += 1;
                    l.played.push((at, actual));
                }
                output::Event::Late { .. } => l.click_late += 1,
                _ => {}
            }
        }
    }
}

impl ApplicationHandler for App {
    fn resumed(&mut self, el: &ActiveEventLoop) {
        if self.gfx.is_some() {
            return;
        }
        let window = Arc::new(
            el.create_window(
                Window::default_attributes()
                    .with_title("PS spike")
                    .with_inner_size(winit::dpi::LogicalSize::new(480, 320)),
            )
            .unwrap(),
        );
        let instance = wgpu::Instance::new(wgpu::InstanceDescriptor::new_without_display_handle());
        let surface = instance.create_surface(window.clone()).unwrap();
        let adapter = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
            compatible_surface: Some(&surface),
            ..Default::default()
        }))
        .unwrap();
        let (device, queue) =
            pollster::block_on(adapter.request_device(&Default::default())).unwrap();
        let size = window.inner_size();
        let mut config = surface
            .get_default_config(&adapter, size.width.max(1), size.height.max(1))
            .unwrap();
        config.present_mode = match std::env::var("PS_PRESENT").as_deref() {
            Ok("immediate") => wgpu::PresentMode::Immediate,
            Ok("mailbox") => wgpu::PresentMode::Mailbox,
            _ => wgpu::PresentMode::Fifo,
        };
        config.desired_maximum_frame_latency = std::env::var("PS_FRAME_LATENCY")
            .ok()
            .and_then(|s| s.parse().ok())
            .unwrap_or(1);
        println!(
            "present mode {:?}, max frame latency {}",
            config.present_mode, config.desired_maximum_frame_latency
        );
        surface.configure(&device, &config);
        self.gfx = Some(Gfx {
            window,
            surface,
            device,
            queue,
            config,
        });
    }

    fn user_event(&mut self, _el: &ActiveEventLoop, _e: ()) {
        self.drain();
    }

    fn window_event(&mut self, el: &ActiveEventLoop, _id: WindowId, event: WindowEvent) {
        match event {
            WindowEvent::CloseRequested => el.exit(),
            WindowEvent::Resized(s) => {
                if let Some(g) = self.gfx.as_mut() {
                    g.config.width = s.width.max(1);
                    g.config.height = s.height.max(1);
                    g.surface.configure(&g.device, &g.config);
                }
            }
            WindowEvent::KeyboardInput { event, .. } => {
                if event.state.is_pressed() && !event.repeat {
                    let now = now_s();
                    // Match the newest NSEvent seen by the local monitor.
                    if let Some((ts, _)) = self.key_times.lock().unwrap().back().copied() {
                        self.log.lock().unwrap().key_to_winit.push((now - ts) * 1e3);
                    }
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
        let now = now_s();
        if now > self.end_at {
            el.exit();
            return;
        }
        if std::env::var("PS_IDLE").is_ok() {
            // No continuous redraw: wake every 5 ms to check the clock.
            el.set_control_flow(ControlFlow::WaitUntil(
                std::time::Instant::now() + Duration::from_millis(5),
            ));
            return;
        }
        if let Some(g) = &self.gfx {
            g.window.request_redraw();
        }
        el.set_control_flow(ControlFlow::Poll);
    }

    fn exiting(&mut self, _el: &ActiveEventLoop) {
        let active: bool = unsafe {
            let app: *mut objc2::runtime::AnyObject =
                objc2::msg_send![objc2::class!(NSApplication), sharedApplication];
            objc2::msg_send![app, isActive]
        };
        println!("app active at exit: {active}");
        self.stop.store(true, Ordering::Relaxed);
    }
}

pub fn run(secs: f64, dir: &std::path::Path, mute: bool) {
    // Output first: our process object must exist before the tap excludes it.
    let out = Output::start().expect("output");
    let l_out = Output::reported_latency_s();
    let l_tap = l_tap_s(l_out);
    let tap = Tap::start(true).expect("tap");
    println!(
        "tap at {} Hz; L_out (reported, no buffer) {:.2} ms; L_tap {:.2} ms",
        tap.rate,
        l_out * 1e3,
        l_tap * 1e3
    );
    let event_loop = EventLoop::new().unwrap();
    let proxy = event_loop.create_proxy();
    let log = Arc::new(Mutex::new(Log::default()));
    let stop = Arc::new(AtomicBool::new(false));
    let (tx, rx) = mpsc::channel();
    let (htx, hrx) = mpsc::channel();
    {
        let (log, stop, proxy) = (log.clone(), stop.clone(), proxy.clone());
        std::thread::Builder::new()
            .name("analyzer".into())
            .spawn(move || analyzer_thread(tap, l_tap, tx, proxy, log, stop, htx))
            .unwrap();
    }
    {
        let log = log.clone();
        std::thread::Builder::new()
            .name("hindsight".into())
            .spawn(move || hindsight_thread(hrx, log))
            .unwrap();
    }

    // Key events: NSEvent timestamps through a local monitor beside winit.
    let key_times: Arc<Mutex<VecDeque<(f64, f64)>>> = Default::default();
    // Synthetic key presses: NSEvents posted to our own queue from a thread at
    // random phases (CGEventPostToPid is dropped without Accessibility). They
    // measure the main thread's pickup, not the HID and window-server path.
    let posted: Arc<Mutex<std::collections::HashSet<u64>>> = Default::default();
    let posted_m = posted.clone();
    {
        let stop = stop.clone();
        std::thread::spawn(move || {
            let mut seed = 0x2545F4914F6CDD1Du64;
            std::thread::sleep(Duration::from_secs(2));
            while !stop.load(Ordering::Relaxed) {
                seed ^= seed << 13;
                seed ^= seed >> 7;
                seed ^= seed << 17;
                std::thread::sleep(Duration::from_micros(150_000 + seed % 300_000));
                let ts = now_s();
                posted.lock().unwrap().insert(ts.to_bits());
                let chars = objc2_foundation::NSString::from_str(" ");
                let ev = NSEvent::keyEventWithType_location_modifierFlags_timestamp_windowNumber_context_characters_charactersIgnoringModifiers_isARepeat_keyCode(
                    objc2_app_kit::NSEventType::KeyDown,
                    objc2_foundation::NSPoint::new(0.0, 0.0),
                    objc2_app_kit::NSEventModifierFlags::empty(),
                    ts,
                    0,
                    None,
                    &chars,
                    &chars,
                    false,
                    49,
                );
                if let Some(ev) = ev {
                    unsafe {
                        let app: *mut objc2::runtime::AnyObject =
                            objc2::msg_send![objc2::class!(NSApplication), sharedApplication];
                        let _: () = objc2::msg_send![app, postEvent: &*ev, atStart: false];
                    }
                }
            }
        });
    }
    let kt = key_times.clone();
    let klog = log.clone();
    let block = RcBlock::new(move |ev: NonNull<NSEvent>| -> *mut NSEvent {
        let now = now_s();
        let ts = unsafe { ev.as_ref() }.timestamp();
        let mut k = kt.lock().unwrap();
        k.push_back((ts, now));
        if k.len() > 64 {
            k.pop_front();
        }
        // Events our own thread posted (their timestamps are recorded) vs real presses.
        let synthetic = posted_m
            .lock()
            .unwrap()
            .iter()
            .any(|&p| (f64::from_bits(p) - ts).abs() < 2e-6);
        let mut l = klog.lock().unwrap();
        if synthetic {
            l.synthetic_key_to_main.push((now - ts) * 1e3);
        } else {
            l.key_to_main.push((now - ts) * 1e3);
        }
        ev.as_ptr()
    });
    let _monitor = unsafe {
        NSEvent::addLocalMonitorForEventsMatchingMask_handler(NSEventMask::KeyDown, &block)
    };

    // The display link, for frame targets.
    let vsync = Arc::new(Mutex::new(Vsync::default()));
    let mut link: *mut CVDisplayLink = std::ptr::null_mut();
    #[allow(deprecated)]
    unsafe {
        objc2_core_video::CVDisplayLinkCreateWithActiveCGDisplays(NonNull::from(&mut link));
        let l = &*link;
        l.set_output_callback(Some(display_link_cb), Arc::as_ptr(&vsync) as *mut _);
        objc2_core_video::CVDisplayLinkStart(l);
    }

    let mut app = App {
        gfx: None,
        rx,
        beats: VecDeque::new(),
        out,
        l_out,
        log: log.clone(),
        vsync,
        end_at: now_s() + secs,
        stop: stop.clone(),
        key_times,
        mute,
        last_frame_s: 0.0,
    };
    event_loop.run_app(&mut app).unwrap();
    stop.store(true, Ordering::Relaxed);
    std::thread::sleep(Duration::from_millis(50));
    let l = log.lock().unwrap();
    report(&l, l_out, l_tap);
    dump(&l, dir, l_out, l_tap);
}

fn dump(l: &Log, dir: &std::path::Path, l_out: f64, l_tap: f64) {
    let bytes: Vec<u8> = l.stream.iter().flat_map(|x| x.to_le_bytes()).collect();
    std::fs::write(dir.join("app-stream.f32"), bytes).unwrap();
    let beats: Vec<_> = l
        .beats
        .iter()
        .map(|b| serde_json::json!({"t": b.beat.t, "knownAt": b.beat.known_at, "heard": b.heard}))
        .collect();
    let run = serde_json::json!({
        "lOut": l_out, "lTap": l_tap, "anchors": l.anchors, "beats": beats, "played": l.played,
        "flashOffsetMs": l.flash_offset,
    });
    std::fs::write(
        dir.join("app-run.json"),
        serde_json::to_string(&run).unwrap(),
    )
    .unwrap();
}

fn report(l: &Log, l_out: f64, l_tap: f64) {
    println!("--- live spike report (ms) ---");
    println!("tap peak level: {:.4}", l.peak);
    println!("capture buffer frames: {}", summarize(&l.buffer_frames));
    println!("capture buffer interval: {}", summarize(&l.buffer_interval));
    println!("IOProc now - tap input time: {}", summarize(&l.ioproc_lag));
    println!(
        "capture -> arrival, every activation frame: {}",
        summarize(&l.frame_latency)
    );
    println!(
        "capture -> arrival, committed beats: {}",
        summarize(&l.beat_latency)
    );
    println!(
        "IOProc run -> arrival (the app's share): {}",
        summarize(&l.app_share)
    );
    println!("tap frames dropped (ring full): {}", l.dropped);
    println!(
        "committed beat heard time - arrival (lead): {}",
        summarize(&l.beat_lead)
    );
    println!(
        "analyzer real-time factor (live, beats only): {:.4} ({:.1} s audio)",
        l.analyzer_busy_s / l.audio_s.max(1e-9),
        l.audio_s
    );
    println!(
        "hindsight 20 s decode (live, beside the app): {}",
        summarize(&l.hindsight_ms)
    );
    println!(
        "hindsight ready after window end: {}",
        summarize(&l.hindsight_ready_after_window_ms)
    );
    println!(
        "key event -> main thread (NSEvent.timestamp): {}",
        summarize(&l.key_to_main)
    );
    println!(
        "key event -> winit KeyboardInput: {}",
        summarize(&l.key_to_winit)
    );
    println!(
        "synthetic key (posted from a thread) -> main thread: {}",
        summarize(&l.synthetic_key_to_main)
    );
    println!(
        "main thread -> display-link target: {}",
        summarize(&l.main_to_target)
    );
    println!(
        "main thread -> first vsync after present: {}",
        summarize(&l.main_to_vsync_after_present)
    );
    println!(
        "frame CPU (start -> present returned): {}",
        summarize(&l.frame_cpu)
    );
    println!(
        "flash frame's vsync - heard time: {}",
        summarize(&l.flash_offset)
    );
    println!(
        "first vsync after present - flash's vsync: {}",
        summarize(&l.shown_vs_after)
    );
    println!("clicks played {} late {}", l.clicks, l.click_late);
    println!("L_out {:.2} L_tap {:.2}", l_out * 1e3, l_tap * 1e3);
    println!("committed beats: {}", l.beats.len());
    let periods: Vec<f64> = l
        .beats
        .windows(2)
        .map(|w| (w[1].beat.t - w[0].beat.t) * 1e3)
        .collect();
    println!("committed beat intervals: {}", summarize(&periods));
    let ev: Vec<f64> = l.beats.iter().map(|b| b.beat.evidence).collect();
    println!("evidence: {}", summarize(&ev));
}
