/**
 * The HUD, drawn at screen resolution over the letterboxed view. Every element
 * pulses with the music in song time.
 */
import { Container, Graphics } from "pixi.js";
import type { CreditsFile } from "../assets/types.ts";
import type { ContentLibrary } from "../content/loader.ts";
import { clamp, smoothstep } from "../core/shaping.ts";
import type { Frame, KeyName } from "../plan/evaluate.ts";
import type { Plan } from "../plan/types.ts";
import { PixelText, textWidth } from "./font.ts";

/** Press ratings: gold for Perfect, white for Great, pale blue for Good, plain for unrated. */
const RATING_COLORS: Record<string, number> = { perfect: 0xffd040, great: 0xffffff, good: 0x9fd0ff, extra: 0x4a4a54 };
/** Play mode's telegraphs: the cleaves' accents and the beats to press for them. */
const TELEGRAPH = 0xffc040;
const PLAIN = 0x8890a8;
const ratingColor = (r: string | undefined) => (r ? (RATING_COLORS[r] ?? PLAIN) : PLAIN);

/** The key display: each key's place (column, row) in the cluster. */
const KEY_PLACES: Record<KeyName, [number, number, number]> = { up: [1, 0, -Math.PI / 2], left: [0, 1, Math.PI], down: [1, 1, Math.PI / 2], right: [2, 1, 0] };

/** A menu the HUD draws (the pause menu, the end card's menu). */
export interface HudMenu {
  title?: string;
  items: string[];
  selected: number;
  /** Lines under the title. */
  notes?: string[];
}

/** Play mode's end card: the grade, the best grade, and where the player took over. */
export interface EndCardInfo {
  grade: string;
  best?: string;
  newBest: boolean;
  /** Song time the player took over from the virtual player (a mid-song start). */
  startedAt?: number;
}

export interface HudOptions {
  metronome: boolean;
  menu?: HudMenu;
  /** A count-in the game is waiting through (a resume or a mid-song start): it replaces the frame's own. */
  countIn?: { count: number; of: number; fade: number; beatPhase: number };
  end?: EndCardInfo;
}

/** A menu panel's height in HUD units. */
function menuHeight(m: HudMenu): number {
  return 24 + ((m.title ? 1 : 0) + (m.notes?.length ?? 0)) * 16 + m.items.length * 22;
}

