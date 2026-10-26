//! Instanced sprites: each a quad on screen, textured from a region of an atlas page (or solid),
//! tinted, drawn in the order given. Nearest sampling keeps the pixel art crisp.
#![allow(clippy::default_trait_access)]

use crate::atlas::{Page, Region};

/// The most atlas pages bound at once.
pub const MAX_PAGES: usize = 8;
/// The page index of a solid (untextured) sprite.
pub const SOLID: u32 = u32::MAX;
const ARC: u32 = u32::MAX - 1;
/// A procedural sweep: head angle and trailing span in radians, thickness in pixels.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Arc {
    pub head: f32,
    pub span: f32,
    pub width: f32,
}

/// One quad: where it goes (logical pixels, origin top left), what it shows, and its tint
/// (multiplied, with alpha); turned `rot` radians about its centre, and drawn additively (light
/// added to what is below) or over it.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Sprite {
    pub x: f32,
    pub y: f32,
    pub w: f32,
    pub h: f32,
    pub region: Region,
    pub tint: [f32; 4],
    pub rot: f32,
    pub additive: bool,
    /// A shear about its bottom edge: the top moves left by this times its height (a sway).
    pub skew: f32,
    arc: Option<Arc>,
}

impl Sprite {
    /// A solid rectangle.
    pub fn solid(x: f32, y: f32, w: f32, h: f32, color: [f32; 4]) -> Sprite {
        Sprite {
            x,
            y,
            w,
            h,
            region: Region {
                page: SOLID,
                x: 0,
                y: 0,
                w: 0,
                h: 0,
            },
            tint: color,
            rot: 0.0,
            additive: false,
            skew: 0.0,
            arc: None,
        }
    }

    /// An antialiased luminous arc with no raster asset. Its centre line is `radius`.
    pub fn arc(x: f32, y: f32, radius: f32, arc: Arc, tint: [f32; 4]) -> Sprite {
        let diameter = 2.0 * radius / 0.82;
        let mut s = Self::solid(
            x - diameter / 2.0,
            y - diameter / 2.0,
            diameter,
            diameter,
            tint,
        )
        .additive();
        s.region.page = ARC;
        s.arc = Some(arc);
        s
    }

    /// `region` drawn at `scale`, its anchor (pixels within the region) at (x, y).
    pub fn anchored(region: Region, x: f32, y: f32, anchor: (f32, f32), scale: f32) -> Sprite {
        Sprite {
            x: x - anchor.0 * scale,
            y: y - anchor.1 * scale,
            w: region.w as f32 * scale,
            h: region.h as f32 * scale,
            region,
            tint: [1.0; 4],
            rot: 0.0,
            additive: false,
            skew: 0.0,
            arc: None,
        }
    }

    /// `region` drawn `w` x `h`, centred on (x, y).
    pub fn centred(region: Region, x: f32, y: f32, w: f32, h: f32) -> Sprite {
        Sprite {
            x: x - w / 2.0,
            y: y - h / 2.0,
            w,
            h,
            region,
            tint: [1.0; 4],
            rot: 0.0,
            additive: false,
            skew: 0.0,
            arc: None,
        }
    }

    /// The same sprite turned `rot` radians about its centre.
    #[must_use]
    pub fn rotated(self, rot: f32) -> Sprite {
        Sprite { rot, ..self }
    }

    /// The same sprite sheared about its bottom edge (`skew` of its height).
    #[must_use]
    pub fn skewed(self, skew: f32) -> Sprite {
        Sprite { skew, ..self }
    }

    /// The same sprite drawn additively.
    #[must_use]
    pub fn additive(self) -> Sprite {
        Sprite {
            additive: true,
            ..self
        }
    }

    /// The same sprite tinted.
    #[must_use]
    pub fn tinted(self, tint: [f32; 4]) -> Sprite {
        Sprite { tint, ..self }
    }

