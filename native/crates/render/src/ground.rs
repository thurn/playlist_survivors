//! The painted ground (`--art heroeditor`, docs/archive/design/heroeditor-native.md): a full-screen pass
//! under the world's sprites that paints grass, moss, clearings, worn paths, tufts, flowers and
//! pebbles from noise in world space, so it scrolls with the world and holds still as the camera
//! moves and zooms. Nothing is baked: every pixel is the same function of its world position.
#![allow(clippy::default_trait_access)]

/// A zone's ground colours, sRGB: deep and light grass, moss, the clearings' earth, the paths, a
/// tuft's base and tip, and the pebbles.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Palette {
    pub grass: [f32; 3],
    pub lush: [f32; 3],
    pub moss: [f32; 3],
    pub earth: [f32; 3],
    pub path: [f32; 3],
    pub tuft: [f32; 3],
    pub tip: [f32; 3],
    pub pebble: [f32; 3],
}

/// The ground in view: the world point at the screen's centre, logical pixels per world pixel,
/// the run's seed, the zone's palette, and the next zone's, inside the transform wave's ring
/// (its centre and radius, world px).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Ground {
    pub center: [f32; 2],
    pub scale: f32,
    pub seed: u32,
    pub palette: Palette,
    pub wave: Option<(Palette, [f32; 3])>,
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Uniform {
    center: [f32; 2],
    size: [f32; 2],
    scale: f32,
    seed: u32,
    wave_r: f32,
    _pad: f32,
    wave: [f32; 2],
    _pad2: [f32; 2],
    a: [[f32; 4]; 8],
    b: [[f32; 4]; 8],
}

fn colors(p: &Palette) -> [[f32; 4]; 8] {
    [
        p.grass, p.lush, p.moss, p.earth, p.path, p.tuft, p.tip, p.pebble,
    ]
    .map(|c| [c[0], c[1], c[2], 1.0])
}

const SHADER: &str = r"
struct G {
    center: vec2<f32>, size: vec2<f32>,
    scale: f32, seed: u32, wave_r: f32, pad: f32,
    wave: vec2<f32>, pad2: vec2<f32>,
    a: array<vec4<f32>, 8>,
    b: array<vec4<f32>, 8>,
};
@group(0) @binding(0) var<uniform> g: G;
@group(0) @binding(1) var grain: texture_2d<f32>;
@group(0) @binding(2) var wrap: sampler;

struct Full { @builtin(position) pos: vec4<f32> };
@vertex
fn full_vs(@builtin(vertex_index) v: u32) -> Full {
    let uv = vec2<f32>(f32((v << 1u) & 2u), f32(v & 2u));
    var o: Full;
    o.pos = vec4<f32>(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, 0.0, 1.0);
    return o;
}

fn decode(c: vec3<f32>) -> vec3<f32> {
    let lo = c / 12.92;
    let hi = pow((max(c, vec3<f32>(0.0)) + 0.055) / 1.055, vec3<f32>(2.4));
    return select(hi, lo, c <= vec3<f32>(0.04045));
}

