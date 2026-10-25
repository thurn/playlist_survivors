//! The game's look over the sprites, as `src/runtime/renderer.ts` and `shaders.ts` draw it: the
//! world drawn into a texture, soft additive lights into another, a bloom (bright pass, then a
//! blur, at half size), and a composite that lights, grades, vignettes and distorts the world
//! before the overlay (the HUD) is drawn on top, untouched. The grading works on sRGB-encoded
//! values, as the TS game's WebGL did.
#![allow(
    clippy::default_trait_access,
    clippy::too_many_lines,
    clippy::too_many_arguments
)]

use crate::atlas::Page;
use crate::sprites::{Sprite, SpriteRenderer};

/// The most shockwaves at once.
pub const MAX_WAVES: usize = 4;

const WORLD: wgpu::TextureFormat = wgpu::TextureFormat::Rgba8UnormSrgb;
const LINEAR: wgpu::TextureFormat = wgpu::TextureFormat::Rgba8Unorm;

/// A soft light (logical pixels): white at its centre, 0.4 halfway, none at `radius`.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Light {
    pub x: f32,
    pub y: f32,
    pub radius: f32,
    pub color: [f32; 3],
    pub strength: f32,
}

/// The composite's settings. Positions and lengths are logical pixels; `pixel` is the logical
/// pixels per world pixel, which scales the distortions.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Grade {
    /// The light everything gets, and its beat pulse (a factor).
    pub ambient: [f32; 3],
    pub pulse: f32,
    pub bloom: f32,
    pub mul: [f32; 3],
    pub lift: [f32; 3],
    pub contrast: f32,
    pub saturation: f32,
    pub vignette: f32,
    /// A colour mixed over everything, by its alpha.
    pub flash: [f32; 4],
    /// Heat haze (0 or 1), on the clock `time` (s).
    pub haze: f32,
    pub time: f32,
    pub aberration: f32,
    /// Each shockwave's centre, radius and strength (none at strength 0).
    pub waves: [[f32; 4]; MAX_WAVES],
    pub pixel: f32,
}

impl Default for Grade {
    /// No change: the world as drawn.
    fn default() -> Grade {
        Grade {
            ambient: [1.0; 3],
            pulse: 1.0,
            bloom: 0.0,
            mul: [1.0; 3],
            lift: [0.0; 3],
            contrast: 1.0,
            saturation: 1.0,
            vignette: 0.0,
            flash: [0.0; 4],
            haze: 0.0,
            time: 0.0,
            aberration: 0.0,
            waves: [[0.0; 4]; MAX_WAVES],
            pixel: 1.0,
        }
    }
}

/// One frame: the world under `clear`, its lights and grade, and the overlay drawn over it all.
pub struct Frame<'a> {
    pub clear: [f64; 3],
    pub world: &'a [Sprite],
    pub lights: &'a [Light],
    pub grade: Grade,
    pub overlay: &'a [Sprite],
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct LightInstance {
    at: [f32; 4],
    color: [f32; 4],
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Comp {
    ambient: [f32; 3],
    pulse: f32,
    mul: [f32; 3],
    bloom: f32,
    lift: [f32; 3],
    contrast: f32,
    flash: [f32; 4],
    saturation: f32,
    vignette: f32,
    haze: f32,
    time: f32,
    aberration: f32,
    pixel: f32,
    srgb_out: f32,
    _pad: f32,
    size: [f32; 4],
    waves: [[f32; 4]; MAX_WAVES],
}

const SHADER: &str = r"
struct View { size: vec2<f32> };
struct Comp {
    ambient: vec3<f32>, pulse: f32,
    mul: vec3<f32>, bloom: f32,
    lift: vec3<f32>, contrast: f32,
    flash: vec4<f32>,
    saturation: f32, vignette: f32, haze: f32, time: f32,
    aberration: f32, pixel: f32, srgb_out: f32, pad: f32,
    size: vec4<f32>,
    waves: array<vec4<f32>, 4>,
};
struct Blur { step: vec4<f32> };

@group(0) @binding(0) var<uniform> view: View;

struct LightOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) local: vec2<f32>,
    @location(1) color: vec4<f32>,
};
@vertex
fn light_vs(@builtin(vertex_index) v: u32, @location(0) at: vec4<f32>,
            @location(1) color: vec4<f32>) -> LightOut {
    let corner = vec2<f32>(f32(v & 1u), f32((v >> 1u) & 1u)) * 2.0 - 1.0;
    let p = at.xy + corner * at.z;
    var o: LightOut;
    o.pos = vec4<f32>(p.x / view.size.x * 2.0 - 1.0, 1.0 - p.y / view.size.y * 2.0, 0.0, 1.0);
    o.local = corner;
    o.color = color;
    return o;
}
@fragment
fn light_fs(i: LightOut) -> @location(0) vec4<f32> {
    let d = length(i.local);
    if d >= 1.0 { discard; }
    let f = select(mix(0.4, 0.0, (d - 0.5) * 2.0), mix(1.0, 0.4, d * 2.0), d < 0.5);
    return vec4<f32>(i.color.rgb * f, 1.0);
}

