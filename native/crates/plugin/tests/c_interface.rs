//! Whole File runs through the C interface: the entries are `ps entries`' dump, the music plays
//! continuously at the output rate, the anchor advances by exactly the frames rendered, the
//! session is recorded, and failures come back as codes.

mod common;

use common::{empty_entries, last_error, native, open, status, temp, wait_for_music, write_wav};
use ps_plugin::decode::{SR, decode};
use ps_plugin::{
    PS_ENDED, PS_ERR_ARGUMENT, PS_ERR_FAILED, PS_ERR_SOURCE, PS_ERR_UNSUPPORTED, PS_OK, PS_PLAYING,
    PS_SOURCE_SYSTEM, PS_WAITING, Session,
};

/// Renders and polls until the analyzer is done and every entry is polled; returns the entries'
/// lines in `ps entries`' format.
fn run_to_the_end(s: *mut Session) -> Vec<String> {
    let mut lines = Vec::new();
    let mut entries = empty_entries(256);
    let mut out = vec![0.0f32; 2 * 512];
    let mut dsp = 0.0;
    loop {
        let st = status(s);
        assert_eq!(st.failed, 0, "{}", last_error());
        let rendered = unsafe { ps_plugin::ps_render_music(s, out.as_mut_ptr(), 512, 2, dsp) };
        assert_eq!(rendered, PS_OK);
        dsp += 512.0 / 48_000.0;
        let n = unsafe { ps_plugin::ps_poll(s, entries.as_mut_ptr(), 256) };
        assert!(n >= 0, "{}", last_error());
        lines.extend(
            entries[..n as usize]
                .iter()
                .map(|e| serde_json::to_string(&e.line()).unwrap()),
        );
        if st.analysis_done == 1 && n == 0 {
            return lines;
        }
        std::thread::yield_now();
    }
}

#[test]
fn a_file_run_yields_the_dumps_entries_and_records_the_session() {
    // The dump's fixture: `ps entries house-128.ogg --seconds 20`.
    let fixture =
        std::fs::read_to_string(native().join("fixtures/entries/house-128-20s.jsonl")).unwrap();
    let want: Vec<&str> = fixture.lines().skip(1).collect();
    let dir = temp("dump");
    let audio = decode(&native().join("../public/reactive/house-128/house-128.ogg")).unwrap();
    let excerpt = &audio.mono[..(20.0 * SR) as usize];
    let wav = dir.join("house-128-20s.wav");
    write_wav(&wav, 1, excerpt);
    let record = dir.join("session");

    let s = open(&wav, 48_000.0, Some(&record)).unwrap();
    let got = run_to_the_end(s);
    unsafe { ps_plugin::ps_close(s) };

    assert_eq!(got.len(), want.len(), "records");
    for (i, (g, w)) in got.iter().zip(&want).enumerate() {
        assert_eq!(g, w, "record {i}");
    }
    // The record: the analyzer's input exactly, every entry, and its meta.
    let entries = std::fs::read_to_string(record.join("entries.jsonl")).unwrap();
    assert!(entries.lines().eq(want.iter().copied()));
    let wav = std::fs::read(record.join("audio.wav")).unwrap();
    let recorded: Vec<f32> = wav[44..]
        .as_chunks::<4>()
        .0
        .iter()
        .map(|b| f32::from_le_bytes(*b))
        .collect();
    assert!(recorded == excerpt, "the recorded audio differs");
    let meta: serde_json::Value =
        serde_json::from_slice(&std::fs::read(record.join("meta.json")).unwrap()).unwrap();
    assert_eq!(meta["complete"], true);
    assert_eq!(meta["records"], want.len());
    assert_eq!(meta["duration"], 20.0);
    let _ = std::fs::remove_dir_all(dir);
}

/// A stereo tone (left 220 Hz, right 330 Hz) at song time `t` of a track.
fn tone(t: f64) -> (f32, f32) {
    let w = 2.0 * std::f64::consts::PI * t;
    (
        (0.5 * (220.0 * w).sin()) as f32,
        (0.5 * (330.0 * w).sin()) as f32,
    )
}

fn tone_wav(path: &std::path::Path, seconds: f64) {
    let samples: Vec<f32> = (0..(seconds * SR) as usize)
        .flat_map(|i| {
            let (l, r) = tone(i as f64 / SR);
            [l, r]
        })
        .collect();
    write_wav(path, 2, &samples);
}

