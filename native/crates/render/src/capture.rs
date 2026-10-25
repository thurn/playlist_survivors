//! Offscreen stills: a frame drawn into a texture on a headless device and written to PNG (the
//! seed of C1's capture).
#![allow(clippy::default_trait_access)]

use std::path::Path;

use crate::atlas::Page;
use crate::post::{Frame, Post};

const FORMAT: wgpu::TextureFormat = wgpu::TextureFormat::Rgba8UnormSrgb;

/// A headless device drawing frames into an offscreen target, for timing the frame's work.
pub struct Offscreen {
    device: wgpu::Device,
    queue: wgpu::Queue,
    post: Post,
    view: wgpu::TextureView,
    size: (u32, u32),
}

impl Offscreen {
    pub fn new(pages: &[Page], width: u32, height: u32) -> Result<Offscreen, String> {
        let instance = wgpu::Instance::new(wgpu::InstanceDescriptor::new_without_display_handle());
        let adapter = pollster::block_on(instance.request_adapter(&Default::default()))
            .map_err(|e| e.to_string())?;
        let (device, queue) = pollster::block_on(adapter.request_device(&Default::default()))
            .map_err(|e| e.to_string())?;
        let post = Post::new(&device, &queue, FORMAT, pages)?;
        let view = device
            .create_texture(&wgpu::TextureDescriptor {
                label: Some("offscreen"),
                size: wgpu::Extent3d {
                    width,
                    height,
                    depth_or_array_layers: 1,
                },
                mip_level_count: 1,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format: FORMAT,
                usage: wgpu::TextureUsages::RENDER_ATTACHMENT,
                view_formats: &[],
            })
            .create_view(&Default::default());
        Ok(Offscreen {
            device,
            queue,
            post,
            view,
            size: (width, height),
        })
    }

    /// Encodes and submits `frame` (the CPU's share; the GPU runs on).
    pub fn draw(&mut self, frame: &Frame) {
        let mut encoder = self.device.create_command_encoder(&Default::default());
        let (w, h) = self.size;
        self.post.draw(
            &self.device,
            &self.queue,
            &mut encoder,
            &self.view,
            (w, h),
            (w as f32, h as f32),
            frame,
        );
        self.queue.submit([encoder.finish()]);
        // (Keep the GPU from falling ever further behind.)
        let _ = self.device.poll(wgpu::PollType::Poll);
    }
}

/// Draws `frame` into a `width` x `height` image, RGBA8 rows top first.
pub fn render(pages: &[Page], width: u32, height: u32, frame: &Frame) -> Result<Vec<u8>, String> {
    let instance = wgpu::Instance::new(wgpu::InstanceDescriptor::new_without_display_handle());
    let adapter = pollster::block_on(instance.request_adapter(&Default::default()))
        .map_err(|e| e.to_string())?;
    let (device, queue) = pollster::block_on(adapter.request_device(&Default::default()))
        .map_err(|e| e.to_string())?;
    let mut post = Post::new(&device, &queue, FORMAT, pages)?;
    let size = wgpu::Extent3d {
        width,
        height,
        depth_or_array_layers: 1,
    };
    let texture = device.create_texture(&wgpu::TextureDescriptor {
        label: Some("still"),
        size,
        mip_level_count: 1,
        sample_count: 1,
        dimension: wgpu::TextureDimension::D2,
        format: FORMAT,
        usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC,
        view_formats: &[],
    });
    let view = texture.create_view(&Default::default());
    // Rows padded to the copy alignment.
    let row = (4 * width).div_ceil(wgpu::COPY_BYTES_PER_ROW_ALIGNMENT)
        * wgpu::COPY_BYTES_PER_ROW_ALIGNMENT;
    let buffer = device.create_buffer(&wgpu::BufferDescriptor {
        label: Some("still readback"),
        size: u64::from(row * height),
        usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
        mapped_at_creation: false,
    });
    let mut encoder = device.create_command_encoder(&Default::default());
    post.draw(
        &device,
        &queue,
        &mut encoder,
        &view,
        (width, height),
        (width as f32, height as f32),
        frame,
    );
    encoder.copy_texture_to_buffer(
        wgpu::TexelCopyTextureInfo {
            texture: &texture,
            mip_level: 0,
            origin: wgpu::Origin3d::ZERO,
            aspect: wgpu::TextureAspect::All,
        },
        wgpu::TexelCopyBufferInfo {
            buffer: &buffer,
            layout: wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(row),
                rows_per_image: Some(height),
            },
        },
        size,
    );
    queue.submit([encoder.finish()]);
    let slice = buffer.slice(..);
    let (tx, rx) = std::sync::mpsc::channel();
    slice.map_async(wgpu::MapMode::Read, move |r| {
        let _ = tx.send(r);
    });
    device
        .poll(wgpu::PollType::wait_indefinitely())
        .map_err(|e| e.to_string())?;
    rx.recv()
        .map_err(|e| e.to_string())?
        .map_err(|e| e.to_string())?;
    let data = slice.get_mapped_range().map_err(|e| e.to_string())?;
    let mut out = Vec::with_capacity((4 * width * height) as usize);
    for y in 0..height {
        let start = (y * row) as usize;
        out.extend_from_slice(&data[start..start + 4 * width as usize]);
    }
    Ok(out)
}

