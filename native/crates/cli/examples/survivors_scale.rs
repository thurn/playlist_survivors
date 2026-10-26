//! Offline SF10 measurements: actual snapshot allocations, CPU step cost and late-key rollback.
//! Usage: cargo run --release -p cli --example survivors_scale -- SESSION [equal]
//! SESSION is a decompressed SF9 recorded-normal-30 session. Equal mode uses its arrived stream,
//! with a labelled synthetic 300-enemy, unkillable, damage-exempt density fixture.
use eval::{
    live::Engine,
    session::{AppliedKey, Arrival, rebase_append},
};
use serde_json::json;
use sim::{
    content::{SourceFile, load_content},
    movement::InputKey,
};
use std::{
    alloc::{GlobalAlloc, Layout, System},
    hint::black_box,
    path::{Path, PathBuf},
    sync::atomic::{AtomicBool, AtomicUsize, Ordering},
    time::Instant,
};
#[path = "../src/batch.rs"]
mod batch;
struct Counting;
static TRACK: AtomicBool = AtomicBool::new(false);
static BYTES: AtomicUsize = AtomicUsize::new(0);
static ALLOCS: AtomicUsize = AtomicUsize::new(0);
// SAFETY: all allocation operations delegate unchanged to System; the counters only observe.
unsafe impl GlobalAlloc for Counting {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        if TRACK.load(Ordering::Relaxed) {
            BYTES.fetch_add(layout.size(), Ordering::Relaxed);
            ALLOCS.fetch_add(1, Ordering::Relaxed);
        }
        // SAFETY: the caller supplies the GlobalAlloc layout.
        unsafe { System.alloc(layout) }
    }
    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        // SAFETY: the pointer and layout came from this allocator's System allocation.
        unsafe {
            System.dealloc(ptr, layout);
        }
    }
    unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, size: usize) -> *mut u8 {
        if TRACK.load(Ordering::Relaxed) {
            BYTES.fetch_add(size, Ordering::Relaxed);
            ALLOCS.fetch_add(1, Ordering::Relaxed);
        }
        // SAFETY: GlobalAlloc's caller supplies the old allocation and requested size.
        unsafe { System.realloc(ptr, layout, size) }
    }
}
#[global_allocator]
static ALLOCATOR: Counting = Counting;
fn cpu_s() -> f64 {
    let mut ts = libc::timespec {
        tv_sec: 0,
        tv_nsec: 0,
    };
    // SAFETY: clock_gettime writes this initialized timespec.
    let status = unsafe { libc::clock_gettime(libc::CLOCK_THREAD_CPUTIME_ID, &raw mut ts) };
    assert_eq!(status, 0);
    ts.tv_sec as f64 + ts.tv_nsec as f64 * 1e-9
}
fn percentile(v: &mut [f64], p: f64) -> f64 {
    v.sort_by(f64::total_cmp);
    v.get(((v.len().saturating_sub(1)) as f64 * p) as usize)
        .copied()
        .unwrap_or(0.0)
}
fn lines<T: serde::de::DeserializeOwned>(
    path: &Path,
) -> Result<Vec<T>, Box<dyn std::error::Error>> {
    std::fs::read_to_string(path)?
        .lines()
        .filter(|l| !l.trim().is_empty())
        .map(|l| Ok(serde_json::from_str(l)?))
        .collect()
}
fn measure(engine: &Engine) -> serde_json::Value {
    let mut times = Vec::new();
    let mut bytes = 0;
    let mut allocations = 0;
    for _ in 0..31 {
        BYTES.store(0, Ordering::Relaxed);
        ALLOCS.store(0, Ordering::Relaxed);
        let start = cpu_s();
        TRACK.store(true, Ordering::Relaxed);
        let snap = black_box(engine.sim.snapshot());
        TRACK.store(false, Ordering::Relaxed);
        times.push((cpu_s() - start) * 1e6);
        bytes = BYTES.load(Ordering::Relaxed);
        allocations = ALLOCS.load(Ordering::Relaxed);
        black_box(snap);
    }
    json!({"song":engine.sim.s(),"run":engine.sim.run_s(),"storage":engine.sim.storage(),
        "snapshotAllocatedBytes":bytes,"snapshotInlineBytes":size_of::<sim::engine::Snapshot>(),
        "snapshotTotalCopiedBytes":bytes+size_of::<sim::engine::Snapshot>(),"snapshotAllocations":allocations,
        "snapshotCpuMicrosMedian":percentile(&mut times,0.5),"snapshotCpuMicrosP99":percentile(&mut times,0.99)})
}
fn main() -> Result<(), Box<dyn std::error::Error>> {
    batch::enter()?;
    let args: Vec<_> = std::env::args().skip(1).collect();
    let dir = PathBuf::from(args.first().ok_or("session folder required")?);
    let equal = args.get(1).is_some_and(|s| s == "equal");
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
    let meta: serde_json::Value = serde_json::from_slice(&std::fs::read(dir.join("meta.json"))?)?;
    let mut paths: Vec<_> = std::fs::read_dir(root.join("content"))?
        .map(|p| p.map(|p| p.path()))
        .collect::<Result<_, _>>()?;
    paths.retain(|p| p.extension().is_some_and(|s| s == "yaml"));
    paths.sort();
    let files: Vec<_> = paths
        .iter()
        .map(|p| {
            Ok(SourceFile {
                name: p.display().to_string(),
                text: std::fs::read_to_string(p)?,
            })
        })
        .collect::<Result<_, std::io::Error>>()?;
    let lib = load_content(&files).map_err(|e| format!("{e:?}"))?;
    let level = SourceFile {
        name: "survivors.yaml".into(),
        text: meta["runs"][0]["level"]
            .as_str()
            .ok_or("recorded level missing")?
            .into(),
    };
    let mut engine = if equal {
        eval::scale::density_engine(&level, &lib)?
    } else {
        Engine::new(
            &level,
            &lib,
            &indexmap::IndexMap::new(),
            0.2,
            true,
            true,
            f64::INFINITY,
        )
        .map_err(|e| format!("{e:?}"))?
    };
    let arrivals: Vec<Arrival> = lines(&dir.join("stream.jsonl"))?;
    let keys: Vec<AppliedKey> = if equal {
        Vec::new()
    } else {
        lines(&dir.join("keys.jsonl"))?
    };
    let (mut next, mut next_key) = (0, 0);
    let mut samples = Vec::new();
    let checkpoints = [59.9, 899.9, 1799.9];
    let mut frames = Vec::new();
    let mut rollback_cpu = Vec::new();
    let mut replayed = 0.0;
    let mut injected = 0;
    let start = Instant::now();
    for frame in 1..=(2300 * 120) {
        let now = f64::from(frame) / 120.0;
        let from = next;
        while next < arrivals.len() && arrivals[next].arrived <= now {
            next += 1;
        }
        if next > from {
            engine.feed(|log| {
                for a in &arrivals[from..next] {
                    rebase_append(log, &a.entry, 0.0);
                }
            });
        }
        while next_key < keys.len() && keys[next_key].s <= now {
            let k = &keys[next_key];
            engine.key(k.s, InputKey::parse_recorded(&k.key)?, k.pressed);
            next_key += 1;
        }
        let run = engine.sim.run_s();
        let stress = equal && (1499.9..1559.9).contains(&run);
        if stress && frame % 12 == 0 {
            engine.key(now - 0.025, InputKey::Space, (frame / 12) % 2 == 0);
            injected += 1;
        }
        let r0 = engine.stats.rollbacks;
        let rs0 = engine.stats.replayed_s;
        let t0 = cpu_s();
        engine.frame(now, if equal { 0.2 } else { 0.0 });
        let cpu = (cpu_s() - t0) * 1e3;
        if stress {
            frames.push(cpu);
            if engine.stats.rollbacks > r0 {
                rollback_cpu.push(cpu);
                replayed += engine.stats.replayed_s - rs0;
            }
        }
        engine.view.borrow_mut().take_moments();
        if samples.len() < checkpoints.len() && engine.sim.run_s() >= checkpoints[samples.len()] {
            let sample = measure(&engine);
            println!("{sample}");
            samples.push(sample);
        }
        if engine.done() || engine.fallen() {
            break;
        }
    }
    if equal {
        assert_eq!(samples.len(), 3);
        assert!(samples.iter().all(|s| s["storage"]["alive"] == 300));
        let first = samples[0]["snapshotAllocatedBytes"].as_u64().unwrap();
        let last = samples[2]["snapshotAllocatedBytes"].as_u64().unwrap();
        assert!(last <= first * 2, "snapshot age growth: {last}/{first}");
    }
    println!(
        "{}",
        json!({"equalDensity":equal,"damageExempt":equal,"wallSeconds":start.elapsed().as_secs_f64(),"samples":samples,
        "minute25Frames":frames.len(),"injectedLateKeys":injected,"minute25StepCpuMsP50":percentile(&mut frames,0.5),"minute25StepCpuMsP99":percentile(&mut frames,0.99),
        "minute25RollbackFrames":rollback_cpu.len(),"minute25RollbackCpuMsTotal":rollback_cpu.iter().sum::<f64>(),"minute25RollbackCpuMsP99":percentile(&mut rollback_cpu,0.99),"minute25ReplayedSongSeconds":replayed,
        "settledRecords":engine.settled_count,"settledHash":format!("{:016x}",engine.settled_hash),"retainedSettledLog":engine.settled_log.is_some(),"finalStorage":engine.sim.storage()})
    );
    Ok(())
}
