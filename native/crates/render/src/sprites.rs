//! Instanced sprites: each a quad on screen, textured from a region of an atlas page (or solid),
//! tinted, drawn in the order given. Nearest sampling keeps the pixel art crisp.
#![allow(clippy::default_trait_access)]

use crate::atlas::{Page, Region};

/// The most atlas pages bound at once.
pub const MAX_PAGES: usize = 4;
/// The page index of a solid (untextured) sprite.
pub const SOLID: u32 = u32::MAX;

/// One quad: where it goes (logical pixels, origin top left), what it shows, and its tint
/// (multiplied, with alpha). `flash` mixes the texel toward the tint's colour (0 to 1).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Sprite {
    pub x: f32,
    pub y: f32,
    pub w: f32,
    pub h: f32,
    pub region: Region,
    pub tint: [f32; 4],
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
        }
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
    _pad: [u32; 3],
}

const SHADER: &str = r"
struct View { size: vec2<f32> };
@group(0) @binding(0) var<uniform> view: View;
@group(0) @binding(1) var samp: sampler;
@group(0) @binding(2) var page0: texture_2d<f32>;
@group(0) @binding(3) var page1: texture_2d<f32>;
@group(0) @binding(4) var page2: texture_2d<f32>;
@group(0) @binding(5) var page3: texture_2d<f32>;
struct Out {
    @builtin(position) pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) tint: vec4<f32>,
    @location(2) @interpolate(flat) page: u32,
};
@vertex
fn vs(@builtin(vertex_index) v: u32, @location(0) rect: vec4<f32>, @location(1) uv: vec4<f32>,
      @location(2) tint: vec4<f32>, @location(3) page: u32) -> Out {
    let corner = vec2<f32>(f32(v & 1u), f32((v >> 1u) & 1u));
    let p = rect.xy + corner * rect.zw;
    var o: Out;
    o.pos = vec4<f32>(p.x / view.size.x * 2.0 - 1.0, 1.0 - p.y / view.size.y * 2.0, 0.0, 1.0);
    o.uv = uv.xy + corner * uv.zw;
    o.tint = tint;
    o.page = page;
    return o;
}
@fragment
fn fs(i: Out) -> @location(0) vec4<f32> {
    var t = vec4<f32>(1.0);
    if i.page == 0u { t = textureSampleLevel(page0, samp, i.uv, 0.0); }
    else if i.page == 1u { t = textureSampleLevel(page1, samp, i.uv, 0.0); }
    else if i.page == 2u { t = textureSampleLevel(page2, samp, i.uv, 0.0); }
    else if i.page == 3u { t = textureSampleLevel(page3, samp, i.uv, 0.0); }
    let c = t * i.tint;
    if c.a < 0.004 { discard; }
    return c;
}
";

/// The sprite pipeline, the pages as textures and an instance buffer.
pub struct SpriteRenderer {
    pipeline: wgpu::RenderPipeline,
    view: wgpu::Buffer,
    bind: wgpu::BindGroup,
    instances: wgpu::Buffer,
    capacity: usize,
    page_sizes: Vec<(f32, f32)>,
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
        let view = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("view"),
            size: 16,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
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
            ],
        });
        let bind = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: None,
            layout: &layout,
            entries: &[
                wgpu::BindGroupEntry {
                    binding: 0,
                    resource: view.as_entire_binding(),
                },
                wgpu::BindGroupEntry {
                    binding: 1,
                    resource: wgpu::BindingResource::Sampler(&sampler),
                },
                wgpu::BindGroupEntry {
                    binding: 2,
                    resource: wgpu::BindingResource::TextureView(&views[0]),
                },
                wgpu::BindGroupEntry {
                    binding: 3,
                    resource: wgpu::BindingResource::TextureView(&views[1]),
                },
                wgpu::BindGroupEntry {
                    binding: 4,
                    resource: wgpu::BindingResource::TextureView(&views[2]),
                },
                wgpu::BindGroupEntry {
                    binding: 5,
                    resource: wgpu::BindingResource::TextureView(&views[3]),
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
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("sprites"),
            layout: Some(&pipeline_layout),
            vertex: wgpu::VertexState {
                module: &module,
                entry_point: Some("vs"),
                compilation_options: Default::default(),
                buffers: &[Some(wgpu::VertexBufferLayout {
                    array_stride: size_of::<Instance>() as u64,
                    step_mode: wgpu::VertexStepMode::Instance,
                    attributes: &wgpu::vertex_attr_array![
                        0 => Float32x4, 1 => Float32x4, 2 => Float32x4, 3 => Uint32
                    ],
                })],
            },
            fragment: Some(wgpu::FragmentState {
                module: &module,
                entry_point: Some("fs"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState {
                    format,
                    blend: Some(wgpu::BlendState::ALPHA_BLENDING),
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
        let capacity = 4096;
        Ok(SpriteRenderer {
            pipeline,
            view,
            bind,
            instances: instance_buffer(device, capacity),
            capacity,
            page_sizes: pages
                .iter()
                .map(|p| (p.width as f32, p.height as f32))
                .collect(),
            scratch: Vec::new(),
        })
    }

    /// Clears `target` to `clear` and draws `sprites` in order, for a `size` in logical pixels.
    #[allow(clippy::too_many_arguments)]
    pub fn draw(
        &mut self,
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        encoder: &mut wgpu::CommandEncoder,
        target: &wgpu::TextureView,
        size: (f32, f32),
        clear: [f64; 3],
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
                page: if (r.page as usize) < self.page_sizes.len() {
                    r.page
                } else {
                    SOLID
                },
                _pad: [0; 3],
            });
        }
        if self.scratch.len() > self.capacity {
            self.capacity = self.scratch.len().next_power_of_two();
            self.instances = instance_buffer(device, self.capacity);
        }
        queue.write_buffer(
            &self.view,
            0,
            bytemuck::cast_slice(&[size.0, size.1, 0.0, 0.0]),
        );
        queue.write_buffer(&self.instances, 0, bytemuck::cast_slice(&self.scratch));
        let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
            label: Some("sprites"),
            color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                view: target,
                depth_slice: None,
                resolve_target: None,
                ops: wgpu::Operations {
                    load: wgpu::LoadOp::Clear(wgpu::Color {
                        r: clear[0],
                        g: clear[1],
                        b: clear[2],
                        a: 1.0,
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
            pass.set_pipeline(&self.pipeline);
            pass.set_bind_group(0, &self.bind, &[]);
            pass.set_vertex_buffer(0, self.instances.slice(..));
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
