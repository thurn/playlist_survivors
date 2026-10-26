//! Kept diagnostic, copied temporarily into cli/examples by the evidence runner.
use std::path::{Path,PathBuf};
use sim::content::{SourceFile,load_content};
fn root()->PathBuf {Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")}
fn main() {
 let root=root();
 let mut paths:Vec<_>=std::fs::read_dir(root.join("content")).unwrap().map(|p|p.unwrap().path()).filter(|p|p.extension().is_some_and(|x|x=="yaml")).collect();paths.sort();
 let lib=load_content(&paths.iter().map(|p|SourceFile{name:p.display().to_string(),text:std::fs::read_to_string(p).unwrap()}).collect::<Vec<_>>()).unwrap();
 let level=SourceFile{name:"survivors.yaml".into(),text:std::fs::read_to_string(root.join("levels/survivors.yaml")).unwrap()};
 let file=stream::StreamFile::parse(&std::fs::read_to_string(root.join("public/reactive/house-128/stream.json")).unwrap()).unwrap();
 let beats:Vec<_>=file.log.beats.iter().map(|b|b.t).collect();
 let mut replay=stream::Replay::new(file.log);
 let mut engine=eval::scale::density_engine(&level,&lib).unwrap();
 let mut next=0;
 for frame in 1..=3600 {
  let s=f64::from(frame)/120.0;
  engine.feed(|l|replay.feed_to(l,s));
  while next<beats.len() && beats[next]+0.025<=s {
   engine.key(beats[next],sim::movement::InputKey::Space,true);
   engine.key(beats[next]+0.08,sim::movement::InputKey::Space,false);next+=1;
  }
  engine.frame(s,0.23);
  engine.view.borrow_mut().take_moments();
 }
 engine.frame(30.5,0.0);
 println!("{}",serde_json::json!({"seconds":30,"lead":0.23,"late_ms":25,"settled_records":engine.settled_count,"settled_hash":format!("{:016x}",engine.settled_hash),"rollbacks":engine.stats.rollbacks,"live":engine.view.borrow().alive().count(),"gems":engine.view.borrow().gems.len()}));
}