const fmtTime = (s: number) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, "0")}`;

export class Hud {
  readonly root = new Container();
  /** Everything except the letterbox bars; it pulses with the beat. */
  private layer = new Container();
  private bars = new Graphics();
  private g = new Graphics();
  private texts: PixelText[] = [];
  private used = 0;
  private creditLines: { text: string; tint: number; scale: number }[] = [];
  /** Screen rectangles of the menu's items as last drawn (for clicks). */
  menuRects: { x: number; y: number; w: number; h: number }[] = [];

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

  draw(f: Frame, out: { x: number; y: number; w: number; h: number }, options: HudOptions): void {
    const g = this.g;
    g.clear();
    this.bars.clear();
    this.used = 0;
    const u = out.h / 540; // HUD pixels per base unit
    const px = (v: number) => out.x + v * u;
    const py = (v: number) => out.y + v * u;
    const hud = options.countIn ? { ...f.hud, countIn: options.countIn, beatPhase: options.countIn.beatPhase } : f.hud;
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

      // Kills and the rated presses so far.
      this.text("KILLS", px(948), py(14), fs, 0xb0b0c0, "right", intro);
      this.text(`${hud.kills}`, px(948), py(24), Math.round(2 * u), 0xffffff, "right", intro);
      this.text(`PERFECT ${hud.ratings.perfect}`, px(948), py(44), fs, RATING_COLORS.perfect, "right", intro);
      this.text(`GREAT ${hud.ratings.great}`, px(948), py(54), fs, RATING_COLORS.great, "right", intro);
      this.text(`GOOD ${hud.ratings.good}`, px(948), py(64), fs, RATING_COLORS.good, "right", intro);

      // The key display: four arrow keys, lit while pressed, tinted by the press's rating.
      for (const k of Object.keys(KEY_PLACES) as KeyName[]) {
        const [col, row, ang] = KEY_PLACES[k];
        const key = hud.keys[k];
        const ks = 22;
        const kx = 26 + col * (ks + 3);
        const ky = 452 + row * (ks + 3);
        const col0 = ratingColor(key.rating);
        g.roundRect(px(kx), py(ky), ks * u, ks * u, 4 * u).fill({ color: key.lit > 0 ? col0 : 0x101018, alpha: (0.25 + 0.65 * key.lit) * intro });
        g.roundRect(px(kx), py(ky), ks * u, ks * u, 4 * u).stroke({ color: key.lit > 0 ? col0 : 0x606878, width: Math.max(1, u), alpha: intro });
        // The arrow.
        const cx0 = px(kx + ks / 2);
        const cy0 = py(ky + ks / 2);
        const r = 6 * u;
        const tip = { x: cx0 + Math.cos(ang) * r, y: cy0 + Math.sin(ang) * r };
        const l = { x: cx0 + Math.cos(ang + 2.4) * r, y: cy0 + Math.sin(ang + 2.4) * r };
        const rr = { x: cx0 + Math.cos(ang - 2.4) * r, y: cy0 + Math.sin(ang - 2.4) * r };
        g.poly([tip.x, tip.y, l.x, l.y, rr.x, rr.y]).fill({ color: key.lit > 0.5 ? 0x101018 : 0xd0d8e8, alpha: intro });
      }

      // Song progress line with section marks.
      const pl = 20;
      const pw = 920;
      g.rect(px(pl), py(532), pw * u, 2 * u).fill({ color: 0xffffff, alpha: 0.18 });
      g.rect(px(pl), py(532), pw * u * hud.progress, 2 * u).fill({ color: 0xffe080, alpha: 0.85 });
      for (const sec of this.plan.sections) g.rect(px(pl + (pw * sec.s0) / this.plan.duration), py(529), Math.max(1, u), 6 * u).fill({ color: 0xffffff, alpha: 0.5 });
      const mm = Math.floor(hud.elapsed / 60);
      const ss = Math.floor(hud.elapsed % 60);
      this.text(`${mm}:${ss.toString().padStart(2, "0")}`, px(pl), py(520), fs, 0xc0c0c0, "left", 0.9);

      // Beat track: the song's beats slide toward a beating heart, and presses land on it.
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
      // Ticks fade in with the count-in before step movement.
      const trackA = hud.free ? (hud.countIn?.fade ?? 0) : 1;
      for (const t of hud.ticks) {
        const d = t.dt * 90;
        if (d > 186) continue;
        const a = clamp(1 - t.dt / 2.2, 0, 1) * (t.dt < 0 ? 1 + t.dt * 8 : 1) * trackA;
        // An accent that will cleave: a large gold tick.
        const sz = (t.cleave ? 6 : t.down ? 4 : 2.5) * u;
        const color = t.cleave ? TELEGRAPH : t.down ? 0xffe8b0 : 0xffffff;
        for (const side of [-1, 1]) {
          g.roundRect(px(cx + side * (14 + d)) - sz / 2, py(cy) - sz * 1.6, sz, sz * 3.2, sz / 2).fill({ color, alpha: a });
          // "Press here": a gold ring on the beat before it.
          if (t.pressHere) g.circle(px(cx + side * (14 + d)), py(cy), 7 * u).stroke({ color: TELEGRAPH, width: Math.max(1, 1.5 * u), alpha: a });
        }
      }
      // Each press flashes on the track: early to the left, late to the right, colored by its rating.
      for (const pr of hud.presses) {
        if (!pr.steps) continue;
        const a = clamp(1 - pr.age / 0.6, 0, 1);
        const x = cx + clamp(pr.delta * 400, -70, 70);
        const r = (4 + 6 * (1 - a)) * u;
        g.circle(px(x), py(cy - 20), r).stroke({ color: ratingColor(pr.rating), width: 2 * u, alpha: a });
        g.circle(px(x), py(cy - 20), 2.5 * u).fill({ color: ratingColor(pr.rating), alpha: a });
      }
      // The groove meter beside the heart, with the damage bonus.
      {
        const gx = cx + 212;
        const gw = 70;
        g.roundRect(px(gx), py(cy - 5), gw * u, 10 * u, 3 * u).fill({ color: 0x000000, alpha: 0.55 * intro });
        g.roundRect(px(gx + 1), py(cy - 4), (gw - 2) * u * clamp(hud.groove, 0, 1), 8 * u, 3 * u).fill({ color: hud.groove > 0.85 ? 0xffd040 : 0x60c0ff, alpha: intro });
        this.text("GROOVE", px(gx), py(cy - 16), fs, 0xb0b0c0, "left", intro);
        this.text(`+${Math.round(hud.bonus * 100)}%`, px(gx + gw + 6), py(cy - 4), fs, hud.groove > 0.85 ? 0xffd040 : 0xffffff, "left", intro);
      }
      // The count-in before step movement: four marks count the last four beats.
      if (hud.countIn) {
        const ci = hud.countIn;
        const of = ci.of ?? 4;
        for (let k = 1; k <= of; k++) {
          const lit = k <= ci.count;
          const now = k === ci.count;
          const r = (3 + (now ? 3 * Math.exp(-6 * hud.beatPhase) : 0)) * u;
          g.circle(px(cx - 15 * (of - 1) + (k - 1) * 30), py(cy - 22), r).fill({ color: lit ? 0xffe080 : 0x606070, alpha: (lit ? 1 : 0.5) * (0.4 + 0.6 * ci.fade) });
        }
        g.roundRect(px(cx - 200), py(cy - 9), 400 * u, 18 * u, 9 * u).stroke({ color: 0xffe080, width: Math.max(1, u), alpha: 0.6 * ci.fade });
      }
      const heart = (10 + 4 * pulse) * u;
      // Over the beat before a cleave's accent, the heart glows gold.
      const glow = hud.heartGlow ?? 0;
      if (glow > 0) g.circle(px(cx), py(cy + 2), heart * (1.6 + 0.4 * glow)).fill({ color: TELEGRAPH, alpha: 0.35 * glow });
      this.heart(g, px(cx), py(cy), heart * (1 + 0.15 * glow), options.metronome ? 0xffffff : glow > 0.5 ? 0xffa040 : 0xff3060);

      // Boss bar.
      if (hud.boss && hud.boss.remaining > -1) {
        const a = clamp(hud.boss.age * 2, 0, 1) * clamp(1 + hud.boss.remaining, 0, 1);
        const bw = 360;
        g.roundRect(px(480 - bw / 2), py(54), bw * u, 10 * u, 3 * u).fill({ color: 0x000000, alpha: 0.65 * a });
        g.roundRect(px(480 - bw / 2 + 1), py(55), (bw - 2) * u * clamp(hud.boss.health, 0, 1), 8 * u, 3 * u).fill({ color: 0xc02030, alpha: a });
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
      const big = Math.max(1, Math.round(2 * u));
      this.text(`KILLS ${hud.kills}`, px(480), py(y0 + 60), big, 0xffffff, "center", a);
      this.text(`PERFECT ${hud.ratings.perfect}`, px(480), py(y0 + 82), big, RATING_COLORS.perfect, "center", a);
      this.text(`GREAT ${hud.ratings.great}`, px(480), py(y0 + 104), big, RATING_COLORS.great, "center", a);
      this.text(`GOOD ${hud.ratings.good}`, px(480), py(y0 + 126), big, RATING_COLORS.good, "center", a);
      this.text(`AVERAGE GROOVE +${Math.round(hud.end.averageGroove * this.lib.hero.groove.maxBonus * 100)}%`, px(480), py(y0 + 148), big, 0x80e0ff, "center", a);
      let y = y0 + 200;
      // Play mode: the grade, the best grade and where the player took over.
      const end = options.end;
      if (end) {
        this.text(`GRADE ${end.grade}`, px(480), py(y0 + 176), Math.max(1, Math.round(4 * u)), 0xffd040, "center", a);
        const notes = [end.newBest ? "NEW BEST!" : end.best ? `BEST ${end.best}` : "", end.startedAt ? `STARTED AT ${fmtTime(end.startedAt)}` : ""].filter((x) => x);
        notes.forEach((n, i) => this.text(n, px(480), py(y0 + 214 + i * 14), fs, 0xc0c0d8, "center", a));
        y += 50 + notes.length * 14;
      }
      // (Above the end card's menu, when there is one.)
      const bottom = options.menu ? 540 - menuHeight(options.menu) - 34 : 540;
      for (const line of this.creditLines) {
        const sc = Math.max(1, Math.round(line.scale * u));
        if (py(y) > out.y - 20 && y < bottom) this.text(line.text, px(480), py(y), sc, line.tint, "center", a);
        y += 12 * line.scale + 2;
      }
    }

    // A menu, over everything (the pause menu; the end card's menu at the bottom).
    this.menuRects = [];
    if (options.menu) this.drawMenu(options.menu, out, !!hud.end);

    for (let i = this.used; i < this.texts.length; i++) this.texts[i].visible = false;
  }

  private drawMenu(m: HudMenu, out: { x: number; y: number; w: number; h: number }, atBottom: boolean): void {
    const g = this.g;
    const u = out.h / 540;
    const px = (v: number) => out.x + v * u;
    const py = (v: number) => out.y + v * u;
    const h = menuHeight(m);
    const top = atBottom ? 540 - h - 26 : 270 - h / 2;
    if (!atBottom) g.rect(out.x, out.y, out.w, out.h).fill({ color: 0x05040a, alpha: 0.55 });
    g.roundRect(px(330), py(top), 300 * u, h * u, 6 * u).fill({ color: 0x0c0a16, alpha: 0.9 });
    g.roundRect(px(330), py(top), 300 * u, h * u, 6 * u).stroke({ color: 0xffe080, width: Math.max(1, u), alpha: 0.5 });
    let y = top + 12;
    const fs = Math.max(1, Math.round(u));
    if (m.title) {
      this.text(m.title, px(480), py(y), Math.max(1, Math.round(2 * u)), 0xffe080, "center");
      y += 20;
    }
    for (const n of m.notes ?? []) {
      this.text(n, px(480), py(y), fs, 0xa0a0c0, "center");
      y += 14;
    }
    m.items.forEach((item, i) => {
      const sel = i === m.selected;
      const iy = y + i * 22;
      if (sel) g.roundRect(px(350), py(iy - 3), 260 * u, 18 * u, 4 * u).fill({ color: 0xffe080, alpha: 0.18 });
      this.text(`${sel ? "> " : ""}${item}${sel ? " <" : ""}`, px(480), py(iy), Math.max(1, Math.round(2 * u)), sel ? 0xffffff : 0xb0b0c8, "center");
      this.menuRects.push({ x: px(350), y: py(iy - 3), w: 260 * u, h: 18 * u });
    });
  }

  private heart(g: Graphics, x: number, y: number, r: number, color: number) {
    g.circle(x - r * 0.45, y - r * 0.2, r * 0.55).fill(color);
    g.circle(x + r * 0.45, y - r * 0.2, r * 0.55).fill(color);
    g.poly([x - r, y, x + r, y, x, y + r * 1.05]).fill(color);
    g.rect(x - r * 0.95, y - r * 0.25, r * 1.9, r * 0.3).fill(color);
  }
}
