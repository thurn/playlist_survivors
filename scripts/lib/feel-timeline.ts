/**
 * A feel dossier's timeline.png (docs/design/reactive-feel.md, "The Dossier"): one chart over the
 * song with lanes for the reference, the stream, the game's events, and the honest player's and
 * mash4's tier and groove. Drawn as SVG and rendered with sharp.
 */
import sharp from "sharp";
import type { Plan } from "../../src/plan/types.ts";
import { soundRows } from "../../src/planner/checks.ts";
import { setPieceTimes } from "../../src/planner/parity.ts";
import type { FrameSeries, TrackRuns } from "./feel.ts";
import { peaks } from "./feel-metrics.ts";

const W = 2400;
const LEFT = 200;
const RIGHT = 20;

const esc = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

export async function renderTimeline(r: TrackRuns, frames: FrameSeries, identityS: number | undefined, file: string): Promise<void> {
  const stream = r.run.stream;
  const dur = stream.audio.duration;
  const x = (t: number) => (LEFT + (t / dur) * (W - LEFT - RIGHT)).toFixed(1);
  const parts: string[] = [];
  let y = 0;
  // Each lane: its title in the left column, an optional legend inside it, then its drawing.
  let shade = 0;
  const lane = (title: string, h: number, draw: (top: number, h: number) => void, legend = "") => {
    parts.push(`<rect x="0" y="${y}" width="${W}" height="${h}" fill="${shade++ % 2 ? "#15151c" : "#101016"}"/>`);
    parts.push(`<text x="8" y="${y + 18}" font-size="14" fill="#c8c8d8">${esc(title)}</text>`);
    draw(y, h);
    if (legend) {
      // (Over a backing, so it stays readable on the lane's drawing.)
      const lw = legend.length * 6.7 + 10;
      parts.push(`<rect x="${W - RIGHT - lw}" y="${y + 2}" width="${lw}" height="16" fill="#101016" opacity="0.85"/>`);
      parts.push(`<text x="${W - RIGHT - 6}" y="${y + 14}" font-size="11" fill="#9a9aaa" text-anchor="end">${esc(legend)}</text>`);
    }
    y += h;
  };
  const ticks = (ts: readonly number[], top: number, h: number, color: string, width = 1) => {
    for (const t of ts) parts.push(`<line x1="${x(t)}" x2="${x(t)}" y1="${top}" y2="${top + h}" stroke="${color}" stroke-width="${width}"/>`);
  };
  const polyline = (ts: readonly number[], vs: readonly number[], top: number, h: number, color: string, max = 1) => {
    const pts: string[] = [];
    const step = Math.max(1, Math.floor(ts.length / 3000));
    for (let i = 0; i < ts.length; i += step) pts.push(`${x(ts[i])},${(top + h - 4 - (Math.min(max, Math.max(0, vs[i])) / max) * (h - 8)).toFixed(1)}`);
    parts.push(`<polyline fill="none" stroke="${color}" stroke-width="1.5" points="${pts.join(" ")}"/>`);
  };
  lane(`${r.id} (${dur.toFixed(0)} s)`, 30, (top) => {
    for (let t = 0; t <= dur; t += 10) parts.push(`<text x="${x(t)}" y="${top + 22}" font-size="12" fill="#888">${t}s</text>`);
  });
  lane(
    "reference",
    70,
    (top, h) => {
      (r.ref.sections ?? []).forEach((s, i) => {
        parts.push(`<rect x="${x(s.s0)}" y="${top + 4}" width="${(Number(x(s.s1)) - Number(x(s.s0))).toFixed(1)}" height="${h - 8}" fill="${i % 2 ? "#2a3a5a" : "#3a2a5a"}" opacity="0.6"/>`);
        parts.push(`<text x="${Number(x(s.s0)) + 3}" y="${top + h - 8}" font-size="11" fill="#bbb">${esc(s.label)}</text>`);
      });
      ticks(r.ref.beats, top + 30, 14, "#7a7a90");
      ticks(r.ref.downbeats ?? [], top + 20, 24, "#e0e0ff", 1.5);
      ticks(
        (r.ref.accents ?? []).map((a) => a.t),
        top + 6,
        12,
        "#ffb040",
        2,
      );
    },
    `${r.ref.fit ?? r.ref.source}: sections, downbeats (tall), beats, accents (orange)`,
  );
  lane(
    "stream",
    110,
    (top, h) => {
      const ticksT = stream.ticks.map((k) => k.t);
      const levelColor: Record<string, string> = { quiet: "#1a2a3a", medium: "#2a3a2a", loud: "#4a2a2a" };
      let from = 0;
      for (let i = 1; i <= stream.ticks.length; i++) {
        const lv = stream.ticks[from].level;
        if (i < stream.ticks.length && stream.ticks[i].level === lv) continue;
        if (lv) parts.push(`<rect x="${x(ticksT[from])}" y="${top + 4}" width="${(Number(x(ticksT[i - 1])) - Number(x(ticksT[from]))).toFixed(1)}" height="${h - 8}" fill="${levelColor[lv]}"/>`);
        from = i;
      }
      const gy = top + h - 4 - stream.params.gate * (h - 8);
      parts.push(`<line x1="${LEFT}" x2="${W - RIGHT}" y1="${gy}" y2="${gy}" stroke="#4c4" stroke-dasharray="6 4"/>`);
      polyline(
        ticksT,
        stream.ticks.map((k) => k.evidence),
        top,
        h,
        "#5f5",
      );
      polyline(
        ticksT,
        stream.ticks.map((k) => k.energy),
        top,
        h,
        "#fa5",
      );
    },
    "evidence (green, the gate dashed), energy (orange), level (quiet blue, medium green, loud red)",
  );
  lane("bands", 24, () => undefined, "n/a until E1");
  const honest = r.plans["reactive-moderate"];
  const rows = soundRows(r.lib);
  const roleOf = (key: string | undefined) => (key === undefined ? "other" : (rows.get(key) ?? key));
  const roles = new Map<string, number[]>();
  for (const s of honest.events.sounds) {
    const role = roleOf(s.key);
    (roles.get(role) ?? roles.set(role, []).get(role)!).push(s.s);
  }
  const pieces = setPieceTimes(honest);
  const events: [string, number[], string][] = [
    ["strikes", honest.swings.map((w) => w.s), "#9fd8ff"],
    ...[...roles].map(([role, ts]): [string, number[], string] => [`sounds: ${role}`, ts, "#c0a0ff"]),
    ["flashes", peaks(frames.flash).map((i) => frames.s[i]), "#ffffff"],
    ["punches", peaks(frames.zoom).map((i) => frames.s[i]), "#ff8080"],
    ["zone waves", honest.open.waves.map((w) => w.s), "#80ffa0"],
    [
      "set pieces",
      Object.entries(pieces)
        .filter(([k]) => k !== "the finale")
        .flatMap(([, ts]) => ts.slice(0, 1)),
      "#ffd040",
    ],
    ["level-ups", honest.upgrades.map((u) => u.s), "#40d0ff"],
    ["identity, finale", [...(identityS !== undefined ? [identityS] : []), ...pieces["the finale"].slice(0, 1)], "#ff60ff"],
  ];
  lane(
    "game",
    18 * events.length + 26,
    (top) => {
      events.forEach(([name, ts, color], i) => {
        const ty = top + 22 + i * 18;
        parts.push(`<text x="${LEFT - 6}" y="${ty + 13}" font-size="11" fill="#aaa" text-anchor="end">${esc(name)}</text>`);
        ticks(ts, ty + 2, 14, color);
      });
    },
    "the honest run (reactive-moderate)",
  );
  const tierLane = (name: string, p: Plan) =>
    lane(
      name,
      70,
      (top, h) => {
        const t = p.tier;
        if (t) {
          for (let k = 0; k < t.s.length; k++) {
            const t1 = k + 1 < t.s.length ? t.s[k + 1] : dur;
            const tierH = (t.v[k] / 3) * (h - 8);
            parts.push(
              `<rect x="${x(t.s[k])}" y="${(top + h - 4 - tierH).toFixed(1)}" width="${Math.max(0, Number(x(t1)) - Number(x(t.s[k]))).toFixed(1)}" height="${tierH.toFixed(1)}" fill="#806020" opacity="0.7"/>`,
            );
          }
        }
        // (The groove holds each value until the next: a step line.)
        const gs = Array.from(p.groove.s);
        const gv = Array.from(p.groove.v);
        polyline(
          gs.flatMap((t, k) => (k ? [t, t] : [t])),
          gv.flatMap((v, k) => (k ? [gv[k - 1], v] : [v])),
          top,
          h,
          "#ffe080",
        );
      },
      "tier (bars), groove (line)",
    );
  tierLane("reactive-moderate", honest);
  tierLane("mash4", r.plans.mash4);
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${y}" font-family="Menlo, monospace">${parts.join("")}</svg>`;
  await sharp(Buffer.from(svg)).png().toFile(file);
}