    /// The same sprite mirrored left to right.
    #[must_use]
    pub fn mirrored(self) -> Sprite {
        Sprite {
            x: self.x + self.w,
            w: -self.w,
            ..self
        }
    }
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Instance {
    rect: [f32; 4],
    uv: [f32; 4],
    tint: [f32; 4],
    page: u32,
    additive: u32,
    rot: f32,
    skew: f32,
    shape: [f32; 4],
}

const SHADER: &str = r"
struct View { size: vec2<f32> };
@group(0) @binding(0) var<uniform> view: View;
@group(0) @binding(1) var samp: sampler;
@group(0) @binding(2) var page0: texture_2d<f32>;
@group(0) @binding(3) var page1: texture_2d<f32>;
@group(0) @binding(4) var page2: texture_2d<f32>;
@group(0) @binding(5) var page3: texture_2d<f32>;
@group(0) @binding(6) var smooth_samp: sampler;
@group(0) @binding(7) var page4: texture_2d<f32>;
@group(0) @binding(8) var page5: texture_2d<f32>;
@group(0) @binding(9) var page6: texture_2d<f32>;
@group(0) @binding(10) var page7: texture_2d<f32>;
struct Out {
    @builtin(position) pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) tint: vec4<f32>,
    @location(2) @interpolate(flat) page: u32,
    @location(3) @interpolate(flat) additive: u32,
    @location(4) @interpolate(flat) shape: vec4<f32>,
};
@vertex
fn vs(@builtin(vertex_index) v: u32, @location(0) rect: vec4<f32>, @location(1) uv: vec4<f32>,
      @location(2) tint: vec4<f32>, @location(3) page: u32, @location(4) additive: u32,
      @location(5) rot: f32, @location(6) skew: f32, @location(7) shape: vec4<f32>) -> Out {
    let corner = vec2<f32>(f32(v & 1u), f32((v >> 1u) & 1u));
    var local = (corner - 0.5) * rect.zw;
    // (A sway shears it about its bottom edge.)
    local.x = local.x + skew * (local.y - 0.5 * rect.w);
    let c = cos(rot);
    let s = sin(rot);
    let p = rect.xy + 0.5 * rect.zw + vec2<f32>(c * local.x - s * local.y, s * local.x + c * local.y);
    var o: Out;
    o.pos = vec4<f32>(p.x / view.size.x * 2.0 - 1.0, 1.0 - p.y / view.size.y * 2.0, 0.0, 1.0);
    o.uv = uv.xy + corner * uv.zw;
    o.tint = tint;
    o.page = page;
    o.additive = additive;
    o.shape = shape;
    return o;
}
@fragment
fn fs(i: Out) -> @location(0) vec4<f32> {
    var t = vec4<f32>(1.0);
    let q = (i.uv - 0.5) * 2.0;
    let radius = length(q);
    let aa = max(fwidth(radius), 0.002);
    if i.page == 4294967294u {
        let theta = atan2(q.y, q.x);
        let delta = (i.shape.x - theta + 12.566371) % 6.283185;
        let span = clamp(i.shape.y, 0.01, 6.283185);
        let along = clamp(delta / span, 0.0, 1.0);
        let full = span > 6.27;
        let taper = select(pow(1.0 - along, 0.7), 1.0, full);
        let width = max(i.shape.z * (0.3 + 0.7 * taper), 0.002);
        let d = abs(radius - 0.82);
        let core = 1.0 - smoothstep(width * 0.35, width * 0.6 + aa, d);
        let halo = 0.3 * exp(-d*d / max(width*width*5.0, 0.00001));
        let tail = select(pow(1.0-along,1.2) * (1.0-smoothstep(max(span-0.08,0.0),span,delta)),1.0,full);
        t = vec4<f32>(1.0,1.0,1.0,min(1.0,core+halo)*tail);
    }

    else if i.page == 0u {
        if (i.additive & 2u) != 0u { t = textureSampleLevel(page0, smooth_samp, i.uv, 0.0); }
        else { t = textureSampleLevel(page0, samp, i.uv, 0.0); }
    }
    else if i.page == 1u {
        if (i.additive & 2u) != 0u { t = textureSampleLevel(page1, smooth_samp, i.uv, 0.0); }
        else { t = textureSampleLevel(page1, samp, i.uv, 0.0); }
    }
    else if i.page == 2u {
        if (i.additive & 2u) != 0u { t = textureSampleLevel(page2, smooth_samp, i.uv, 0.0); }
        else { t = textureSampleLevel(page2, samp, i.uv, 0.0); }
    }
    else if i.page == 3u {
        if (i.additive & 2u) != 0u { t = textureSampleLevel(page3, smooth_samp, i.uv, 0.0); }
        else { t = textureSampleLevel(page3, samp, i.uv, 0.0); }
    }
    else if i.page == 4u {
        if (i.additive & 2u) != 0u { t = textureSampleLevel(page4, smooth_samp, i.uv, 0.0); }
        else { t = textureSampleLevel(page4, samp, i.uv, 0.0); }
    }
    else if i.page == 5u {
        if (i.additive & 2u) != 0u { t = textureSampleLevel(page5, smooth_samp, i.uv, 0.0); }
        else { t = textureSampleLevel(page5, samp, i.uv, 0.0); }
    }
    else if i.page == 6u {
        if (i.additive & 2u) != 0u { t = textureSampleLevel(page6, smooth_samp, i.uv, 0.0); }
        else { t = textureSampleLevel(page6, samp, i.uv, 0.0); }
    }
    else if i.page == 7u {
        if (i.additive & 2u) != 0u { t = textureSampleLevel(page7, smooth_samp, i.uv, 0.0); }
        else { t = textureSampleLevel(page7, samp, i.uv, 0.0); }
    }
    let c = t * i.tint;
    if c.a < 0.004 { discard; }
    // (Premultiplied: an additive sprite leaves what is below it, adding its light.)
    return vec4<f32>(c.rgb * c.a, select(c.a, 0.0, (i.additive & 1u) == 1u));
}
";

