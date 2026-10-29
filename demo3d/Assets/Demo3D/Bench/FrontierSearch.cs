using System;
using System.Collections.Generic;

namespace Demo3D.Bench
{
    public enum RunKind
    {
        /// <summary>A short run for the fit.</summary>
        Probe,
        /// <summary>A full run at the fit's candidate.</summary>
        Confirm,
        /// <summary>A full run one resolution step above a passing candidate.</summary>
        Above,
        /// <summary>A full run at the search's ceiling (capacity or n.search's top).</summary>
        Ceiling,
        /// <summary>A full run of the bisection fallback.</summary>
        Bisect,
        /// <summary>A full run at a count the spec lists (n.fixed), outside any search.</summary>
        Fixed,
    }

    public sealed class RunRequest
    {
        public readonly int N;
        public readonly RunKind Kind;

        public RunRequest(int n, RunKind kind)
        {
            N = n;
            Kind = kind;
        }

        public bool Full => Kind != RunKind.Probe;

        public string KindName => Kind.ToString().ToLowerInvariant();
    }

    public sealed class FrontierResult
    {
        /// <summary>The largest passing N, or null when even the range's bottom fails.</summary>
        public int? N;
        /// <summary>gpu, main, render (the worst resource of the smallest failing full run), spawns,
        /// lights, or capacity.</summary>
        public string Limit;
        public bool CapacityBound;
        public bool BelowRange;
        public int Ceiling;
        public int? FitCandidate;
        public string FitLimit;
        /// <summary>The fit's candidate passed and the run 5% above it failed: no bisection.</summary>
        public bool FitConfirmed;
        public int ProbeRuns, FullRuns;
        public bool HitRunCap;
    }

    /// <summary>
    /// The fitted search for one point (benchmarks.md, The sweep): three short probes, a line per
    /// resource (GPU, main thread, render thread) whose lowest crossing of its pass line is the
    /// candidate; a full run at the candidate must pass and one 5% above must fail; otherwise
    /// bisection between the closest full-run pass and fail, to the resolution. Probes start around
    /// a neighbour's frontier when one is given. The ceiling is the smaller of n.search's top and
    /// the view's packing capacity; a point that passes there is capacity-bound.
    ///
    /// A pure state machine: the caller asks <see cref="Next"/> for a run, measures it, and
    /// passes the result to <see cref="Report"/> until Next returns null.
    /// </summary>
    public sealed class FrontierSearch
    {
        public const int MaxFullRuns = 14;

        readonly int _lo, _ceiling;
        readonly double _res;
        readonly Lines _lines;
        readonly Queue<RunRequest> _pending = new Queue<RunRequest>();
        readonly List<RunResult> _probes = new List<RunResult>();
        readonly HashSet<int> _tested = new HashSet<int>();
        int? _pass; // the largest full-run pass
        RunResult _fail; // the smallest full-run fail above _pass
        bool _bisected;
        public readonly FrontierResult Result = new FrontierResult();
        public bool Done { get; private set; }

        public FrontierSearch(int lo, int hi, int capacity, double resolution, int? seed, Lines lines)
        {
            _lo = Math.Max(1, lo);
            _ceiling = Math.Max(_lo, Math.Min(hi, capacity));
            _res = resolution;
            _lines = lines;
            Result.Ceiling = _ceiling;
            foreach (var n in ProbeNs(_lo, _ceiling, seed)) _pending.Enqueue(new RunRequest(n, RunKind.Probe));
        }

        public int Ceiling => _ceiling;

        /// <summary>
        /// Three probes: around a neighbour's frontier (0.75, 1, 1.25 times it), or spread over the
        /// range (20%, 50% and 80% of the way to the ceiling). Clamped and deduplicated.
        /// </summary>
        public static List<int> ProbeNs(int lo, int ceiling, int? seed)
        {
            var raw = new List<double>();
            if (seed.HasValue && seed.Value > 0)
                foreach (var f in new[] { 0.75, 1.0, 1.25 }) raw.Add(seed.Value * f);
            else
                foreach (var f in new[] { 0.2, 0.5, 0.8 }) raw.Add(lo + (ceiling - lo) * f);
            var ns = new SortedSet<int>();
            foreach (var r in raw) ns.Add(Math.Max(lo, Math.Min(ceiling, (int)Math.Round(r))));
            return new List<int>(ns);
        }

        public RunRequest Next() => Done || _pending.Count == 0 ? null : _pending.Peek();

