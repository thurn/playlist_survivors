use super::*;
fn state() -> HudState<'static> {
    HudState {
        keys: [KeyLight::default(); 5],
        presses: &[],
        beats: &[-0.1, 0.4, 0.9],
        beat_phase: 0.1,
        elapsed: 10.0,
        bitten_ago: 10.0,
        head: Some((480.0, 240.0)),
        next_down: true,
        tier_flash: 0.0,
        delayed: 3.0,
        zen: true,
        rhythm: Rhythm {
            locked: true,
            groove: 0.55,
        },
        keys_visible: true,
    }
}
#[test]
fn zen_meter_is_heard_music_and_legacy_meter_is_earned_accuracy() {
    let mut view = LiveView {
        groove: 1.0,
        tier: 3.0,
        ..LiveView::default()
    };
    let mut st = state();
    assert_eq!(meter(&view, &st), (0.55, 2));
    view.groove = 0.0;
    view.tier = 0.0;
    assert_eq!(meter(&view, &st), (0.55, 2));
    st.zen = false;
    assert_eq!(meter(&view, &st), (0.0, 0));
}
#[test]
fn low_health_pulses_on_a_reliable_beat_and_damage_flashes_white() {
    let mut st = state();
    st.beat_phase = 0.0;
    let bright = life_color(0.1, &st);
    st.beat_phase = 0.8;
    assert_ne!(bright, life_color(0.1, &st));
    st.rhythm.locked = false;
    st.beat_phase = 0.0;
    assert_eq!(life_color(0.1, &st), rgb(0xff4050));
    st.bitten_ago = 0.05;
    assert_eq!(life_color(0.1, &st), [1.0; 4]);
}

#[test]
#[ignore = "release: offscreen HUD state qualification"]
fn hud_state_render_qualification() {
    use crate::slice::{cards, live, scene};
    use render::{
        atlas::Atlases,
        capture,
        post::{Frame, Grade},
    };
    use sim::survivors::{
        Card, FreezeReason, Inventory, Item, Offer, PassiveId, RunState, WeaponId,
    };
    let art = Atlases::load(&live::resource("assets", "../../../public/assets")).unwrap();
    let dir = std::env::var_os("PS_HUD_EXPORT_DIR").map_or_else(
        || std::env::temp_dir().join("ps-hud-fr4"),
        std::path::PathBuf::from,
    );
    std::fs::create_dir_all(&dir).unwrap();
    for (w, h) in [(960, 540), (1920, 1080)] {
        let mut gpu = capture::Offscreen::new(&art.pages, w, h).unwrap();
        for name in ["quiet", "waiting", "hurt", "legacy", "cards", "end"] {
            let mut inv = Inventory::default();
            inv.weapons.upgrade(WeaponId::Sword, 8).unwrap();
            let mut view = LiveView {
                inventory: Some(inv),
                health: 1.0,
                groove: 0.67,
                tier: 2.0,
                level: 1,
                xp: 0.4,
                run: Some(RunState::default()),
                ..LiveView::default()
            };
            let mut st = state();
            st.head = Some((w as f32 * 0.5, h as f32 * 0.45));
            if name == "waiting" {
                st.rhythm.locked = false;
                st.keys_visible = false;
            }
            if name == "hurt" {
                st.bitten_ago = 0.05;
                view.health = 0.12;
                st.beat_phase = 0.0;
            }
            if name == "legacy" {
                st.zen = false;
                view.ratings = [12, 4, 1];
            }
            if name == "cards" {
                let run = view.run.as_mut().unwrap();
                run.offer = Some(Offer {
                    level: 2,
                    cards: [
                        Card {
                            item: Item::Weapon(WeaponId::Garlic),
                            before: 0,
                            after: 1,
                        },
                        Card {
                            item: Item::Passive(PassiveId::Spinach),
                            before: 0,
                            after: 1,
                        },
                        Card {
                            item: Item::Weapon(WeaponId::MagicWand),
                            before: 0,
                            after: 1,
                        },
                    ],
                    reveals: [9.0, 9.25, 9.5],
                });
                run.frozen = Some(FreezeReason::LevelUp);
                run.song = 10.0;
            }
            let mut overlay = Vec::new();
            draw(&view, &st, &art, w as f32, h as f32, &mut overlay);
            let mut cards = cards::Screen::default();
            cards.update(view.run.as_ref(), view.inventory, 10.0);
            cards.draw(&art, 10.6, w as f32, h as f32, st.head, &mut overlay);
            if name == "end" {
                scene::end_card(
                    w as f32,
                    h as f32,
                    1.0,
                    90,
                    scene::End::Fell,
                    &view,
                    None,
                    &art,
                    &mut overlay,
                );
            }
            gpu.draw(&Frame {
                clear: [0.025, 0.04, 0.055],
                world: &[],
                lights: &[],
                grade: Grade::default(),
                overlay: &overlay,
            });
            capture::write_png(
                &dir.join(format!("{name}-{w}.png")),
                w,
                h,
                &gpu.rgba().unwrap(),
            )
            .unwrap();
        }
    }
}
