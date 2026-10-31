//! `include/ps_plugin.h` matches the Rust side: a small C program compiled against it prints its
//! structs' layouts and its constants, and they are the library's.

use std::fmt::Write;
use std::mem::{offset_of, size_of};
use std::path::Path;
use std::process::Command;

use analysis::flat;
use ps_plugin::{App, Config, Entry, Status};

const PROGRAM: &str = r#"
#include <stddef.h>
#include <stdio.h>
#include "ps_plugin.h"
#define FIELD(T, f) printf(#T "." #f " %zu\n", offsetof(T, f))
#define SIZE(T) printf(#T " %zu\n", sizeof(T))
#define CONST(c) printf(#c " %d\n", c)
#define STATUS(f) printf("ps_status." #f " %zu\n", offsetof(struct ps_status, f))
int main(void) {
    SIZE(ps_entry); FIELD(ps_entry, kind); FIELD(ps_entry, t); FIELD(ps_entry, known_at);
    FIELD(ps_entry, v);
    SIZE(ps_config); FIELD(ps_config, source); FIELD(ps_config, m);
    FIELD(ps_config, output_rate); FIELD(ps_config, path); FIELD(ps_config, models);
    FIELD(ps_config, session_dir);
    SIZE(ps_app); FIELD(ps_app, pid); FIELD(ps_app, name);
    printf("ps_status %zu\n", sizeof(struct ps_status)); STATUS(anchor_dsp); STATUS(anchor_song);
    STATUS(anchor_frames); STATUS(output_latency);
    STATUS(buffered); STATUS(analysed); STATUS(stalls);
    STATUS(state); STATUS(muted); STATUS(analysis_done);
    STATUS(failed); STATUS(follower_gain);
    CONST(PS_OK); CONST(PS_ERR_ARGUMENT); CONST(PS_ERR_SOURCE); CONST(PS_ERR_MODELS);
    CONST(PS_ERR_UNSUPPORTED); CONST(PS_ERR_PANIC); CONST(PS_ERR_FAILED);
    CONST(PS_SOURCE_FILE); CONST(PS_SOURCE_SYSTEM); CONST(PS_SOURCE_REPLAY);
    CONST(PS_WAITING); CONST(PS_PLAYING); CONST(PS_ENDED);
    CONST(PS_KIND_BEAT); CONST(PS_KIND_TICK); CONST(PS_KIND_ONSET); CONST(PS_KIND_HINDSIGHT);
    CONST(PS_KIND_HINDSIGHT_BEAT); CONST(PS_KIND_CHANGE); CONST(PS_KIND_JUMP);
    CONST(PS_KIND_END); CONST(PS_KIND_FORECAST); CONST(PS_KIND_TIMBRE);
    return 0;
}
"#;

