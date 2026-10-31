using System;
using System.Collections.Generic;
using PS.Game.Sim;

namespace Game.Client.Look
{
    /// <summary>What the fast lane shows at one song time (<see cref="FastLane.Read"/>).</summary>
    public struct LaneFrame
    {
        /// <summary>The drum bands' envelopes (0 to 1): each band's latest hit decaying from when it sounds, the bass's slow release.</summary>
        public double Kick, Snare, Hat, Bass;
        /// <summary>The next kick's anticipation, rising to it over <see cref="FastLane.AnticipateS"/> before it sounds.</summary>
        public double KickPre;
        /// <summary>A drop's build-up, rising to its loudness jump over <see cref="FastLane.DropRiseS"/> before it lands.</summary>
        public double Rise;
        /// <summary>The beat's pulse: 1 on a reliable beat (<see cref="FastLane.BelowGatePulse"/> below the gate), decaying as (1 - u)² over it; 0 with no beat known ahead.</summary>
        public double Pulse;
        /// <summary>The latest beat's index plus the phase into it (-1 before any), its period, and the time since it and since its bar's downbeat.</summary>
        public double Beat, Period, SinceBeat, SinceDownbeat;
        /// <summary>The energy over the last 0.5 s, and the loudness level over the last 2 s (0 quiet, 1 medium, 2 loud; -1 before any tick).</summary>
        public double Energy, Level;
    }

    /// <summary>
    /// The fast lane (unity-game.md, "Post-processing and Camera"; native's
    /// native/crates/app/src/slice/lane.rs): presentation that reads the belief stream directly,
    /// beside the sim. It reads the sim's revealed entries, every one known by the frontier, which
    /// runs ahead of frameSong by the lead, so it sees the music up to about 2.45 s before it is
    /// heard (M - A - B). An entry's effect shows when it sounds, at its <c>t</c>, or when it became
    /// known if that is later. From them come the drum-band envelopes, the kick's anticipation about
    /// 90 ms ahead, a drop's build-up in the 1.6 s before it, the beat's pulse, the energy and
    /// loudness, and the accents (strong onsets, as native's flashes) and drops (loudness jumps),
    /// which it adds to the feel's moments as it learns them. Reading at a song time is a function of the revealed stream, so
    /// fixed-time frames reproduce. Nothing is allocated per frame once its lists have grown.
    /// </summary>
    public sealed class FastLane
    {
        /// <summary>Each drum band's hit decays over this long from when it sounds, and the bass level releases over this long (s).</summary>
        public const double KickDecayS = 0.11, SnareDecayS = 0.09, HatDecayS = 0.045, BassReleaseS = 0.22;
        /// <summary>No envelope looks further back than this (s).</summary>
        public const double EnvelopeWindowS = 0.6;
        /// <summary>A kick is anticipated over this long before it sounds (s); a tick's kick flux only above the floor.</summary>
        public const double AnticipateS = 0.09, KickPreFloor = 0.4;
        /// <summary>A loudness jump of at least <see cref="DropDb"/> builds over this long before it lands (s).</summary>
        public const double DropRiseS = 1.6, DropDb = 6;
        /// <summary>Accents come at most this often (s); an onset's strength is measured against a running peak halving over this long (s).</summary>
        public const double AccentGapS = 0.5, PeakHalfS = 8;
        /// <summary>The energy is averaged over this long (s), the loudness level over this long (s).</summary>
        public const double EnergyS = 0.5, LevelS = 2;
        /// <summary>The pulse's strength on beats known ahead but below the gate or in unclear music.</summary>
        public const double BelowGatePulse = 0.6;
        /// <summary>The period assumed before any beat (s).</summary>
        public const double DefaultPeriod = 0.5;

        readonly List<double> _shares = new List<double>(4096);
        double _peak, _peakAt, _lastAccent = double.NegativeInfinity;
        int _jumps;

        /// <summary>
        /// The lane at song time <paramref name="s"/> from the entries revealed in <paramref name="log"/>,
        /// with <paramref name="beats"/> for the beat's state and bars. Newly learned accents go to
        /// <paramref name="moments"/> (when given) as <see cref="FeelMoments.Accent"/>s.
        /// </summary>
        public LaneFrame Read(BeliefLog log, BeatClock beats, double s, FeelMoments moments = null)
        {
            Learn(log, moments);
            var f = new LaneFrame { Beat = -1, Level = -1, Period = DefaultPeriod, SinceBeat = double.PositiveInfinity, SinceDownbeat = double.PositiveInfinity };
            ReadBeat(log, beats, s, ref f);
            ReadTicks(log, s, ref f);
            ReadOnsets(log, s, ref f);
            ReadJumps(log, s, ref f);
            return f;
        }

        /// <summary>When an entry's effect shows: when it sounds, or when it became known if later.</summary>
        static double Shown(double t, double knownAt) => Math.Max(t, knownAt);

        /// <summary>
        /// Each onset's strength share as it is revealed, and the strong ones as accents, at most one
        /// per <see cref="AccentGapS"/>; each loudness jump of at least <see cref="DropDb"/> as a drop.
        /// </summary>
        void Learn(BeliefLog log, FeelMoments moments)
        {
            int jumps = log.Count(EntryKind.Jump);
            for (; _jumps < jumps; _jumps++)
            {
                var jump = log.Get(EntryKind.Jump, _jumps).AsJump;
                if (jump.Db >= DropDb) moments?.Drop(Shown(jump.T, log.KnownAt(EntryKind.Jump, _jumps)), jump.Db);
            }
            int n = log.Count(EntryKind.Onset);
            for (int j = _shares.Count; j < n; j++)
            {
                var o = log.Get(EntryKind.Onset, j).AsOnset;
                double knownAt = log.KnownAt(EntryKind.Onset, j);
                double decay = Math.Pow(0.5, Math.Max(0, knownAt - _peakAt) / PeakHalfS);
                _peak = Math.Max(o.Strength, _peak * decay);
                _peakAt = knownAt;
                double share = _peak > 0 ? Math.Sqrt(o.Strength / _peak) : 0;
                _shares.Add(share);
                double at = Shown(o.T, knownAt);
                if (o.Strong && at - _lastAccent >= AccentGapS - 1e-9)
                {
                    _lastAccent = at;
                    moments?.Accent(at, share);
                }
            }
        }

