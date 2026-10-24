//! Core Audio helpers: properties, devices, host time.
#![allow(deprecated)]

use std::ffi::c_void;
use std::ptr::NonNull;

use objc2::rc::Retained;
use objc2_core_audio::*;
use objc2_foundation::NSString;

pub type Id = AudioObjectID;
pub const SYSTEM: Id = kAudioObjectSystemObject as Id;

/// Seconds per mach tick.
pub fn tick_s() -> f64 {
    static TB: std::sync::OnceLock<f64> = std::sync::OnceLock::new();
    *TB.get_or_init(|| {
        let mut info = libc::mach_timebase_info { numer: 0, denom: 0 };
        unsafe { libc::mach_timebase_info(&mut info) };
        info.numer as f64 / info.denom as f64 * 1e-9
    })
}

pub fn now_ticks() -> u64 {
    unsafe { libc::mach_absolute_time() }
}

pub fn host_s(ticks: u64) -> f64 {
    ticks as f64 * tick_s()
}

pub fn now_s() -> f64 {
    host_s(now_ticks())
}

pub fn addr(selector: u32, scope: u32) -> AudioObjectPropertyAddress {
    AudioObjectPropertyAddress {
        mSelector: selector,
        mScope: scope,
        mElement: kAudioObjectPropertyElementMain,
    }
}

pub fn check(status: i32, what: &str) -> Result<(), String> {
    if status == 0 {
        Ok(())
    } else {
        let b = status.to_be_bytes();
        let fourcc = if b.iter().all(|c| c.is_ascii_graphic()) {
            format!(" '{}'", String::from_utf8_lossy(&b))
        } else {
            String::new()
        };
        Err(format!("{what}: OSStatus {status}{fourcc}"))
    }
}

/// A fixed-size property value.
pub fn get<T: Copy>(obj: Id, selector: u32, scope: u32) -> Result<T, String> {
    get_q(obj, selector, scope, None::<&u8>)
}

pub fn get_q<T: Copy, Q>(
    obj: Id,
    selector: u32,
    scope: u32,
    qualifier: Option<&Q>,
) -> Result<T, String> {
    let a = addr(selector, scope);
    let mut out = std::mem::MaybeUninit::<T>::zeroed();
    let mut size = std::mem::size_of::<T>() as u32;
    let (qs, qp) = match qualifier {
        Some(q) => (
            std::mem::size_of::<Q>() as u32,
            q as *const Q as *const c_void,
        ),
        None => (0, std::ptr::null()),
    };
    let st = unsafe {
        AudioObjectGetPropertyData(
            obj,
            NonNull::from(&a),
            qs,
            qp,
            NonNull::from(&mut size),
            NonNull::new(out.as_mut_ptr() as *mut c_void).unwrap(),
        )
    };
    check(st, &format!("get {:?} on {obj}", fourcc(selector)))?;
    Ok(unsafe { out.assume_init() })
}

/// A variable-length array property.
pub fn get_vec<T: Copy + Default>(obj: Id, selector: u32, scope: u32) -> Result<Vec<T>, String> {
    let a = addr(selector, scope);
    let mut size = 0u32;
    let st = unsafe {
        AudioObjectGetPropertyDataSize(
            obj,
            NonNull::from(&a),
            0,
            std::ptr::null(),
            NonNull::from(&mut size),
        )
    };
    check(st, "size")?;
    let n = size as usize / std::mem::size_of::<T>();
    let mut v = vec![T::default(); n];
    if n == 0 {
        return Ok(v);
    }
    let st = unsafe {
        AudioObjectGetPropertyData(
            obj,
            NonNull::from(&a),
            0,
            std::ptr::null(),
            NonNull::from(&mut size),
            NonNull::new(v.as_mut_ptr() as *mut c_void).unwrap(),
        )
    };
    check(st, "get vec")?;
    v.truncate(size as usize / std::mem::size_of::<T>());
    Ok(v)
}

