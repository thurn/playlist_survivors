/**
 * Draws evaluated frames with PixiJS. The world is rendered at native pixel
 * size into a render texture and composited to the screen in one pass.
 */
import {
  BlurFilter,
  Container,
  Filter,
  GlProgram,
  Graphics,
  Mesh,
  MeshGeometry,
  Particle,
  ParticleContainer,
  Rectangle,
  RenderTexture,
  Shader,
  Sprite,
  Texture,
  defaultFilterVert,
  type Application,
} from "pixi.js";
import type { ContentLibrary } from "../content/loader.ts";
import type { ZoneTheme } from "../content/schema.ts";
import { rand01 } from "../core/rng.ts";
import { beatSwell, clamp, frac, lerp } from "../core/shaping.ts";
import type { Frame } from "../plan/evaluate.ts";
import { hex } from "../plan/evaluate.ts";
import type { MapProp, Plan } from "../plan/types.ts";
import type { GameAssets } from "./assets.ts";
import { PixelText } from "./font.ts";
import { brightFrag, compositeFrag, compositeVert, MAX_WAVES } from "./shaders.ts";

export const VIEW_W = 960;
export const VIEW_H = 540;
const RT_W = 1400;
const RT_H = 790;
const CHUNK = 512;
const MAX_CHUNKS = 40;

interface Grade {
  mul: [number, number, number];
  lift: [number, number, number];
  contrast: number;
  saturation: number;
}

const GRADES: Record<ZoneTheme["colorGrade"], Grade> = {
  twilight: { mul: [0.86, 0.92, 1.12], lift: [0.0, 0.01, 0.04], contrast: 1.05, saturation: 0.85 },
  neutral: { mul: [1.02, 1.0, 0.97], lift: [0, 0, 0], contrast: 1.04, saturation: 1.05 },
  autumn: { mul: [1.08, 0.98, 0.86], lift: [0.02, 0.01, 0], contrast: 1.06, saturation: 1.1 },
  ember: { mul: [1.15, 0.9, 0.78], lift: [0.03, 0.0, 0.0], contrast: 1.1, saturation: 1.05 },
  infernal: { mul: [1.2, 0.84, 0.72], lift: [0.05, 0.0, 0.0], contrast: 1.16, saturation: 1.1 },
};

function rgb(c: number): [number, number, number] {
  return [((c >> 16) & 255) / 255, ((c >> 8) & 255) / 255, (c & 255) / 255];
}

/** A simple pool of display objects in one container. */
class Pool<T extends Container> {
  private items: T[] = [];
  private used = 0;
  constructor(
    readonly parent: Container,
    private make: () => T,
  ) {}
  begin() {
    this.used = 0;
  }
  next(): T {
    let it = this.items[this.used];
    if (!it) {
      it = this.make();
      this.items.push(it);
      this.parent.addChild(it);
    }
    it.visible = true;
    this.used++;
    return it;
  }
  end() {
    for (let i = this.used; i < this.items.length; i++) this.items[i].visible = false;
  }
}

export class Renderer {
  readonly worldRT: RenderTexture;
  readonly lightRT: RenderTexture;
  readonly bloomRT: RenderTexture;
  private world = new Container();
  private ground = new Container();
  private decalLayer = new Container();
  private shadowLayer = new Container();
  private sorted = new Container({ sortableChildren: true });
  private overLayer = new Container();
  private fxNormal = new Container();
  private fxAdd = new Container();
  private partNormal: ParticleContainer;
  private partAdd: ParticleContainer;
  private textLayer = new Container();
  private lights = new Container();
  private bolts = new Graphics();
  private chunks = new Map<string, { sprite: Sprite; rt: RenderTexture; used: number }>();
  private propGrid = new Map<number, MapProp[]>();
  private composite: Mesh<MeshGeometry, Shader>;
  private compositeUniforms: { uniforms: Record<string, unknown> };
  private brightSprite: Sprite;
  private worldView: Texture;
  readonly screen = new Container();
  private pools: Record<string, Pool<Sprite>>;
  private textPool: Pool<PixelText>;
  private particlePoolN: Particle[] = [];
  private particlePoolA: Particle[] = [];
  private themes: ZoneTheme[];
  private frameCounter = 0;

