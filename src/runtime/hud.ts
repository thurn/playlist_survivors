/**
 * The HUD, drawn at screen resolution over the letterboxed view. Every element
 * pulses with the music in song time.
 */
import { Container, Graphics } from "pixi.js";
import type { CreditsFile } from "../assets/types.ts";
import type { ContentLibrary } from "../content/loader.ts";
import { clamp, smoothstep } from "../core/shaping.ts";
import type { Frame } from "../plan/evaluate.ts";
import type { Plan } from "../plan/types.ts";
import { PixelText, textWidth } from "./font.ts";

const COMBO_COLORS: [number, number][] = [
  [250, 0xff60ff],
  [100, 0xffd040],
  [50, 0x80e0ff],
  [0, 0xffffff],
];

export class Hud {
  readonly root = new Container();
  /** Everything except the letterbox bars; it pulses with the beat. */
  private layer = new Container();
  private bars = new Graphics();
  private g = new Graphics();
  private texts: PixelText[] = [];
  private used = 0;
  private creditLines: { text: string; tint: number; scale: number }[] = [];

  constructor(
    readonly plan: Plan,
    readonly lib: ContentLibrary,
    credits: CreditsFile,
  ) {
    this.root.addChild(this.bars, this.layer);
    this.layer.addChild(this.g);
    const r = credits.recording;
    const lines: Hud["creditLines"] = [];
    const push = (text: string, tint = 0xffffff, scale = 1) => lines.push({ text, tint, scale });
    push("CREDITS", 0xffe080, 2);
    push("");
    push("MUSIC", 0xa0d0ff);
    push(`${r.composer} — ${r.title}`);
    push(`${r.performer}`);
    push(`${r.licence}`, 0xb0b0b0);
    push("");
    push("ART AND SOUND", 0xa0d0ff);
    for (const e of credits.entries) {
      push(e.title ?? e.file, 0xffffff);
      push(`by ${e.authors.slice(0, 4).join(", ")}${e.authors.length > 4 ? " and others" : ""}`, 0xd0d0d0);
      push(`${e.licence}${e.licence.includes("SA") ? " (share-alike)" : ""}`, 0xa0a0a0);
      push("");
    }
    push("Baked art derived from CC-BY-SA sources is shared under CC-BY-SA.", 0xb0b0b0);
    push("");
    push("PLAYLIST SURVIVORS — TECH DEMO", 0xffe080);
    this.creditLines = lines;
  }

  private text(s: string, x: number, y: number, scale: number, tint = 0xffffff, align: "left" | "center" | "right" = "left", alpha = 1): PixelText {
    let t = this.texts[this.used];
    if (!t) {
      t = new PixelText();
      this.texts.push(t);
      this.layer.addChild(t);
    }
    this.used++;
    t.visible = true;
    t.set(s, Math.max(1, Math.round(scale)), tint);
    const w = textWidth(s, Math.max(1, Math.round(scale)));
    t.x = Math.round(align === "left" ? x : align === "center" ? x - w / 2 : x - w);
    t.y = Math.round(y);
    t.alpha = alpha;
    return t;
  }

