/*
 * The Unity game's music plugin (docs/design/unity-game.md, "Music: the Rust Plugin"):
 * libps_plugin, built from native/crates/plugin. The game's C# mirrors this header; keep the
 * two in step (crates/plugin/tests/header.rs checks the layouts against the Rust side).
 *
 * One session plays one source M seconds late through Unity's output and analyses it ahead of
 * that. Threads: ps_render_music on Unity's audio thread; every other call on one other thread
 * (the main thread). Times are seconds; song time is the position in the source's music.
 */
#ifndef PS_PLUGIN_H
#define PS_PLUGIN_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Return codes. Calls that can fail return one of these (or a count, >= 0); ps_last_error
 * describes the failure. */
#define PS_OK 0
#define PS_ERR_ARGUMENT -1    /* a null pointer, an unknown source, an M or rate out of range */
#define PS_ERR_SOURCE -2      /* the file or playlist can't be read or decoded */
#define PS_ERR_MODELS -3      /* the analyzer's models can't be loaded */
#define PS_ERR_UNSUPPORTED -4 /* the source isn't available yet (System, Replay) */
#define PS_ERR_PANIC -5       /* the plugin panicked; the session should be closed */
#define PS_ERR_FAILED -6      /* ps_poll: the session's source, analysis or record failed */

/* Sources. */
#define PS_SOURCE_FILE 0   /* an audio file, or an M3U playlist with #PS-GAP lines */
#define PS_SOURCE_SYSTEM 1 /* an app's audio, tapped and muted (M2: not yet) */
#define PS_SOURCE_REPLAY 2 /* a recorded session (not yet) */

typedef struct ps_session ps_session;

typedef struct {
    int32_t source;          /* PS_SOURCE_* */
    double m;                /* the music delay M: 3.0 (1 to 10) */
    double output_rate;      /* Unity's output sample rate (Hz) */
    const char *path;        /* File: the audio file or .m3u (UTF-8) */
    const char *models;      /* the analyzer's madmom.bin; NULL: $PS_MODELS, else the checkout's */
    const char *session_dir; /* the folder to record the session in (created); NULL: none */
} ps_config;

/* An app producing audio now (System listening). */
typedef struct {
    int32_t pid;
    char name[128]; /* UTF-8, NUL-terminated */
} ps_app;

/*
 * One record of the belief stream. known_at is the plugin's stamp, position - (M - A) with
 * A = 0.048 s: position is how far into the music the analyzer had read when it emitted the
 * entry (the game adds B). Unused v slots are 0. Records arrive in the analyzer's order.
 *
 * kind  group           t                                   v
 *  1    beat            when it falls                       period, evidence
 *  2    tick            its time (every 50 ms)              evidence, energy, level (0 quiet,
 *                                                           1 medium, 2 loud), key (index in
 *                                                           analysis::structure::KEYS, -1 none),
 *                                                           kick, snare, hat, bass, brightness
 *  3    onset           when it sounds                      strength, strong (0 or 1), band (0
 *                                                           kick, 1 snare, 2 hat, 3 bass,
 *                                                           -1 none)
 *  4    hindsight       the window's end                    offline count, online count
 *  5    hindsight beat  the beat                            grid (0 offline, 1 online), the
 *                                                           window's end
 *  6    change          the boundary                        strength
 *  7    jump            when it jumps                       dB
 *  8    end             where the quiet began               (none)
 *  9    forecast        the accented beat                   cycle (beats), confidence
 * 10    timbre          the end of the span it describes    brightness, flatness, percussive,
 *                                                           density
 *
 * A hindsight entry's grids don't fit 12 slots: it is a header record (kind 4) followed by one
 * kind-5 record per beat, offline then online, each with the header's stamp. The source of
 * truth is native/crates/analysis/src/flat.rs.
 */
typedef struct {
    int32_t kind;
    double t;
    double known_at;
    double v[12];
} ps_entry;

#define PS_KIND_BEAT 1
#define PS_KIND_TICK 2
#define PS_KIND_ONSET 3
#define PS_KIND_HINDSIGHT 4
#define PS_KIND_HINDSIGHT_BEAT 5
#define PS_KIND_CHANGE 6
#define PS_KIND_JUMP 7
#define PS_KIND_END 8
#define PS_KIND_FORECAST 9
#define PS_KIND_TIMBRE 10

/* Playback states. */
#define PS_WAITING 0 /* the music hasn't started: ps_render_music writes silence */
#define PS_PLAYING 1
#define PS_ENDED 2   /* the source has been rendered to its end */

/* A struct tag, not a typedef: ps_status is also the function that fills it. */
struct ps_status {
    /* The clock anchor: the first frame of the latest buffer ps_render_music wrote while
     * playing. Song time anchor_song (= anchor_frames / output_rate) starts at DSP time
     * anchor_dsp. Valid once state is PS_PLAYING. */
    double anchor_dsp;
    double anchor_song;
    int64_t anchor_frames;
    /* The output device's latency as the OS reports it (s): device, safety offset and stream
     * latency, without Unity's DSP buffering. */
    double output_latency;
    /* How far ahead of the render position the music is buffered, and how far the analyzer
     * has read (song s). */
    double buffered;
    double analysed;
    /* Buffers that ran out of music before the source's end (capture or decode stalls). */
    int32_t stalls;
    int32_t state;         /* PS_WAITING, PS_PLAYING or PS_ENDED */
    int32_t muted;         /* System: the tapped app is still muted. File: 0 */
    int32_t analysis_done; /* the analyzer has emitted its last entry */
    int32_t failed;        /* the source, analysis or record failed: ps_poll says why */
    float follower_gain;   /* the effects' gain, following the music's loudness (0.3 to 1) */
};

/* Opens a session: validates the source, loads the models and starts decoding and analysis. */
int32_t ps_open(const ps_config *config, ps_session **out);

/* Fills out with up to max apps producing audio now; returns how many. (Empty until System
 * listening lands.) */
int32_t ps_list_apps(ps_app *out, int32_t max);

/* Copies up to max new entries into out; returns how many, or PS_ERR_FAILED once every entry
 * before a failure has been polled. Never allocates. */
int32_t ps_poll(ps_session *s, ps_entry *out, int32_t max);

/* Unity's audio thread: writes `frames` frames of `channels` interleaved channels of the
 * delayed music (left, right, then silence; mono gets their mean), resampled to the output
 * rate, and records the anchor at dsp_time. Never blocks, allocates or logs. Returns PS_OK; on
 * a failure the buffer is silent. */
int32_t ps_render_music(ps_session *s, float *out, int32_t frames, int32_t channels,
                        double dsp_time);

/* The anchor, latency, stalls, mute, gain and progress. Cannot fail. */
void ps_status(ps_session *s, struct ps_status *out);

/* Stops the session's threads, finishes its record and frees it. NULL is a no-op. */
void ps_close(ps_session *s);

/* The calling thread's last failure, until its next failing call; "" if none. */
const char *ps_last_error(void);

#ifdef __cplusplus
}
#endif

#endif
