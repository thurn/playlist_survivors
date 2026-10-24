//! The slice's game rules: a player who moves freely, enemies that step toward them on each
//! committed beat, and an attack that only lands on a beat. All times are host seconds of when
//! things are heard; the arena is a fixed 960 x 640 field.

use super::gfx::Rect;

pub const WIDTH: f32 = 960.0;
pub const HEIGHT: f32 = 640.0;
/// An attack this close to a committed beat's heard time is on the beat.
pub const ON_BEAT_S: f64 = 0.09;
const PLAYER_SPEED: f32 = 280.0;
const PLAYER_SIZE: f32 = 26.0;
const ENEMY_SIZE: f32 = 22.0;
const STEP: f32 = 46.0;
/// How long a step's slide takes.
const STEP_S: f64 = 0.09;
const ATTACK_RADIUS: f32 = 150.0;
pub const HEALTH: u32 = 5;
/// After a hit the player can't be hurt again for this long.
const INVULNERABLE_S: f64 = 0.8;

/// Why a run ended.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum End {
    Died,
    /// The audio stopped.
    Silence,
}

/// The movement keys held.
#[allow(clippy::struct_excessive_bools)]
#[derive(Clone, Copy, Debug, Default)]
pub struct Input {
    pub left: bool,
    pub right: bool,
    pub up: bool,
    pub down: bool,
}

#[derive(Clone, Copy, Debug)]
pub struct Enemy {
    pub x: f32,
    pub y: f32,
    /// Where the current step started, and when.
    from: (f32, f32),
    stepped_at: f64,
}

impl Enemy {
    /// The drawn position: sliding from the last step's start.
    fn shown(&self, now: f64) -> (f32, f32) {
        let k = (((now - self.stepped_at) / STEP_S).clamp(0.0, 1.0)) as f32;
        let k = 1.0 - (1.0 - k) * (1.0 - k);
        (
            self.from.0 + (self.x - self.from.0) * k,
            self.from.1 + (self.y - self.from.1) * k,
        )
    }
}

/// How an attack was judged.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Judgement {
    /// On the beat: how far off (s), and how many enemies it hit.
    Hit { off: f64, killed: usize },
    /// Off the beat (or no beat known near it).
    Miss,
}

pub struct Game {
    pub x: f32,
    pub y: f32,
    pub enemies: Vec<Enemy>,
    pub score: u64,
    pub combo: u32,
    /// Committed beats' heard times, ascending, not yet stepped on.
    pending: Vec<f64>,
    /// Heard times of recent beats already stepped on (for judging late presses).
    recent: Vec<f64>,
    beats_seen: u64,
    rng: u64,
    last_attack: Option<(f64, Judgement)>,
    last_beat: f64,
    pub health: u32,
    hurt_at: f64,
    /// When and why the run ended.
    pub over: Option<(f64, End)>,
}

impl Default for Game {
    fn default() -> Self {
        Game::new(0x9E37_79B9_7F4A_7C15)
    }
}

impl Game {
    pub fn new(seed: u64) -> Game {
        Game {
            x: WIDTH / 2.0,
            y: HEIGHT / 2.0,
            enemies: Vec::new(),
            score: 0,
            combo: 0,
            pending: Vec::new(),
            recent: Vec::new(),
            beats_seen: 0,
            rng: seed | 1,
            last_attack: None,
            last_beat: f64::NEG_INFINITY,
            health: HEALTH,
            hurt_at: f64::NEG_INFINITY,
            over: None,
        }
    }

    fn random(&mut self) -> f32 {
        self.rng ^= self.rng << 13;
        self.rng ^= self.rng >> 7;
        self.rng ^= self.rng << 17;
        (self.rng >> 40) as f32 / (1u64 << 24) as f32
    }

    /// A committed beat, heard at `heard`.
    pub fn beat(&mut self, heard: f64) {
        let at = self.pending.partition_point(|&b| b < heard);
        self.pending.insert(at, heard);
    }

    /// Advances to `now`, moving the player by `input` over `dt` seconds; every beat heard by
    /// `now` steps the enemies and may spawn more.
    pub fn update(&mut self, now: f64, dt: f32, input: Input) {
        if self.over.is_some() {
            return;
        }
        let dx = f32::from(i8::from(input.right) - i8::from(input.left));
        let dy = f32::from(i8::from(input.down) - i8::from(input.up));
        let len = (dx * dx + dy * dy).sqrt().max(1.0);
        self.x = (self.x + dx / len * PLAYER_SPEED * dt).clamp(PLAYER_SIZE, WIDTH - PLAYER_SIZE);
        self.y = (self.y + dy / len * PLAYER_SPEED * dt).clamp(PLAYER_SIZE, HEIGHT - PLAYER_SIZE);
        while self.pending.first().is_some_and(|&b| b <= now) {
            let b = self.pending.remove(0);
            self.step(b);
            self.recent.push(b);
        }
        self.recent.retain(|&b| b > now - 1.0);
        self.contact(now);
    }

