//! Curated level data checked against its kept upstream documentation.
#![allow(clippy::float_cmp)]
use sim::survivors::{Stats, WeaponCatalog, WeaponId, weapon_catalog};

#[test]
fn every_combat_rank_matches_the_sourced_feel_appendix() {
    let docs = include_str!(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../../docs/survivors/appendix-feel.md"
    ));
    let catalog = weapon_catalog();
    let mut checked = 0;
    for def in catalog.weapons.values() {
        let heading = format!("(`{}`)", def.source);
        let section = docs.split(&heading).nth(1).expect("documented source");
        let section = section.split("\n### ").next().unwrap();
        for line in section.lines() {
            let cells: Vec<_> = line.trim_matches('|').split('|').map(str::trim).collect();
            if cells.len() != 11 {
                continue;
            }
            let Ok(rank) = cells[0].parse::<u8>() else {
                continue;
            };
            let expected: Vec<_> = cells[1..]
                .iter()
                .map(|c| c.parse::<f64>().unwrap())
                .collect();
            let got = def.level(rank);
            let actual = [
                got.power,
                got.amount,
                got.interval_ms,
                got.repeat_ms,
                got.area,
                got.speed,
                got.duration_ms,
                got.pierce,
                got.hit_delay_ms,
                got.knockback,
            ];
            for (i, (a, b)) in actual.iter().zip(expected).enumerate() {
                assert!(
                    (a - b).abs() < 1e-9,
                    "{} rank{rank} column{i}: {a} != {b}",
                    def.source
                );
            }
            checked += 1;
        }
    }
    assert_eq!(checked, 45);
    eprintln!("{checked} combat ranks / 450 numeric fields match docs/survivors/appendix-feel.md");
}

#[test]
fn numeric_rank_deltas_add_and_boolean_fields_replace() {
    let source = include_str!(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../../content/survivors/weapons.yaml"
    ));
    let mut data: serde_json::Value = serde_saphyr::from_str(source).unwrap();
    let sword = data["weapons"][0].clone();
    data["weapons"] = serde_json::json!([sword]);
    data["weapons"][0]["levels"][1] =
        serde_json::json!({"power":0.5,"amount":1,"critEnabled":true});
    let catalog = WeaponCatalog::parse(&data.to_string()).unwrap();
    let level = catalog.get(WeaponId::Sword).level(2);
    assert_eq!(level.power, 1.5);
    assert_eq!(level.amount, 2.0);
    assert!(level.crit_enabled);
    data["weapons"][0]["levels"][0]["intervalMs"] = serde_json::json!(-1);
    assert!(WeaponCatalog::parse(&data.to_string()).is_err());
}

#[test]
fn runtime_specific_intervals_and_crit_flags_are_preserved() {
    let catalog = weapon_catalog();
    let whip = catalog.get(WeaponId::Sword).level(1);
    assert!(!whip.crit_enabled);
    assert_eq!(whip.crit_chance, 0.2);
    assert!(catalog.get(WeaponId::BloodyTear).level(1).crit_enabled);
    let tome = Stats {
        cooldown: 0.6,
        ..Stats::default()
    };
    assert_eq!(catalog.get(WeaponId::HolyWand).level(1).interval(tome), 0.3);
    assert_eq!(
        catalog
            .get(WeaponId::KingBible)
            .level(1)
            .interval(Stats::default()),
        6.0
    );
    assert!(
        !catalog
            .get(WeaponId::UnholyVespers)
            .level(1)
            .interval_depends_on_duration
    );
    assert!(catalog.get(WeaponId::Sword).implemented);
    assert!(catalog.get(WeaponId::Garlic).implemented);
}