        public void Report(RunRequest req, RunResult r)
        {
            if (Done || _pending.Count == 0 || !ReferenceEquals(_pending.Peek(), req))
                throw new InvalidOperationException("FrontierSearch: report the run Next returned");
            _pending.Dequeue();
            r.N = req.N;
            PassRule.Evaluate(r, _lines);
            if (!req.Full)
            {
                Result.ProbeRuns++;
                _probes.Add(r);
                if (_pending.Count == 0) AfterProbes();
                return;
            }
            Result.FullRuns++;
            _tested.Add(req.N);
            if (req.Kind == RunKind.Bisect) _bisected = true;
            if (r.Pass)
            {
                _pass = req.N;
                if (_fail != null && _fail.N <= req.N) _fail = null; // a contradiction: trust the newer run
            }
            else
            {
                if (_fail == null || req.N < _fail.N) _fail = r;
                if (_pass.HasValue && _pass.Value >= req.N) _pass = null;
            }

            if (Result.FullRuns >= MaxFullRuns)
            {
                Result.HitRunCap = true;
                Finish();
                return;
            }
            if ((req.Kind == RunKind.Confirm || req.Kind == RunKind.Ceiling) && r.Pass && req.N < _ceiling)
            {
                int above = Math.Min(_ceiling, (int)Math.Ceiling(req.N * (1 + _res)));
                if (above <= req.N) above = req.N + 1;
                Enqueue(above, RunKind.Above);
                return;
            }
            NextStep();
        }

        void AfterProbes()
        {
            var x = new List<double>();
            foreach (var p in _probes) x.Add(p.N);
            double best = double.PositiveInfinity;
            string limit = null;
            Cross("gpu", x, _probes.ConvertAll(p => p.GpuP99), _lines.GpuMs, ref best, ref limit);
            Cross("main", x, _probes.ConvertAll(p => p.MainP99), _lines.MainMs, ref best, ref limit);
            Cross("render", x, _probes.ConvertAll(p => p.RenderP99), _lines.RenderMs, ref best, ref limit);
            int candidate;
            if (_probes.Count < 2)
            {
                // One probe (a range of one count): no line, so confirm the probe itself.
                candidate = _probes.Count == 1 ? _probes[0].N : _ceiling;
                limit = null;
            }
            else if (best >= _ceiling)
            {
                candidate = _ceiling;
                limit = limit == null ? "capacity" : limit;
            }
            else candidate = Math.Max(_lo, (int)Math.Floor(best));
            Result.FitCandidate = candidate;
            Result.FitLimit = limit;
            Enqueue(candidate, candidate >= _ceiling ? RunKind.Ceiling : RunKind.Confirm);
        }

        static void Cross(string name, List<double> x, List<double> y, double line, ref double best, ref string limit)
        {
            if (!Stats.FitLine(x, y, out var a, out var b)) return;
            if (double.IsNaN(a) || double.IsNaN(b)) return;
            double at;
            if (b <= 1e-12) at = a > line ? 0 : double.PositiveInfinity; // flat: always or never over
            else at = (line - a) / b;
            if (at < best)
            {
                best = at;
                limit = name;
            }
        }

        void NextStep()
        {
            if (_pass.HasValue && _fail != null)
            {
                if (_fail.N <= Math.Ceiling(_pass.Value * (1 + _res)) || _fail.N - _pass.Value <= 1)
                {
                    Finish();
                    return;
                }
                int mid = (int)Math.Round(Math.Sqrt((double)_pass.Value * _fail.N));
                mid = Math.Max(_pass.Value + 1, Math.Min(_fail.N - 1, mid));
                Enqueue(mid, RunKind.Bisect);
                return;
            }
            if (_pass.HasValue)
            {
                // Everything tested at full length passed: look higher, first at a failing probe.
                if (_pass.Value >= _ceiling) { Finish(); return; }
                int? upper = null;
                foreach (var p in _probes)
                    if (!p.Pass && p.N > _pass.Value && !_tested.Contains(p.N) && (upper == null || p.N < upper))
                        upper = p.N;
                if (upper.HasValue) Enqueue(upper.Value, RunKind.Bisect);
                else if (!_tested.Contains(_ceiling)) Enqueue(_ceiling, RunKind.Ceiling);
                else Finish();
                return;
            }
            if (_fail != null)
            {
                // Everything failed so far: look lower, first at a passing probe.
                if (_fail.N <= _lo) { Finish(); return; }
                int? lower = null;
                foreach (var p in _probes)
                    if (p.Pass && p.N < _fail.N && !_tested.Contains(p.N) && (lower == null || p.N > lower))
                        lower = p.N;
                int next = lower ?? Math.Max(_lo, (int)Math.Floor(_fail.N * 0.7));
                if (next >= _fail.N) next = _lo;
                Enqueue(next, RunKind.Bisect);
                return;
            }
            Finish();
        }

        void Enqueue(int n, RunKind kind) => _pending.Enqueue(new RunRequest(n, kind));

        void Finish()
        {
            Done = true;
            _pending.Clear();
            Result.N = _pass;
            Result.BelowRange = !_pass.HasValue;
            Result.CapacityBound = _pass.HasValue && _pass.Value >= _ceiling;
            Result.FitConfirmed = !_bisected && _pass.HasValue && _pass == Result.FitCandidate
                                  && (Result.CapacityBound || _fail != null);
            if (Result.CapacityBound) Result.Limit = "capacity";
            else if (_fail != null) Result.Limit = _fail.Worst;
            else Result.Limit = Result.FitLimit;
        }
    }
}
