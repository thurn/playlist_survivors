//! Choreography recording through actual speculation/late-key rollback.
use crate::live::Engine;
use sim::content::{ContentLibrary, SourceFile};
fn content() -> (SourceFile, ContentLibrary) {
    let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
    let mut paths: Vec<_> = std::fs::read_dir(root.join("content"))
        .unwrap()
        .map(|p| p.unwrap().path())
        .filter(|p| p.extension().is_some_and(|s| s == "yaml"))
        .collect();
    paths.sort();
    let sources: Vec<_> = paths
        .iter()
        .map(|p| SourceFile {
            name: p.display().to_string(),
            text: std::fs::read_to_string(p).unwrap(),
        })
        .collect();
    let level = SourceFile { name: "musical-check.yaml".into(),
        text: std::fs::read_to_string(root.join("levels/survivors.yaml")).unwrap()
            .lines().map(|l| if l.trim().starts_with("- {\"minute\":") {
                let minute = l.trim().split(',').next().unwrap().trim_start_matches("- {\"minute\":");
                format!("    - {{\"minute\":{minute},\"minimum\":0,\"frequency\":1000,\"enemies\":[],\"bosses\":[],\"events\":[],\"treasure\":[]}}")
            } else { l.into() }).collect::<Vec<_>>().join("\n") };
    let mut lib = sim::content::load_content(&sources).unwrap();
    // Isolate choreography from unrelated card freezes/health, without changing movement.
    for e in lib.enemies.values_mut() {
        e.survivors
            .get_or_insert_with(|| sim::survivors::EnemyStats::for_art(&e.id))
            .xp = 0.0;
    }
    (level, lib)
}
#[test]
fn formation_records_survive_late_keys_with_nondefault_lead() {
    let (level, lib) = content();
    let make = || {
        Engine::new_with_setup(
            &level,
            &lib,
            &indexmap::IndexMap::new(),
            0.2,
            true,
            true,
            f64::INFINITY,
            sim::engine::Sim::disable_hero_damage_for_check,
        )
        .unwrap()
    };
    let period = 60.0 / 128.0;
    let log = stream::StreamLog {
        beats: (0..850)
            .map(|i| {
                let t = f64::from(i) * period;
                stream::Beat {
                    t,
                    known_at: (t - period).max(0.0),
                    period,
                    evidence: 1.0,
                }
            })
            .collect(),
        ticks: (0..8000)
            .map(|i| {
                let t = f64::from(i) * 0.05;
                stream::Tick {
                    t,
                    known_at: t,
                    evidence: 1.0,
                    energy: 0.5,
                    level: Some(if (170.0..235.0).contains(&t) {
                        stream::Level::Medium
                    } else if t >= 300.0 {
                        stream::Level::Loud
                    } else {
                        stream::Level::Quiet
                    }),
                    key: None,
                    bands: None,
                    brightness: None,
                }
            })
            .collect(),
        jumps: Some(vec![stream::Jump {
            t: 120.0,
            known_at: 120.0,
            db: 9.0,
        }]),
        ..Default::default()
    };
    let mut a = make();
    let mut b = make();
    a.settled_log = Some(Vec::new());
    a.feed(|l| *l = log.clone());
    b.feed(|l| *l = log.clone());
    for i in 1..=40000 {
        let now = f64::from(i) * 0.01;
        if i % 1000 == 0 && i < 40000 {
            a.key(now, sim::movement::InputKey::Space, (i / 1000) % 2 == 0);
        }
        if i > 3 && (i - 3) % 1000 == 0 {
            b.key(
                f64::from(i - 3) * 0.01,
                sim::movement::InputKey::Space,
                ((i - 3) / 1000) % 2 == 0,
            );
        }
        a.frame(now, 0.237);
        b.frame(now, 0.237);
        a.view.borrow_mut().take_moments();
        b.view.borrow_mut().take_moments();
    }
    a.frame(400.1, 0.237);
    b.frame(400.1, 0.237);
    assert!(b.stats.rollbacks > 20);
    assert!(a.sim.run_s() > 399.0 && b.sim.run_s() > 399.0);
    assert_eq!(a.settled_hash, b.settled_hash);
    let records = a.settled_log.as_ref().unwrap();
    assert!(
        records
            .iter()
            .any(|(_, r)| matches!(r, crate::live::Record::Dance { active: true, .. }))
    );
    assert!(
        records
            .iter()
            .any(|(_, r)| matches!(r, crate::live::Record::Dance { active: false, .. }))
    );
    for (role, minimum) in [("goblin", 80), ("skeleton-ember", 12), ("bee", 9)] {
        assert!(
            records
                .iter()
                .filter(|(_, r)| matches!(r,crate::live::Record::Spawn {kind,..} if kind==role))
                .count()
                >= minimum,
            "{role} missing"
        );
    }
    eprintln!(
        "400s authored musical roles; late30ms lead0.237: {} rollbacks, settled hash{:x}",
        b.stats.rollbacks, b.settled_hash
    );
}