  constructor(
    readonly app: Application,
    readonly assets: GameAssets,
    readonly plan: Plan,
    readonly lib: ContentLibrary,
  ) {
    this.worldRT = RenderTexture.create({ width: RT_W, height: RT_H, scaleMode: "linear", antialias: false });
    this.lightRT = RenderTexture.create({ width: RT_W, height: RT_H, scaleMode: "linear" });
    this.bloomRT = RenderTexture.create({ width: RT_W / 2, height: RT_H / 2, scaleMode: "linear" });
    this.partNormal = new ParticleContainer({ dynamicProperties: { position: true, rotation: true, color: true, vertex: true, uvs: true }, texture: new Texture({ source: assets.procSource }) });
    this.partAdd = new ParticleContainer({ dynamicProperties: { position: true, rotation: true, color: true, vertex: true, uvs: true }, texture: new Texture({ source: assets.procSource }) });
    this.partAdd.blendMode = "add";
    this.fxAdd.blendMode = "add";
    this.world.addChild(this.ground, this.decalLayer, this.shadowLayer, this.sorted, this.overLayer, this.fxNormal, this.partNormal, this.bolts, this.fxAdd, this.partAdd, this.textLayer);
    this.bolts.blendMode = "add";

    const sp = (_parent: Container, blend?: "add" | "normal") => () => {
      const s = new Sprite();
      if (blend) s.blendMode = blend;
      return s;
    };
    this.pools = {
      decal: new Pool(this.decalLayer, sp(this.decalLayer)),
      shadow: new Pool(this.shadowLayer, sp(this.shadowLayer)),
      sorted: new Pool(this.sorted, sp(this.sorted)),
      over: new Pool(this.overLayer, sp(this.overLayer)),
      fxN: new Pool(this.fxNormal, sp(this.fxNormal)),
      fxA: new Pool(this.fxAdd, sp(this.fxAdd)),
      light: new Pool(this.lights, sp(this.lights, "add")),
    };
    this.textPool = new Pool(this.textLayer, () => new PixelText());

    for (const p of plan.map.props) {
      if (!p.tall && !p.light) continue;
      const k = Math.floor(p.x / 256) * 65536 + Math.floor(p.y / 256);
      let l = this.propGrid.get(k);
      if (!l) this.propGrid.set(k, (l = []));
      l.push(p);
    }
    this.themes = plan.map.zones.map((z) => lib.zones.get(z.id)!);

    // Bloom: bright pass then blur, at half resolution.
    this.worldView = new Texture({ source: this.worldRT.source, frame: new Rectangle(0, 0, VIEW_W, VIEW_H) });
    this.brightSprite = new Sprite(this.worldView);
    this.brightSprite.scale.set(0.5);
    const bright = new Filter({
      glProgram: GlProgram.from({ vertex: defaultFilterVert, fragment: brightFrag, name: "bright" }),
      resources: { brightUniforms: { uThreshold: { value: 0.72, type: "f32" } } },
    });
    this.brightSprite.filters = [bright, new BlurFilter({ strength: 6, quality: 3 })];

    const geometry = new MeshGeometry({ positions: new Float32Array([0, 0, 1, 0, 1, 1, 0, 1]), uvs: new Float32Array([0, 0, 1, 0, 1, 1, 0, 1]), indices: new Uint32Array([0, 1, 2, 0, 2, 3]) });
    const uniforms = {
      uViewSize: { value: new Float32Array([VIEW_W, VIEW_H]), type: "vec2<f32>" },
      uRTSize: { value: new Float32Array([RT_W, RT_H]), type: "vec2<f32>" },
      uOffset: { value: new Float32Array([0, 0]), type: "vec2<f32>" },
      uScale: { value: new Float32Array([2, 2]), type: "vec2<f32>" },
      uAmbient: { value: new Float32Array([1, 1, 1]), type: "vec3<f32>" },
      uLightPulse: { value: 1, type: "f32" },
      uBloomStrength: { value: 0.55, type: "f32" },
      uWaves: { value: new Float32Array(MAX_WAVES * 4), type: "vec4<f32>", size: MAX_WAVES },
      uAberration: { value: 0, type: "f32" },
      uGradeMul: { value: new Float32Array([1, 1, 1]), type: "vec3<f32>" },
      uGradeLift: { value: new Float32Array([0, 0, 0]), type: "vec3<f32>" },
      uContrast: { value: 1, type: "f32" },
      uSaturation: { value: 1, type: "f32" },
      uVignette: { value: 0.5, type: "f32" },
      uFlash: { value: new Float32Array([1, 1, 1, 0]), type: "vec4<f32>" },
      uHaze: { value: 0, type: "f32" },
      uTime: { value: 0, type: "f32" },
    };
    const shader = Shader.from({
      gl: { vertex: compositeVert, fragment: compositeFrag, name: "composite" },
      resources: { uWorld: this.worldRT.source, uLight: this.lightRT.source, uBloom: this.bloomRT.source, comp: uniforms },
    });
    this.composite = new Mesh({ geometry, shader });
    this.compositeUniforms = shader.resources.comp as { uniforms: Record<string, unknown> };
    this.screen.addChild(this.composite);
  }