struct Full { @builtin(position) pos: vec4<f32>, @location(0) uv: vec2<f32> };
@vertex
fn full_vs(@builtin(vertex_index) v: u32) -> Full {
    let uv = vec2<f32>(f32((v << 1u) & 2u), f32(v & 2u));
    var o: Full;
    o.pos = vec4<f32>(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, 0.0, 1.0);
    o.uv = uv;
    return o;
}

fn encode(c: vec3<f32>) -> vec3<f32> {
    let lo = c * 12.92;
    let hi = 1.055 * pow(max(c, vec3<f32>(0.0)), vec3<f32>(1.0 / 2.4)) - 0.055;
    return select(hi, lo, c <= vec3<f32>(0.0031308));
}
fn decode(c: vec3<f32>) -> vec3<f32> {
    let lo = c / 12.92;
    let hi = pow((max(c, vec3<f32>(0.0)) + 0.055) / 1.055, vec3<f32>(2.4));
    return select(hi, lo, c <= vec3<f32>(0.04045));
}

@group(0) @binding(1) var near: sampler;
@group(0) @binding(2) var soft: sampler;
@group(0) @binding(3) var world: texture_2d<f32>;
@group(0) @binding(4) var lights: texture_2d<f32>;
@group(0) @binding(5) var bloom: texture_2d<f32>;
@group(0) @binding(6) var<uniform> comp: Comp;
@group(0) @binding(7) var<uniform> blur: Blur;

@fragment
fn bright_fs(i: Full) -> @location(0) vec4<f32> {
    let c = encode(textureSampleLevel(world, soft, i.uv, 0.0).rgb);
    let l = max(c.r, max(c.g, c.b));
    return vec4<f32>(c * smoothstep(0.72, 1.0, l), 1.0);
}

@fragment
fn blur_fs(i: Full) -> @location(0) vec4<f32> {
    let w = array<f32, 5>(0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);
    var c = textureSampleLevel(bloom, soft, i.uv, 0.0).rgb * w[0];
    for (var k = 1; k < 5; k++) {
        let d = blur.step.xy * f32(k);
        c += textureSampleLevel(bloom, soft, i.uv + d, 0.0).rgb * w[k];
        c += textureSampleLevel(bloom, soft, i.uv - d, 0.0).rgb * w[k];
    }
    return vec4<f32>(c, 1.0);
}

fn world_at(p: vec2<f32>) -> vec3<f32> {
    return encode(textureSampleLevel(world, near, p / comp.size.xy, 0.0).rgb);
}

