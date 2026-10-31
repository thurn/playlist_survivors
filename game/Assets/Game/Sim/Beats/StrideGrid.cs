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
    /// The stride grid (unity-game.md, "Key Presses" and "Speeds Follow the Tempo"):
    /// the times the hero may stride on.
    ///
    /// - Each beat interval is cut into 2^h equal steps. A song's first interval takes the fewest
    ///   halvings that bring the step to <see cref="MaxPeriod"/> or less, so a 149 BPM song strides
    ///   every 0.20 s and a 151 BPM one every 0.40 s.
    /// - <b>Hysteresis</b>: later intervals keep h while the step stays within
    ///   [<see cref="BandLow"/>, <see cref="BandHigh"/>]. Outside it, h switches to the fewest
    ///   halvings again, and only on an interval that starts on a downbeat, or on any beat while
    ///   no lock has given the song bars.
    /// - <b>The latest beat continues</b>: committed beats come from hindsight in batches, so the
    ///   next beat is often not yet known when the grid reaches the latest one. The grid keeps
    ///   stepping at its step period from that beat (as native's last grid time keeps the step
    ///   before it), for up to <see cref="BeatClock.TrackingGap"/> of the beat's period, and joins
    ///   the real interval at its next step once the closing beat is revealed.
    /// - With no beat interval known otherwise (<see cref="Reliability.NoBeatAhead"/>), the grid is
    ///   a free pulse every <see cref="FreePeriod"/> from the last step, and chooses its halvings
    ///   afresh when it joins a beat: a beatless stretch already broke the song's pace.
    /// - The grid joins a beat interval (from the one holding the last step on) at its first step
    ///   that is at least half a step after the last one, if that comes no later than the next
    ///   free step, or within one and a half steps of the last while continuing a beat.
    ///
    /// Each step is decided from what is revealed when the grid reaches it, and never revised.
    /// </summary>
    public sealed class StrideGrid
    {
        public const double MaxPeriod = 0.4;
        public const double BandLow = 0.18;
        public const double BandHigh = 0.45;
        public const double FreePeriod = 0.35;
        const int MaxHalvings = 8;
        internal const int HistoryLength = 64;

        enum PlanKind { Continue, Boundary, Enter, Carry, Free }

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
        // Past the latest beat known (carryBeat), stepping on at carryPeriod until carryUntil.
        int carryBeat = -1;
        double carryPeriod, carryUntil;
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
            int bar = clock.BarOf(beat, out bool downbeat);
            return downbeat || bar < 0 ? fewest : current;
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
            var fallback = carryBeat >= 0 && lastStep + carryPeriod <= carryUntil + 1e-9
                ? new Plan { Kind = PlanKind.Carry, Time = Math.Max(lastStep + carryPeriod, floor) }
                : new Plan { Kind = PlanKind.Free, Time = Math.Max(Math.Max(lastStep, freeFrom) + FreePeriod, floor) };
            // A carried step yields to the real grid's next step even when that is a little later.
            double bound = fallback.Kind == PlanKind.Carry ? lastStep + 1.5 * carryPeriod : fallback.Time;
            var beats = log.Beats;
            while (scan + 1 < beats.Length && beats[scan + 1].T <= lastStep + 1e-9) scan++;
            for (int j = scan; j < beats.Length && beats[j].T <= bound + 1e-9; j++)
            {
                if (!clock.StartsInterval(j)) continue;
                double len = beats[j + 1].T - beats[j].T;
                int h = ChooseHalvings(j, len, Halvings);
                int n = 1 << h;
                double step = len / n;
                double from = Math.Max(lastStep + 0.5 * step, floor);
                int k = Math.Max(0, (int)Math.Ceiling((from - beats[j].T) / step - 1e-9));
                if (k >= n) continue;
                double at = beats[j].T + k * step;
                if (at > bound + 1e-9) break;
                return new Plan { Kind = PlanKind.Enter, Time = at, Beat = j, Halvings = h, K = k };
            }
            return fallback;
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
                    else if (clock.Continues(j, t))
                    {
                        // The latest beat known: carry on at the step period until its interval is.
                        inSegment = false;
                        carryBeat = j;
                        carryPeriod = segLen / segN;
                        carryUntil = log.Beats[j].T + BeatClock.TrackingGap * log.Beats[j].V0;
                        period = carryPeriod;
                        beat = j;
                    }
                    else
                    {
                        // A tracking gap: the next beat is known, and too far away.
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
                case PlanKind.Carry:
                    period = carryPeriod;
                    beat = carryBeat;
                    break;
                default:
                    carryBeat = -1;
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
            carryBeat = -1;
            segBeat = beat;
            segStart = log.Beats[beat].T;
            segLen = len;
            segN = 1 << Halvings;
            nextK = next;
        }
    }
}