  /** The letterboxed output rectangle on screen. */
  outputRect(): { x: number; y: number; w: number; h: number } {
    const W = this.app.renderer.width / this.app.renderer.resolution;
    const H = this.app.renderer.height / this.app.renderer.resolution;
    const s = Math.min(W / VIEW_W, H / VIEW_H);
    const w = VIEW_W * s;
    const h = VIEW_H * s;
    return { x: (W - w) / 2, y: (H - h) / 2, w, h };
  }

  private chunkFor(cx: number, cy: number): Sprite {
    const key = `${cx},${cy}`;
    const c = this.chunks.get(key);
    if (c) {
      c.used = this.frameCounter;
      return c.sprite;
    }
    if (this.chunks.size >= MAX_CHUNKS) {
      let oldest: string | undefined;
      let ou = Infinity;
      for (const [k, v] of this.chunks)
        if (v.used < ou) {
          ou = v.used;
          oldest = k;
        }
      const o = this.chunks.get(oldest!)!;
      o.sprite.destroy();
      o.rt.destroy(true);
      this.chunks.delete(oldest!);
    }
    const map = this.plan.map;
    const rt = RenderTexture.create({ width: CHUNK, height: CHUNK, scaleMode: "nearest" });
    const tmp = new Container();
    const x0 = cx * CHUNK;
    const y0 = cy * CHUNK;
    const t0x = Math.floor((x0 - map.originX) / 32);
    const t0y = Math.floor((y0 - map.originY) / 32);
    for (let ty = t0y - 1; ty <= t0y + CHUNK / 32; ty++) {
      for (let tx = t0x - 1; tx <= t0x + CHUNK / 32; tx++) {
        const cxl = clamp(tx, 0, map.cols - 1);
        const cyl = clamp(ty, 0, map.rows - 1);
        const k = cyl * map.cols + cxl;
        const id = map.tileIds[map.tiles[k]];
        const vars = this.assets.tiles.get(id) ?? this.assets.tiles.get("grass-summer")!;
        const s = new Sprite(vars[map.tileVariants[k] % vars.length]);
        s.x = map.originX + tx * 32 - x0;
        s.y = map.originY + ty * 32 - y0;
        const theme = this.themes[map.tileZone[k]];
        s.tint = hex(theme?.tint ?? "#ffffff");
        tmp.addChild(s);
      }
    }
    // Ground props (patches, flowers) are baked into the chunk.
    for (const p of map.props) {
      if (p.tall || p.x < x0 - 128 || p.x > x0 + CHUNK + 128 || p.y < y0 - 128 || p.y > y0 + CHUNK + 128) continue;
      const def = this.assets.props.get(p.sprite);
      if (!def || def.variants[0].frames) continue;
      const v = def.variants[p.variant % def.variants.length];
      const s = new Sprite(v.tex);
      s.x = Math.round(p.x - x0 - (p.flip ? -v.ax : v.ax));
      s.y = Math.round(p.y - y0 - v.ay);
      if (p.flip) s.scale.x = -1;
      tmp.addChild(s);
    }
    this.app.renderer.render({ container: tmp, target: rt, clear: true });
    tmp.destroy({ children: true });
    const sprite = new Sprite(rt);
    sprite.x = x0;
    sprite.y = y0;
    this.ground.addChild(sprite);
    this.chunks.set(key, { sprite, rt, used: this.frameCounter });
    return sprite;
  }

