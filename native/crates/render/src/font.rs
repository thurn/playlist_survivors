//! The TS game's hand-made 5 x 7 pixel font (`src/runtime/font.ts`), drawn as one solid sprite
//! per lit pixel, scaled by whole pixels so it stays crisp, with a dark shadow behind.

use crate::sprites::Sprite;

pub const GLYPH_W: usize = 5;
pub const GLYPH_H: usize = 7;
/// The advance per character, in font pixels.
const ADV: usize = 6;

/// Each glyph's rows, top first, `#` lit.
const GLYPHS: &[(char, [&str; 7])] = &[
    (
        'A',
        [
            ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#",
        ],
    ),
    (
        'B',
        [
            "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####.",
        ],
    ),
    (
        'C',
        [
            ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###.",
        ],
    ),
    (
        'D',
        [
            "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####.",
        ],
    ),
    (
        'E',
        [
            "#####", "#....", "#....", "####.", "#....", "#....", "#####",
        ],
    ),
    (
        'F',
        [
            "#####", "#....", "#....", "####.", "#....", "#....", "#....",
        ],
    ),
    (
        'G',
        [
            ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####",
        ],
    ),
    (
        'H',
        [
            "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#",
        ],
    ),
    (
        'I',
        [
            ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###.",
        ],
    ),
    (
        'J',
        [
            "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##..",
        ],
    ),
    (
        'K',
        [
            "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#",
        ],
    ),
    (
        'L',
        [
            "#....", "#....", "#....", "#....", "#....", "#....", "#####",
        ],
    ),
    (
        'M',
        [
            "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#",
        ],
    ),
    (
        'N',
        [
            "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#",
        ],
    ),
    (
        'O',
        [
            ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.",
        ],
    ),
    (
        'P',
        [
            "####.", "#...#", "#...#", "####.", "#....", "#....", "#....",
        ],
    ),
    (
        'Q',
        [
            ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#",
        ],
    ),
    (
        'R',
        [
            "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#",
        ],
    ),
    (
        'S',
        [
            ".####", "#....", "#....", ".###.", "....#", "....#", "####.",
        ],
    ),
    (
        'T',
        [
            "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#..",
        ],
    ),
    (
        'U',
        [
            "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.",
        ],
    ),
    (
        'V',
        [
            "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#..",
        ],
    ),
    (
        'W',
        [
            "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#.",
        ],
    ),
    (
        'X',
        [
            "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#",
        ],
    ),
    (
        'Y',
        [
            "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#..",
        ],
    ),
    (
        'Z',
        [
            "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####",
        ],
    ),
    (
        '0',
        [
            ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###.",
        ],
    ),
    (
        '1',
        [
            "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###.",
        ],
    ),
    (
        '2',
        [
            ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####",
        ],
    ),
    (
        '3',
        [
            "####.", "....#", "....#", ".###.", "....#", "....#", "####.",
        ],
    ),
    (
        '4',
        [
            "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#.",
        ],
    ),
    (
        '5',
        [
            "#####", "#....", "####.", "....#", "....#", "#...#", ".###.",
        ],
    ),
    (
        '6',
        [
            ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###.",
        ],
    ),
    (
        '7',
        [
            "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#...",
        ],
    ),
    (
        '8',
        [
            ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###.",
        ],
    ),
    (
        '9',
        [
            ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###.",
        ],
    ),
    (
        '.',
        [
            ".....", ".....", ".....", ".....", ".....", ".##..", ".##..",
        ],
    ),
    (
        ',',
        [
            ".....", ".....", ".....", ".....", ".##..", "..#..", ".#...",
        ],
    ),
    (
        ':',
        [
            ".....", ".##..", ".##..", ".....", ".##..", ".##..", ".....",
        ],
    ),
    (
        '!',
        [
            "..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#..",
        ],
    ),
    (
        '?',
        [
            ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#..",
        ],
    ),
    (
        '-',
        [
            ".....", ".....", ".....", "#####", ".....", ".....", ".....",
        ],
    ),
    (
        '—',
        [
            ".....", ".....", ".....", "#####", ".....", ".....", ".....",
        ],
    ),
    (
        '>',
        [
            "#....", ".#...", "..#..", "...#.", "..#..", ".#...", "#....",
        ],
    ),
    (
        '<',
        [
            "....#", "...#.", "..#..", ".#...", "..#..", "...#.", "....#",
        ],
    ),
    (
        '+',
        [
            ".....", "..#..", "..#..", "#####", "..#..", "..#..", ".....",
        ],
    ),
    (
        '%',
        [
            "##...", "##..#", "...#.", "..#..", ".#...", "#..##", "...##",
        ],
    ),
    (
        '/',
        [
            "....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#....",
        ],
    ),
    (
        '\'',
        [
            "..#..", "..#..", ".#...", ".....", ".....", ".....", ".....",
        ],
    ),
    (
        '(',
        [
            "...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#.",
        ],
    ),
    (
        ')',
        [
            ".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#...",
        ],
    ),
    (
        '×',
        [
            ".....", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", ".....",
        ],
    ),
    (
        '&',
        [
            ".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#",
        ],
    ),
    (
        '©',
        [
            ".###.", "#...#", "#.#.#", "##..#", "#.#.#", "#...#", ".###.",
        ],
    ),
    (
        ' ',
        [
            ".....", ".....", ".....", ".....", ".....", ".....", ".....",
        ],
    ),
];