/// One draw's instance buffer and view, so draws recorded into one encoder don't share them.
struct Slot {
    view: wgpu::Buffer,
    bind: wgpu::BindGroup,
    instances: wgpu::Buffer,
    capacity: usize,
}

/// The sprite pipelines (one per target format), the pages as textures and the draws' slots.
pub struct SpriteRenderer {
    module: wgpu::ShaderModule,
    pipeline_layout: wgpu::PipelineLayout,
    layout: wgpu::BindGroupLayout,
    sampler: wgpu::Sampler,
    smooth: wgpu::Sampler,
    views: Vec<wgpu::TextureView>,
    pipelines: Vec<(wgpu::TextureFormat, wgpu::RenderPipeline)>,
    slots: Vec<Slot>,
    next_slot: usize,
    page_sizes: Vec<(f32, f32)>,
    smooth_pages: Vec<bool>,
    scratch: Vec<Instance>,
}

impl SpriteRenderer {
    /// Uploads `pages` (at most [`MAX_PAGES`]) and builds the pipeline for `format` targets.
    pub fn new(
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        format: wgpu::TextureFormat,
        pages: &[Page],
    ) -> Result<SpriteRenderer, String> {
        if pages.len() > MAX_PAGES {
            return Err(format!("{} pages, at most {MAX_PAGES}", pages.len()));
        }
        let white = Page {
            name: "white".into(),
            width: 1,
            height: 1,
            rgba: vec![255; 4],
        };
        let views: Vec<wgpu::TextureView> = (0..MAX_PAGES)
            .map(|k| upload(device, queue, pages.get(k).unwrap_or(&white)))
            .collect();
        let sampler = device.create_sampler(&wgpu::SamplerDescriptor {
            mag_filter: wgpu::FilterMode::Nearest,
            min_filter: wgpu::FilterMode::Nearest,
            ..Default::default()
        });
        let smooth = device.create_sampler(&wgpu::SamplerDescriptor {
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            ..Default::default()
        });
        let texture_entry = |binding| wgpu::BindGroupLayoutEntry {
            binding,
            visibility: wgpu::ShaderStages::FRAGMENT,
            ty: wgpu::BindingType::Texture {
                sample_type: wgpu::TextureSampleType::Float { filterable: true },
                view_dimension: wgpu::TextureViewDimension::D2,
                multisampled: false,
            },
            count: None,
        };
        let layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: None,
            entries: &[
                wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: wgpu::ShaderStages::VERTEX,
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
                    ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
                    count: None,
                },
                texture_entry(2),
                texture_entry(3),
                texture_entry(4),
                texture_entry(5),
                texture_entry(7),
                texture_entry(8),
                texture_entry(9),
                texture_entry(10),
                wgpu::BindGroupLayoutEntry {
                    binding: 6,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
                    count: None,
                },
            ],
        });
        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("sprites"),
            source: wgpu::ShaderSource::Wgsl(SHADER.into()),
        });
        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: None,
            bind_group_layouts: &[Some(&layout)],
            immediate_size: 0,
        });
        let mut renderer = SpriteRenderer {
            module,
            pipeline_layout,
            layout,
            sampler,
            smooth,
            views,
            pipelines: Vec::new(),
            slots: Vec::new(),
            next_slot: 0,
            smooth_pages: pages.iter().map(|p| p.name == "proc").collect(),
            page_sizes: pages
                .iter()
                .map(|p| (p.width as f32, p.height as f32))
                .collect(),
            scratch: Vec::new(),
        };
        renderer.pipeline(device, format);
        Ok(renderer)
    }

    /// The pipeline drawing into `format` targets, built the first time.
    fn pipeline(&mut self, device: &wgpu::Device, format: wgpu::TextureFormat) -> usize {
        if let Some(k) = self.pipelines.iter().position(|(f, _)| *f == format) {
            return k;
        }
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("sprites"),
            layout: Some(&self.pipeline_layout),
            vertex: wgpu::VertexState {
                module: &self.module,
                entry_point: Some("vs"),
                compilation_options: Default::default(),
                buffers: &[Some(wgpu::VertexBufferLayout {
                    array_stride: size_of::<Instance>() as u64,
                    step_mode: wgpu::VertexStepMode::Instance,
                    attributes: &wgpu::vertex_attr_array![
                        0 => Float32x4, 1 => Float32x4, 2 => Float32x4, 3 => Uint32, 4 => Uint32,
                        5 => Float32, 6 => Float32, 7 => Float32x4
                    ],
                })],
            },
            fragment: Some(wgpu::FragmentState {
                module: &self.module,
                entry_point: Some("fs"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState {
                    format,
                    blend: Some(wgpu::BlendState::PREMULTIPLIED_ALPHA_BLENDING),
                    write_mask: wgpu::ColorWrites::ALL,
                })],
            }),
            primitive: wgpu::PrimitiveState {
                topology: wgpu::PrimitiveTopology::TriangleStrip,
                ..Default::default()
            },
            depth_stencil: None,
            multisample: wgpu::MultisampleState::default(),
            multiview_mask: None,
            cache: None,
        });
        self.pipelines.push((format, pipeline));
        self.pipelines.len() - 1
    }

    /// Starts a frame: its draws take slots from the first again. (Call once per submitted
    /// encoder, before its first draw.)
    pub fn begin(&mut self) {
        self.next_slot = 0;
    }

    fn slot(&mut self, device: &wgpu::Device, needed: usize) -> usize {
        let k = self.next_slot;
        self.next_slot += 1;
        if k == self.slots.len() || self.slots[k].capacity < needed {
            let capacity = needed.max(4096).next_power_of_two();
            let view = device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("view"),
                size: 16,
                usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
                mapped_at_creation: false,
            });
            let mut entries = vec![
                wgpu::BindGroupEntry {
                    binding: 0,
                    resource: view.as_entire_binding(),
                },
                wgpu::BindGroupEntry {
                    binding: 1,
                    resource: wgpu::BindingResource::Sampler(&self.sampler),
                },
            ];
            for (b, v) in self.views.iter().enumerate() {
                entries.push(wgpu::BindGroupEntry {
                    binding: if b < 4 { 2 + b as u32 } else { 3 + b as u32 },
                    resource: wgpu::BindingResource::TextureView(v),
                });
            }
            entries.push(wgpu::BindGroupEntry {
                binding: 6,
                resource: wgpu::BindingResource::Sampler(&self.smooth),
            });
            let bind = device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: None,
                layout: &self.layout,
                entries: &entries,
            });
            let slot = Slot {
                view,
                bind,
                instances: instance_buffer(device, capacity),
                capacity,
            };
            if k == self.slots.len() {
                self.slots.push(slot);
            } else {
                self.slots[k] = slot;
            }
        }
        k
    }

    /// Draws `sprites` in order into `target` (of `format`), for a `size` in logical pixels,
    /// clearing it to `clear` first or, with `None`, drawing over what it holds.
    #[allow(clippy::too_many_arguments)]
    pub fn draw(
        &mut self,
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        encoder: &mut wgpu::CommandEncoder,
        target: &wgpu::TextureView,
        format: wgpu::TextureFormat,
        size: (f32, f32),
        clear: Option<[f64; 3]>,
        sprites: &[Sprite],
    ) {
        self.scratch.clear();
        for s in sprites {
            let r = s.region;
            let uv = match self.page_sizes.get(r.page as usize) {
                Some(&(pw, ph)) => [
                    r.x as f32 / pw,
                    r.y as f32 / ph,
                    r.w as f32 / pw,
                    r.h as f32 / ph,
                ],
                None => [0.0, 0.0, 1.0, 1.0],
            };
            let (x, w) = if s.w < 0.0 {
                // (Mirrored: the quad's left edge samples the region's right.)
                (s.x + s.w, -s.w)
            } else {
                (s.x, s.w)
            };
            let uv = if s.w < 0.0 {
                [uv[0] + uv[2], uv[1], -uv[2], uv[3]]
            } else {
                uv
            };
            self.scratch.push(Instance {
                rect: [x, s.y, w, s.h],
                uv,
                tint: s.tint,
                page: if s.arc.is_some() {
                    ARC
                } else if (r.page as usize) < self.page_sizes.len() {
                    r.page
                } else {
                    SOLID
                },
                // Bit 0 controls blending, bit 1 controls the atlas sampler.
                additive: u32::from(s.additive)
                    | if self
                        .smooth_pages
                        .get(r.page as usize)
                        .copied()
                        .unwrap_or(false)
                    {
                        2
                    } else {
                        0
                    },
                rot: s.rot,
                skew: s.skew,
                shape: s.arc.map_or([0.0; 4], |arc| {
                    [arc.head, arc.span, 2.0 * arc.width / w.max(0.001), 0.0]
                }),
            });
        }
        let pipeline = self.pipeline(device, format);
        let slot = self.slot(device, self.scratch.len());
        let slot = &self.slots[slot];
        queue.write_buffer(
            &slot.view,
            0,
            bytemuck::cast_slice(&[size.0, size.1, 0.0, 0.0]),
        );
        queue.write_buffer(&slot.instances, 0, bytemuck::cast_slice(&self.scratch));
        let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
            label: Some("sprites"),
            color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                view: target,
                depth_slice: None,
                resolve_target: None,
                ops: wgpu::Operations {
                    load: clear.map_or(wgpu::LoadOp::Load, |c| {
                        wgpu::LoadOp::Clear(wgpu::Color {
                            r: c[0],
                            g: c[1],
                            b: c[2],
                            a: 1.0,
                        })
                    }),
                    store: wgpu::StoreOp::Store,
                },
            })],
            depth_stencil_attachment: None,
            timestamp_writes: None,
            occlusion_query_set: None,
            multiview_mask: None,
        });
        if !self.scratch.is_empty() {
            pass.set_pipeline(&self.pipelines[pipeline].1);
            pass.set_bind_group(0, &slot.bind, &[]);
            pass.set_vertex_buffer(0, slot.instances.slice(..));
            pass.draw(0..4, 0..self.scratch.len() as u32);
        }
    }
}

