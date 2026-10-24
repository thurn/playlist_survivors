//! Flat rectangles through wgpu: the slice's whole renderer.
#![allow(clippy::default_trait_access)]

use std::sync::Arc;

use winit::window::Window;

/// A rectangle in logical pixels (origin top left), with a colour.
#[repr(C)]
#[derive(Clone, Copy, Debug, bytemuck::Pod, bytemuck::Zeroable)]
pub struct Rect {
    pub x: f32,
    pub y: f32,
    pub w: f32,
    pub h: f32,
    pub color: [f32; 4],
}

impl Rect {
    pub fn new(x: f32, y: f32, w: f32, h: f32, color: [f32; 4]) -> Rect {
        Rect { x, y, w, h, color }
    }

    /// A square of side `s` centred on (x, y).
    pub fn centered(x: f32, y: f32, s: f32, color: [f32; 4]) -> Rect {
        Rect::new(x - s / 2.0, y - s / 2.0, s, s, color)
    }
}

const SHADER: &str = r"
struct View { size: vec2<f32> };
@group(0) @binding(0) var<uniform> view: View;
struct Out { @builtin(position) pos: vec4<f32>, @location(0) color: vec4<f32> };
@vertex
fn vs(@builtin(vertex_index) v: u32, @location(0) rect: vec4<f32>, @location(1) color: vec4<f32>) -> Out {
    let corner = vec2<f32>(f32(v & 1u), f32((v >> 1u) & 1u));
    let p = rect.xy + corner * rect.zw;
    var o: Out;
    o.pos = vec4<f32>(p.x / view.size.x * 2.0 - 1.0, 1.0 - p.y / view.size.y * 2.0, 0.0, 1.0);
    o.color = color;
    return o;
}
@fragment
fn fs(i: Out) -> @location(0) vec4<f32> { return i.color; }
";

pub struct Gfx {
    pub window: Arc<Window>,
    surface: wgpu::Surface<'static>,
    device: wgpu::Device,
    queue: wgpu::Queue,
    config: wgpu::SurfaceConfiguration,
    pipeline: wgpu::RenderPipeline,
    view: wgpu::Buffer,
    bind: wgpu::BindGroup,
    rects: wgpu::Buffer,
    capacity: usize,
}

impl Gfx {
    pub fn new(window: Arc<Window>) -> Result<Gfx, String> {
        let instance = wgpu::Instance::new(wgpu::InstanceDescriptor::new_without_display_handle());
        let surface = instance
            .create_surface(window.clone())
            .map_err(|e| e.to_string())?;
        let adapter = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
            compatible_surface: Some(&surface),
            ..Default::default()
        }))
        .map_err(|e| e.to_string())?;
        let (device, queue) = pollster::block_on(adapter.request_device(&Default::default()))
            .map_err(|e| e.to_string())?;
        let size = window.inner_size();
        let mut config = surface
            .get_default_config(&adapter, size.width.max(1), size.height.max(1))
            .ok_or("no surface configuration")?;
        config.present_mode = wgpu::PresentMode::Fifo;
        config.desired_maximum_frame_latency = 1;
        surface.configure(&device, &config);
        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("rects"),
            source: wgpu::ShaderSource::Wgsl(SHADER.into()),
        });
        let view = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("view"),
            size: 16,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: None,
            entries: &[wgpu::BindGroupLayoutEntry {
                binding: 0,
                visibility: wgpu::ShaderStages::VERTEX,
                ty: wgpu::BindingType::Buffer {
                    ty: wgpu::BufferBindingType::Uniform,
                    has_dynamic_offset: false,
                    min_binding_size: None,
                },
                count: None,
            }],
        });
        let bind = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: None,
            layout: &layout,
            entries: &[wgpu::BindGroupEntry {
                binding: 0,
                resource: view.as_entire_binding(),
            }],
        });
        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: None,
            bind_group_layouts: &[Some(&layout)],
            immediate_size: 0,
        });
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("rects"),
            layout: Some(&pipeline_layout),
            vertex: wgpu::VertexState {
                module: &module,
                entry_point: Some("vs"),
                compilation_options: Default::default(),
                buffers: &[Some(wgpu::VertexBufferLayout {
                    array_stride: size_of::<Rect>() as u64,
                    step_mode: wgpu::VertexStepMode::Instance,
                    attributes: &wgpu::vertex_attr_array![0 => Float32x4, 1 => Float32x4],
                })],
            },
            fragment: Some(wgpu::FragmentState {
                module: &module,
                entry_point: Some("fs"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState {
                    format: config.format,
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
        let capacity = 1024;
        let rects = Gfx::rect_buffer(&device, capacity);
        Ok(Gfx {
            window,
            surface,
            device,
            queue,
            config,
            pipeline,
            view,
            bind,
            rects,
            capacity,
        })
    }

    fn rect_buffer(device: &wgpu::Device, capacity: usize) -> wgpu::Buffer {
        device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("rects"),
            size: (capacity * size_of::<Rect>()) as u64,
            usage: wgpu::BufferUsages::VERTEX | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        })
    }

    pub fn resize(&mut self, width: u32, height: u32) {
        self.config.width = width.max(1);
        self.config.height = height.max(1);
        self.surface.configure(&self.device, &self.config);
    }

    /// The window's size in logical pixels.
    pub fn logical_size(&self) -> (f32, f32) {
        let s = self
            .window
            .inner_size()
            .to_logical::<f32>(self.window.scale_factor());
        (s.width, s.height)
    }

    /// Clears to `clear` and draws `rects` in order.
    pub fn draw(&mut self, clear: [f64; 3], rects: &[Rect]) {
        let (wgpu::CurrentSurfaceTexture::Success(frame)
        | wgpu::CurrentSurfaceTexture::Suboptimal(frame)) = self.surface.get_current_texture()
        else {
            self.surface.configure(&self.device, &self.config);
            return;
        };
        if rects.len() > self.capacity {
            self.capacity = rects.len().next_power_of_two();
            self.rects = Gfx::rect_buffer(&self.device, self.capacity);
        }
        let (w, h) = self.logical_size();
        self.queue
            .write_buffer(&self.view, 0, bytemuck::cast_slice(&[w, h, 0.0, 0.0]));
        self.queue
            .write_buffer(&self.rects, 0, bytemuck::cast_slice(rects));
        let view = frame.texture.create_view(&Default::default());
        let mut enc = self.device.create_command_encoder(&Default::default());
        {
            let mut pass = enc.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: None,
                color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                    view: &view,
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
            if !rects.is_empty() {
                pass.set_pipeline(&self.pipeline);
                pass.set_bind_group(0, &self.bind, &[]);
                pass.set_vertex_buffer(0, self.rects.slice(..));
                pass.draw(0..4, 0..rects.len() as u32);
            }
        }
        self.queue.submit([enc.finish()]);
        self.window.pre_present_notify();
        self.queue.present(frame);
    }
}