#[test]
fn a_playlist_plays_continuously_at_the_output_rate_and_the_anchor_follows_the_frames() {
    const RATE: f64 = 48_000.0;
    const FRAMES: usize = 256;
    let dir = temp("playlist");
    tone_wav(&dir.join("a.wav"), 1.0);
    tone_wav(&dir.join("b.wav"), 1.5);
    std::fs::write(dir.join("list.m3u"), "a.wav\n#PS-GAP:0.5\nb.wav\n").unwrap();
    // The playlist's song time: a for 1 s, 0.5 s of silence, then b for 1.5 s.
    let total = 3.0;
    let want = |t: f64| match t {
        t if t < 1.0 => tone(t),
        t if (1.5..total).contains(&t) => tone(t - 1.5),
        _ => (0.0, 0.0),
    };

    let s = open(&dir.join("list.m3u"), RATE, None).unwrap();
    let mut out = vec![0.0f32; 2 * FRAMES];
    let mut played: Vec<f32> = Vec::new();
    let mut frames = 0u64;
    let mut dsp = 100.0;
    while frames as f64 / RATE < total + 0.1 {
        wait_for_music(s, frames as f64 / RATE, FRAMES as f64 / RATE, total);
        let was_playing = status(s).state != PS_WAITING;
        assert_eq!(
            unsafe { ps_plugin::ps_render_music(s, out.as_mut_ptr(), FRAMES as i32, 2, dsp) },
            PS_OK
        );
        let st = status(s);
        if st.state == PS_WAITING {
            assert!(out.iter().all(|&x| x == 0.0), "silence before the music");
        } else {
            if !was_playing {
                assert_eq!(st.state, PS_PLAYING);
            }
            assert_eq!(st.anchor_dsp, dsp);
            assert_eq!(st.anchor_frames, frames as i64);
            assert_eq!(st.anchor_song, frames as f64 / RATE);
            played.extend_from_slice(&out);
            frames += FRAMES as u64;
        }
        dsp += FRAMES as f64 / RATE;
    }
    let st = status(s);
    unsafe { ps_plugin::ps_close(s) };
    assert_eq!(st.state, PS_ENDED);
    assert_eq!(st.stalls, 0);
    assert!(st.follower_gain > 0.0 && st.follower_gain <= 1.0);
    // Every frame is the source at its song time, linearly interpolated (away from the edges
    // of the silence, where it interpolates across them).
    let edges = [1.0, 1.5, total];
    for (n, frame) in played.as_chunks::<2>().0.iter().enumerate() {
        let t = n as f64 / RATE;
        if edges.iter().any(|e| (t - e).abs() < 2.0 / SR) {
            continue;
        }
        let (l, r) = want(t);
        assert!(
            (frame[0] - l).abs() < 2e-3 && (frame[1] - r).abs() < 2e-3,
            "frame {n} at {t:.5} s: {frame:?}, want {:?}",
            (l, r)
        );
    }
    let _ = std::fs::remove_dir_all(dir);
}

#[test]
fn failures_return_codes_that_ps_last_error_describes() {
    let dir = temp("errors");
    assert_eq!(
        open(&dir.join("missing.ogg"), 48_000.0, None),
        Err(PS_ERR_SOURCE)
    );
    assert!(last_error().contains("missing.ogg"), "{}", last_error());
    std::fs::write(dir.join("list.m3u"), "a.wav\nmissing.wav\n").unwrap();
    tone_wav(&dir.join("a.wav"), 0.5);
    assert_eq!(
        open(&dir.join("list.m3u"), 48_000.0, None),
        Err(PS_ERR_SOURCE)
    );
    assert!(last_error().contains("missing.wav"), "{}", last_error());
    assert_eq!(open(&dir.join("a.wav"), 0.0, None), Err(PS_ERR_ARGUMENT));
    assert_eq!(
        unsafe { ps_plugin::ps_open(std::ptr::null(), std::ptr::null_mut()) },
        PS_ERR_ARGUMENT
    );
    let path = std::ffi::CString::new(dir.join("a.wav").to_str().unwrap()).unwrap();
    let mut system = ps_plugin::Config {
        source: PS_SOURCE_SYSTEM,
        m: 3.0,
        output_rate: 48_000.0,
        path: path.as_ptr(),
        models: std::ptr::null(),
        session_dir: std::ptr::null(),
    };
    let mut s = std::ptr::null_mut();
    assert_eq!(
        unsafe { ps_plugin::ps_open(&raw const system, &raw mut s) },
        PS_ERR_UNSUPPORTED
    );
    system.source = 7;
    assert_eq!(
        unsafe { ps_plugin::ps_open(&raw const system, &raw mut s) },
        PS_ERR_ARGUMENT
    );
    assert_eq!(
        unsafe { ps_plugin::ps_list_apps(std::ptr::null_mut(), 0) },
        0
    );
    assert_eq!(status(std::ptr::null_mut()), ps_plugin::Status::default());
    unsafe { ps_plugin::ps_close(std::ptr::null_mut()) };

    // A file that isn't audio opens, then fails as it decodes: polling reports it.
    std::fs::write(dir.join("noise.ogg"), "not audio").unwrap();
    let s = open(&dir.join("noise.ogg"), 48_000.0, None).unwrap();
    let mut entries = empty_entries(16);
    let code = loop {
        let n = unsafe { ps_plugin::ps_poll(s, entries.as_mut_ptr(), 16) };
        if n != 0 {
            break n;
        }
        std::thread::yield_now();
    };
    assert_eq!(code, PS_ERR_FAILED);
    assert!(last_error().contains("afconvert"), "{}", last_error());
    assert_eq!(status(s).failed, 1);
    unsafe { ps_plugin::ps_close(s) };
    let _ = std::fs::remove_dir_all(dir);
}