@fragment
fn composite_fs(i: Full) -> @location(0) vec4<f32> {
    var p = i.uv * comp.size.xy;
    let px = comp.pixel;
    // Shockwaves push pixels outward along a thin ring.
    for (var k = 0; k < 4; k++) {
        let w = comp.waves[k];
        if w.w <= 0.0 { continue; }
        let d = p - w.xy;
        let dist = length(d);
        let band = 1.0 - clamp(abs(dist - w.z) / (18.0 * px), 0.0, 1.0);
        if dist > 0.001 { p += (d / dist) * band * w.w * 7.0 * px; }
    }
    if comp.haze > 0.0 {
        let q = p / px;
        p.x += sin(q.y * 0.09 + comp.time * 3.0) * 0.6 * comp.haze * px;
        p.y += sin(q.x * 0.07 + comp.time * 2.3) * 0.4 * comp.haze * px;
    }
    var c: vec3<f32>;
    if comp.aberration > 0.001 {
        let dir = (i.uv - 0.5) * comp.aberration * 3.0 * px;
        c = vec3<f32>(world_at(p + dir).r, world_at(p).g, world_at(p - dir).b);
    } else {
        c = world_at(p);
    }
    let uv = p / comp.size.xy;
    let light = textureSampleLevel(lights, soft, uv, 0.0).rgb;
    var lit = c * (comp.ambient * comp.pulse + light * 1.3);
    lit += textureSampleLevel(bloom, soft, uv, 0.0).rgb * comp.bloom;
    // Colour grade.
    let luma = dot(lit, vec3<f32>(0.299, 0.587, 0.114));
    lit = mix(vec3<f32>(luma), lit, comp.saturation);
    lit = (lit - 0.5) * comp.contrast + 0.5;
    lit = lit * comp.mul + comp.lift;
    // Vignette.
    let q = i.uv - 0.5;
    let v = smoothstep(0.35, 0.95, length(q * vec2<f32>(1.0, 0.75)) * 1.25);
    lit *= 1.0 - v * comp.vignette;
    lit = clamp(mix(lit, comp.flash.rgb, comp.flash.a), vec3<f32>(0.0), vec3<f32>(1.0));
    if comp.srgb_out > 0.5 { lit = decode(lit); }
    return vec4<f32>(lit, 1.0);
}
";

/// The textures sized to the target, and the bind groups that read them.
struct Targets {
    size: (u32, u32),
    world: wgpu::TextureView,
    lights: wgpu::TextureView,
    bloom_a: wgpu::TextureView,
    bloom_b: wgpu::TextureView,
    /// The bright pass (reads the world, draws into A), the blur across (reads A, into B) and
    /// down (reads B, into A), and the composite (reads A).
    bright: wgpu::BindGroup,
    across: wgpu::BindGroup,
    down: wgpu::BindGroup,
    composite: wgpu::BindGroup,
}

/// The sprites and the passes over them.
pub struct Post {
    sprites: SpriteRenderer,
    format: wgpu::TextureFormat,
    layout: wgpu::BindGroupLayout,
    light_bind: wgpu::BindGroup,
    light_view: wgpu::Buffer,
    light_pipeline: wgpu::RenderPipeline,
    light_instances: wgpu::Buffer,
    light_capacity: usize,
    bright: wgpu::RenderPipeline,
    blur: wgpu::RenderPipeline,
    composite: wgpu::RenderPipeline,
    near: wgpu::Sampler,
    smooth: wgpu::Sampler,
    comp: wgpu::Buffer,
    across: wgpu::Buffer,
    down: wgpu::Buffer,
    targets: Option<Targets>,
}

fn uniform(device: &wgpu::Device, label: &str, size: u64) -> wgpu::Buffer {
    device.create_buffer(&wgpu::BufferDescriptor {
        label: Some(label),
        size,
        usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
        mapped_at_creation: false,
    })
}