fn instance_buffer(device: &wgpu::Device, capacity: usize) -> wgpu::Buffer {
    device.create_buffer(&wgpu::BufferDescriptor {
        label: Some("sprite instances"),
        size: (capacity * size_of::<Instance>()) as u64,
        usage: wgpu::BufferUsages::VERTEX | wgpu::BufferUsages::COPY_DST,
        mapped_at_creation: false,
    })
}

fn upload(device: &wgpu::Device, queue: &wgpu::Queue, page: &Page) -> wgpu::TextureView {
    let size = wgpu::Extent3d {
        width: page.width,
        height: page.height,
        depth_or_array_layers: 1,
    };
    let texture = device.create_texture(&wgpu::TextureDescriptor {
        label: Some(&page.name),
        size,
        mip_level_count: 1,
        sample_count: 1,
        dimension: wgpu::TextureDimension::D2,
        format: wgpu::TextureFormat::Rgba8UnormSrgb,
        usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
        view_formats: &[],
    });
    queue.write_texture(
        wgpu::TexelCopyTextureInfo {
            texture: &texture,
            mip_level: 0,
            origin: wgpu::Origin3d::ZERO,
            aspect: wgpu::TextureAspect::All,
        },
        &page.rgba,
        wgpu::TexelCopyBufferLayout {
            offset: 0,
            bytes_per_row: Some(4 * page.width),
            rows_per_image: Some(page.height),
        },
        size,
    );
    texture.create_view(&Default::default())
}
