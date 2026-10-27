//! Two labelled, synthetic presentation stills of all six passive cards and HUD slots.
use super::card_draw;
use crate::slice::{HudState, KeyLight, hud, live};
use eval::live::LiveView;
use render::{
    atlas::Atlases,
    capture,
    font::Align,
    post::{Frame, Grade},
};
use sim::survivors::{Card, Inventory, Item, PassiveId, RunState, WeaponId};
use std::path::Path;
const PASSIVES: [PassiveId; 6] = [
    PassiveId::Spinach,
    PassiveId::HollowHeart,
    PassiveId::Pummarola,
    PassiveId::EmptyTome,
    PassiveId::Spellbinder,
    PassiveId::Duplicator,
];
pub fn run(dir: &Path) -> Result<(), String> {
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let mut inv = Inventory::default();
    for w in [
        WeaponId::Sword,
        WeaponId::Garlic,
        WeaponId::MagicWand,
        WeaponId::KingBible,
        WeaponId::LightningRing,
    ] {
        inv.weapons
            .upgrade(w, 8)
            .map_err(|e| format!("fixture weapon: {e:?}"))?;
    }
    for p in PASSIVES {
        inv.passives
            .upgrade(p, p.max_rank())
            .map_err(|e| format!("fixture passive: {e:?}"))?;
        if !art.icons.contains_key(p.as_str()) {
            return Err(format!("missing {} icon", p.as_str()));
        }
    }
    let view = LiveView {
        inventory: Some(inv),
        health: 1.0,
        level: 12,
        run: Some(RunState {
            elapsed: 90.0,
            level: 12,
            max_hp: 144.0,
            ..RunState::default()
        }),
        ..LiveView::default()
    };
    let state = HudState {
        keys: [KeyLight::default(); 5],
        presses: &[],
        beats: &[],
        beat_phase: 0.0,
        elapsed: 90.0,
        bitten_ago: f64::INFINITY,
        head: None,
        next_down: false,
        tier_flash: 0.0,
        delayed: 3.0,
        zen: true,
        rhythm: hud::Rhythm::default(),
        keys_visible: true,
    };
    let mut gpu = capture::Offscreen::new(&art.pages, 1280, 720)?;
    for (page, passives) in PASSIVES.chunks(3).enumerate() {
        let mut overlay = Vec::new();
        hud::draw(&view, &state, &art, 1280.0, 720.0, &mut overlay);
        art.fonts["pixel"].text(
            "PASSIVES",
            640.0,
            210.0,
            3.0,
            Align::Centre,
            [1.0; 4],
            &mut overlay,
        );
        for (i, &p) in passives.iter().enumerate() {
            let card = Card {
                item: Item::Passive(p),
                before: 1,
                after: 2,
            };
            card_draw(
                card,
                inv,
                i == 0,
                &art,
                640.0 + (i as f32 - 1.0) * 346.0,
                255.0,
                1.25,
                1.0,
                1.0,
                1.0,
                0.0,
                &mut overlay,
            );
            eprintln!(
                "{} rank1>2: {} / {}",
                p.as_str(),
                card.item.name(),
                card.description()
            );
        }
        art.fonts["pixel"].text(
            "SYNTHETIC PRESENTATION FIXTURE - ALL SIX OWNED AT RANK 1",
            640.0,
            690.0,
            1.2,
            Align::Centre,
            [0.7, 0.8, 0.9, 1.0],
            &mut overlay,
        );
        gpu.draw(&Frame {
            clear: [0.015, 0.025, 0.035],
            world: &[],
            lights: &[],
            grade: Grade::default(),
            overlay: &overlay,
        });
        capture::write_png(
            &dir.join(format!("passives-{}.png", page + 1)),
            1280,
            720,
            &gpu.rgba()?,
        )?;
    }
    eprintln!(
        "passives-check: two silent offscreen card/HUD stills, synthetic rank1 inventory; real card/stat progression is covered separately by sim tests, no survival or pacing claim"
    );
    Ok(())
}