impl Post {
    /// Uploads `pages` and builds the passes for `format` targets.
    pub fn new(
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        format: wgpu::TextureFormat,
        pages: &[Page],
    ) -> Result<Post, String> {
        let mut sprites = SpriteRenderer::new(device, queue, WORLD, pages)?;
        // (The overlay's pipeline, built now rather than on the first frame.)
        let mut enc = device.create_command_encoder(&Default::default());
        sprites.begin();
        let probe = device.create_texture(&wgpu::TextureDescriptor {
            label: None,
            size: wgpu::Extent3d {
                width: 1,
                height: 1,
                depth_or_array_layers: 1,
            },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format,
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT,
            view_formats: &[],
        });
        let probe_view = probe.create_view(&Default::default());
        sprites.draw(
            device,
            queue,
            &mut enc,
            &probe_view,
            format,
            (1.0, 1.0),
            Some([0.0; 3]),
            &[],
        );
        queue.submit([enc.finish()]);
        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("post"),
            source: wgpu::ShaderSource::Wgsl(SHADER.into()),
        });
        let buffer = |binding, visibility| wgpu::BindGroupLayoutEntry {
            binding,
            visibility,
            ty: wgpu::BindingType::Buffer {
                ty: wgpu::BufferBindingType::Uniform,
                has_dynamic_offset: false,
                min_binding_size: None,
            },
            count: None,
        };
        let sampler = |binding| wgpu::BindGroupLayoutEntry {
            binding,
            visibility: wgpu::ShaderStages::FRAGMENT,
            ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
            count: None,
        };
        let texture = |binding| wgpu::BindGroupLayoutEntry {
            binding,
            visibility: wgpu::ShaderStages::FRAGMENT,
            ty: wgpu::BindingType::Texture {
                sample_type: wgpu::TextureSampleType::Float { filterable: true },
                view_dimension: wgpu::TextureViewDimension::D2,
                multisampled: false,
            },
            count: None,
        };
        let vf = wgpu::ShaderStages::VERTEX_FRAGMENT;
        // One layout for every pass (unused bindings get the light view and the world).
        let layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("post"),
            entries: &[
                buffer(0, vf),
                sampler(1),
                sampler(2),
                texture(3),
                texture(4),
                texture(5),
                buffer(6, vf),
                buffer(7, vf),
            ],
        });
        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: None,
            bind_group_layouts: &[Some(&layout)],
            immediate_size: 0,
        });
        let pipeline =
            |label, vs, fs, format, blend, buffers: &[Option<wgpu::VertexBufferLayout>]| {
                device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
                    label: Some(label),
                    layout: Some(&pipeline_layout),
                    vertex: wgpu::VertexState {
                        module: &module,
                        entry_point: Some(vs),
                        compilation_options: Default::default(),
                        buffers,
                    },
                    fragment: Some(wgpu::FragmentState {
                        module: &module,
                        entry_point: Some(fs),
                        compilation_options: Default::default(),
                        targets: &[Some(wgpu::ColorTargetState {
                            format,
                            blend,
                            write_mask: wgpu::ColorWrites::ALL,
                        })],
                    }),
                    primitive: wgpu::PrimitiveState {
                        topology: if buffers.is_empty() {
                            wgpu::PrimitiveTopology::TriangleList
                        } else {
                            wgpu::PrimitiveTopology::TriangleStrip
                        },
                        ..Default::default()
                    },
                    depth_stencil: None,
                    multisample: wgpu::MultisampleState::default(),
                    multiview_mask: None,
                    cache: None,
                })
            };
        let add = wgpu::BlendComponent {
            src_factor: wgpu::BlendFactor::One,
            dst_factor: wgpu::BlendFactor::One,
            operation: wgpu::BlendOperation::Add,
        };
        let light_pipeline = pipeline(
            "lights",
            "light_vs",
            "light_fs",
            LINEAR,
            Some(wgpu::BlendState {
                color: add,
                alpha: add,
            }),
            &[Some(wgpu::VertexBufferLayout {
                array_stride: size_of::<LightInstance>() as u64,
                step_mode: wgpu::VertexStepMode::Instance,
                attributes: &wgpu::vertex_attr_array![0 => Float32x4, 1 => Float32x4],
            })],
        );
        let bright = pipeline("bright", "full_vs", "bright_fs", LINEAR, None, &[]);
        let blur = pipeline("blur", "full_vs", "blur_fs", LINEAR, None, &[]);
        let composite = pipeline("composite", "full_vs", "composite_fs", format, None, &[]);
        let near = device.create_sampler(&wgpu::SamplerDescriptor {
            mag_filter: wgpu::FilterMode::Nearest,
            min_filter: wgpu::FilterMode::Nearest,
            address_mode_u: wgpu::AddressMode::ClampToEdge,
            address_mode_v: wgpu::AddressMode::ClampToEdge,
            ..Default::default()
        });
        let smooth = device.create_sampler(&wgpu::SamplerDescriptor {
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            address_mode_u: wgpu::AddressMode::ClampToEdge,
            address_mode_v: wgpu::AddressMode::ClampToEdge,
            ..Default::default()
        });
        let light_view = uniform(device, "light view", 16);
        let comp = uniform(device, "composite", size_of::<Comp>() as u64);
        let across = uniform(device, "blur across", 16);
        let down = uniform(device, "blur down", 16);
        // The lights' bind group reads nothing but its view; a 1 x 1 texture fills the rest.
        let blank = device
            .create_texture(&wgpu::TextureDescriptor {
                label: None,
                size: wgpu::Extent3d {
                    width: 1,
                    height: 1,
                    depth_or_array_layers: 1,
                },
                mip_level_count: 1,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format: LINEAR,
                usage: wgpu::TextureUsages::TEXTURE_BINDING,
                view_formats: &[],
            })
            .create_view(&Default::default());
        let light_bind = bind(
            device,
            &layout,
            [&light_view, &comp, &across],
            [&near, &smooth],
            [&blank, &blank, &blank],
        );
        let light_capacity = 256;
        Ok(Post {
            sprites,
            format,
            layout,
            light_bind,
            light_view,
            light_pipeline,
            light_instances: light_buffer(device, light_capacity),
            light_capacity,
            bright,
            blur,
            composite,
            near,
            smooth,
            comp,
            across,
            down,
            targets: None,
        })
    }

    fn targets(&mut self, device: &wgpu::Device, size: (u32, u32)) {
        if self.targets.as_ref().is_some_and(|t| t.size == size) {
            return;
        }
        let half = ((size.0 / 2).max(1), (size.1 / 2).max(1));
        let make = |label, (w, h): (u32, u32), format| {
            device
                .create_texture(&wgpu::TextureDescriptor {
                    label: Some(label),
                    size: wgpu::Extent3d {
                        width: w,
                        height: h,
                        depth_or_array_layers: 1,
                    },
                    mip_level_count: 1,
                    sample_count: 1,
                    dimension: wgpu::TextureDimension::D2,
                    format,
                    usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                        | wgpu::TextureUsages::TEXTURE_BINDING,
                    view_formats: &[],
                })
                .create_view(&Default::default())
        };
        let world = make("world", size, WORLD);
        let lights = make("lights", half, LINEAR);
        let bloom_a = make("bloom a", half, LINEAR);
        let bloom_b = make("bloom b", half, LINEAR);
        let samplers = [&self.near, &self.smooth];
        let group = |blur: &wgpu::Buffer, bloom: &wgpu::TextureView| {
            bind(
                device,
                &self.layout,
                [&self.light_view, &self.comp, blur],
                samplers,
                [&world, &lights, bloom],
            )
        };
        self.targets = Some(Targets {
            size,
            bright: group(&self.across, &bloom_b),
            across: group(&self.across, &bloom_a),
            down: group(&self.down, &bloom_b),
            composite: group(&self.across, &bloom_a),
            world,
            lights,
            bloom_a,
            bloom_b,
        });
    }

    /// Draws `frame` into `target` (`physical` pixels, `logical` in the sprites' units).
    pub fn draw(
        &mut self,
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        encoder: &mut wgpu::CommandEncoder,
        target: &wgpu::TextureView,
        physical: (u32, u32),
        logical: (f32, f32),
        frame: &Frame,
    ) {
        self.targets(device, physical);
        let dpi = physical.0 as f32 / logical.0.max(1.0);
        self.sprites.begin();
        let t = self.targets.as_ref().expect("sized");
        self.sprites.draw(
            device,
            queue,
            encoder,
            &t.world,
            WORLD,
            logical,
            Some(frame.clear),
            frame.world,
        );
        // Lights.
        let instances: Vec<LightInstance> = frame
            .lights
            .iter()
            .filter(|l| l.radius > 0.0 && l.strength > 0.0)
            .map(|l| LightInstance {
                at: [l.x, l.y, l.radius, 0.0],
                color: [
                    l.color[0] * l.strength,
                    l.color[1] * l.strength,
                    l.color[2] * l.strength,
                    1.0,
                ],
            })
            .collect();
        if instances.len() > self.light_capacity {
            self.light_capacity = instances.len().next_power_of_two();
            self.light_instances = light_buffer(device, self.light_capacity);
        }
        queue.write_buffer(
            &self.light_view,
            0,
            bytemuck::cast_slice(&[logical.0, logical.1, 0.0, 0.0]),
        );
        queue.write_buffer(&self.light_instances, 0, bytemuck::cast_slice(&instances));
        {
            let mut pass = begin(encoder, &t.lights, "lights");
            if !instances.is_empty() {
                pass.set_pipeline(&self.light_pipeline);
                pass.set_bind_group(0, &self.light_bind, &[]);
                pass.set_vertex_buffer(0, self.light_instances.slice(..));
                pass.draw(0..4, 0..instances.len() as u32);
            }
        }
        // Bloom: the bright pass into A, blurred across into B and down into A again, which the
        // composite reads. (No pass binds the texture it draws into.)
        let g = frame.grade;
        let half = (
            (physical.0 / 2).max(1) as f32,
            (physical.1 / 2).max(1) as f32,
        );
        // (About 12 world pixels of blur: four taps a side, in half-size pixels.)
        let step = 1.5 * g.pixel * dpi / 2.0;
        queue.write_buffer(
            &self.across,
            0,
            bytemuck::cast_slice(&[step / half.0, 0.0, 0.0, 0.0]),
        );
        queue.write_buffer(
            &self.down,
            0,
            bytemuck::cast_slice(&[0.0, step / half.1, 0.0, 0.0]),
        );
        let full = |encoder: &mut wgpu::CommandEncoder,
                    out: &wgpu::TextureView,
                    pipeline: &wgpu::RenderPipeline,
                    group: &wgpu::BindGroup| {
            let mut pass = begin(encoder, out, "post");
            pass.set_pipeline(pipeline);
            pass.set_bind_group(0, group, &[]);
            pass.draw(0..3, 0..1);
        };
        full(encoder, &t.bloom_a, &self.bright, &t.bright);
        full(encoder, &t.bloom_b, &self.blur, &t.across);
        full(encoder, &t.bloom_a, &self.blur, &t.down);
        // Composite.
        let mut waves = g.waves;
        for w in &mut waves {
            for v in &mut w[..3] {
                *v *= dpi;
            }
        }
        let comp = Comp {
            ambient: g.ambient,
            pulse: g.pulse,
            mul: g.mul,
            bloom: g.bloom,
            lift: g.lift,
            contrast: g.contrast,
            flash: g.flash,
            saturation: g.saturation,
            vignette: g.vignette,
            haze: g.haze,
            time: g.time,
            aberration: g.aberration,
            pixel: g.pixel * dpi,
            srgb_out: if self.format.is_srgb() { 1.0 } else { 0.0 },
            _pad: 0.0,
            size: [physical.0 as f32, physical.1 as f32, 0.0, 0.0],
            waves,
        };
        queue.write_buffer(&self.comp, 0, bytemuck::bytes_of(&comp));
        full(encoder, target, &self.composite, &t.composite);
        self.sprites.draw(
            device,
            queue,
            encoder,
            target,
            self.format,
            logical,
            None,
            frame.overlay,
        );
    }
}