/// Characters drawn as others (accents dropped, curly quotes straightened).
fn alias(c: char) -> char {
    match c {
        'É' | 'é' | 'è' | 'ê' => 'E',
        'à' => 'A',
        'ö' => 'O',
        'ü' => 'U',
        'ñ' => 'N',
        '–' => '-',
        '’' | '‘' | '“' | '”' | '"' => '\'',
        c => c.to_ascii_uppercase(),
    }
}

fn glyph(c: char) -> &'static [&'static str; 7] {
    let c = alias(c);
    GLYPHS
        .iter()
        .find(|(k, _)| *k == c)
        .or_else(|| GLYPHS.iter().find(|(k, _)| *k == '?'))
        .map(|(_, rows)| rows)
        .expect("the font has '?'")
}

/// A line's width in screen pixels at `scale` screen pixels per font pixel.
pub fn width(text: &str, scale: f32) -> f32 {
    (text.chars().count() * ADV).saturating_sub(1) as f32 * scale
}

/// Where a line sits against its x.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Align {
    Left,
    Centre,
    Right,
}

/// `text` at (x, y) (its top), `scale` screen pixels per font pixel (whole pixels keep it crisp),
/// in `color`, over a shadow one pixel down and right.
pub fn text(
    text: &str,
    x: f32,
    y: f32,
    scale: f32,
    align: Align,
    color: [f32; 4],
    out: &mut Vec<Sprite>,
) {
    let w = width(text, scale);
    let x0 = match align {
        Align::Left => x,
        Align::Centre => x - w / 2.0,
        Align::Right => x - w,
    };
    let shadow = [0.0, 0.0, 0.0, 0.6 * color[3]];
    for (dx, tint) in [(scale, shadow), (0.0, color)] {
        for (i, c) in text.chars().enumerate() {
            let rows = glyph(c);
            let gx = x0 + (i * ADV) as f32 * scale + dx;
            for (r, row) in rows.iter().enumerate() {
                // (Runs of lit pixels as one sprite each.)
                let bytes = row.as_bytes();
                let mut k = 0;
                while k < GLYPH_W {
                    if bytes[k] == b'#' {
                        let start = k;
                        while k < GLYPH_W && bytes[k] == b'#' {
                            k += 1;
                        }
                        out.push(Sprite::solid(
                            gx + start as f32 * scale,
                            y + r as f32 * scale + dx,
                            (k - start) as f32 * scale,
                            scale,
                            tint,
                        ));
                    } else {
                        k += 1;
                    }
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_glyph_is_five_by_seven() {
        assert!(GLYPHS.len() > 50);
        for (c, rows) in GLYPHS {
            assert!(
                rows.iter()
                    .all(|r| r.len() == GLYPH_W && r.bytes().all(|b| b == b'.' || b == b'#')),
                "{c:?}"
            );
        }
    }

    #[test]
    fn text_is_runs_of_pixels_aligned_as_asked() {
        let mut out = Vec::new();
        text("HI", 100.0, 0.0, 2.0, Align::Right, [1.0; 4], &mut out);
        assert_eq!(width("HI", 2.0), 22.0);
        // H: 2 + 2 + 1 + 2 + 2 + 2 + 2 runs (the bar row is one run); I: 7 runs; with shadows.
        let lit: Vec<&Sprite> = out.iter().filter(|s| s.tint == [1.0; 4]).collect();
        assert_eq!(lit.len(), 13 + 7);
        assert!(lit.iter().all(|s| s.x >= 78.0 && s.x + s.w <= 100.0 + 1e-3));
        assert_eq!(glyph('é'), glyph('E'));
        assert_ne!(glyph(':'), glyph('?'), "the clock's colon");
        assert_eq!(glyph('§'), glyph('?'));
    }
}

/// One trimmed glyph, relative to a text baseline, from the licensed bitmap atlas.
#[derive(Clone, Debug, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BitmapGlyph {
    pub rect: crate::atlas::Region,
    pub advance: f32,
    pub bearing_x: f32,
    pub bearing_y: f32,
}
#[derive(Clone, Debug, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BitmapFont {
    pub line_height: f32,
    pub ascent: f32,
    pub glyphs: std::collections::HashMap<char, BitmapGlyph>,
}
impl BitmapFont {
    fn glyph(&self, c: char) -> &BitmapGlyph {
        let c = match c {
            '×' => 'x',
            '–' | '—' => '-',
            '’' | '‘' => '\'',
            '“' | '”' => '"',
            c => c,
        };
        self.glyphs
            .get(&c)
            .or_else(|| self.glyphs.get(&'?'))
            .expect("validated bitmap font")
    }
    fn factor(&self, scale: f32) -> f32 {
        scale * 7.0 / self.glyph('M').rect.h as f32
    }
    /// Uses the HUD's existing font-pixel scale: capitals are 7*scale high.
    pub fn width(&self, text: &str, scale: f32) -> f32 {
        text.chars().map(|c| self.glyph(c).advance).sum::<f32>() * self.factor(scale)
    }
    #[allow(clippy::too_many_arguments)]
    pub fn text(
        &self,
        text: &str,
        x: f32,
        y: f32,
        scale: f32,
        align: Align,
        color: [f32; 4],
        out: &mut Vec<Sprite>,
    ) {
        let factor = self.factor(scale);
        let width = self.width(text, scale);
        let x = match align {
            Align::Left => x,
            Align::Centre => x - width / 2.0,
            Align::Right => x - width,
        };
        let baseline = y - self.glyph('M').bearing_y * factor;
        for (shift, tint) in [(scale, [0.0, 0.0, 0.0, 0.7 * color[3]]), (0.0, color)] {
            let mut pen = x;
            for c in text.chars() {
                let g = self.glyph(c);
                if c != ' ' {
                    out.push(
                        Sprite::anchored(
                            g.rect,
                            pen + g.bearing_x * factor + shift,
                            baseline + g.bearing_y * factor + shift,
                            (0.0, 0.0),
                            factor,
                        )
                        .tinted(tint),
                    );
                }
                pen += g.advance * factor;
            }
        }
    }
}

#[cfg(test)]
mod bitmap_tests {
    use super::*;
    #[test]
    fn bitmap_text_uses_baselines_alignment_and_fallback() {
        let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../public/assets");
        let art = crate::atlas::Atlases::load(&root).unwrap();
        let face = &art.fonts["pixel"];
        let mut out = Vec::new();
        face.text("M1?", 100.0, 20.0, 2.0, Align::Right, [1.0; 4], &mut out);
        let lit: Vec<_> = out.iter().filter(|s| s.tint == [1.0; 4]).collect();
        assert_eq!(lit.len(), 3);
        assert!((lit[0].y - 20.0).abs() < 0.01);
        assert!(lit.iter().all(|s| s.region.page == 4));
        assert_eq!(face.width("§", 2.0), face.width("?", 2.0));
        assert!(lit.iter().all(|s| s.x + s.w <= 100.0 + 0.01));
    }
}