    /// Enemies touching the player cost a health each (then vanish), unless just hurt.
    fn contact(&mut self, now: f64) {
        let reach = f32::midpoint(PLAYER_SIZE, ENEMY_SIZE);
        let (px, py) = (self.x, self.y);
        let touching = |e: &Enemy| (e.x - px).abs() < reach && (e.y - py).abs() < reach;
        if !self.enemies.iter().any(touching) {
            return;
        }
        self.enemies.retain(|e| !touching(e));
        if now - self.hurt_at < INVULNERABLE_S {
            return;
        }
        self.hurt_at = now;
        self.combo = 0;
        self.health = self.health.saturating_sub(1);
        if self.health == 0 {
            self.over = Some((now, End::Died));
        }
    }

    /// Ends the run (the audio stopped).
    pub fn end(&mut self, now: f64, why: End) {
        if self.over.is_none() {
            self.over = Some((now, why));
        }
    }

    /// Whether the player was hurt since `since`.
    pub fn hurt_since(&self, since: f64) -> bool {
        self.hurt_at > since
    }

    fn step(&mut self, at: f64) {
        self.beats_seen += 1;
        self.last_beat = at;
        let (px, py) = (self.x, self.y);
        for e in &mut self.enemies {
            e.from = (e.x, e.y);
            e.stepped_at = at;
            let (dx, dy) = (px - e.x, py - e.y);
            let d = (dx * dx + dy * dy).sqrt();
            let s = STEP.min(d);
            if d > 0.0 {
                e.x += dx / d * s;
                e.y += dy / d * s;
            }
        }
        // More enemies as the run goes on: one every other beat at first, then every beat, then two.
        let n = match self.beats_seen {
            0..=16 => usize::from(self.beats_seen.is_multiple_of(2)),
            17..=64 => 1,
            _ => 2,
        };
        for _ in 0..n {
            self.spawn(at);
        }
    }

    fn spawn(&mut self, at: f64) {
        let t = self.random();
        let (x, y) = match (self.random() * 4.0) as u32 {
            0 => (t * WIDTH, -ENEMY_SIZE),
            1 => (t * WIDTH, HEIGHT + ENEMY_SIZE),
            2 => (-ENEMY_SIZE, t * HEIGHT),
            _ => (WIDTH + ENEMY_SIZE, t * HEIGHT),
        };
        self.enemies.push(Enemy {
            x,
            y,
            from: (x, y),
            stepped_at: at,
        });
    }

    /// The attack key, pressed at `now`: judged against the nearest committed beat.
    pub fn attack(&mut self, now: f64) -> Judgement {
        if self.over.is_some() {
            return Judgement::Miss;
        }
        let nearest = self
            .recent
            .iter()
            .chain(&self.pending)
            .map(|&b| now - b)
            .min_by(|a, b| a.abs().total_cmp(&b.abs()));
        let j = match nearest {
            Some(off) if off.abs() <= ON_BEAT_S => {
                let (px, py) = (self.x, self.y);
                let before = self.enemies.len();
                self.enemies.retain(|e| {
                    let (dx, dy) = (e.x - px, e.y - py);
                    dx * dx + dy * dy > ATTACK_RADIUS * ATTACK_RADIUS
                });
                let killed = before - self.enemies.len();
                self.combo += 1;
                self.score += (10 + 50 * killed as u64) * u64::from(self.combo.min(16));
                Judgement::Hit { off, killed }
            }
            _ => {
                self.combo = 0;
                Judgement::Miss
            }
        };
        self.last_attack = Some((now, j));
        j
    }