/// A CFString property, as an NSString.
pub fn get_string(obj: Id, selector: u32, scope: u32) -> Result<Retained<NSString>, String> {
    let p: *mut c_void = get(obj, selector, scope)?;
    unsafe { Retained::from_raw(p as *mut NSString) }.ok_or_else(|| "null string".into())
}

pub fn fourcc(x: u32) -> String {
    String::from_utf8_lossy(&x.to_be_bytes()).into_owned()
}

pub struct Device {
    pub id: Id,
    pub name: String,
    pub uid: String,
    pub transport: String,
    pub rate: f64,
    pub buffer_frames: u32,
    pub out: Option<Latency>,
    pub input: Option<Latency>,
}

/// What Core Audio reports for one direction of a device, in frames.
#[derive(Clone, Copy, Debug, serde::Serialize)]
pub struct Latency {
    pub device: u32,
    pub safety: u32,
    pub stream: u32,
}

impl Latency {
    pub fn frames(&self, buffer: u32) -> u32 {
        self.device + self.safety + self.stream + buffer
    }
}

fn latency(id: Id, scope: u32) -> Option<Latency> {
    let streams: Vec<Id> = get_vec(id, kAudioDevicePropertyStreams, scope).ok()?;
    if streams.is_empty() {
        return None;
    }
    Some(Latency {
        device: get(id, kAudioDevicePropertyLatency, scope).unwrap_or(0),
        safety: get(id, kAudioDevicePropertySafetyOffset, scope).unwrap_or(0),
        stream: get(
            streams[0],
            kAudioStreamPropertyLatency,
            kAudioObjectPropertyScopeGlobal,
        )
        .unwrap_or(0),
    })
}

pub fn device(id: Id) -> Device {
    let g = kAudioObjectPropertyScopeGlobal;
    Device {
        id,
        name: get_string(id, kAudioObjectPropertyName, g)
            .map(|s| s.to_string())
            .unwrap_or_default(),
        uid: get_string(id, kAudioDevicePropertyDeviceUID, g)
            .map(|s| s.to_string())
            .unwrap_or_default(),
        transport: fourcc(get(id, kAudioDevicePropertyTransportType, g).unwrap_or(0)),
        rate: get(id, kAudioDevicePropertyNominalSampleRate, g).unwrap_or(0.0),
        buffer_frames: get(id, kAudioDevicePropertyBufferFrameSize, g).unwrap_or(0),
        out: latency(id, kAudioObjectPropertyScopeOutput),
        input: latency(id, kAudioObjectPropertyScopeInput),
    }
}

pub fn devices() -> Vec<Device> {
    get_vec::<Id>(
        SYSTEM,
        kAudioHardwarePropertyDevices,
        kAudioObjectPropertyScopeGlobal,
    )
    .unwrap_or_default()
    .into_iter()
    .map(device)
    .collect()
}

pub fn default_output() -> Id {
    get(
        SYSTEM,
        kAudioHardwarePropertyDefaultOutputDevice,
        kAudioObjectPropertyScopeGlobal,
    )
    .unwrap()
}

pub fn default_input() -> Id {
    get(
        SYSTEM,
        kAudioHardwarePropertyDefaultInputDevice,
        kAudioObjectPropertyScopeGlobal,
    )
    .unwrap()
}

pub fn print_devices() {
    let def_out = default_output();
    let def_in = default_input();
    for d in devices() {
        let mark = if d.id == def_out {
            " [default out]"
        } else if d.id == def_in {
            " [default in]"
        } else {
            ""
        };
        println!(
            "{} '{}' transport={} rate={} buffer={}{}",
            d.id, d.name, d.transport, d.rate, d.buffer_frames, mark
        );
        for (dir, l) in [("out", d.out), ("in", d.input)] {
            if let Some(l) = l {
                let f = l.frames(d.buffer_frames);
                println!(
                    "    {dir}: device {} + safety {} + stream {} + buffer {} = {} frames = {:.2} ms",
                    l.device,
                    l.safety,
                    l.stream,
                    d.buffer_frames,
                    f,
                    f as f64 / d.rate * 1e3
                );
            }
        }
    }
}
