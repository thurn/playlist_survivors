using System;

namespace PS.Game.Sim
{
    /// <summary>One stride step as the grid emitted it.</summary>
    public struct StrideStepRecord
    {
        public double T;
        /// <summary>The planned time to the next step.</summary>
        public double Period;
        /// <summary>The halvings of the beat (-1 on the free pulse).</summary>
        public int Halvings;
    }

    /// <summary>
    /// The stride grid (unity-game.md, "Key Presses and Late Steps" and "Speeds Follow the Tempo"):
    /// the times the hero may stride on.
    ///
    /// - Each beat interval is cut into 2^h equal steps. A song's first interval takes the fewest
    ///   halvings that bring the step to <see cref="MaxPeriod"/> or less, so a 149 BPM song strides
    ///   every 0.20 s and a 151 BPM one every 0.40 s.
    /// - <b>Hysteresis</b>: later intervals keep h while the step stays within
    ///   [<see cref="BandLow"/>, <see cref="BandHigh"/>]. Outside it, h switches to the fewest
    ///   halvings again, and only on an interval that starts on a downbeat.
    /// - With no beat interval known (<see cref="Reliability.NoBeatAhead"/>), the grid is a free
    ///   pulse every <see cref="FreePeriod"/> from the last step. The grid joins the next beat
    ///   interval known at its first step that is at least half a step after the last one, and
    ///   chooses its halvings afresh there: a beatless stretch already broke the song's pace.
    ///
    /// An interval's halvings are decided when the grid enters it, from what is revealed then, and
    /// never revised.
    /// </summary>
    public sealed class StrideGrid
    {
        public const double MaxPeriod = 0.4;
        public const double BandLow = 0.18;
        public const double BandHigh = 0.45;
        public const double FreePeriod = 0.35;
        const int MaxHalvings = 8;
        const int HistoryLength = 64;

        enum PlanKind { Continue, Boundary, Enter, Free }

        struct Plan
        {
            public PlanKind Kind;
            public double Time;
            public int Beat, Halvings, K;
        }

        readonly BeliefLog log;
        readonly BeatClock clock;
        readonly StrideStepRecord[] history = new StrideStepRecord[HistoryLength];
        Plan plan;

        // The interval being stepped: from beat segBeat, segLen long, in segN steps; nextK is next.
        bool inSegment;
        int segBeat, segN, nextK;
        double segStart, segLen;
        double lastStep = double.NegativeInfinity;
        double freeFrom;
        int scan;

        public StrideGrid(BeliefLog log, BeatClock clock, double startTime)
        {
            this.log = log;
            this.clock = clock;
            freeFrom = startTime;
        }

        /// <summary>How many steps have been emitted.</summary>
        public int Count { get; private set; }

        /// <summary>The halvings in use (-1 on the free pulse).</summary>
        public int Halvings { get; private set; } = -1;

        /// <summary>The latest step's time (-infinity before any).</summary>
        public double LastStep => lastStep;

        /// <summary>The latest step's planned period (the free pulse's before any).</summary>
        public double Period => Count > 0 ? Recent(0).Period : FreePeriod;

        /// <summary>A recent step: 0 is the latest, up to <see cref="RecentCount"/> - 1.</summary>
        public StrideStepRecord Recent(int back)
        {
            if (back < 0 || back >= RecentCount) throw new ArgumentOutOfRangeException(nameof(back));
            return history[(Count - 1 - back) % HistoryLength];
        }

        public int RecentCount => Math.Min(Count, HistoryLength);

        /// <summary>The fewest halvings that bring a beat interval to <see cref="MaxPeriod"/> or less.</summary>
        public static int FewestHalvings(double interval)
        {
            int h = 0;
            while (h < MaxHalvings && interval / (1 << h) > MaxPeriod + 1e-9) h++;
            return h;
        }

        /// <summary>The halvings for the interval from beat <paramref name="beat"/>, <paramref name="len"/> long, given the current ones.</summary>
        int ChooseHalvings(int beat, double len, int current)
        {
            int fewest = FewestHalvings(len);
            if (current < 0) return fewest;
            double step = len / (1 << current);
            if (step >= BandLow - 1e-9 && step <= BandHigh + 1e-9) return current;
            return clock.IsDownbeat(beat) ? fewest : current;
        }

        /// <summary>The next step's time at or after <paramref name="floor"/>, from what is revealed now.</summary>
        internal double NextTime(double floor)
        {
            plan = MakePlan(floor);
            return plan.Time;
        }

        Plan MakePlan(double floor)
        {
            if (inSegment)
            {
                if (nextK < segN)
                    return new Plan { Kind = PlanKind.Continue, Time = segStart + nextK * segLen / segN };
                return new Plan { Kind = PlanKind.Boundary, Time = log.Beats[segBeat + 1].T };
            }
            var beats = log.Beats;
            double free = Math.Max(Math.Max(lastStep, freeFrom) + FreePeriod, floor);
            while (scan < beats.Length && beats[scan].T <= lastStep + 1e-9) scan++;
            for (int j = scan; j < beats.Length && beats[j].T <= free + 1e-9; j++)
            {
                if (!clock.StartsInterval(j)) continue;
                double len = beats[j + 1].T - beats[j].T;
                int h = ChooseHalvings(j, len, -1);
                int n = 1 << h;
                double step = len / n;
                double from = Math.Max(lastStep + 0.5 * step, floor);
                int k = Math.Max(0, (int)Math.Ceiling((from - beats[j].T) / step - 1e-9));
                if (k >= n) continue;
                double at = beats[j].T + k * step;
                if (at > free + 1e-9) break;
                return new Plan { Kind = PlanKind.Enter, Time = at, Beat = j, Halvings = h, K = k };
            }
            return new Plan { Kind = PlanKind.Free, Time = free };
        }

        internal void Fire(Simulation sim, double t)
        {
            double period;
            int beat;
            switch (plan.Kind)
            {
                case PlanKind.Continue:
                    nextK++;
                    period = segLen / segN;
                    beat = segBeat;
                    break;
                case PlanKind.Boundary:
                {
                    int j = segBeat + 1;
                    if (clock.StartsInterval(j))
                    {
                        var beats = log.Beats;
                        double len = beats[j + 1].T - beats[j].T;
                        Halvings = ChooseHalvings(j, len, Halvings);
                        Enter(j, len, 1);
                        period = segLen / segN;
                        beat = j;
                    }
                    else
                    {
                        inSegment = false;
                        Halvings = -1;
                        period = FreePeriod;
                        beat = -1;
                    }
                    break;
                }
                case PlanKind.Enter:
                {
                    var beats = log.Beats;
                    Halvings = plan.Halvings;
                    Enter(plan.Beat, beats[plan.Beat + 1].T - beats[plan.Beat].T, plan.K + 1);
                    period = segLen / segN;
                    beat = plan.Beat;
                    break;
                }
                default:
                    Halvings = -1;
                    period = FreePeriod;
                    beat = -1;
                    break;
            }
            lastStep = t;
            history[Count % HistoryLength] = new StrideStepRecord { T = t, Period = period, Halvings = Halvings };
            sim.Emit(SimEvent.StrideStep(t, Count, Halvings, beat, period));
            Count++;
        }

        void Enter(int beat, double len, int next)
        {
            inSegment = true;
            segBeat = beat;
            segStart = log.Beats[beat].T;
            segLen = len;
            segN = 1 << Halvings;
            nextK = next;
        }
    }
}