  private charTexture(sprite: string, anim: string, facing: number, frame: number, flash: boolean) {
    const a = this.assets.chars.get(sprite)?.get(anim) ?? this.assets.chars.get(sprite)?.get("walk");
    if (!a) return undefined;
    const list = (flash ? a.flash : a.frames)[facing] ?? a.frames[2];
    return { tex: list[clamp(frame, 0, list.length - 1)], a };
  }

  draw(f: Frame): void {
    this.frameCounter++;
    const r = this.app.renderer;
    const zoom = f.camera.zoom;
    const viewW = Math.min(RT_W - 2, VIEW_W / zoom);
    const viewH = Math.min(RT_H - 2, VIEW_H / zoom);
    const left = f.camera.x + f.camera.shakeX / zoom - viewW / 2;
    const top = f.camera.y + f.camera.shakeY / zoom - viewH / 2;
    const ix = Math.floor(left);
    const iy = Math.floor(top);
    this.world.position.set(-ix, -iy);
    this.lights.position.set(-ix, -iy);
    const s = f.s;
    const w = f.w;
    const sec = f.section;
    const free = sec.feel === "free";
    const beatPulse = free ? beatSwell(frac(f.sbi / 4)) : Math.exp(-6 * frac(f.sbi));
    const barPulse = free ? beatSwell(frac(f.sbi / 4)) : Math.exp(-6 * ((f.hud.barPhase * 1) % 1));

    // Ground chunks.
    for (const c of this.chunks.values()) c.sprite.visible = false;
    for (let cy = Math.floor(top / CHUNK); cy <= Math.floor((top + viewH) / CHUNK); cy++)
      for (let cx = Math.floor(left / CHUNK); cx <= Math.floor((left + viewW) / CHUNK); cx++) this.chunkFor(cx, cy).visible = true;

    for (const p of Object.values(this.pools)) p.begin();
    this.textPool.begin();

    // Decals and slam markers.
    const proc = this.assets.proc;
    for (const d of f.decals) {
      const sp = this.pools.decal.next();
      sp.texture = proc.get(d.art.replace("proc:", "")) ?? proc.get("splat")!;
      sp.anchor.set(0.5);
      sp.position.set(Math.round(d.x), Math.round(d.y));
      sp.rotation = d.rot;
      sp.scale.set(d.scaleX, d.scaleY);
      sp.alpha = d.alpha;
      sp.tint = d.tint;
    }
    for (const sl of f.slams) {
      const sp = this.pools.decal.next();
      sp.texture = proc.get("glow")!;
      sp.anchor.set(0.5);
      sp.position.set(sl.x, sl.y);
      const rr = sl.radius * (0.3 + 0.7 * sl.progress);
      sp.scale.set((rr * 2) / 32, (rr * 1.4) / 32);
      sp.tint = 0xff3020;
      sp.alpha = 0.35 + 0.4 * Math.abs(Math.sin(sl.progress * Math.PI * 4));
      const ring = this.pools.decal.next();
      ring.texture = proc.get("ring")!;
      ring.anchor.set(0.5);
      ring.position.set(sl.x, sl.y);
      ring.scale.set((sl.radius * 2) / 128, (sl.radius * 1.4) / 128);
      ring.tint = 0xff6040;
      ring.alpha = 0.8;
    }

    // Characters and tall props, depth sorted.
    const shadow = proc.get("shadow")!;
    const addShadow = (x: number, y: number, sc: number, a = 1) => {
      const sp = this.pools.shadow.next();
      sp.texture = shadow;
      sp.anchor.set(0.5);
      sp.position.set(Math.round(x), Math.round(y));
      sp.scale.set(sc);
      sp.alpha = a;
    };
    const h = f.hero;
    for (const ai of h.afterimages) {
      const t = this.charTexture("hero", ai.anim, ai.facing, ai.frame, true);
      if (!t) continue;
      const sp = this.pools.sorted.next();
      sp.texture = t.tex;
      sp.anchor.set(t.a.anchorX / t.a.frameW, t.a.anchorY / t.a.frameH);
      sp.position.set(Math.round(ai.x), Math.round(ai.y));
      sp.scale.set(1);
      sp.alpha = ai.alpha;
      sp.tint = 0x9fd8ff;
      sp.zIndex = ai.y - 0.5;
    }
    {
      const t = this.charTexture("hero", h.anim, h.facing, h.frame, false);
      if (t) {
        addShadow(h.x, h.y, 1 - h.z / 40);
        const sp = this.pools.sorted.next();
        sp.texture = t.tex;
        sp.anchor.set(t.a.anchorX / t.a.frameW, t.a.anchorY / t.a.frameH);
        sp.position.set(Math.round(h.x), Math.round(h.y - h.z));
        sp.scale.set(1);
        sp.alpha = 1;
        sp.tint = 0xffffff;
        sp.zIndex = h.y;
      }
    }
    for (const e of f.enemies) {
      const def = this.lib.enemies.get(e.type)!;
      const t = this.charTexture(def.atlas, e.anim, e.facing, e.frame, e.flash);
      if (!t) continue;
      addShadow(e.x, e.y, (def.radius / 12) * (def.hover > 0 ? 0.7 : 1), e.alpha * (e.z > 100 ? 0.4 + 0.6 * (1 - e.z / 260) : 1));
      const sp = this.pools.sorted.next();
      sp.texture = t.tex;
      sp.anchor.set(t.a.anchorX / t.a.frameW, t.a.anchorY / t.a.frameH);
      sp.position.set(Math.round(e.x), Math.round(e.y - e.z));
      sp.scale.set(e.scaleX, e.scaleY);
      sp.alpha = e.alpha;
      sp.tint = 0xffffff;
      sp.zIndex = e.y;
    }
    // Tall props, swaying with the bar.
    const sway = (free ? beatSwell(frac(f.sbi / 4)) : beatSwell(f.hud.barPhase)) - 0.5;
    for (let gy = Math.floor((top - 160) / 256); gy <= Math.floor((top + viewH + 160) / 256); gy++) {
      for (let gx = Math.floor((left - 160) / 256); gx <= Math.floor((left + viewW + 160) / 256); gx++) {
        const list = this.propGrid.get(gx * 65536 + gy);
        if (!list) continue;
        for (const p of list) {
          const def = this.assets.props.get(p.sprite);
          if (!def) continue;
          const v = def.variants[p.variant % def.variants.length];
          if (!p.tall && !v.frames) continue;
          const sp = this.pools.sorted.next();
          sp.texture = v.frames ? v.frames[Math.floor(s * def.fps + p.x) % v.frames.length] : v.tex;
          sp.anchor.set(v.ax / v.tex.width, v.ay / v.tex.height);
          sp.position.set(p.x, p.y);
          sp.scale.set(p.flip ? -1 : 1, 1);
          sp.skew.x = p.sprite.startsWith("tree") || p.sprite.startsWith("bush") || p.sprite.startsWith("mushroom") ? sway * 0.06 * (0.6 + 0.4 * rand01(p.x, p.y)) : 0;
          sp.alpha = 1;
          sp.tint = 0xffffff;
          sp.zIndex = p.y;
          if (p.light) {
            const l = this.pools.light.next();
            l.texture = proc.get("light")!;
            l.anchor.set(0.5);
            l.position.set(p.x, p.y - 20);
            const flick = 0.85 + 0.15 * Math.sin(s * 9 + p.x);
            l.scale.set(((p.light.radius * 2) / 64) * flick);
            l.tint = hex(p.light.color);
            l.alpha = 0.8;
          }
        }
      }
    }
    for (const c of this.sorted.children) if (c.visible === false) c.zIndex = -1e9;

    // Weapons and projectiles.
    for (const b of f.blades) {
      const sp = this.pools.over.next();
      sp.texture = proc.get("blade")!;
      sp.anchor.set(0.5);
      sp.position.set(Math.round(b.x), Math.round(b.y - 18));
      sp.rotation = b.rot;
      sp.scale.set(1);
      sp.alpha = 1;
      sp.tint = 0xffffff;
      const g = this.pools.fxA.next();
      g.texture = proc.get("glow")!;
      g.anchor.set(0.5);
      g.position.set(b.x, b.y - 18);
      g.scale.set(0.8);
      g.tint = 0x80c8ff;
      g.alpha = 0.6;
      g.rotation = 0;
    }
    const fire = this.assets.fx.get("fire");
    for (const fl of f.flames) {
      if (!fire) break;
      const sp = this.pools.fxA.next();
      sp.texture = fire.frames[fl.frame % fire.frames.length];
      sp.anchor.set(fire.ax / sp.texture.width, fire.ay / sp.texture.height);
      sp.position.set(Math.round(fl.x), Math.round(fl.y));
      sp.scale.set(0.8);
      sp.rotation = 0;
      sp.tint = 0xffffff;
      sp.alpha = fl.alpha;
    }
    for (const a of f.arrows) {
      const sp = this.pools.over.next();
      sp.texture = proc.get("arrow")!;
      sp.anchor.set(0.5);
      sp.position.set(Math.round(a.x), Math.round(a.y));
      sp.rotation = a.rot;
      sp.scale.set(1);
      sp.alpha = 1;
      sp.tint = 0xffffff;
    }
    for (const g of f.gems) {
      const sp = this.pools.over.next();
      sp.texture = proc.get(g.big ? "gem-big" : "gem")!;
      sp.anchor.set(0.5);
      sp.position.set(Math.round(g.x), Math.round(g.y));
      sp.rotation = 0;
      sp.scale.set(1);
      sp.alpha = 1;
      sp.tint = 0xffffff;
    }
    this.bolts.clear();
    for (const b of f.bolts) {
      for (let k = 0; k + 1 < b.points.length; k++) {
        const p0 = b.points[k];
        const p1 = b.points[k + 1];
        const seg = 6;
        this.bolts.moveTo(p0.x, p0.y);
        for (let j = 1; j <= seg; j++) {
          const u = j / seg;
          const jit = j === seg ? 0 : (rand01(k, j, Math.floor(s * 30)) - 0.5) * 12;
          this.bolts.lineTo(lerp(p0.x, p1.x, u) + jit, lerp(p0.y, p1.y, u) + jit * 0.6);
        }
      }
      this.bolts.stroke({ width: 3, color: 0x8fb8ff, alpha: 0.5 * b.alpha });
      for (let k = 0; k + 1 < b.points.length; k++) {
        this.bolts.moveTo(b.points[k].x, b.points[k].y);
        this.bolts.lineTo(b.points[k + 1].x, b.points[k + 1].y);
      }
      this.bolts.stroke({ width: 1, color: 0xffffff, alpha: b.alpha });
    }

    // Effect sprites and rings.
    for (const e of f.fx) {
      const sp = (e.additive ? this.pools.fxA : this.pools.fxN).next();
      if (e.art.startsWith("fx:")) {
        const a = this.assets.fx.get(e.art.slice(3));
        if (!a) {
          sp.visible = false;
          continue;
        }
        sp.texture = a.frames[Math.min(a.frames.length - 1, e.frame)];
        sp.anchor.set(a.ax / sp.texture.width, a.ay / sp.texture.height);
      } else {
        sp.texture = proc.get(e.art.slice(5)) ?? proc.get("glow")!;
        sp.anchor.set(0.5);
      }
      sp.position.set(Math.round(e.x), Math.round(e.y));
      sp.rotation = e.rot;
      sp.scale.set(e.scaleX, e.scaleY);
      sp.alpha = clamp(e.alpha, 0, 1);
      sp.tint = e.tint;
    }
    const ringTex = proc.get("ring")!;
    for (const rg of f.rings) {
      const sp = (rg.additive ? this.pools.fxA : this.pools.fxN).next();
      sp.texture = ringTex;
      sp.anchor.set(0.5);
      sp.position.set(rg.x, rg.y - 8);
      const sc = (rg.radius * 2) / 122;
      sp.scale.set(sc, sc * 0.75);
      sp.rotation = 0;
      sp.alpha = clamp(rg.alpha, 0, 1) * clamp(rg.width / 4, 0.4, 1);
      sp.tint = rg.tint;
    }

    // Particles.
    this.drawParticles(f);

    // Floating text (PERFECT, LEVEL UP!).
    for (const t of f.texts) {
      const pt = this.textPool.next();
      const scale = t.size >= 24 ? 2 : 1;
      pt.set(t.text, scale, t.tint, true);
      pt.position.set(Math.round(t.x - pt.textWidth / 2), Math.round(t.y - 40));
      pt.alpha = t.alpha;
    }

    // Ambient light and particles.
    const za = this.themes[f.zone.a];
    const zb = this.themes[f.zone.b];
    this.drawAmbient(f, left, top, viewW, viewH, za, zb);

    for (const [name, p] of Object.entries(this.pools)) if (name !== "light") p.end();
    this.textPool.end();

    const amb = rgb(hex(za.ambientLight)).map((v, i) => lerp(v, rgb(hex(zb.ambientLight))[i], f.zone.t)) as [number, number, number];
    for (const l of f.lights) {
      const sp = this.pools.light.next();
      sp.texture = proc.get("light")!;
      sp.anchor.set(0.5);
      sp.position.set(l.x, l.y - 12);
      sp.scale.set((l.radius * 2) / 64);
      sp.tint = l.tint;
      sp.alpha = clamp(l.strength * 0.7, 0, 1);
    }
    // The hero carries a soft light, brighter in dark zones.
    {
      const sp = this.pools.light.next();
      sp.texture = proc.get("light")!;
      sp.anchor.set(0.5);
      sp.position.set(h.x, h.y - 16);
      sp.scale.set(4.5);
      sp.tint = 0xffe8c0;
      sp.alpha = 0.35;
    }
    this.pools.light.end();

    // Render passes.
    r.render({ container: this.world, target: this.worldRT, clear: true, clearColor: [0.06, 0.07, 0.05, 1] });
    r.render({ container: this.lights, target: this.lightRT, clear: true, clearColor: [0, 0, 0, 1] });
    this.worldView.frame.width = Math.ceil(viewW) + 1;
    this.worldView.frame.height = Math.ceil(viewH) + 1;
    this.worldView.updateUvs();
    this.brightSprite.texture = this.worldView;
    r.render({ container: this.brightSprite, target: this.bloomRT, clear: true, clearColor: [0, 0, 0, 1] });

    // Composite.
    const out = this.outputRect();
    this.composite.position.set(out.x, out.y);
    this.composite.scale.set(out.w, out.h);
    const u = this.compositeUniforms.uniforms as Record<string, Float32Array | number>;
    (u.uViewSize as Float32Array).set([viewW, viewH]);
    (u.uOffset as Float32Array).set([left - ix, top - iy]);
    (u.uScale as Float32Array).set([out.w / viewW, out.h / viewH]);
    const pulseAmt = (0.05 + 0.1 * f.energy) * sec.intensity;
    u.uLightPulse = 1 + pulseAmt * beatPulse;
    (u.uAmbient as Float32Array).set(amb.map((v) => Math.min(1.2, v * 1.1 + 0.08)));
    const waves = u.uWaves as Float32Array;
    waves.fill(0);
    f.shockwaves.slice(0, MAX_WAVES).forEach((sw, i) => waves.set([sw.x - left, sw.y - top, sw.radius, sw.strength], i * 4));
    u.uAberration = f.aberration;
    const ga = GRADES[za.colorGrade];
    const gb = GRADES[zb.colorGrade];
    const t = f.zone.t;
    (u.uGradeMul as Float32Array).set(ga.mul.map((v, i) => lerp(v, gb.mul[i], t)));
    (u.uGradeLift as Float32Array).set(ga.lift.map((v, i) => lerp(v, gb.lift[i], t)));
    u.uContrast = lerp(ga.contrast, gb.contrast, t);
    u.uSaturation = lerp(ga.saturation, gb.saturation, t);
    // The vignette tightens on downbeats.
    u.uVignette = 0.45 + 0.25 * barPulse * sec.intensity;
    const fl = Math.max(f.flash.alpha, f.metronome * 0.55);
    (u.uFlash as Float32Array).set([...rgb(f.metronome > f.flash.alpha ? 0xffffff : f.flash.tint), fl]);
    u.uHaze = za.heatHaze || zb.heatHaze ? 1 : 0;
    u.uTime = s;
    void w;
  }

