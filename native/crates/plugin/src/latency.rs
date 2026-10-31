//! The output device's latency as the OS reports it: the default output device's latency, safety
//! offset and its first stream's latency, at its nominal rate. Unity's DSP buffering is the
//! client's to add (docs/design/unity-game.md, "Clocks").

/// The default output device's latency (s); 0 when the OS doesn't say.
#[cfg(target_os = "macos")]
#[allow(deprecated)]
pub fn output_latency() -> f64 {
    use std::ffi::c_void;
    use std::ptr::NonNull;

    use objc2_core_audio::{
        AudioObjectGetPropertyData, AudioObjectID, AudioObjectPropertyAddress,
        kAudioDevicePropertyLatency, kAudioDevicePropertyNominalSampleRate,
        kAudioDevicePropertySafetyOffset, kAudioDevicePropertyStreams,
        kAudioHardwarePropertyDefaultOutputDevice, kAudioObjectPropertyElementMain,
        kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyScopeOutput, kAudioObjectSystemObject,
        kAudioStreamPropertyLatency,
    };

    fn get<T: Copy + Default>(obj: AudioObjectID, selector: u32, scope: u32) -> Option<T> {
        let address = AudioObjectPropertyAddress {
            mSelector: selector,
            mScope: scope,
            mElement: kAudioObjectPropertyElementMain,
        };
        let mut out = T::default();
        let mut size = size_of::<T>() as u32;
        // SAFETY: `out` is a `T` of `size` bytes, and Core Audio writes at most `size`.
        let status = unsafe {
            AudioObjectGetPropertyData(
                obj,
                NonNull::from(&address),
                0,
                std::ptr::null(),
                NonNull::from(&mut size),
                NonNull::from(&mut out).cast::<c_void>(),
            )
        };
        (status == 0).then_some(out)
    }

    let global = kAudioObjectPropertyScopeGlobal;
    let output = kAudioObjectPropertyScopeOutput;
    let Some(device) = get::<AudioObjectID>(
        kAudioObjectSystemObject as AudioObjectID,
        kAudioHardwarePropertyDefaultOutputDevice,
        global,
    )
    .filter(|&d| d != 0) else {
        return 0.0;
    };
    let rate = get::<f64>(device, kAudioDevicePropertyNominalSampleRate, global).unwrap_or(0.0);
    if rate <= 0.0 {
        return 0.0;
    }
    // (The first output stream; a property list of several is read for its first element.)
    let stream = get::<AudioObjectID>(device, kAudioDevicePropertyStreams, output)
        .and_then(|s| get::<u32>(s, kAudioStreamPropertyLatency, global))
        .unwrap_or(0);
    let frames = get::<u32>(device, kAudioDevicePropertyLatency, output).unwrap_or(0)
        + get::<u32>(device, kAudioDevicePropertySafetyOffset, output).unwrap_or(0)
        + stream;
    f64::from(frames) / rate
}

/// The default output device's latency (s): not reported yet off macOS (Windows comes in M5).
#[cfg(not(target_os = "macos"))]
pub fn output_latency() -> f64 {
    0.0
}
