from pathlib import Path

from demo3d_tools import perf

HEADER = ("frame,frame_song,alive,frame_ms,wait_ms,probe_ms,sim_ms,events_ms,crowd_ms,shatter_ms,effects_ms,"
          "weapons_ms,pickups_ms,structures_ms,physics_ms,hero_ms,look_ms,hud_ms,sound_ms,loop_ms,engine_ms,alloc_bytes,gc,cpu_ms")


def profile(tmp_path: Path, rows: list[tuple[float, int, float, float, float, float, int, int]],
            cpu: dict[int, float] | None = None) -> Path:
    """A profile CSV as the player writes it: (song, alive, frame_ms, wait_ms, sim_ms, crowd_ms, alloc, gc) per
    frame, the crowd and the sim the timed parts and the engine the rest; its CPU time the frame's, but where
    `cpu` gives frame index: ms."""
    lines = [HEADER]
    for i, (song, alive, frame, wait, sim, crowd, alloc, gc) in enumerate(rows):
        cols = {c: 0.0 for c in HEADER.split(",")}
        cols.update(frame=i, frame_song=song, alive=alive, frame_ms=frame, wait_ms=wait, sim_ms=sim, crowd_ms=crowd,
                    engine_ms=frame - sim - crowd, alloc_bytes=alloc, gc=gc, cpu_ms=(cpu or {}).get(i, frame))
        lines.append(",".join(f"{cols[c]:g}" for c in HEADER.split(",")))
    path = tmp_path / "profile.csv"
    path.write_text("\n".join(lines) + "\n")
    return path


def test_the_summary_covers_the_window_and_never_counts_the_analysis_wait(tmp_path: Path) -> None:
    rows = [(10.0, 100, 50.0, 0, 1, 1, 0, 0)]  # before the window
    # 100 frames in it: 8 ms of work each but the last two (40 ms), however long each waited on the analysis.
    rows += [(20 + i / 60, 290 if i % 2 else 200, 40.0 if i >= 98 else 8.0, 300.0, 0.5 if i < 98 else 2.0, 3.0,
              1024 if i == 50 else 0, 1 if i == 70 else 0) for i in range(100)]
    # The last frame's 40 ms had the main thread for only 5 ms: the OS gave the rest to others.
    s = perf.summarise(perf.read_profile(profile(tmp_path, rows, cpu={100: 5.0})), (15, 30))

    assert s["frames"] == 100
    assert s["frame_ms"] == {"p50": 8.0, "p99": 40.0, "p99.9": 40.0, "max": 40.0, "mean": 8.64}
    assert s["over_16_7ms"] == 2 and s["over_33ms"] == 2
    assert s["cpu_ms"]["max"] == 40.0 and s["cpu_ms"]["p50"] == 8.0
    assert s["slow"] == [{"song": 21.633, "frame_ms": 40.0, "cpu_ms": 40.0, "top": "engine", "alloc_bytes": 0},
                         {"song": 21.65, "frame_ms": 40.0, "cpu_ms": 5.0, "top": "engine", "alloc_bytes": 0}]
    assert s["wait_s"] == 30.0  # reported apart, in no part or frame
    assert s["parts_ms"]["crowd"]["mean"] == 3.0
    assert set(s["parts_ms"]) >= {"sim", "crowd", "engine", "loop"} and not {"frame", "wait", "probe"} & set(s["parts_ms"])
    assert s["top"][:2] == ["engine", "crowd"]  # by mean: about 4.9 ms and 3 ms
    assert s["alloc_bytes"] == {"mean": 10, "p99": 0, "max": 1024, "frames": 1}
    assert s["gc_collections"] == 1
    assert s["alive"] == {"median": 290, "max": 290}
    # The sim's step at the peak takes only the frames near the cap: the odd ones, 2 ms in frame 99.
    assert s["sim_at_peak"] == {"alive_from": perf.PEAK_ALIVE, "frames": 50, "p99_ms": 2.0}


def test_a_window_without_frames_says_so(tmp_path: Path) -> None:
    rows = [(10.0, 100, 5.0, 0, 1, 1, 0, 0)]
    assert perf.summarise(perf.read_profile(profile(tmp_path, rows)), (20, 30)) == {"frames": 0}