/// Writes RGBA8 pixels to a PNG.
pub fn write_png(path: &Path, width: u32, height: u32, rgba: &[u8]) -> Result<(), String> {
    let file = std::fs::File::create(path).map_err(|e| format!("{}: {e}", path.display()))?;
    let mut enc = png::Encoder::new(std::io::BufWriter::new(file), width, height);
    enc.set_color(png::ColorType::Rgba);
    enc.set_depth(png::BitDepth::Eight);
    let mut w = enc.write_header().map_err(|e| e.to_string())?;
    w.write_image_data(rgba).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::atlas::{Atlases, Facing};
    use crate::post::{Grade, Light};
    use crate::sprites::Sprite;

    fn plain(world: &[Sprite]) -> Frame<'_> {
        Frame {
            clear: [0.0; 3],
            world,
            lights: &[],
            grade: Grade::default(),
            overlay: &[],
        }
    }

    #[test]
    fn a_sprite_lands_where_it_is_drawn() {
        let assets = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../public/assets");
        let a = Atlases::load(&assets).unwrap();
        let walk = a.animation("hero", "walk").unwrap();
        let hero = Sprite::anchored(
            walk.frame(Facing::Down, 0, false),
            32.0,
            62.0,
            (walk.anchor_x, walk.anchor_y),
            1.0,
        );
        let solid = Sprite::solid(80.0, 0.0, 16.0, 16.0, [1.0, 0.0, 0.0, 1.0]);
        let px = render(&a.pages, 96, 64, &plain(&[hero, solid])).unwrap();
        assert_eq!(px.len(), 96 * 64 * 4);
        let at = |x: usize, y: usize| &px[(y * 96 + x) * 4..(y * 96 + x) * 4 + 4];
        assert_eq!(at(88, 8), &[255, 0, 0, 255], "the solid square");
        // The hero's frame has opaque pixels inside its 64 x 64 box and none right of it.
        let opaque = (0..64)
            .flat_map(|y| (0..64).map(move |x| (x, y)))
            .filter(|&(x, y)| at(x, y)[0..3] != [0, 0, 0])
            .count();
        assert!(opaque > 200, "{opaque} hero pixels");
        assert_eq!(at(70, 40), &[0, 0, 0, 255]);
    }

    #[test]
    fn the_grade_lights_and_overlay_change_what_is_drawn() {
        let grey = [Sprite::solid(0.0, 0.0, 64.0, 64.0, [0.5, 0.5, 0.5, 1.0])];
        let at = |px: &[u8], x: usize, y: usize| px[(y * 64 + x) * 4];
        let base = render(&[], 64, 64, &plain(&grey)).unwrap();
        // Half the ambient light halves the (encoded) grey; a light brightens around its centre.
        let mut dim = plain(&grey);
        dim.grade.ambient = [0.5; 3];
        let dark = render(&[], 64, 64, &dim).unwrap();
        assert!((i32::from(at(&base, 32, 32)) - 2 * i32::from(at(&dark, 32, 32))).abs() <= 2);
        let lamp = [Light {
            x: 16.0,
            y: 16.0,
            radius: 12.0,
            color: [1.0; 3],
            strength: 1.0,
        }];
        dim.lights = &lamp;
        let lit = render(&[], 64, 64, &dim).unwrap();
        assert!(at(&lit, 16, 16) > at(&dark, 16, 16) + 40, "lit at the lamp");
        assert_eq!(at(&lit, 50, 50), at(&dark, 50, 50), "unlit far from it");
        // The vignette darkens the corners only; the overlay is drawn over it, ungraded.
        let mut vig = plain(&grey);
        vig.grade.vignette = 1.0;
        let red = [Sprite::solid(0.0, 0.0, 4.0, 4.0, [1.0, 0.0, 0.0, 1.0])];
        vig.overlay = &red;
        let v = render(&[], 64, 64, &vig).unwrap();
        assert_eq!(at(&v, 32, 32), at(&base, 32, 32));
        assert!(at(&v, 62, 62) < at(&base, 62, 62));
        assert_eq!(&v[0..4], &[255, 0, 0, 255]);
    }
}
