//! The slice's window: a wgpu surface drawn by the render crate's sprites and post passes.
#![allow(clippy::default_trait_access)]

use std::sync::Arc;

use render::atlas::Page;
use render::post::{Frame, Post};
pub use render::sprites::Sprite;
use winit::window::Window;

/// A solid rectangle in logical pixels (origin top left).
pub fn rect(x: f32, y: f32, w: f32, h: f32, color: [f32; 4]) -> Sprite {
    Sprite::solid(x, y, w, h, color)
}

/// A solid square of side `s` centred on (x, y).
pub fn square(x: f32, y: f32, s: f32, color: [f32; 4]) -> Sprite {
    Sprite::solid(x - s / 2.0, y - s / 2.0, s, s, color)
}

pub struct Gfx {
    pub window: Arc<Window>,
    surface: wgpu::Surface<'static>,
    device: wgpu::Device,
    queue: wgpu::Queue,
    config: wgpu::SurfaceConfiguration,
    post: Post,
}

impl Gfx {
    pub fn new(window: Arc<Window>, pages: &[Page]) -> Result<Gfx, String> {
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
        let post = Post::new(&device, &queue, config.format, pages)?;
        Ok(Gfx {
            window,
            surface,
            device,
            queue,
            config,
            post,
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

    /// Draws `frame`.
    pub fn draw(&mut self, frame: &Frame) {
        let (wgpu::CurrentSurfaceTexture::Success(texture)
        | wgpu::CurrentSurfaceTexture::Suboptimal(texture)) = self.surface.get_current_texture()
        else {
            self.surface.configure(&self.device, &self.config);
            return;
        };
        let size = self.logical_size();
        let view = texture.texture.create_view(&Default::default());
        let mut enc = self.device.create_command_encoder(&Default::default());
        self.post.draw(
            &self.device,
            &self.queue,
            &mut enc,
            &view,
            (self.config.width, self.config.height),
            size,
            frame,
        );
        self.queue.submit([enc.finish()]);
        self.window.pre_present_notify();
        self.queue.present(texture);
    }
}