  private drawParticles(f: Frame): void {
    const proc = this.assets.proc;
    let nN = 0;
    let nA = 0;
    for (const p of f.particles) {
      const tex = proc.get(p.tex) ?? proc.get("spark")!;
      let part: Particle;
      if (p.additive) {
        part = this.particlePoolA[nA] ?? (this.particlePoolA[nA] = new Particle({ texture: tex }));
        nA++;
      } else {
        part = this.particlePoolN[nN] ?? (this.particlePoolN[nN] = new Particle({ texture: tex }));
        nN++;
      }
      part.texture = tex;
      part.x = Math.round(p.x);
      part.y = Math.round(p.y);
      part.anchorX = 0.5;
      part.anchorY = 0.5;
      part.rotation = p.rot;
      part.scaleX = p.scale;
      part.scaleY = p.scale;
      part.tint = p.tint;
      part.alpha = clamp(p.alpha, 0, 1);
    }
    this.syncParticles(this.partNormal, this.particlePoolN, nN);
    this.syncParticles(this.partAdd, this.particlePoolA, nA);
  }

  private syncParticles(c: ParticleContainer, pool: Particle[], n: number) {
    const list = c.particleChildren;
    if (list.length !== n || list[0] !== pool[0]) {
      list.length = 0;
      for (let i = 0; i < n; i++) list.push(pool[i]);
    }
    c.update();
  }

