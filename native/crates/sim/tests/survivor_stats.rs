//! Numerical contracts from docs/survivors, not the retired TS prototype.
#![allow(clippy::float_cmp)]
use sim::core::rng::{Part, Rng};
use sim::survivors::{
    GemColor, HERO_SPEED, PICKUP_RADIUS, PX_PER_VS_UNIT, Progress, StatDelta, Stats, enemy_speed,
    gem_color, gem_value, total_xp,
};

#[test]
fn vs_stat_layers_add_except_compounding_max_health_and_final_clamps() {
    let mut stats = Stats::default();
    stats.apply(StatDelta {
        might: 0.25,
        ..StatDelta::default()
    });
    for _ in 0..5 {
        stats.apply(StatDelta {
            might: 0.1,
            max_hp: 0.2,
            recovery: 0.2,
            cooldown: -0.08,
            duration: 0.1,
            ..StatDelta::default()
        });
    }
    assert!((stats.might - 1.75).abs() < 1e-12);
    assert!((stats.max_hp - 298.5984).abs() < 1e-9);
    assert!((stats.cooldown - 0.6).abs() < 1e-12);
    assert_eq!(stats.recovery, 1.0);
    stats.apply(StatDelta {
        might: 30.0,
        area: 30.0,
        amount: 30.0,
        projectile_speed: 30.0,
        duration: 30.0,
        armor: 100.0,
        cooldown: -10.0,
        ..StatDelta::default()
    });
    let final_stats = stats.finalized();
    assert_eq!(
        (final_stats.might, final_stats.area, final_stats.amount),
        (10.0, 10.0, 10.0)
    );
    assert_eq!(
        (
            final_stats.projectile_speed,
            final_stats.duration,
            final_stats.armor
        ),
        (5.0, 5.0, 50.0)
    );
    assert_eq!(final_stats.cooldown, 0.1);
    // Final clamps never destroy the underlying sum.
    stats.apply(StatDelta {
        might: -25.0,
        ..StatDelta::default()
    });
    assert!((stats.finalized().might - 6.75).abs() < 1e-12);
}

#[test]
fn contact_damage_scales_power_before_armor_with_one_hp_floor() {
    let stats = Stats::default();
    assert_eq!(stats.contact_damage(5.0, 0.5), 1.5);
    assert_eq!(stats.contact_damage(10.0, 0.5), 4.0);
    assert_eq!(stats.contact_damage(30.0, 0.5), 14.0);
    let armored = Stats {
        armor: 50.0,
        ..stats
    };
    assert_eq!(armored.contact_damage(5.0, 0.5), 1.0);
}

#[test]
fn the_xp_appendix_curve_and_temporary_growth_bonus_are_exact() {
    for (level, total) in [
        (1, 5.0),
        (2, 20.0),
        (10, 500.0),
        (19, 1805.0),
        (20, 2600.0),
        (21, 2866.5),
        (25, 4062.5),
        (39, 9886.5),
        (40, 12800.0),
        (41, 13448.0),
        (60, 28800.0),
        (100, 80000.0),
    ] {
        assert_eq!(total_xp(level), total);
    }
    for (level, previous, needed) in [(20, 1805.0, 397.5), (40, 9886.5, 1456.75)] {
        let mut p = Progress {
            level,
            xp: previous,
            ..Progress::default()
        };
        assert_eq!(p.growth(), 2.0);
        p.gain_xp(needed);
        assert_eq!(p.level, level + 1);
        assert_eq!(p.pending_levels, 1);
        assert_eq!(p.growth(), 1.0);
        assert_eq!(p.fill(), 0.0);
    }
    let bonus = Progress {
        level: 20,
        stats: Stats {
            growth: 1.4,
            ..Stats::default()
        },
        ..Progress::default()
    };
    assert_eq!(bonus.growth(), 2.4);
    let mut p = Progress::default();
    p.gain_xp(45.0);
    assert_eq!((p.level, p.pending_levels, p.xp), (4, 3, 45.0));
}

#[test]
fn seeded_gem_values_include_zero_and_match_source_distribution_and_colors() {
    let mut rng = Rng::new(&[Part::Str("SF4-gems")]);
    let mut sum = 0.0;
    let mut zero = 0;
    for _ in 0..20_000 {
        let value = gem_value(1.0, rng.next());
        assert!(value == 0.0 || value == 1.0);
        sum += value;
        if value == 0.0 {
            zero += 1;
        }
    }
    assert!(zero > 9_500 && zero < 10_500);
    assert!((sum / 20_000.0 - 0.5).abs() < 0.025);
    for (xp, lo, hi) in [
        (1.5, 0.0, 2.0),
        (2.0, 1.0, 2.0),
        (2.5, 1.0, 3.0),
        (50.0, 25.0, 74.0),
    ] {
        assert_eq!(gem_value(xp, 0.0), lo);
        assert_eq!(gem_value(xp, 1.0 - f64::EPSILON), hi);
    }
    assert_eq!(gem_color(2.0), GemColor::Blue);
    assert_eq!(gem_color(3.0), GemColor::Green);
    assert_eq!(gem_color(9.0), GemColor::Green);
    assert_eq!(gem_color(10.0), GemColor::Red);
    eprintln!(
        "20,000 xp=1 drops: {zero} zero, {sum} total XP, mean {}",
        sum / 20_000.0
    );
}

#[test]
fn movement_and_magnet_share_one_forest_unit_conversion() {
    assert!((PX_PER_VS_UNIT - 154.269_972_451_790_65).abs() < 1e-9);
    assert!((HERO_SPEED - 140.0).abs() < 1e-9);
    assert!((enemy_speed(140.0) - 54.88).abs() < 1e-9);
    assert!((enemy_speed(100.0) / HERO_SPEED - 0.28).abs() < 1e-9);
    assert_eq!(PICKUP_RADIUS, 120.0);
    eprintln!(
        "px/U={PX_PER_VS_UNIT}, hero={HERO_SPEED}, bat={}, radius={PICKUP_RADIUS}",
        enemy_speed(140.0)
    );
}
