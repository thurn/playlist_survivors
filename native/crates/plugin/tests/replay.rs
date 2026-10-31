//! The Replay source through the C interface: a recorded session's entries come back exactly, its
//! audio plays from song time 0 on both channels, and it ends with the recording.

mod common;

use std::ffi::CString;
use std::path::Path;

use common::{
    empty_entries, hang_guard, last_error, native, status, temp, wait_for_music, write_wav,
};
use ps_plugin::decode::SR;
use ps_plugin::{
    Config, PS_ENDED, PS_ERR_ARGUMENT, PS_ERR_SOURCE, PS_OK, PS_SOURCE_REPLAY, PS_WAITING, Session,
};

fn open_replay(dir: Option<&Path>, rate: f64) -> Result<*mut Session, i32> {
    let path = dir.map(|d| CString::new(d.to_str().unwrap()).unwrap());
    let config = Config {
        source: PS_SOURCE_REPLAY,
        pid: 0,
        m: 3.0,
        output_rate: rate,
        path: path.as_ref().map_or(std::ptr::null(), |p| p.as_ptr()),
        models: std::ptr::null(),
        session_dir: std::ptr::null(),
    };
    let mut s = std::ptr::null_mut();
    match unsafe { ps_plugin::ps_open(&raw const config, &raw mut s) } {
        PS_OK => Ok(s),
        code => Err(code),
    }
}

/// A session folder as the record writes one: `seconds` of a 330 Hz tone and the house-128
/// dump's records.
fn session(dir: &Path, seconds: f64) -> (Vec<f32>, Vec<String>) {
    std::fs::create_dir_all(dir).unwrap();
    let audio: Vec<f32> = (0..(seconds * SR) as usize)
        .map(|i| (0.5 * (2.0 * std::f64::consts::PI * 330.0 * i as f64 / SR).sin()) as f32)
        .collect();
    write_wav(&dir.join("audio.wav"), 1, &audio);
    let fixture =
        std::fs::read_to_string(native().join("fixtures/entries/house-128-20s.jsonl")).unwrap();
    let lines: Vec<String> = fixture.lines().skip(1).map(str::to_string).collect();
    std::fs::write(dir.join("entries.jsonl"), lines.join("\n") + "\n").unwrap();
    (audio, lines)
}

#[test]
fn a_replay_gives_back_the_recorded_entries_and_audio_and_ends_with_it() {
    const FRAMES: usize = 512;
    let dir = temp("replay");
    let (audio, lines) = session(&dir.join("session"), 2.0);
    // At the recording's own rate, every output frame is a recorded sample.
    let s = open_replay(Some(&dir.join("session")), SR).unwrap();
    let total = audio.len() as f64 / SR;
    let mut out = vec![0.0f32; 2 * FRAMES];
    let mut played = Vec::new();
    let mut frames = 0u64;
    let mut dsp = 0.0;
    while frames as f64 / SR < total + 0.1 {
        wait_for_music(s, frames as f64 / SR, FRAMES as f64 / SR, total);
        assert_eq!(
            unsafe { ps_plugin::ps_render_music(s, out.as_mut_ptr(), FRAMES as i32, 2, dsp) },
            PS_OK
        );
        dsp += FRAMES as f64 / SR;
        if status(s).state != PS_WAITING {
            played.extend_from_slice(&out);
            frames += FRAMES as u64;
        }
    }
    let mut entries = empty_entries(256);
    let mut polled = Vec::new();
    let started = std::time::Instant::now();
    loop {
        hang_guard(started, "the entries never ended");
        let done = status(s).analysis_done == 1;
        let n = unsafe { ps_plugin::ps_poll(s, entries.as_mut_ptr(), 256) };
        assert!(n >= 0, "{}", last_error());
        polled.extend(
            entries[..n as usize]
                .iter()
                .map(|e| serde_json::to_string(&e.line()).unwrap()),
        );
        if done && n == 0 {
            break;
        }
    }
    let st = status(s);
    unsafe { ps_plugin::ps_close(s) };

    assert_eq!(polled, lines, "the recorded entries, exactly and in order");
    assert_eq!(st.state, PS_ENDED);
    assert_eq!(st.stalls, 0);
    assert_eq!(st.analysed, 2.0);
    // (The last sample has nothing after it to interpolate to, as a File source's last frame.)
    for (n, &want) in audio[..audio.len() - 1].iter().enumerate() {
        let frame = &played[2 * n..2 * n + 2];
        assert!(
            frame[0] == want && frame[1] == want,
            "frame {n}: {frame:?}, want {want}"
        );
    }
    assert!(
        played[2 * audio.len()..].iter().all(|&x| x == 0.0),
        "silence after the recording"
    );
    let _ = std::fs::remove_dir_all(dir);
}

#[test]
fn a_replay_needs_a_whole_session() {
    let dir = temp("replay-errors");
    assert_eq!(open_replay(None, 48_000.0), Err(PS_ERR_ARGUMENT));
    assert_eq!(
        open_replay(Some(&dir.join("missing")), 48_000.0),
        Err(PS_ERR_SOURCE)
    );
    assert!(last_error().contains("audio.wav"), "{}", last_error());
    session(&dir.join("bad"), 0.5);
    std::fs::write(dir.join("bad/entries.jsonl"), "{\"kind\":1,\"t\":1.0}\n").unwrap();
    assert_eq!(
        open_replay(Some(&dir.join("bad")), 48_000.0),
        Err(PS_ERR_SOURCE)
    );
    assert!(last_error().contains("entries.jsonl:1"), "{}", last_error());
    let _ = std::fs::remove_dir_all(dir);
}