fn bind(
    device: &wgpu::Device,
    layout: &wgpu::BindGroupLayout,
    buffers: [&wgpu::Buffer; 3],
    samplers: [&wgpu::Sampler; 2],
    textures: [&wgpu::TextureView; 3],
) -> wgpu::BindGroup {
    device.create_bind_group(&wgpu::BindGroupDescriptor {
        label: None,
        layout,
        entries: &[
            wgpu::BindGroupEntry {
                binding: 0,
                resource: buffers[0].as_entire_binding(),
            },
            wgpu::BindGroupEntry {
                binding: 1,
                resource: wgpu::BindingResource::Sampler(samplers[0]),
            },
            wgpu::BindGroupEntry {
                binding: 2,
                resource: wgpu::BindingResource::Sampler(samplers[1]),
            },
            wgpu::BindGroupEntry {
                binding: 3,
                resource: wgpu::BindingResource::TextureView(textures[0]),
            },
            wgpu::BindGroupEntry {
                binding: 4,
                resource: wgpu::BindingResource::TextureView(textures[1]),
            },
            wgpu::BindGroupEntry {
                binding: 5,
                resource: wgpu::BindingResource::TextureView(textures[2]),
            },
            wgpu::BindGroupEntry {
                binding: 6,
                resource: buffers[1].as_entire_binding(),
            },
            wgpu::BindGroupEntry {
                binding: 7,
                resource: buffers[2].as_entire_binding(),
            },
        ],
    })
}

fn begin<'e>(
    encoder: &'e mut wgpu::CommandEncoder,
    target: &wgpu::TextureView,
    label: &str,
) -> wgpu::RenderPass<'e> {
    encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
        label: Some(label),
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
    })
}

fn light_buffer(device: &wgpu::Device, capacity: usize) -> wgpu::Buffer {
    device.create_buffer(&wgpu::BufferDescriptor {
        label: Some("lights"),
        size: (capacity * size_of::<LightInstance>()) as u64,
        usage: wgpu::BufferUsages::VERTEX | wgpu::BufferUsages::COPY_DST,
        mapped_at_creation: false,
    })
}
