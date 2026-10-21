"""`report`: diagnostics for the current song map.

Writes into the output directory:

- `strip_NNN.png`: spectrogram strips (8 s each by default) with the onset
  strength curve (red), onset peaks (red dots), beats (blue), downbeats (thick
  red with bar numbers), section starts (green), accents (magenta, with
  labels), anchors (orange) and each tracker's beats as ticks along the bottom.
- `ibi.png`: the gap between consecutive beats across the excerpt, plus the
  gap-to-gap change with the ±8% smoothness limit.
- `disagreements.json` / `disagreements.md`: beats where a tracker that has a
  matching beat (within a third of the local beat gap) disagrees by more than
  40 ms, and beats more than 25 ms from the nearest onset peak.
- `peaks.json`: every onset peak (time, global strength, local strength), for
  choosing anchors and accents.
- `report.json` and `summary.md`: per-section statistics and the check result.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import numpy as np

from . import audio as au
from . import check as ck
from . import songmap as sm

TRACKER_COLORS = {
    "madmom": "#1f77b4",
    "madmom-fast": "#2ca02c",
    "madmom-downbeat": "#9467bd",
    "librosa": "#ff7f0e",
}


def disagreements(
    m: sm.SongMap,
    trackers: dict[str, list[float]],
    onsets: au.Onsets,
    limit_ms: float = 40.0,
) -> list[dict[str, Any]]:
    idx = sm.index_song(m)
    beats = np.asarray(m["beats"], dtype=float)
    rows = []
    for i, t in enumerate(beats):
        gap = beats[min(i + 1, len(beats) - 1)] - beats[max(i - 1, 0)]
        gap = gap / (2 if 0 < i < len(beats) - 1 else 1)
        offs: dict[str, float | None] = {}
        flag = False
        for name, tb in trackers.items():
            if name.startswith("_") or not tb:
                continue
            arr = np.asarray(tb)
            k = int(np.argmin(np.abs(arr - t)))
            d = t - arr[k]
            if abs(d) <= gap / 3:
                offs[name] = round(d * 1000, 1)
                if abs(d) * 1000 > limit_ms:
                    flag = True
            else:
                offs[name] = None
        _, po = onsets.nearest_peak(t)
        if abs(po) * 1000 > ck.ALIGN_MS and abs(po) * 1000 <= ck.ALIGN_WINDOW_MS:
            flag = True
        if flag:
            bar, bb = sm.position_of_beat(idx, i)
            sec = sm.section_at_time(idx, t)
            rows.append(
                {
                    "beat": i,
                    "bar": bar,
                    "beatInBar": bb,
                    "time": round(float(t), 3),
                    "section": sec["label"] if sec else None,
                    "peakOffsetMs": round(po * 1000, 1),
                    "trackerOffsetsMs": offs,
                }
            )
    return rows


def _strips(
    m: sm.SongMap,
    y: np.ndarray,
    sr: int,
    onsets: au.Onsets,
    trackers: dict[str, list[float]],
    out: Path,
    seconds: float,
    per_image: int,
    t_from: float | None,
    t_to: float | None,
) -> list[str]:
    import librosa
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    hop = au.HOP
    S = librosa.power_to_db(
        librosa.feature.melspectrogram(y=y, sr=sr, hop_length=hop, n_mels=96),
        ref=np.max,
    )
    st = librosa.frames_to_time(np.arange(S.shape[1]), sr=sr, hop_length=hop)
    idx = sm.index_song(m)
    beats = m["beats"]
    downbeats = {idx.bar_start[b]: b for b in range(1, idx.bar_count + 1)}
    sec_starts = {
        idx.bar_start[s["bars"][0]]: s["label"]
        for s in m["sections"]
        if s["bars"][0] <= idx.bar_count
    }
    anchors = [a for s in m["sections"] for a in (s.get("anchors") or [])]
    duration = len(y) / sr
    t0 = t_from if t_from is not None else 0.0
    t1 = min(duration, t_to) if t_to is not None else duration
    windows = []
    t = t0
    while t < t1 - 1e-6:
        windows.append((t, min(t + seconds, t1)))
        t += seconds
    names = []
    tracker_names = [n for n in trackers if not n.startswith("_")]
    for g in range(0, len(windows), per_image):
        group = windows[g : g + per_image]
        fig, axes = plt.subplots(
            len(group), 1, figsize=(18, 3.4 * len(group)), squeeze=False
        )
        for ax, (lo, hi) in zip(axes[:, 0], group):
            msk = (st >= lo) & (st < lo + seconds)
            Sm = S[:, msk]
            if Sm.size:
                a_lo, a_hi = np.percentile(Sm, 40), np.percentile(Sm, 99.7)
                img = np.clip((Sm - a_lo) / (a_hi - a_lo + 1e-9), 0, 1)
                ax.imshow(
                    img,
                    aspect="auto",
                    origin="lower",
                    extent=[st[msk][0], st[msk][-1], 0, 96],
                    cmap="gray_r",
                )
            om = (onsets.times >= lo) & (onsets.times < lo + seconds)
            if om.any():
                env = onsets.env[om]
                scale = 70.0 / (np.percentile(onsets.env, 99.5) + 1e-9)
                ax.plot(onsets.times[om], 14 + env * scale, color="red", lw=0.7)
                pm = (onsets.peak_times >= lo) & (onsets.peak_times < lo + seconds)
                pk = np.interp(onsets.peak_times[pm], onsets.times, onsets.env)
                ax.plot(
                    onsets.peak_times[pm], 14 + pk * scale, "o", color="red", ms=2.5
                )
            for i, b in enumerate(beats):
                if not (lo <= b < lo + seconds):
                    continue
                if i in sec_starts:
                    ax.axvline(b, color="green", lw=3, alpha=0.8)
                    ax.text(
                        b,
                        88,
                        " " + sec_starts[i],
                        color="green",
                        fontsize=9,
                        weight="bold",
                    )
                if i in downbeats:
                    ax.axvline(b, color="crimson", lw=1.6, alpha=0.9)
                    ax.text(
                        b,
                        96,
                        str(downbeats[i]),
                        color="crimson",
                        fontsize=8,
                        va="bottom",
                        ha="center",
                    )
                else:
                    ax.axvline(b, color="royalblue", lw=0.9, alpha=0.8)
                ax.text(b, 1, str(i), color="royalblue", fontsize=5.5, ha="center")
            for k, name in enumerate(tracker_names):
                tb = [x for x in trackers[name] if lo <= x < lo + seconds]
                ax.vlines(
                    tb,
                    4 + 2.2 * k,
                    6 + 2.2 * k,
                    color=TRACKER_COLORS.get(name, "k"),
                    lw=2,
                )
            for a in m["accents"]:
                if lo <= a["time"] < lo + seconds:
                    ax.plot(
                        [a["time"]],
                        [80],
                        marker="v",
                        color="magenta",
                        ms=4 + 6 * a["strength"],
                    )
                    if a.get("label"):
                        ax.text(
                            a["time"],
                            74,
                            a["label"],
                            color="magenta",
                            fontsize=7,
                            rotation=90,
                            va="top",
                        )
            for a in anchors:
                if lo <= a["time"] < lo + seconds:
                    ax.plot(
                        [a["time"]],
                        [84],
                        marker="*",
                        color="darkorange",
                        ms=6 + 8 * a["strength"],
                    )
            ax.set_xlim(lo, lo + seconds)
            ax.set_ylim(0, 100)
            ax.set_yticks([])
            ticks = np.arange(np.ceil(lo * 4) / 4, lo + seconds, 0.25)
            ax.set_xticks(ticks)
            ax.set_xticklabels(
                [f"{x:.0f}" if abs(x - round(x)) < 1e-6 else "" for x in ticks],
                fontsize=8,
            )
            ax.grid(axis="x", color="green", alpha=0.15)
        handles = [
            plt.Line2D([], [], color=TRACKER_COLORS.get(n, "k"), lw=3, label=n)
            for n in tracker_names
        ]
        axes[0, 0].legend(
            handles=handles, loc="upper right", fontsize=7, ncol=len(handles)
        )
        fig.tight_layout()
        name = f"strip_{g // per_image:03d}.png"
        fig.savefig(out / name, dpi=72)
        plt.close(fig)
        names.append(name)
    return names


def _ibi_plot(m: sm.SongMap, trackers: dict[str, list[float]], out: Path) -> None:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    idx = sm.index_song(m)
    b = np.asarray(m["beats"], dtype=float)
    g = np.diff(b)
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(18, 7), sharex=True)
    for name, tb in trackers.items():
        if name.startswith("_") or len(tb) < 2:
            continue
        tb = np.asarray(tb)
        a1.plot(
            tb[1:],
            np.diff(tb) * 1000,
            ".",
            ms=2,
            color=TRACKER_COLORS.get(name, "k"),
            alpha=0.5,
            label=name,
        )
    a1.plot(b[1:], g * 1000, "-o", ms=2, lw=0.8, color="k", label="song map")
    a1.set_ylabel("beat gap (ms)")
    a1.set_ylim(0, min(2000, float(np.percentile(g * 1000, 99)) * 1.6))
    r = g[1:] / g[:-1] - 1
    a2.plot(b[2:], r * 100, "-o", ms=2, lw=0.6, color="k")
    a2.axhline(8, color="r", lw=0.6)
    a2.axhline(-8, color="r", lw=0.6)
    a2.set_ylim(-30, 30)
    a2.set_ylabel("gap change (%)")
    for ax in (a1, a2):
        for s in m["sections"]:
            if s["bars"][0] <= idx.bar_count:
                t = b[idx.bar_start[s["bars"][0]]]
                ax.axvline(t, color="green", lw=1.5)
                if ax is a1:
                    ax.text(
                        t,
                        ax.get_ylim()[1] * 0.95,
                        " " + s["label"],
                        color="green",
                        fontsize=8,
                        va="top",
                    )
        for tc in m["tempoChanges"]:
            x0, x1 = sm.time_of(idx, tc["bars"][0]), sm.time_of(idx, tc["bars"][1] + 1)
            ax.axvspan(x0, x1, color="yellow", alpha=0.3)
    a1.legend(fontsize=7, loc="lower left")
    a2.set_xlabel("time (s)")
    fig.tight_layout()
    fig.savefig(out / "ibi.png", dpi=72)
    plt.close(fig)


def summary_markdown(m: sm.SongMap, res: ck.CheckResult, n_disagree: int) -> str:
    lines = [
        f"# Song map report: {m['song']}",
        "",
        (
            f"- Audio: `{m['audio']['file']}` ({m['audio']['duration']:.3f} s, {m['audio']['sampleRate']} Hz), "
            f"cut from {m['audio']['cropStart']:.3f}–{m['audio']['cropEnd']:.3f} s of the source"
        ),
        (
            f"- Beats: {len(m['beats'])}; bars: {sm.index_song(m).bar_count}; "
            f"meter: {', '.join(f'{r["beatsPerBar"]}/4 from bar {r["fromBar"]}' for r in m['meter'])}"
        ),
        f"- Tempo changes: {', '.join(f'bars {t["bars"][0]}–{t["bars"][1]}' for t in m['tempoChanges']) or 'none'}",
        f"- Accents: {res.accents.get('count')} {res.accents.get('labels')}",
        f"- Beats flagged in the disagreement table: {n_disagree}",
        "",
        "## Sections",
        "",
        "| Section | Bars | Feel | Key | Start (s) | End (s) | Beats | BPM | Onset alignment | Max gap change | Anchors |",
        "|---|---|---|---|---|---|---|---|---|---|---|",
    ]
    for st in res.sections:
        oa = st["onsetAlignment"]
        an = st.get("anchors")
        lines.append(
            f"| {st['label']} | {st['bars'][0]}–{st['bars'][1]} | {st['feel']} | {st.get('key') or ''} | "
            f"{st['start']:.3f} | {st['end']:.3f} | {st['beatCount']} | {st.get('bpm', 0):.1f} | "
            f"{oa['fraction']:.1%} ({oa['aligned']}/{oa['counted']}, median {oa['medianAbsOffsetMs']} ms) | "
            f"{st['smoothness']['maxChange']:.1%} | "
            f"{f'{an["count"]} (max gap {an["maxGapS"]:.2f} s)' if an else ''} |"
        )
    lines += ["", "## Check", "", "```text", ck.format_text(res), "```", ""]
    return "\n".join(lines)


def write_report(
    m: sm.SongMap,
    y: np.ndarray,
    sr: int,
    onsets: au.Onsets,
    trackers: dict[str, list[float]],
    out: Path,
    seconds: float = 8.0,
    per_image: int = 1,
    t_from: float | None = None,
    t_to: float | None = None,
    images: bool = True,
) -> dict[str, Any]:
    out.mkdir(parents=True, exist_ok=True)
    duration = len(y) / sr
    res = ck.check_map(m, onsets.peak_times, duration)
    rows = disagreements(m, trackers, onsets) if not res.errors or res.sections else []
    strips: list[str] = []
    if images:
        for old in out.glob("strip_*.png"):
            old.unlink()
        strips = _strips(
            m, y, sr, onsets, trackers, out, seconds, per_image, t_from, t_to
        )
        _ibi_plot(m, trackers, out)
    (out / "disagreements.json").write_text(json.dumps(rows, indent=1))
    md = [
        "| beat | bar.beat | time | section | peak ms | "
        + " | ".join(n for n in trackers if not n.startswith("_"))
        + " |"
    ]
    md.append("|" + "---|" * (5 + len([n for n in trackers if not n.startswith("_")])))
    for r in rows:
        md.append(
            f"| {r['beat']} | {r['bar']}.{r['beatInBar']} | {r['time']:.3f} | {r['section']} | {r['peakOffsetMs']} | "
            + " | ".join(
                "—" if v is None else f"{v}" for v in r["trackerOffsetsMs"].values()
            )
            + " |"
        )
    (out / "disagreements.md").write_text("\n".join(md) + "\n")
    peaks = [
        {
            "time": round(float(t), 4),
            "strength": round(float(s), 3),
            "local": round(float(lc), 2),
        }
        for t, s, lc in zip(onsets.peak_times, onsets.peak_strengths, onsets.peak_local)
    ]
    (out / "peaks.json").write_text(json.dumps(peaks))
    report = {"check": res.to_json(), "disagreements": len(rows), "strips": strips}
    (out / "report.json").write_text(json.dumps(report, indent=1))
    (out / "summary.md").write_text(summary_markdown(m, res, len(rows)))
    return report