        static void ReadBeat(BeliefLog log, BeatClock beats, double s, ref LaneFrame f)
        {
            var list = log.Beats;
            int i = LastAtOrBefore(list, s);
            while (i >= 0 && log.KnownAt(EntryKind.Beat, i) > s + 1e-9) i--;
            if (i < 0) return;
            double period = beats.PeriodOf(i);
            double since = s - list[i].T;
            f.Period = period;
            f.SinceBeat = since;
            double u = Math.Min(1, since / period);
            f.Beat = i + u;
            for (int d = i; d >= 0 && d >= i - 1; d--)
                if (beats.IsDownbeat(d)) { f.SinceDownbeat = s - list[d].T; break; }
            // The beat continues for TrackingGap periods while the next isn't known (BeatClock.Continues).
            // Beats below the gate are still the music's: they pulse, more softly.
            var state = beats.StateAt(s);
            if (since <= BeatClock.TrackingGap * period && state != Reliability.NoBeatAhead)
                f.Pulse = (state == Reliability.Reliable ? 1 : BelowGatePulse) * (1 - u) * (1 - u);
        }

        static void ReadTicks(BeliefLog log, double s, ref LaneFrame f)
        {
            var ticks = log.Ticks;
            int end = LastAtOrBefore(ticks, s + AnticipateS) + 1;
            int energyN = 0, levelN = 0;
            double energy = 0, level = 0;
            for (int k = end - 1; k >= 0; k--)
            {
                var t = ticks[k].AsTick;
                double age = s - t.T;
                if (age >= LevelS) break;
                if (age < 0)
                {
                    double strong = (t.Kick - KickPreFloor) / (1 - KickPreFloor);
                    f.KickPre = Math.Max(f.KickPre, Pre(strong, -age));
                    continue;
                }
                if (log.KnownAt(EntryKind.Tick, k) > s + 1e-9) continue;
                level += (int)t.Level;
                levelN++;
                if (age < EnergyS) { energy += t.Energy; energyN++; }
                if (age >= EnvelopeWindowS) continue;
                f.Kick = Math.Max(f.Kick, Hit(t.Kick, age, KickDecayS));
                f.Snare = Math.Max(f.Snare, Hit(t.Snare, age, SnareDecayS));
                f.Hat = Math.Max(f.Hat, Hit(t.Hat, age, HatDecayS));
                f.Bass = Math.Max(f.Bass, Hit(t.Bass, age, BassReleaseS));
            }
            if (energyN > 0) f.Energy = energy / energyN;
            if (levelN > 0) f.Level = level / levelN;
        }

        void ReadOnsets(BeliefLog log, double s, ref LaneFrame f)
        {
            var onsets = log.Revealed(EntryKind.Onset);
            for (int j = _shares.Count - 1; j >= 0; j--)
            {
                var o = onsets[j].AsOnset;
                double age = s - o.T;
                if (age >= EnvelopeWindowS) break;
                if (age < -AnticipateS || !o.HasBand) continue;
                if (age < 0)
                {
                    if (o.Band == DrumBand.Kick) f.KickPre = Math.Max(f.KickPre, Pre(_shares[j], -age));
                    continue;
                }
                // A late-known onset shows from when it became known, never back-dated.
                if (log.KnownAt(EntryKind.Onset, j) > s + 1e-9) continue;
                switch (o.Band)
                {
                    case DrumBand.Kick: f.Kick = Math.Max(f.Kick, Hit(_shares[j], age, KickDecayS)); break;
                    case DrumBand.Snare: f.Snare = Math.Max(f.Snare, Hit(_shares[j], age, SnareDecayS)); break;
                    case DrumBand.Hat: f.Hat = Math.Max(f.Hat, Hit(_shares[j], age, HatDecayS)); break;
                }
            }
        }

        /// <summary>The next drop already known: its build, easing in, as strong as the jump.</summary>
        static void ReadJumps(BeliefLog log, double s, ref LaneFrame f)
        {
            var jumps = log.Revealed(EntryKind.Jump);
            for (int j = 0; j < jumps.Length; j++)
            {
                var jump = jumps[j].AsJump;
                if (jump.T <= s || jump.T - s >= DropRiseS || jump.Db < DropDb) continue;
                double u = 1 - (jump.T - s) / DropRiseS;
                f.Rise = Math.Min(1, jump.Db / 12) * u * u * (3 - 2 * u);
                break;
            }
        }

        static double Hit(double v, double age, double decay) => Clamp01(v) * Math.Exp(-Math.Max(0, age) / decay);

        /// <summary>Rising to v as it sounds, <paramref name="lead"/> s ahead.</summary>
        static double Pre(double v, double lead)
        {
            double k = Math.Max(0, 1 - lead / AnticipateS);
            return Clamp01(v) * k * k;
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>The last entry at or before <paramref name="s"/> in a list sorted by t, or -1.</summary>
        static int LastAtOrBefore(ReadOnlySpan<StreamEntry> list, double s)
        {
            int lo = 0, hi = list.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (list[mid].T <= s + 1e-9) lo = mid + 1;
                else hi = mid;
            }
            return lo - 1;
        }
    }
}