    /// The scene at `now`, scaled into a `w` x `h` window.
    pub fn draw(&self, now: f64, w: f32, h: f32, out: &mut Vec<Rect>) {
        let scale = (w / WIDTH).min(h / HEIGHT);
        let (ox, oy) = ((w - WIDTH * scale) / 2.0, (h - HEIGHT * scale) / 2.0);
        let at = |x: f32, y: f32| (ox + x * scale, oy + y * scale);
        let (fx, fy) = at(0.0, 0.0);
        // The field pulses on each beat.
        let pulse = (1.0 - (now - self.last_beat) / 0.15).clamp(0.0, 1.0) as f32;
        out.push(Rect::new(
            fx,
            fy,
            WIDTH * scale,
            HEIGHT * scale,
            [
                0.08 + 0.10 * pulse,
                0.07 + 0.08 * pulse,
                0.12 + 0.05 * pulse,
                1.0,
            ],
        ));
        if let Some((t, j)) = self.last_attack {
            let age = (now - t) as f32;
            if age < 0.25 {
                let (cx, cy) = at(self.x, self.y);
                let color = match j {
                    Judgement::Hit { .. } => [1.0, 0.9, 0.4, 0.5 * (1.0 - age / 0.25)],
                    Judgement::Miss => [0.6, 0.6, 0.7, 0.3 * (1.0 - age / 0.25)],
                };
                let r = match j {
                    Judgement::Hit { .. } => ATTACK_RADIUS * (0.6 + 0.4 * age / 0.25),
                    Judgement::Miss => 30.0,
                };
                out.push(Rect::centered(cx, cy, 2.0 * r * scale, color));
            }
        }
        for e in &self.enemies {
            let (x, y) = e.shown(now);
            let (sx, sy) = at(x, y);
            out.push(Rect::centered(
                sx,
                sy,
                ENEMY_SIZE * scale,
                [0.9, 0.25, 0.3, 1.0],
            ));
        }
        let (px, py) = at(self.x, self.y);
        let hurt = now - self.hurt_at < INVULNERABLE_S;
        let blink = hurt && ((now - self.hurt_at) * 12.0) as i64 % 2 == 0;
        if !blink {
            out.push(Rect::centered(
                px,
                py,
                PLAYER_SIZE * scale,
                [0.4, 0.85, 1.0, 1.0],
            ));
        }
        if hurt {
            let a = (1.0 - (now - self.hurt_at) / INVULNERABLE_S) as f32;
            out.push(Rect::new(
                fx,
                fy,
                WIDTH * scale,
                HEIGHT * scale,
                [0.8, 0.1, 0.1, 0.25 * a],
            ));
        }
        for i in 0..HEALTH {
            let (hx, hy) = at(24.0 + i as f32 * 22.0, 24.0);
            let color = if i < self.health {
                [0.95, 0.3, 0.4, 1.0]
            } else {
                [0.3, 0.2, 0.25, 1.0]
            };
            out.push(Rect::centered(hx, hy, 16.0 * scale, color));
        }
        let (sx, sy) = at(WIDTH - 20.0, 12.0);
        digits(self.score, sx, sy, 24.0 * scale, [1.0, 1.0, 1.0, 0.9], out);
        // The next beats approach a marker at the top: press on the marker.
        let (mx, my) = at(WIDTH * 0.5, 22.0);
        out.push(Rect::new(
            mx - 2.0,
            my - 14.0,
            4.0,
            28.0,
            [1.0, 1.0, 1.0, 0.9],
        ));
        for &b in self.recent.iter().chain(&self.pending) {
            let dx = (b - now) as f32 * 300.0 * scale;
            if dx.abs() < WIDTH * scale * 0.5 {
                let a = if dx < 0.0 { 0.3 } else { 1.0 };
                out.push(Rect::centered(
                    mx + dx,
                    my,
                    12.0 * scale,
                    [1.0, 0.8, 0.3, a],
                ));
                out.push(Rect::centered(
                    mx - dx,
                    my,
                    12.0 * scale,
                    [1.0, 0.8, 0.3, a],
                ));
            }
        }
        // Combo pips.
        for i in 0..self.combo.min(16) {
            let (cx, cy) = at(20.0 + i as f32 * 14.0, HEIGHT - 20.0);
            out.push(Rect::centered(cx, cy, 10.0 * scale, [1.0, 0.9, 0.4, 1.0]));
        }
        if let Some((t, why)) = self.over {
            let a = ((now - t) / 0.5).clamp(0.0, 1.0) as f32;
            out.push(Rect::new(
                fx,
                fy,
                WIDTH * scale,
                HEIGHT * scale,
                [0.0, 0.0, 0.0, 0.7 * a],
            ));
            let color = match why {
                End::Died => [0.95, 0.3, 0.4, a],
                End::Silence => [0.6, 0.7, 0.9, a],
            };
            let (cx, cy) = at(WIDTH / 2.0, HEIGHT / 2.0 - 40.0);
            out.push(Rect::centered(cx, cy - 70.0 * scale, 40.0 * scale, color));
            let size = 80.0 * scale;
            let width = digit_count(self.score) as f32 * size * 0.75;
            digits(
                self.score,
                cx + width / 2.0,
                cy,
                size,
                [1.0, 1.0, 1.0, a],
                out,
            );
        }
    }
}

fn digit_count(mut n: u64) -> usize {
    let mut k = 1;
    while n >= 10 {
        n /= 10;
        k += 1;
    }
    k
}