fn hash(c: vec2<i32>, s: u32) -> u32 {
    var h = (bitcast<u32>(c.x) * 0x8da6b343u) ^ (bitcast<u32>(c.y) * 0xd8163841u) ^ (s * 0xcb1ab31fu);
    h ^= h >> 15u; h *= 0x2c1b3c6du;
    h ^= h >> 12u; h *= 0x297a2d39u;
    h ^= h >> 15u;
    return h;
}
fn rnd(c: vec2<i32>, s: u32) -> f32 {
    return f32(hash(c, s) & 0xffffffu) / 16777216.0;
}
// Value noise from the hashes, exact: for the paths, whose edges come from its slope.
fn exact(p: vec2<f32>, s: u32) -> f32 {
    let i = floor(p);
    let f = p - i;
    let u = f * f * (3.0 - 2.0 * f);
    let c = vec2<i32>(i);
    let x0 = mix(rnd(c, s), rnd(c + vec2<i32>(1, 0), s), u.x);
    let x1 = mix(rnd(c + vec2<i32>(0, 1), s), rnd(c + vec2<i32>(1, 1), s), u.x);
    return mix(x0, x1, u.y);
}
// The same from the grain texture, one filtered read (smoothed by bending the read between
// texel centres), offset by the seed: for everything soft.
fn noise(p: vec2<f32>, s: u32) -> f32 {
    let h = s * 0x9e3779b9u;
    let q = p + vec2<f32>(f32(h >> 24u), f32((h >> 16u) & 255u));
    let i = floor(q);
    let f = q - i;
    let u = f * f * (3.0 - 2.0 * f);
    return textureSampleLevel(grain, wrap, (i + u + 0.5) / 256.0, 0.0).r;
}
// `n` octaves, each turned and offset so no grid shows, 0 to 1 (`exact`'s if `sharp`).
fn fbm(p: vec2<f32>, s: u32, n: u32, sharp: bool) -> f32 {
    var q = p;
    var sum = 0.0;
    var amp = 0.5;
    for (var k = 0u; k < n; k++) {
        if sharp {
            sum += amp * exact(q, s + k * 101u);
        } else {
            sum += amp * noise(q, s + k * 101u);
        }
        q = mat2x2<f32>(1.6, 1.2, -1.2, 1.6) * q + vec2<f32>(5.3, 1.7);
        amp *= 0.5;
    }
    return sum / (1.0 - amp);
}

fn pal(k: i32, t: f32) -> vec3<f32> {
    return mix(g.a[k].rgb, g.b[k].rgb, t);
}

// The worn paths: bands about 30 world px wide where a warped noise field crosses one half (its
// distance from the crossing by the field's screen-space slope), 0 to 1 across, ragged at the edge.
fn path_at(w: vec2<f32>, px: f32, s: u32) -> f32 {
    let q = w / 1500.0 + vec2<f32>(fbm(w / 650.0, s + 40u, 2u, true), fbm(w / 650.0 + 9.1, s + 50u, 2u, true)) * 0.8;
    let n = exact(q, s + 60u) - 0.5;
    let slope = max(length(vec2<f32>(dpdx(n), dpdy(n))), 1e-6);
    let d = abs(n) / slope * px + 6.0 * (fbm(w / 24.0, s + 70u, 2u, false) - 0.5);
    return 1.0 - smoothstep(12.0, 17.0, d);
}
// The clearings: broad patches of earth, ragged at the rim.
fn clearing_at(w: vec2<f32>, s: u32) -> f32 {
    let c = fbm(w / 950.0 + 41.0, s + 20u, 3u, false) + 0.1 * (fbm(w / 55.0, s + 30u, 2u, false) - 0.5);
    return smoothstep(0.68, 0.75, c);
}

