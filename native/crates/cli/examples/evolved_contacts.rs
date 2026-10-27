//! SP5 exact before/after optimization check on actual streamed combat and late keys.
use sim::{
    content::{SourceFile, load_content},
    movement::InputKey,
};
use std::path::Path;
#[path = "../src/batch.rs"]
mod batch;
fn main() -> Result<(), Box<dyn std::error::Error>> {
    batch::enter()?;
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
    let mut paths = std::fs::read_dir(root.join("content"))?
        .map(|p| p.map(|p| p.path()))
        .collect::<Result<Vec<_>, _>>()?;
    paths.retain(|p| p.extension().is_some_and(|e| e == "yaml"));
    paths.sort();
    let files = paths
        .iter()
        .map(|p| {
            Ok(SourceFile {
                name: p.display().to_string(),
                text: std::fs::read_to_string(p)?,
            })
        })
        .collect::<Result<Vec<_>, std::io::Error>>()?;
    let lib = load_content(&files).map_err(|e| format!("{e:?}"))?;
    let level = SourceFile {
        name: "survivors.yaml".into(),
        text: std::fs::read_to_string(root.join("levels/survivors.yaml"))?,
    };
    let file = stream::StreamFile::parse(&std::fs::read_to_string(
        root.join("public/reactive/house-128/stream.json"),
    )?)
    .map_err(|e| e.0)?;
    for moving in [false, true] {
        for killable in [false, true] {
            let mut e = if killable {
                eval::scale::evolved_combat_engine(&level, &lib)?
            } else {
                eval::scale::evolved_density_engine(&level, &lib)?
            };
            let mut replay = stream::Replay::new(file.log.clone());
            for frame in 1..=3600 {
                let s = f64::from(frame) / 120.0;
                e.feed(|l| replay.feed_to(l, s));
                if frame % 48 == 0 {
                    let key = if moving {
                        [
                            InputKey::Right,
                            InputKey::Down,
                            InputKey::Left,
                            InputKey::Up,
                        ][frame as usize / 480 % 4]
                    } else {
                        InputKey::Space
                    };
                    e.key(s - 0.025, key, true);
                    e.key(s + 0.05, key, false);
                }
                e.frame(s, 0.237);
                e.view.borrow_mut().take_moments();
            }
            println!(
                "{}",
                serde_json::json!({"moving":moving,"killable":killable,"hash":format!("{:016x}",e.settled_hash),"records":e.settled_count,"rollbacks":e.stats.rollbacks,"kills":e.view.borrow().kills,"gems":e.view.borrow().gems.len(),"health":e.view.borrow().health})
            );
        }
    }
    Ok(())
}