/// `n` in seven-segment digits of height `size`, right-aligned at `right`, top at `top`.
fn digits(n: u64, right: f32, top: f32, size: f32, color: [f32; 4], out: &mut Vec<Rect>) {
    // Segments a-g, as bits of each digit.
    const SEGMENTS: [u8; 10] = [
        0b011_1111, 0b000_0110, 0b101_1011, 0b100_1111, 0b110_0110, 0b110_1101, 0b111_1101,
        0b000_0111, 0b111_1111, 0b110_1111,
    ];
    let (w, t) = (size * 0.5, size * 0.1);
    let half = size / 2.0;
    let mut n = n;
    let mut x = right - w;
    loop {
        let seg = SEGMENTS[(n % 10) as usize];
        let bars = [
            (x, top, w, t),
            (x + w - t, top, t, half),
            (x + w - t, top + half, t, half),
            (x, top + size - t, w, t),
            (x, top + half, t, half),
            (x, top, t, half),
            (x, top + half - t / 2.0, w, t),
        ];
        for (i, &(bx, by, bw, bh)) in bars.iter().enumerate() {
            if seg & (1 << i) != 0 {
                out.push(Rect::new(bx, by, bw, bh, color));
            }
        }
        n /= 10;
        if n == 0 {
            break;
        }
        x -= size * 0.75;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn none() -> Input {
        Input::default()
    }

    #[test]
    fn enemies_step_toward_the_player_on_each_beat() {
        let mut g = Game::new(7);
        for k in 0..4 {
            g.beat(1.0 + f64::from(k) * 0.5);
        }
        g.update(0.9, 0.0, none());
        assert!(g.enemies.is_empty(), "nothing before the first beat");
        g.update(1.6, 0.0, none());
        assert_eq!(g.beats_seen, 2);
        let e = g.enemies[0];
        let d0 = ((e.x - g.x).powi(2) + (e.y - g.y).powi(2)).sqrt();
        g.update(2.1, 0.0, none());
        let e = g.enemies[0];
        let d1 = ((e.x - g.x).powi(2) + (e.y - g.y).powi(2)).sqrt();
        assert!((d0 - d1 - STEP).abs() < 1e-3, "{d0} then {d1}");
    }

    #[test]
    fn an_attack_lands_only_on_a_beat() {
        let mut g = Game::new(7);
        g.beat(10.0);
        g.enemies.push(Enemy {
            x: g.x + 50.0,
            y: g.y,
            from: (0.0, 0.0),
            stepped_at: 0.0,
        });
        assert_eq!(g.attack(9.8), Judgement::Miss);
        assert_eq!(g.enemies.len(), 1);
        // Early by 80 ms (the beat still pending), then late after it has stepped.
        assert!(matches!(g.attack(9.92), Judgement::Hit { killed: 1, .. }));
        g.update(10.05, 0.0, none());
        assert!(matches!(g.attack(10.08), Judgement::Hit { .. }));
        assert_eq!(g.combo, 2);
        assert_eq!(g.attack(10.2), Judgement::Miss);
        assert_eq!(g.combo, 0);
    }

    #[test]
    fn contact_costs_health_until_the_run_ends() {
        let mut g = Game::new(3);
        for k in 0..HEALTH {
            g.enemies.push(Enemy {
                x: g.x + 5.0,
                y: g.y,
                from: (0.0, 0.0),
                stepped_at: 0.0,
            });
            g.update(f64::from(k), 0.0, none());
            assert_eq!(g.health, HEALTH - k - 1);
            assert!(g.enemies.is_empty());
        }
        assert_eq!(g.over, Some((f64::from(HEALTH - 1), End::Died)));
        assert_eq!(g.attack(10.0), Judgement::Miss);
    }

    #[test]
    fn a_second_touch_right_after_a_hit_is_free() {
        let mut g = Game::new(3);
        for t in [1.0, 1.3] {
            g.enemies.push(Enemy {
                x: g.x,
                y: g.y + 5.0,
                from: (0.0, 0.0),
                stepped_at: 0.0,
            });
            g.update(t, 0.0, none());
        }
        assert_eq!(g.health, HEALTH - 1);
    }

    #[test]
    fn scores_draw_one_digit_each() {
        let mut out = Vec::new();
        digits(1088, 100.0, 0.0, 20.0, [1.0; 4], &mut out);
        // 1 (2 bars), 0 (6), 8 (7), 8 (7).
        assert_eq!(out.len(), 22);
        assert_eq!(digit_count(1088), 4);
        assert_eq!(digit_count(0), 1);
    }

    #[test]
    fn the_player_stays_in_the_field() {
        let mut g = Game::new(1);
        let left = Input {
            left: true,
            up: true,
            ..Input::default()
        };
        for _ in 0..1000 {
            g.update(0.0, 0.1, left);
        }
        assert_eq!((g.x, g.y), (PLAYER_SIZE, PLAYER_SIZE));
    }
}
