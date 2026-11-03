"""`champions perf`'s reading of a Metal System Trace: the player's frames told apart by their readbacks, a frame Unity
committed in two command buffers kept whole, uploads counted with their frame, frames another process shared the GPU
in counted apart; and the budgets' verdicts."""

from pathlib import Path

from champions_tools import perf

COLUMNS = ("start", "duration", "channel-name", "event-depth", "process", "cmdbuffer-id")
TAGS = ("start-time", "duration", "gpu-channel-name", "metal-nesting-level", "process", "metal-command-buffer-id")
GAME, SERVER = "Rhythm Champions (4242)", "WindowServer (402)"


def export(rows: list[tuple[int, int, str, str, str]], path: Path) -> Path:
    """An `xctrace export` of metal-gpu-intervals: rows of (start µs, duration µs, channel, process, command buffer),
    each at depth 0; a value repeated is written once with an id and referred to after, as xctrace writes it."""
    ids: dict[tuple[str, str], int] = {}
    out = ["<?xml version=\"1.0\"?>", "<trace-query-result>", "<node><schema name=\"metal-gpu-intervals\">"]
    out += [f"<col><mnemonic>{c}</mnemonic></col>" for c in COLUMNS]
    out.append("</schema>")
    for start, duration, channel, process, buffer in rows:
        cells = []
        for tag, text in zip(TAGS, (str(start * 1000), str(duration * 1000), channel, "0", process, buffer)):
            key = (tag, text)
            if key in ids:
                cells.append(f"<{tag} ref=\"{ids[key]}\"/>")
            else:
                ids[key] = len(ids) + 1
                cells.append(f"<{tag} id=\"{ids[key]}\" fmt=\"{text}\">{text}</{tag}>")
        out.append("<row>" + "".join(cells) + "</row>")
    out += ["</node>", "</trace-query-result>"]
    path.write_text("\n".join(out), encoding="utf-8")
    return path


def test_frames_end_at_their_readbacks(tmp_path: Path) -> None:
    rows = [
        # A frame (its end only starts the count): the crowd's compute, the passes, the readback.
        (0, 100, "Compute", GAME, "0xa"), (100, 300, "Vertex", GAME, "0xa"), (400, 400, "Fragment", GAME, "0xa"),
        (800, 10, "Compute", GAME, "0xa"),
        # A frame committed in two command buffers, an upload between: one frame of 0.82 ms.
        (1000, 300, "Vertex", GAME, "0xb1"), (1300, 300, "Fragment", GAME, "0xb1"), (1650, 10, "Compute", GAME, "0xu"),
        (1700, 200, "Fragment", GAME, "0xb2"), (1900, 10, "Compute", GAME, "0xb2"),
        # A frame the window server shared the GPU in: 0.51 ms.
        (2000, 200, "Vertex", GAME, "0xc"), (2300, 100, "Fragment", SERVER, "0xs"), (2200, 300, "Fragment", GAME, "0xc"),
        (2500, 10, "Compute", GAME, "0xc"),
    ]
    frames = perf.gpu_frames(perf.read_intervals(export(rows, tmp_path / "intervals.xml")))
    assert [(round(ms, 3), shared) for ms, shared in frames] == [(0.82, False), (0.51, True)]
    s = perf.gpu_summary(frames)
    assert s["frames"] == 2 and s["sharedShare"] == 0.5
    assert s["alone"] == {"frames": 1, "p50": 0.82, "p99": 0.82, "max": 0.82}
    assert s["all"]["max"] == 0.82 and s["all"]["p50"] == 0.82


def test_verdicts_hold_the_low_tier_to_the_proxy_and_every_tier_to_60_fps() -> None:
    def tier(gpu, sim: float, frame: float) -> dict:
        return {"gpuMs": gpu, "simMs": {"p99": sim}, "frameMs": {"p99": frame}}

    gpu = {"alone": {"p99": 1.6}}
    budgets = perf.verdicts({"low": tier(gpu, 0.9, 2.0), "high": tier("not traced", 3.0, 17.0)})
    assert [(name.split(" (")[0], kept) for name, _, _, kept in budgets] == [
        ("low tier GPU p99", False), ("low tier sim p99", True), ("low tier frame p99", True), ("high tier frame p99", False)]
    assert perf.verdicts({"low": tier("not traced", 0.5, 2.0)})[0][3] is None