// The distance from p to the segment a to b, and how far along it p lies (0 to 1).
fn segment(p: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> vec2<f32> {
    let ab = b - a;
    let t = clamp(dot(p - a, ab) / dot(ab, ab), 0.0, 1.0);
    return vec2<f32>(length(p - a - ab * t), t);
}

@fragment
fn ground_fs(i: Full) -> @location(0) vec4<f32> {
    let px = 1.0 / g.scale;
    let w = g.center + (i.pos.xy - g.size * 0.5) * px;
    let s = g.seed;
    // The next zone inside the wave's ring, blended over a soft edge.
    var t = 0.0;
    if g.wave_r > 0.0 {
        t = 1.0 - smoothstep(g.wave_r - 40.0, g.wave_r + 10.0, length(w - g.wave));
    }
    // Grass in broad soft tones, brushed in long strokes.
    var col = mix(pal(0, t), pal(1, t), smoothstep(0.28, 0.78, fbm(w / 560.0, s, 3u, false)));
    let stroke = noise(vec2<f32>(w.x / 120.0 + w.y / 300.0, w.y / 26.0), s + 7u);
    col *= 0.93 + 0.13 * stroke;
    col = mix(col, pal(1, t) * 1.08, 0.18 * smoothstep(0.6, 0.9, fbm(w / 140.0, s + 3u, 2u, false)));
    // Brush dabs: short, wide marks of lighter and darker grass, a little ragged.
    let dab = vec2<f32>(w.x / 17.0 + 0.6 * noise(w / 45.0, s + 8u), w.y / 6.5);
    let lightd = smoothstep(0.66, 0.76, noise(dab, s + 9u));
    let darkd = smoothstep(0.68, 0.78, noise(dab + vec2<f32>(31.7, 12.3), s + 10u));
    col = mix(col, pal(1, t) * 1.1, 0.22 * lightd);
    col = mix(col, pal(0, t) * 0.82, 0.2 * darkd);
    // Moss, then the clearings' earth, then the paths over all.
    col = mix(col, pal(2, t), 0.85 * smoothstep(0.6, 0.7, fbm(w / 230.0 + 17.3, s + 1u, 3u, false)));
    let clear = clearing_at(w, s);
    col = mix(col, pal(3, t), clear);
    let path = path_at(w, px, s);
    let rim = 1.0 - smoothstep(0.0, 0.5, abs(path - 0.35));
    col = mix(col * (1.0 - 0.12 * rim), pal(4, t) * (0.94 + 0.12 * stroke), path);
    // A tuft, a flower cluster or a pebble in some cells of a 72 px grid, kept off the paths.
    let cell = floor(w / 72.0);
    let cc = vec2<i32>(cell);
    let base = cell * 72.0 + 16.0 + vec2<f32>(rnd(cc, s + 12u), rnd(cc, s + 13u)) * 40.0;
    let k = rnd(cc, s + 11u);
    let d = w - base;
    let aa = 1.2 * px;
    let before = col;
    if k < 0.2 {
        // Three to five tapered blades fanning up from the base.
        let n = 3 + i32(rnd(cc, s + 14u) * 2.99);
        let h = 10.0 + 8.0 * rnd(cc, s + 15u);
        var cover = 0.0;
        var along = 0.0;
        for (var b = 0; b < n; b++) {
            let f = (f32(b) + 0.5) / f32(n) - 0.5;
            let lean = f * 1.3 + 0.25 * (rnd(cc + vec2<i32>(b, 0), s + 16u) - 0.5);
            let tip = vec2<f32>(lean * h, -h * (1.0 - 0.35 * abs(f)));
            let sd = segment(d, vec2<f32>(f * 5.0, 0.0), tip);
            let c = 1.0 - smoothstep(-aa, aa, sd.x - 1.6 * (1.0 - sd.y) - 0.3);
            if c > cover { cover = c; along = sd.y; }
        }
        col = mix(col, mix(pal(5, t), pal(6, t), along), cover);
    } else if k < 0.235 {
        // A cluster of three small flowers.
        for (var b = 0; b < 3; b++) {
            let o = (vec2<f32>(rnd(cc + vec2<i32>(b, 1), s + 17u), rnd(cc + vec2<i32>(b, 2), s + 17u)) - 0.5) * 14.0;
            let c = 1.0 - smoothstep(-aa, aa, length(d - o) - 2.2);
            col = mix(col, pal(6, t) * 1.25, c);
        }
    } else if k < 0.27 {
        // A pebble: its soft shadow, its body and a light on its upper left.
        let r = vec2<f32>(5.0 + 4.0 * rnd(cc, s + 18u), 3.5 + 2.5 * rnd(cc, s + 19u));
        let sh = length((d - vec2<f32>(1.5, 2.0)) / (r * 1.15));
        col *= 1.0 - 0.35 * (1.0 - smoothstep(0.7, 1.0, sh));
        let e = length(d / r);
        let body = 1.0 - smoothstep(1.0 - aa / r.y, 1.0 + aa / r.y, e);
        let lit = 1.0 + 0.3 * (1.0 - smoothstep(0.0, 0.7, length(d / r + vec2<f32>(0.35, 0.45))));
        col = mix(col, pal(7, t) * lit, body);
    }
    // (Nothing grows on the paths, and less in the clearings.)
    col = mix(col, before, max(smoothstep(0.3, 0.8, path), 0.6 * clear));
    return vec4<f32>(decode(clamp(col, vec3<f32>(0.0), vec3<f32>(1.0))), 1.0);
}
";

/// The grain: 256 x 256 random values (the same every run), which the soft noise reads.
fn grain(device: &wgpu::Device, queue: &wgpu::Queue) -> wgpu::TextureView {
    let mut x = 0x2545_f491_u32;
    let values: Vec<u8> = (0..256 * 256)
        .map(|_| {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            (x >> 24) as u8
        })
        .collect();
    let size = wgpu::Extent3d {
        width: 256,
        height: 256,
        depth_or_array_layers: 1,
    };
    let texture = device.create_texture(&wgpu::TextureDescriptor {
        label: Some("grain"),
        size,
        mip_level_count: 1,
        sample_count: 1,
        dimension: wgpu::TextureDimension::D2,
        format: wgpu::TextureFormat::R8Unorm,
        usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
        view_formats: &[],
    });
    queue.write_texture(
        texture.as_image_copy(),
        &values,
        wgpu::TexelCopyBufferLayout {
            offset: 0,
            bytes_per_row: Some(256),
            rows_per_image: Some(256),
        },
        size,
    );
    texture.create_view(&Default::default())
}

/// The ground's pipeline, its uniform and its grain.
pub struct GroundPass {
    pipeline: wgpu::RenderPipeline,
    uniform: wgpu::Buffer,
    bind: wgpu::BindGroup,
}

impl GroundPass {
    /// Builds the pass for `format` targets.
    pub fn new(
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        format: wgpu::TextureFormat,
    ) -> GroundPass {
        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("ground"),
            source: wgpu::ShaderSource::Wgsl(SHADER.into()),
        });
        let layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("ground"),
            entries: &[
                wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Buffer {
                        ty: wgpu::BufferBindingType::Uniform,
                        has_dynamic_offset: false,
                        min_binding_size: None,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 1,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: true },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 2,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
                    count: None,
                },
            ],
        });
        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: None,
            bind_group_layouts: &[Some(&layout)],
            immediate_size: 0,
        });
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("ground"),
            layout: Some(&pipeline_layout),
            vertex: wgpu::VertexState {
                module: &module,
                entry_point: Some("full_vs"),
                compilation_options: Default::default(),
                buffers: &[],
            },
            fragment: Some(wgpu::FragmentState {
                module: &module,
                entry_point: Some("ground_fs"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState {
                    format,
                    blend: None,
                    write_mask: wgpu::ColorWrites::ALL,
                })],
            }),
            primitive: Default::default(),
            depth_stencil: None,
            multisample: wgpu::MultisampleState::default(),
            multiview_mask: None,
            cache: None,
        });
        let uniform = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("ground"),
            size: size_of::<Uniform>() as u64,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let grain = grain(device, queue);
        let wrap = device.create_sampler(&wgpu::SamplerDescriptor {
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            address_mode_u: wgpu::AddressMode::Repeat,
            address_mode_v: wgpu::AddressMode::Repeat,
            ..Default::default()
        });
        let bind = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: None,
            layout: &layout,
            entries: &[
                wgpu::BindGroupEntry {
                    binding: 0,
                    resource: uniform.as_entire_binding(),
                },
                wgpu::BindGroupEntry {
                    binding: 1,
                    resource: wgpu::BindingResource::TextureView(&grain),
                },
                wgpu::BindGroupEntry {
                    binding: 2,
                    resource: wgpu::BindingResource::Sampler(&wrap),
                },
            ],
        });
        GroundPass {
            pipeline,
            uniform,
            bind,
        }
    }

    /// Paints `ground` over all of `target` (`physical` pixels, `dpi` of them per logical one).
    pub fn draw(
        &self,
        queue: &wgpu::Queue,
        encoder: &mut wgpu::CommandEncoder,
        target: &wgpu::TextureView,
        physical: (u32, u32),
        dpi: f32,
        ground: &Ground,
    ) {
        let (b, wave) = ground.wave.unwrap_or((ground.palette, [0.0; 3]));
        let u = Uniform {
            center: ground.center,
            size: [physical.0 as f32, physical.1 as f32],
            scale: ground.scale * dpi,
            seed: ground.seed,
            wave_r: wave[2],
            _pad: 0.0,
            wave: [wave[0], wave[1]],
            _pad2: [0.0; 2],
            a: colors(&ground.palette),
            b: colors(&b),
        };
        queue.write_buffer(&self.uniform, 0, bytemuck::bytes_of(&u));
        let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
            label: Some("ground"),
            color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                view: target,
                depth_slice: None,
                resolve_target: None,
                ops: wgpu::Operations {
                    load: wgpu::LoadOp::Clear(wgpu::Color::BLACK),
                    store: wgpu::StoreOp::Store,
                },
            })],
            depth_stencil_attachment: None,
            timestamp_writes: None,
            occlusion_query_set: None,
            multiview_mask: None,
        });
        pass.set_pipeline(&self.pipeline);
        pass.set_bind_group(0, &self.bind, &[]);
        pass.draw(0..3, 0..1);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::capture::Offscreen;
    use crate::post::{Frame, Grade};

    fn palette(c: [f32; 3]) -> Palette {
        Palette {
            grass: c,
            lush: c,
            moss: c,
            earth: c,
            path: c,
            tuft: c,
            tip: c,
            pebble: c,
        }
    }

    /// The ground at `center` over 128 x 96 pixels, as its graded frame's bytes.
    fn paint(out: &mut Offscreen, center: [f32; 2], wave: Option<(Palette, [f32; 3])>) -> Vec<u8> {
        let woods = Palette {
            grass: [0.24, 0.48, 0.23],
            lush: [0.42, 0.65, 0.28],
            moss: [0.31, 0.45, 0.19],
            earth: [0.56, 0.48, 0.34],
            path: [0.66, 0.58, 0.44],
            tuft: [0.18, 0.38, 0.19],
            tip: [0.71, 0.83, 0.41],
            pebble: [0.64, 0.61, 0.55],
        };
        out.draw(&Frame {
            clear: [1.0, 0.0, 1.0],
            world: &[],
            lights: &[],
            grade: Grade {
                ground: Some(Ground {
                    center,
                    scale: 1.5,
                    seed: 11,
                    palette: wave.map_or(woods, |_| palette([0.2, 0.5, 0.2])),
                    wave,
                }),
                ..Grade::default()
            },
            overlay: &[],
        });
        out.rgba().unwrap()
    }

    fn px(f: &[u8], x: usize, y: usize) -> &[u8] {
        &f[(y * 128 + x) * 4..(y * 128 + x) * 4 + 3]
    }

    #[test]
    fn the_ground_is_painted_in_world_space_and_holds_still_as_the_camera_moves() {
        let mut out = Offscreen::new(&[], 128, 96).unwrap();
        // (Far from the origin, where a run wanders; moved by whole quads of screen pixels.)
        let a = paint(&mut out, [41_250.0, -17_800.0], None);
        let b = paint(
            &mut out,
            [41_250.0 + 16.0 / 1.5, -17_800.0 + 8.0 / 1.5],
            None,
        );
        let mut worst = 0;
        for y in 0..88 {
            for x in 0..112 {
                for (p, q) in px(&a, x + 16, y + 8).iter().zip(px(&b, x, y)) {
                    worst = worst.max(p.abs_diff(*q));
                }
            }
        }
        assert!(worst <= 2, "moved ground differs by {worst}");
        // Painted, not cleared: no magenta, and not one flat colour.
        assert!((0..128 * 96).all(|i| a[i * 4] < 200 || a[i * 4 + 2] < 200));
        let distinct: std::collections::HashSet<&[u8]> = a.chunks(4).collect();
        assert!(distinct.len() > 50, "{} colours", distinct.len());
    }

    #[test]
    fn the_next_zone_paints_inside_the_waves_ring() {
        let mut out = Offscreen::new(&[], 128, 96).unwrap();
        // A red zone sweeping in over a green one, its ring centred on the screen's left edge.
        let red = palette([0.6, 0.15, 0.1]);
        let f = paint(&mut out, [0.0, 0.0], Some((red, [-64.0 / 1.5, 0.0, 30.0])));
        let (inside, outside) = (px(&f, 2, 48), px(&f, 125, 48));
        assert!(
            inside[0] > inside[1] && outside[1] > outside[0],
            "{inside:?} {outside:?}"
        );
    }
}
