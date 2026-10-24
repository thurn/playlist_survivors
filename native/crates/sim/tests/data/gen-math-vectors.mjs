// More V8 vectors for jsmath, beyond F2's 1000 per function: run with the Node that made F2's
// (24.16.0, V8 13.6.233.17-node.49; it refuses another), from native/:
//   node crates/sim/tests/data/gen-math-vectors.mjs | gzip -9 > crates/sim/tests/data/math-vectors-extra.jsonl.gz
// Same line format as native/oracle/math-vectors.jsonl.gz. Half of each function's vectors are
// over the ranges the game uses, half random finite bit patterns, from a fixed seed; then pow
// with exponent 0.5 on a fine grid.
if (process.versions.v8 !== "13.6.233.17-node.49") throw new Error(`V8 ${process.versions.v8}, not F2's`);
const b = new Float64Array(1), u = new BigUint64Array(b.buffer);
const hex = (x) => { b[0] = x; return u[0].toString(16).padStart(16, "0"); };
let s = 0x9e3779b9;
const rnd = () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
const bits = () => { for (;;) { u[0] = (BigInt(Math.floor(rnd() * 2 ** 32)) << 32n) | BigInt(Math.floor(rnd() * 2 ** 32)); if (Number.isFinite(b[0])) return b[0]; } };
const range = (lo, hi) => lo + (hi - lo) * rnd();
const logRange = (lo, hi) => Math.exp(range(Math.log(lo), Math.log(hi)));
const N = 5000;
const fns = {
  sin: [Math.sin, () => [rnd() < 0.5 ? range(-10, 10) : range(-1e4, 1e4)]],
  cos: [Math.cos, () => [rnd() < 0.5 ? range(-10, 10) : range(-1e4, 1e4)]],
  exp: [Math.exp, () => [range(-50, 50)]],
  log: [Math.log, () => [logRange(1e-9, 1e9)]],
  log2: [Math.log2, () => [logRange(1e-9, 1e9)]],
  atan2: [Math.atan2, () => [range(-2000, 2000), range(-2000, 2000)]],
  pow: [Math.pow, () => [rnd() < 0.5 ? range(0, 4) : logRange(1e-6, 1e6), rnd() < 0.5 ? [2, 3, 0.5, 1.5, -1, -2][Math.floor(rnd() * 6)] : range(-8, 8)]],
  hypot: [Math.hypot, () => [range(-2000, 2000), range(-2000, 2000)]],
};
const out = [];
// pow(x, 0.5) on a fine grid: V8 returns sqrt(x), the platform pow is an ulp off on some x.
for (let i = 0; i < 4000; i++) {
  const x = i < 2000 ? i / 500 + rnd() * 1e-3 : logRange(1e-12, 1e12);
  out.push(JSON.stringify({ fn: "pow", args: [hex(x), hex(0.5)], out: hex(Math.pow(x, 0.5)) }));
}
for (const [name, [f, game]] of Object.entries(fns)) {
  for (let i = 0; i < N; i++) {
    const args = i < N / 2 ? game() : f.length === 2 ? [bits(), bits()] : [bits()];
    out.push(JSON.stringify({ fn: name, args: args.map(hex), out: hex(f(...args)) }));
  }
}
process.stdout.write(out.join("\n") + "\n");