def test_the_probe_summary_pools_every_points_counted_frames() -> None:
    probe = {"graphicsDeviceName": "Apple M5 Max", "size": [1920, 1080], "msaa": 4, "points": [
        {"at": 40, "alive": 299, "medianMs": 6.0, "maxMs": 7.0, "frames": [[40.0, 299, 5.0], [40.02, 299, 6.0], [40.03, 299, 7.0]]},
        {"at": 50, "alive": 300, "medianMs": 9.0, "maxMs": 12.0, "frames": [[50.0, 300, 9.0], [50.02, 300, 12.0], [50.03, 300, 8.0]]},
    ]}
    s = perf.probe_summary(probe)
    # Nearest rank, rounding half to even as the player does: the median of six is the third.
    assert (s["frames"], s["p50_ms"], s["p99_ms"], s["max_ms"]) == (6, 7.0, 12.0, 12.0)
    assert s["points"][1] == {"at": 50, "alive": 300, "median_ms": 9.0, "max_ms": 12.0}
    assert s["msaa"] == 4 and s["first_max_ms"] is None


def test_an_ablation_run_keeps_only_points_its_longer_probe_leaves_room_for() -> None:
    # The break's points fall 0.35 and 0.95 s apart; an ablated point takes 4.2 s of song.
    assert perf.ablate_points([64.05, 64.4, 65]) == [64.05]
    assert perf.ablate_points([40, 50, 60]) == [40, 50, 60]
    assert perf.ablate_points([10, 12, 14, 16, 18]) == [10, 16]


def test_the_probe_summary_carries_the_cpu_share_and_the_ablations() -> None:
    probe = {"cpuP50Ms": 2.1, "cpuP99Ms": 5.0, "points": [{"at": 40, "alive": 300, "medianMs": 7.0, "maxMs": 7.0,
                                                          "frames": [[40.0, 300, 7.0, 2.1]]}],
             "ablations": [{"name": "ssao", "medianMs": 5.9, "savedMs": 1.1, "cpuSavedMs": 0.1,
                            # A burst doubled one pair; the median of the pairs' savings ignores it.
                            "pairs": [[7.0, 6.0], [7.2, 6.1], [14.0, 7.0]]}]}
    s = perf.probe_summary(probe)
    assert (s["cpu_p50_ms"], s["cpu_p99_ms"]) == (2.1, 5.0)
    assert s["ablations"] == [{"name": "ssao", "medianMs": 5.9, "savedMs": 1.1, "cpuSavedMs": 0.1, "pairedSavedMs": 1.1}]
    assert "saved without (paired) ssao 1.1 ms" in perf.report_line("peak", {"ok": True, "wall_s": 1, "probe": s})


def test_a_first_sightings_first_frame_is_kept_apart_from_the_counted_ones() -> None:
    probe = {"points": [
        {"at": 40, "alive": 299, "firstMs": 6.5, "medianMs": 6.0, "maxMs": 7.0, "frames": [[40.02, 299, 6.0], [40.03, 299, 7.0]]},
        {"at": 41.25, "label": "death bat, zone 1", "alive": 300, "firstMs": 28.0, "medianMs": 8.0, "maxMs": 8.0,
         "frames": [[41.27, 300, 8.0], [41.28, 300, 8.0]]},
        {"at": 50, "label": "at 50 s, hit magic-wand", "alive": 300, "firstMs": 9.0, "medianMs": 9.0, "maxMs": 9.0,
         "frames": [[50.02, 300, 9.0]]},
    ]}
    s = perf.probe_summary(probe)
    assert s["first_max_ms"] == 28.0 and s["max_ms"] == 9.0  # the warm-up frames stay out of the percentiles
    assert s["points"][1] == {"at": 41.25, "alive": 300, "median_ms": 8.0, "max_ms": 8.0, "first_ms": 28.0,
                              "label": "death bat, zone 1", "first": True}
    assert "first" not in s["points"][0] and s["points"][2]["first"] is False  # timed points, one with a sighting in it