  /** Ambient particles: fireflies, falling leaves, rising embers (closed form in song time). */
  private drawAmbient(f: Frame, left: number, top: number, vw: number, vh: number, za: ZoneTheme, zb: ZoneTheme) {
    const kind = f.zone.t < 0.5 ? za.particles : zb.particles;
    if (kind === "none") return;
    const s = f.s;
    const n = kind === "fireflies" ? 40 : 60;
    for (let i = 0; i < n; i++) {
      const sp = (kind === "falling-leaves" ? this.pools.fxN : this.pools.fxA).next();
      sp.texture = this.assets.proc.get(kind === "falling-leaves" ? "leaf" : kind === "fireflies" ? "glow" : "ember")!;
      sp.anchor.set(0.5);
      sp.rotation = 0;
      const r1 = rand01(i, 1);
      const r2 = rand01(i, 2);
      let x: number;
      let y: number;
      let a: number;
      if (kind === "fireflies") {
        x = left + frac(r1 + Math.sin(s * 0.3 + i) * 0.03) * vw;
        y = top + frac(r2 + Math.cos(s * 0.25 + i * 1.7) * 0.03) * vh;
        a = 0.4 + 0.6 * Math.max(0, Math.sin(s * (1 + r1) + i));
        sp.scale.set(0.25);
        sp.tint = 0xd8ff80;
      } else if (kind === "falling-leaves") {
        x = left + frac(r1 + s * 0.02 + Math.sin(s + i) * 0.01) * vw;
        y = top + frac(r2 + s * (0.05 + 0.03 * r1)) * vh;
        a = 0.85;
        sp.scale.set(1);
        sp.rotation = s * (1 + r2) + i;
        sp.tint = [0xd87830, 0xc05020, 0xe0a040][i % 3];
      } else {
        x = left + frac(r1 + Math.sin(s * 0.7 + i) * 0.01) * vw;
        y = top + (1 - frac(r2 + s * (0.04 + 0.03 * r1))) * vh;
        a = 0.5 + 0.5 * Math.sin(s * 3 + i);
        sp.scale.set(1);
        sp.tint = 0xff9040;
      }
      sp.position.set(x, y);
      sp.alpha = a;
    }
  }
}
