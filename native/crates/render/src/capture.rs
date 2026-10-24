//! Offscreen stills: sprites drawn into a texture on a headless device and written to PNG (the
//! seed of C1's capture).
#![allow(clippy::default_trait_access)]

use std::path::Path;

use crate::atlas::Page;
use crate::sprites::{Sprite, SpriteRenderer};

const FORMAT: wgpu::TextureFormat = wgpu::TextureFormat::Rgba8UnormSrgb;

/// Draws `sprites` over `clear` into a `width` x `height` image, RGBA8 rows top first.
pub fn render(
    pages: &[Page],
    width: u32,
    height: u32,
    clear: [f64; 3],
    sprites: &[Sprite],
) -> Result<Vec<u8>, String> {
    let instance = wgpu::Instance::new(wgpu::InstanceDescriptor::new_without_display_handle());
    let adapter = pollster::block_on(instance.request_adapter(&Default::default()))
        .map_err(|e| e.to_string())?;
    let (device, queue) = pollster::block_on(adapter.request_device(&Default::default()))
        .map_err(|e| e.to_string())?;
    let mut renderer = SpriteRenderer::new(&device, &queue, FORMAT, pages)?;
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
    renderer.draw(
        &device,
        &queue,
        &mut encoder,
        &view,
        (width as f32, height as f32),
        clear,
        sprites,
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
        let px = render(&a.pages, 96, 64, [0.0, 0.0, 0.0], &[hero, solid]).unwrap();
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
}
