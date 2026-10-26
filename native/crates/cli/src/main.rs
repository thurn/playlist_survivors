//! `ps`: replay, oracle, causality, capture, log summaries and bench.

mod batch;
mod bench;
mod log;
mod parity;
mod replay;
mod report;
mod survive;
mod survive_cache;

#[allow(clippy::single_match_else)]
fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let code = match args.first().map(String::as_str) {
        Some("survive") => survive::main(&args[1..]),
        Some("analysis-parity") => parity::main(&args[1..]),
        Some("runner-bench") => bench::main(&args[1..]),
        Some("replay") => replay::main(&args[1..]),
        Some("log") => log::main(&args[1..]),
        Some("report") => report::main(&args[1..]),
        _ => {
            eprintln!(
                "usage: ps survive --playlist <m3u> [--minutes N] [--record DIR] [--fixture STREAM]\n       ps analysis-parity [--tracks ID,ID | --all] [--jobs N] [--out DIR] [--check]\n       ps runner-bench [--seconds S] [--check]\n       ps replay <session folder> [--reanalyse]\n       ps log [<timing.jsonl>] [--from S] [--to S] [--no-table]\n       ps report [<session folder>]"
            );
            2
        }
    };
    std::process::exit(code);
}