#[test]
fn the_header_matches_the_library() {
    let include = Path::new(env!("CARGO_MANIFEST_DIR")).join("include");
    let dir = std::env::temp_dir().join(format!("ps-plugin-header-{}", std::process::id()));
    std::fs::create_dir_all(&dir).unwrap();
    let (source, binary) = (dir.join("layout.c"), dir.join("layout"));
    std::fs::write(&source, PROGRAM).unwrap();
    let cc = Command::new("cc")
        .args(["-std=c11", "-Wall", "-Werror", "-I"])
        .arg(&include)
        .arg(&source)
        .arg("-o")
        .arg(&binary)
        .output()
        .unwrap();
    assert!(
        cc.status.success(),
        "{}",
        String::from_utf8_lossy(&cc.stderr)
    );
    let out = Command::new(&binary).output().unwrap();
    let _ = std::fs::remove_dir_all(&dir);
    let got = String::from_utf8(out.stdout).unwrap();

    let want: Vec<(&str, i64)> = vec![
        ("ps_entry", size_of::<Entry>() as i64),
        ("ps_entry.kind", offset_of!(Entry, kind) as i64),
        ("ps_entry.t", offset_of!(Entry, t) as i64),
        ("ps_entry.known_at", offset_of!(Entry, known_at) as i64),
        ("ps_entry.v", offset_of!(Entry, v) as i64),
        ("ps_config", size_of::<Config>() as i64),
        ("ps_config.source", offset_of!(Config, source) as i64),
        ("ps_config.m", offset_of!(Config, m) as i64),
        (
            "ps_config.output_rate",
            offset_of!(Config, output_rate) as i64,
        ),
        ("ps_config.path", offset_of!(Config, path) as i64),
        ("ps_config.models", offset_of!(Config, models) as i64),
        (
            "ps_config.session_dir",
            offset_of!(Config, session_dir) as i64,
        ),
        ("ps_app", size_of::<App>() as i64),
        ("ps_app.pid", offset_of!(App, pid) as i64),
        ("ps_app.name", offset_of!(App, name) as i64),
        ("ps_status", size_of::<Status>() as i64),
        (
            "ps_status.anchor_dsp",
            offset_of!(Status, anchor_dsp) as i64,
        ),
        (
            "ps_status.anchor_song",
            offset_of!(Status, anchor_song) as i64,
        ),
        (
            "ps_status.anchor_frames",
            offset_of!(Status, anchor_frames) as i64,
        ),
        (
            "ps_status.output_latency",
            offset_of!(Status, output_latency) as i64,
        ),
        ("ps_status.buffered", offset_of!(Status, buffered) as i64),
        ("ps_status.analysed", offset_of!(Status, analysed) as i64),
        ("ps_status.stalls", offset_of!(Status, stalls) as i64),
        ("ps_status.state", offset_of!(Status, state) as i64),
        ("ps_status.muted", offset_of!(Status, muted) as i64),
        (
            "ps_status.analysis_done",
            offset_of!(Status, analysis_done) as i64,
        ),
        ("ps_status.failed", offset_of!(Status, failed) as i64),
        (
            "ps_status.follower_gain",
            offset_of!(Status, follower_gain) as i64,
        ),
        ("PS_OK", ps_plugin::PS_OK.into()),
        ("PS_ERR_ARGUMENT", ps_plugin::PS_ERR_ARGUMENT.into()),
        ("PS_ERR_SOURCE", ps_plugin::PS_ERR_SOURCE.into()),
        ("PS_ERR_MODELS", ps_plugin::PS_ERR_MODELS.into()),
        ("PS_ERR_UNSUPPORTED", ps_plugin::PS_ERR_UNSUPPORTED.into()),
        ("PS_ERR_PANIC", ps_plugin::PS_ERR_PANIC.into()),
        ("PS_ERR_FAILED", ps_plugin::PS_ERR_FAILED.into()),
        ("PS_SOURCE_FILE", ps_plugin::PS_SOURCE_FILE.into()),
        ("PS_SOURCE_SYSTEM", ps_plugin::PS_SOURCE_SYSTEM.into()),
        ("PS_SOURCE_REPLAY", ps_plugin::PS_SOURCE_REPLAY.into()),
        ("PS_WAITING", ps_plugin::PS_WAITING.into()),
        ("PS_PLAYING", ps_plugin::PS_PLAYING.into()),
        ("PS_ENDED", ps_plugin::PS_ENDED.into()),
        ("PS_KIND_BEAT", flat::BEAT.into()),
        ("PS_KIND_TICK", flat::TICK.into()),
        ("PS_KIND_ONSET", flat::ONSET.into()),
        ("PS_KIND_HINDSIGHT", flat::HINDSIGHT.into()),
        ("PS_KIND_HINDSIGHT_BEAT", flat::HINDSIGHT_BEAT.into()),
        ("PS_KIND_CHANGE", flat::CHANGE.into()),
        ("PS_KIND_JUMP", flat::JUMP.into()),
        ("PS_KIND_END", flat::END.into()),
        ("PS_KIND_FORECAST", flat::FORECAST.into()),
        ("PS_KIND_TIMBRE", flat::TIMBRE.into()),
    ];
    let want: String = want.iter().fold(String::new(), |mut s, (k, v)| {
        writeln!(s, "{k} {v}").unwrap();
        s
    });
    assert_eq!(got, want);
}
