//! Offer presentation and UI input mapping; the sim owns picks, reveals and resuming.
use super::{hud::item_color, sounds::Cue};
use render::{atlas::Atlases, font::Align, post::Grade, sprites::Sprite};
use sim::movement::InputKey;
use sim::survivors::{Inventory, Item, Offer, RunState, weapon_catalog};
pub mod check;
pub mod passives_check;
const FLIP: f64 = 0.2;
const EXIT: f64 = 0.42;
#[derive(Clone, Copy)]
struct Active {
    offer: Offer,
    inv: Inventory,
    selected: usize,
    song: f64,
    host: f64,
    opened: f64,
    highlight: f64,
    highlight_song: f64,
}
#[derive(Clone, Copy)]
struct Closing {
    active: Active,
    at: f64,
    song: f64,
}
#[derive(Default)]
pub struct Screen {
    active: Option<Active>,
    closing: Option<Closing>,
    // Include presses queued since the last frame when navigating again.
    input_selected: usize,
}
fn same(a: Offer, b: Offer) -> bool {
    a.level == b.level && a.reveals == b.reveals
}
impl Screen {
    pub fn update(&mut self, run: Option<RunState>, inv: Option<Inventory>, now: f64) {
        self.input_selected = run.map_or(0, |r| r.selected);
        let next = run.and_then(|r| r.offer);
        if self
            .closing
            .is_some_and(|c| next.is_some_and(|o| same(c.active.offer, o)))
        {
            self.closing = None;
        }
        if self
            .active
            .is_some_and(|a| next.is_none_or(|o| !same(a.offer, o)))
        {
            let old = self.active.take().expect("active offer");
            let inv = inv.unwrap_or_default();
            let applied = old.offer.cards.iter().position(|c| {
                c.before < c.after && c.item.rank(inv) >= c.after && c.item.rank(old.inv) < c.after
            });
            let confirmed = run.is_some_and(|r| {
                (r.confirmed && r.level >= old.offer.level)
                    || next.is_some_and(|o| o.level > old.offer.level)
            });
            if applied.is_some() || confirmed {
                self.closing = Some(Closing {
                    active: Active {
                        selected: applied.unwrap_or_else(|| {
                            run.map_or(old.selected, |r| {
                                if r.offer.is_none() {
                                    r.selected
                                } else {
                                    old.selected
                                }
                            })
                        }),
                        ..old
                    },
                    at: now,
                    song: run.map_or(old.song, |r| r.song),
                });
            } else {
                self.closing = None;
            } // A rollback is not a pick.
        }
        if let (Some(run), Some(offer)) = (run, next) {
            let a = self.active.get_or_insert(Active {
                offer,
                inv: inv.unwrap_or_default(),
                selected: run.selected,
                song: run.song,
                host: now,
                opened: now,
                highlight: now,
                highlight_song: run.song,
            });
            if a.selected != run.selected {
                a.highlight = now;
                a.highlight_song = run.song;
            }
            // A rollback can change eligibility/cards without changing this
            // level's reveal beats. Keep the animation identity, but always
            // show the sim's current offer and evolution-pair inventory.
            a.offer = offer;
            a.inv = inv.unwrap_or_default();
            a.selected = run.selected;
            a.song = run.song;
            a.host = now;
        }
        if self.closing.is_some_and(|c| now - c.at > EXIT) {
            self.closing = None;
        }
        if run.is_none() {
            self.active = None;
            self.closing = None;
        }
    }
    /// Translate horizontal navigation into the sim's recorded, absolute card keys.
    pub fn navigate(&mut self, right: bool) -> Option<InputKey> {
        self.active?;
        self.input_selected = if right {
            (self.input_selected + 1).min(2)
        } else {
            self.input_selected.saturating_sub(1)
        };
        Some(selection_key(self.input_selected))
    }
    /// Logical window coordinates, using the same card bounds and highlight scale as drawing.
    pub fn hit(&self, now: f64, width: f32, height: f32, x: f32, y: f32) -> Option<usize> {
        let a = self.active?;
        if a.song + now - a.host + 1e-9 < a.offer.reveals[2] {
            return None;
        }
        let u = (width / 960.0).min(height / 540.0);
        (0..3).find(|&i| {
            let scale = highlight_scale(i == a.selected, now - a.highlight);
            let cx = width / 2.0 + (i as f32 - 1.0) * 260.0 * u;
            let cy = height * 0.33 + 109.0 * u;
            (x - cx).abs() <= 122.0 * u * scale && (y - cy).abs() <= 113.0 * u * scale
        })
    }
    pub fn cues(&self, forecast: Option<RunState>, now: f64) -> Vec<Cue> {
        let mut offers = Vec::new();
        if let Some(a) = self.active {
            offers.push(a.offer);
        }
        if let Some(o) = forecast.and_then(|r| r.offer) {
            if offers
                .first()
                .is_some_and(|a| a.level == o.level && !same(*a, o))
            {
                offers.clear();
            }
            if !offers.iter().any(|a| same(*a, o)) {
                offers.push(o);
            }
        }
        let mut cues = Vec::new();
        for o in offers {
            let id = o.level as u32 * 8;
            cues.push(Cue {
                key: "offer-open",
                s: o.reveals[0],
                id,
                gain: 0.65,
            });
            for (i, s) in o.reveals.into_iter().enumerate() {
                cues.push(Cue {
                    key: "card-reveal",
                    s,
                    id: id + i as u32 + 1,
                    gain: 0.75,
                });
            }
        }
        if let Some(c) = self.closing {
            // Confirm cannot be known before the key. Leave output lookahead for this response.
            cues.push(Cue {
                key: "card-confirm",
                s: c.song + 0.1,
                id: c.active.offer.level as u32 * 8 + 4,
                gain: 0.8,
            });
        } else if let Some(a) = self.active
            && now - a.highlight < 0.2
            && a.highlight > a.opened + 1e-6
        {
            cues.push(Cue {
                key: "card-highlight",
                s: a.highlight_song + 0.1,
                id: a.offer.level as u32 * 8 + 5,
                gain: 0.4,
            });
        }
        cues
    }
    pub fn covers_hero(&self, now: f64) -> bool {
        self.active.is_some() || self.closing.is_some_and(|c| now - c.at < EXIT)
    }
    fn dim(&self, now: f64) -> f32 {
        let active = self
            .active
            .map_or(0.0, |a| ((now - a.opened) / 0.12).clamp(0.0, 1.0) as f32);
        let closing = self
            .closing
            .map_or(0.0, |c| (1.0 - (now - c.at) / EXIT).clamp(0.0, 1.0) as f32);
        active.max(closing)
    }
    pub fn grade(&self, now: f64, g: &mut Grade) {
        let dim = self.dim(now);
        g.saturation *= 1.0 - 0.38 * dim;
        g.bloom *= 1.0 - 0.2 * dim;
    }
    pub fn draw(
        &self,
        art: &Atlases,
        now: f64,
        width: f32,
        height: f32,
        hero: Option<(f32, f32)>,
        out: &mut Vec<Sprite>,
    ) {
        let dim = self.dim(now);
        if dim <= 0.0 {
            return;
        }
        let unit = (width / 960.0).min(height / 540.0);
        out.push(Sprite::solid(
            0.0,
            0.0,
            width,
            height,
            [0.005, 0.008, 0.025, 0.52 * dim],
        ));
        if let Some(a) = self.active {
            let age = a.song + (now - a.host) - a.offer.reveals[0];
            if (0.0..0.65).contains(&age)
                && let Some(glow) = art.proc.get("glow")
            {
                let (x, y) = hero.unwrap_or((width / 2.0, height / 2.0));
                let progress = (age / 0.65) as f32;
                for i in 0..24 {
                    let theta = i as f32 * std::f32::consts::TAU / 24.0;
                    let r = (20.0 + 145.0 * progress) * unit;
                    out.push(
                        Sprite::centred(
                            *glow,
                            x + theta.cos() * r,
                            y - 22.0 * unit + theta.sin() * r,
                            18.0 * unit * (1.0 - progress),
                            36.0 * unit * (1.0 - progress),
                        )
                        .rotated(theta)
                        .tinted([0.5, 1.0, 0.85, (1.0 - progress) * 0.8])
                        .additive(),
                    );
                }
            }
        }
        if let Some(c) = self.closing {
            let progress = ((now - c.at) / EXIT).clamp(0.0, 1.0) as f32;
            for (i, card) in c.active.offer.cards.into_iter().enumerate() {
                let selected = i == c.active.selected;
                let x = width / 2.0 + (i as f32 - 1.0) * 260.0 * unit;
                let y = height * 0.33
                    + if selected {
                        -24.0 * progress * unit
                    } else {
                        280.0 * progress * progress * unit
                    };
                let scale = if selected {
                    1.0 + 0.16 * (std::f32::consts::PI * progress).sin()
                } else {
                    1.0
                };
                card_draw(
                    card,
                    c.active.inv,
                    selected,
                    art,
                    x,
                    y,
                    unit,
                    1.0,
                    1.0 - progress,
                    scale,
                    if selected {
                        0.0
                    } else {
                        (i as f32 - 1.0) * 0.25 * progress
                    },
                    out,
                );
            }
        }
        if let Some(a) = self.active {
            let face = &art.fonts["pixel"];
            face.text(
                "LEVEL UP",
                width / 2.0,
                height * 0.245,
                3.0 * unit,
                Align::Centre,
                [0.65, 1.0, 0.88, 1.0],
                out,
            );
            for (i, card) in a.offer.cards.into_iter().enumerate() {
                let age = a.song + (now - a.host) - a.offer.reveals[i];
                if age < -FLIP {
                    continue;
                }
                let progress = ((age + FLIP) / FLIP).clamp(0.0, 1.0) as f32;
                let flip = (std::f32::consts::PI * progress).cos().abs().max(0.035);
                let selected = i == a.selected;
                let scale = highlight_scale(selected, now - a.highlight);
                if progress < 0.5 {
                    let x = width / 2.0 + (i as f32 - 1.0) * 260.0 * unit;
                    out.push(Sprite::solid(
                        x - 118.0 * unit * flip,
                        height * 0.33,
                        236.0 * unit * flip,
                        218.0 * unit,
                        [0.13, 0.23, 0.3, 0.98],
                    ));
                } else {
                    card_draw(
                        card,
                        a.inv,
                        selected,
                        art,
                        width / 2.0 + (i as f32 - 1.0) * 260.0 * unit,
                        height * 0.33,
                        unit,
                        flip,
                        1.0,
                        scale,
                        0.0,
                        out,
                    );
                }
            }
            face.text(
                if a.song + now - a.host + 1e-9 >= a.offer.reveals[2] {
                    "LEFT / RIGHT TO SELECT   -   SPACE / ENTER OR CLICK TO CHOOSE"
                } else {
                    "CHOOSE AFTER THE THIRD BEAT"
                },
                width / 2.0,
                height * 0.33 + 243.0 * unit,
                1.25 * unit,
                Align::Centre,
                [0.85, 0.9, 1.0, 1.0],
                out,
            );
        }
    }
}
pub(super) fn selection_key(index: usize) -> InputKey {
    [InputKey::Left, InputKey::Up, InputKey::Right][index]
}
fn highlight_scale(selected: bool, age: f64) -> f32 {
    if selected {
        1.0 + 0.035 * (-age * 13.0).exp() as f32
    } else {
        1.0
    }
}
pub(super) fn icon(item: Item) -> &'static str {
    match item {
        Item::Weapon(w) => {
            if w.as_str() == "sword" {
                "sword-spin"
            } else {
                w.as_str()
            }
        }
        Item::Passive(p) => p.as_str(),
        Item::Heal => "hollow-heart",
    }
}
fn pair(item: Item, inv: Inventory) -> Option<&'static str> {
    for def in weapon_catalog().weapons.values() {
        if let (Some(base), Some(passive)) = (def.evolution_of, def.requires) {
            match item {
                Item::Weapon(w) if w == base && inv.passives.rank(passive).is_some() => {
                    return Some(passive.as_str());
                }
                Item::Passive(p) if p == passive && inv.weapons.rank(base).is_some() => {
                    return Some(if base.as_str() == "sword" {
                        "sword-spin"
                    } else {
                        base.as_str()
                    });
                }
                _ => {}
            }
        }
    }
    None
}
#[allow(clippy::too_many_arguments)]
fn card_draw(
    card: sim::survivors::Card,
    inv: Inventory,
    selected: bool,
    art: &Atlases,
    x: f32,
    y: f32,
    u: f32,
    flip: f32,
    fade: f32,
    scale: f32,
    rot: f32,
    out: &mut Vec<Sprite>,
) {
    let mut local = Vec::new();
    let color = item_color(icon(card.item));
    let face = &art.fonts["pixel"];
    local.push(Sprite::solid(
        x - 122.0 * u,
        y - 4.0 * u,
        244.0 * u,
        226.0 * u,
        if selected {
            [0.35, 1.0, 0.8, 1.0]
        } else {
            [0.22, 0.31, 0.4, 1.0]
        },
    ));
    local.push(Sprite::solid(
        x - 119.0 * u,
        y - u,
        238.0 * u,
        220.0 * u,
        [0.025, 0.04, 0.075, 0.98],
    ));
    if let (Some(glow), Some(icon)) = (art.proc.get("glow"), art.icons.get(icon(card.item))) {
        local.push(
            Sprite::centred(*glow, x, y + 52.0 * u, 110.0 * u, 110.0 * u)
                .tinted([color[0], color[1], color[2], 0.35])
                .additive(),
        );
        local.push(
            Sprite::centred(*icon, x, y + 52.0 * u, 62.0 * u, 62.0 * u)
                .tinted(color)
                .additive(),
        );
    }
    face.text(
        card.item.name(),
        x,
        y + 103.0 * u,
        1.6 * u,
        Align::Centre,
        [1.0; 4],
        &mut local,
    );
    let rank = if card.item == Item::Heal {
        "MAXED".into()
    } else if card.before == 0 {
        "NEW!".into()
    } else {
        format!("LV {} > {}", card.before, card.after)
    };
    face.text(
        &rank,
        x,
        y + 132.0 * u,
        1.5 * u,
        Align::Centre,
        color,
        &mut local,
    );
    face.text(
        card.description(),
        x,
        y + 159.0 * u,
        u,
        Align::Centre,
        [0.85, 0.9, 1.0, 1.0],
        &mut local,
    );
    if let Some(partner) = pair(card.item, inv) {
        face.text(
            "EVO PAIR",
            x - 11.0 * u,
            y + 191.0 * u,
            u,
            Align::Centre,
            [1.0, 0.85, 0.4, 1.0],
            &mut local,
        );
        if let Some(icon) = art.icons.get(partner) {
            local.push(
                Sprite::centred(*icon, x + 42.0 * u, y + 194.0 * u, 19.0 * u, 19.0 * u)
                    .tinted([1.0, 0.85, 0.4, 1.0]),
            );
        }
    }
    let cy = y + 109.0 * u;
    for mut s in local {
        let dx = (s.x + s.w / 2.0 - x) * flip * scale;
        let dy = (s.y + s.h / 2.0 - cy) * scale;
        s.w *= flip * scale;
        s.h *= scale;
        s.x = x + dx * rot.cos() - dy * rot.sin() - s.w / 2.0;
        s.y = cy + dx * rot.sin() + dy * rot.cos() - s.h / 2.0;
        s.rot += rot;
        s.tint[3] *= fade;
        out.push(s);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use sim::survivors::{FreezeReason, WeaponId, offer_table};
    fn run() -> RunState {
        RunState {
            song: 0.0,
            level: 2,
            frozen: Some(FreezeReason::LevelUp),
            offer: Some(Offer {
                level: 2,
                cards: offer_table().cards(2, Inventory::default(), weapon_catalog()),
                reveals: [0.5, 1.0, 1.5],
            }),
            ..RunState::default()
        }
    }
    #[test]
    fn horizontal_navigation_visits_every_card_including_between_frames() {
        let mut screen = Screen::default();
        let r = run();
        screen.update(Some(r), None, 10.0);
        assert_eq!(screen.navigate(false), Some(InputKey::Left));
        assert_eq!(screen.navigate(true), Some(InputKey::Up));
        assert_eq!(screen.navigate(true), Some(InputKey::Right));
        assert_eq!(screen.navigate(true), Some(InputKey::Right));
        assert_eq!(screen.navigate(false), Some(InputKey::Up));
        assert_eq!(screen.navigate(false), Some(InputKey::Left));
        // A frame reconciles against the sim, including a rollback or a chained offer.
        screen.update(Some(RunState { selected: 2, ..r }), None, 10.1);
        assert_eq!(screen.navigate(false), Some(InputKey::Up));
        screen.update(None, None, 10.2);
        assert_eq!(screen.navigate(true), None);
    }
    #[test]
    fn clicks_cover_all_three_card_faces_at_different_window_sizes() {
        for (w, h) in [(960.0, 540.0), (1440.0, 900.0), (540.0, 960.0)] {
            let mut screen = Screen::default();
            let r = run();
            screen.update(Some(r), None, 10.0);
            let u = (w / 960.0_f32).min(h / 540.0_f32);
            for i in 0..3 {
                let x = w / 2.0 + (i as f32 - 1.0) * 260.0 * u;
                let y = h * 0.33 + 109.0 * u;
                assert_eq!(screen.hit(11.49, w, h, x, y), None, "unready offer");
                for (dx, dy) in [(0.0, 0.0), (-121.0, -112.0), (121.0, 112.0)] {
                    assert_eq!(screen.hit(11.5, w, h, x + dx * u, y + dy * u), Some(i));
                }
                assert_eq!(screen.hit(11.5, w, h, x + 128.0 * u, y), None, "gap");
                assert_eq!(screen.hit(11.5, w, h, x, y - 120.0 * u), None, "above");
            }
            screen.update(
                Some(RunState {
                    offer: None,
                    confirmed: true,
                    ..r
                }),
                None,
                11.6,
            );
            assert_eq!(screen.hit(11.7, w, h, w / 2.0, h * 0.5), None, "closing");
        }
    }
    #[test]
    fn a_retraction_does_not_animate_or_sound_a_confirm() {
        let mut s = Screen::default();
        let r = run();
        s.update(Some(r), None, 10.0);
        // A stale confirmed bit from an earlier level must not turn a rollback into a pick.
        s.update(
            Some(RunState {
                offer: None,
                confirmed: true,
                level: 1,
                ..r
            }),
            None,
            10.2,
        );
        assert!(s.active.is_none() && s.closing.is_none());
        assert!(s.cues(None, 10.2).is_empty());
    }
    #[test]
    fn picks_fall_away_but_retracted_picks_reopen_without_confirm() {
        let mut s = Screen::default();
        let r = run();
        s.update(Some(r), None, 10.0);
        s.update(
            Some(RunState {
                offer: None,
                confirmed: true,
                ..r
            }),
            None,
            12.0,
        );
        assert!(s.closing.is_some());
        assert!(s.cues(None, 12.0).iter().any(|c| c.key == "card-confirm"));
        s.update(Some(r), None, 12.1);
        assert!(s.active.is_some() && s.closing.is_none());
        assert!(!s.cues(None, 12.1).iter().any(|c| c.key == "card-confirm"));
    }
    #[test]
    fn chained_offers_keep_the_world_dim_and_cues_use_sim_reveals() {
        let mut s = Screen::default();
        let r = run();
        s.update(Some(r), None, 10.0);
        let cues = s.cues(Some(r), 10.1);
        assert_eq!(cues.len(), 4);
        assert_eq!(
            cues.iter()
                .filter(|c| c.key == "card-reveal")
                .map(|c| c.s)
                .collect::<Vec<_>>(),
            vec![0.5, 1.0, 1.5]
        );
        let mut next = r;
        let mut o = r.offer.unwrap();
        o.level = 3;
        o.reveals = [2.0, 2.5, 3.0];
        next.offer = Some(o);
        next.level = 3;
        next.song = 1.6;
        s.update(Some(next), None, 11.6);
        assert!(s.dim(11.6) > 0.99);
        assert!(s.active.is_some() && s.closing.is_some());
    }
    #[test]
    fn third_card_is_fully_readable_on_the_sims_confirmation_beat() {
        let art = Atlases::load(
            &std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../public/assets"),
        )
        .unwrap();
        let mut screen = Screen::default();
        let mut r = run();
        screen.update(Some(r), None, 0.0);
        let region = art.icons[icon(r.offer.unwrap().cards[2].item)];
        r.song = 1.29;
        screen.update(Some(r), None, 1.29);
        let mut before = Vec::new();
        screen.draw(&art, 1.29, 960.0, 540.0, None, &mut before);
        assert!(!before.iter().any(|s| s.region == region));
        r.song = 1.5;
        screen.update(Some(r), None, 1.5);
        let mut on = Vec::new();
        screen.draw(&art, 1.5, 960.0, 540.0, None, &mut on);
        assert!(
            on.iter().any(|s| s.region == region && s.w >= 61.9),
            "the third front face lands before a confirm can count"
        );
    }
    #[test]
    fn forecast_retiming_replaces_future_cues_instead_of_doubling_them() {
        let mut screen = Screen::default();
        let r = run();
        screen.update(Some(r), None, 0.0);
        let mut forecast = r;
        forecast.offer.as_mut().unwrap().reveals = [0.6, 1.1, 1.6];
        let cues = screen.cues(Some(forecast), 0.0);
        assert_eq!(cues.len(), 4);
        assert_eq!(
            cues.iter()
                .filter(|c| c.key == "card-reveal")
                .map(|c| c.s)
                .collect::<Vec<_>>(),
            vec![0.6, 1.1, 1.6]
        );
    }
    #[test]
    fn rollback_replaces_cards_and_pair_inventory_at_the_same_reveal_times() {
        let mut screen = Screen::default();
        let mut inv = Inventory::default();
        inv.weapons.upgrade(WeaponId::Sword, 8).unwrap();
        let mut r = run();
        r.offer.as_mut().unwrap().cards = offer_table().cards(2, inv, weapon_catalog());
        screen.update(Some(r), Some(inv), 10.0);
        let old = screen.active.unwrap();
        // A different earlier pick/loot can max Garlic before this same level.
        for _ in 0..8 {
            inv.weapons.upgrade(WeaponId::Garlic, 8).unwrap();
        }
        inv.passives
            .upgrade(sim::survivors::PassiveId::HollowHeart, 5)
            .unwrap();
        r.offer.as_mut().unwrap().cards = offer_table().cards(2, inv, weapon_catalog());
        assert_ne!(old.offer.cards, r.offer.unwrap().cards);
        assert_eq!(old.offer.reveals, r.offer.unwrap().reveals);
        screen.update(Some(r), Some(inv), 10.2);
        let shown = screen.active.unwrap();
        assert_eq!(shown.offer, r.offer.unwrap());
        assert_eq!(shown.inv, inv);
        assert_eq!(shown.opened, old.opened, "same reveal animation continues");
        assert!(screen.closing.is_none());
        assert!(
            !screen
                .cues(None, 10.2)
                .iter()
                .any(|c| c.key == "card-confirm")
        );
    }
    #[test]
    fn pair_badge_follows_actual_catalog_partners() {
        let mut inv = Inventory::default();
        inv.weapons.upgrade(WeaponId::Sword, 8).unwrap();
        assert_eq!(
            pair(Item::Passive(sim::survivors::PassiveId::HollowHeart), inv),
            Some("sword-spin")
        );
        assert_eq!(
            pair(Item::Passive(sim::survivors::PassiveId::Spinach), inv),
            None
        );
    }
}