  draw(f: Frame, out: { x: number; y: number; w: number; h: number }, options: { metronome: boolean }): void {
    const g = this.g;
    g.clear();
    this.bars.clear();
    this.used = 0;
    const u = out.h / 540; // HUD pixels per base unit
    const px = (v: number) => out.x + v * u;
    const py = (v: number) => out.y + v * u;
    const hud = f.hud;
    const pulse = hud.free ? 0.5 + 0.5 * Math.cos(2 * Math.PI * hud.barPhase) : Math.exp(-6 * hud.beatPhase);
    const fs = Math.max(1, Math.round(u));
    const intro = smoothstep((f.s - 0.5) / 1.5);
    this.root.alpha = 1;

    // Letterbox bars.
    const W = out.x * 2 + out.w;
    const H = out.y * 2 + out.h;
    if (out.x > 0) this.bars.rect(0, 0, out.x, H).rect(out.x + out.w, 0, out.x + 1, H).fill(0x000000);
    if (out.y > 0) this.bars.rect(0, 0, W, out.y).rect(0, out.y + out.h, W, out.y + 1).fill(0x000000);
    // The whole HUD scales up 3–6% on each beat (in song time), around the screen centre.
    const hudScale = hud.end ? 1 : 1 + (0.03 + 0.03 * hud.energy) * pulse;
    this.layer.pivot.set(out.x + out.w / 2, out.y + out.h / 2);
    this.layer.position.set(out.x + out.w / 2, out.y + out.h / 2);
    this.layer.scale.set(hudScale);

    if (!hud.end) {
      // XP bar along the top.
      const xpW = 520;
      const xpX = 480 - xpW / 2;
      g.roundRect(px(xpX - 2), py(8 - 2), (xpW + 4) * u, 10 * u, 3 * u).fill({ color: 0x000000, alpha: 0.55 * intro });
      g.roundRect(px(xpX), py(8), xpW * u * clamp(hud.xp, 0, 1), 6 * u, 2 * u).fill({ color: 0x60ffc0, alpha: intro });
      this.text(`LV ${hud.level}`, px(xpX - 8), py(7), fs, 0x60ffc0, "right", intro);

      // Health (always full) and weapons, top left.
      g.roundRect(px(12), py(24), 90 * u, 7 * u, 2 * u).fill({ color: 0x000000, alpha: 0.55 * intro });
      g.roundRect(px(13), py(25), 88 * u, 5 * u, 2 * u).fill({ color: 0xff4050, alpha: intro });
      this.text("HP", px(12), py(14), fs, 0xff8090, "left", intro);
      hud.weapons.forEach((wp, i) => {
        const wd = this.lib.weapons.get(wp.id);
        const col = wd ? parseInt(wd.color.slice(1), 16) : 0xffffff;
        const x = px(12 + i * 22);
        const y = py(36);
        const s = 18 * u * (1 + 0.12 * wp.flash);
        g.roundRect(x, y, s, s, 3 * u).fill({ color: 0x101018, alpha: 0.8 });
        g.roundRect(x, y, s, s, 3 * u).stroke({ color: col, width: Math.max(1, u), alpha: 0.6 + 0.4 * wp.flash });
        g.circle(x + s / 2, y + s / 2, (4 + 3 * wp.flash) * u).fill({ color: col, alpha: 0.5 + 0.5 * wp.flash });
      });

      // Combo and kills.
      const comboCol = COMBO_COLORS.find(([n]) => hud.combo >= n)![1];
      this.text("COMBO", px(948), py(14), fs, 0xb0b0c0, "right", intro);
      this.text(`${hud.combo}`, px(948), py(24), Math.round(2 * u), comboCol, "right", intro);
      this.text("KILLS", px(948), py(44), fs, 0xb0b0c0, "right", intro);
      this.text(`${hud.kills}`, px(948), py(54), Math.round(2 * u), 0xffffff, "right", intro);
      this.text(`PERFECT ${hud.perfects}`, px(948), py(74), fs, 0xffe060, "right", intro);

      // Song progress line with section marks.
      const pl = 20;
      const pw = 920;
      g.rect(px(pl), py(532), pw * u, 2 * u).fill({ color: 0xffffff, alpha: 0.18 });
      g.rect(px(pl), py(532), pw * u * hud.progress, 2 * u).fill({ color: 0xffe080, alpha: 0.85 });
      for (const sec of this.plan.sections) g.rect(px(pl + (pw * sec.s0) / this.plan.duration), py(529), Math.max(1, u), 6 * u).fill({ color: 0xffffff, alpha: 0.5 });
      const mm = Math.floor(hud.elapsed / 60);
      const ss = Math.floor(hud.elapsed % 60);
      this.text(`${mm}:${ss.toString().padStart(2, "0")}`, px(pl), py(520), fs, 0xc0c0c0, "left", 0.9);

      // Beat track: markers slide toward a beating heart.
      const cx = 480;
      const cy = 504;
      g.roundRect(px(cx - 200), py(cy - 9), 400 * u, 18 * u, 9 * u).fill({ color: 0x000000, alpha: 0.45 * intro });
      if (hud.free) {
        // Free sections: a flowing ribbon instead of hard markers.
        for (const side of [-1, 1]) {
          g.moveTo(px(cx + side * 16), py(cy));
          for (let k = 0; k <= 40; k++) {
            const x = cx + side * (16 + k * 4.6);
            g.lineTo(px(x), py(cy + Math.sin(k * 0.35 - f.s * 3) * 4 * (1 - k / 40)));
          }
          g.stroke({ color: 0x9fb8ff, width: 2 * u, alpha: 0.7 });
        }
      }
      for (const m of hud.markers) {
        const d = m.dt * 90;
        if (d < -6) continue;
        const a = clamp(1 - m.dt / 2.2, 0, 1) * (m.dt < 0 ? 1 + m.dt * 6 : 1);
        const sz = (2 + m.size * 1.6) * u;
        const col = m.kind === "dodge" ? 0x80f0ff : m.size >= 3 ? 0xffb040 : 0xffffff;
        for (const side of [-1, 1]) {
          g.roundRect(px(cx + side * (14 + d)) - sz / 2, py(cy) - sz * 1.6, sz, sz * 3.2, sz / 2).fill({ color: col, alpha: a * (hud.free ? 0.5 : 1) });
        }
      }
      // The count-in before step movement: the track fades in and four marks count the beats.
      if (hud.countIn) {
        const ci = hud.countIn;
        for (let k = 1; k <= 4; k++) {
          const lit = k <= ci.count;
          const now = k === ci.count;
          const r = (3 + (now ? 3 * Math.exp(-6 * hud.beatPhase) : 0)) * u;
          g.circle(px(cx - 45 + (k - 1) * 30), py(cy - 22), r).fill({ color: lit ? 0xffe080 : 0x606070, alpha: (lit ? 1 : 0.5) * (0.4 + 0.6 * ci.fade) });
        }
        g.roundRect(px(cx - 200), py(cy - 9), 400 * u, 18 * u, 9 * u).stroke({ color: 0xffe080, width: Math.max(1, u), alpha: 0.6 * ci.fade });
      }
      const heart = (10 + 4 * pulse) * u;
      this.heart(g, px(cx), py(cy), heart, options.metronome ? 0xffffff : 0xff3060);

      // Boss bar.
      if (hud.boss && hud.boss.remaining > -1) {
        const a = clamp(hud.boss.age * 2, 0, 1) * clamp(1 + hud.boss.remaining, 0, 1);
        const bw = 360;
        g.roundRect(px(480 - bw / 2), py(54), bw * u, 10 * u, 3 * u).fill({ color: 0x000000, alpha: 0.65 * a });
        const left = 1 - hud.boss.hitsTaken / Math.max(1, hud.boss.hits);
        g.roundRect(px(480 - bw / 2 + 1), py(55), (bw - 2) * u * left, 8 * u, 3 * u).fill({ color: 0xc02030, alpha: a });
        for (let k = 1; k < hud.boss.hits; k++) g.rect(px(480 - bw / 2 + (bw * k) / hud.boss.hits), py(55), Math.max(1, u), 8 * u).fill({ color: 0x000000, alpha: 0.5 * a });
        this.text(hud.boss.name, px(480), py(42), fs, 0xffc0c0, "center", a);
      }
    }

    // Title card.
    if (hud.title) {
      const a = clamp(hud.title.age / 2.5, 0, 1) * clamp((hud.title.remaining + 1.5) / 2.5, 0, 1);
      const t = this.plan.title;
      g.rect(out.x, py(196), out.w, 120 * u).fill({ color: 0x000000, alpha: 0.35 * a });
      this.text(t.piece, px(480), py(212), Math.max(1, Math.round(3 * u)), 0xf0e8ff, "center", a);
      this.text(t.composer, px(480), py(252), Math.max(1, Math.round(2 * u)), 0xc8c0ff, "center", a);
      this.text(t.performer, px(480), py(282), fs, 0xa0a0c0, "center", a);
    }

    // Section banners slide in.
    for (const b of hud.banners) {
      const inT = smoothstep(b.age / 0.35);
      const outT = smoothstep((b.age - (b.dur - 0.4)) / 0.4);
      const x = 480 + (1 - inT) * -700 + outT * 700;
      const a = 1 - outT;
      g.rect(out.x, py(150), out.w, 44 * u).fill({ color: 0x200008, alpha: 0.55 * a });
      g.rect(out.x, py(150), out.w, 2 * u).fill({ color: 0xffc040, alpha: a });
      g.rect(out.x, py(192), out.w, 2 * u).fill({ color: 0xffc040, alpha: a });
      this.text(b.text, px(x), py(158), Math.max(1, Math.round(4 * u)), 0xffe0a0, "center", a);
    }

    // End card with totals, then the credits roll.
    if (hud.end) {
      const a = clamp(hud.end.age / 1.2, 0, 1);
      g.rect(out.x, out.y, out.w, out.h).fill({ color: 0x05040a, alpha: 0.82 * a });
      const scroll = Math.max(0, hud.end.age - 5) * 26;
      const y0 = 120 - scroll;
      this.text("ENCORE!", px(480), py(y0), Math.max(1, Math.round(5 * u)), 0xffe080, "center", a);
      this.text(`KILLS ${hud.kills}`, px(480), py(y0 + 60), Math.max(1, Math.round(2 * u)), 0xffffff, "center", a);
      this.text(`COMBO ${hud.combo}`, px(480), py(y0 + 82), Math.max(1, Math.round(2 * u)), 0x80e0ff, "center", a);
      this.text(`PERFECT ${hud.perfects}`, px(480), py(y0 + 104), Math.max(1, Math.round(2 * u)), 0xffe060, "center", a);
      let y = y0 + 170;
      for (const line of this.creditLines) {
        const sc = Math.max(1, Math.round(line.scale * u));
        if (py(y) > out.y - 20 && py(y) < out.y + out.h) this.text(line.text, px(480), py(y), sc, line.tint, "center", a);
        y += 12 * line.scale + 2;
      }
    }

    for (let i = this.used; i < this.texts.length; i++) this.texts[i].visible = false;
  }

  private heart(g: Graphics, x: number, y: number, r: number, color: number) {
    g.circle(x - r * 0.45, y - r * 0.2, r * 0.55).fill(color);
    g.circle(x + r * 0.45, y - r * 0.2, r * 0.55).fill(color);
    g.poly([x - r, y, x + r, y, x, y + r * 1.05]).fill(color);
    g.rect(x - r * 0.95, y - r * 0.25, r * 1.9, r * 0.3).fill(color);
  }
}
