using System;
using PS.Game.Sim;

namespace Game.Client.Look
{
    /// <summary>
    /// The moments the post stack and the camera react to (unity-game.md, "Post-processing and
    /// Camera"; demo3d's PostFeel, CameraFeel, BlowFeel and BreakMoments): accents from the fast
    /// lane, the hero's heavy strikes and structure breaks from the sim's events, and drops. Each
    /// has a flash (the post's exposure, aberration and saturation spike, gone within a beat), a
    /// punch (a zoom-in over a sixteenth, released over a beat) and a shake (metres, decaying over
    /// its beat). Moments arrive ahead of frameSong (events by the lead, accents by up to the
    /// lookahead) and in no particular order, so they sit in a ring and each query takes the
    /// strongest still running. Every query is a function of song time, so fixed-time frames
    /// reproduce. Nothing is allocated after construction.
    /// </summary>
    public sealed class FeelMoments
    {
        public enum Kind { Accent, Blow, Break, Drop }

        /// <summary>A punch's zoom-in on an accent, a weight-1 blow's on top of it, and a break's.</summary>
        public const float AccentPunch = 0.02f, BlowPunch = 0.03f, BreakPunch = 0.035f, DropPunch = 0.03f;
        /// <summary>A weight-1 blow's flash and shake (m); the shake's cap, which breaks and drops may pass up to <see cref="BreakShakeCap"/>.</summary>
        public const float BlowFlash = 0.6f, BlowShake = 0.12f, ShakeCap = 0.25f, BreakShakeCap = 0.4f;
        /// <summary>A blow's weight is 0 at or below <see cref="CrispKills"/> kills and 1 at <see cref="HeavyKills"/>, log-scaled between (BlowFeel).</summary>
        public const int CrispKills = 2, HeavyKills = 24;
        /// <summary>Moments older than this no longer matter (s): every effect is over within a beat of the slowest tempo.</summary>
        const double Horizon = 2.0;
        public const int Capacity = 256;

        struct Moment
        {
            public double T;
            public Kind Kind;
            public int Kills;
            public float Flash, Punch, Shake;
        }

        readonly Moment[] _ring = new Moment[Capacity];
        int _next, _count;

        public int Count => _count;

        /// <summary>A strong onset's accent at its share of the running peak (0 to 1).</summary>
        public void Accent(double t, double share) =>
            Add(new Moment { T = t, Kind = Kind.Accent, Flash = (float)share, Punch = AccentPunch });

        /// <summary>A loudness jump landing: a full flash, a punch and a shake, scaled by its size.</summary>
        public void Drop(double t, double db)
        {
            float k = (float)Math.Min(1, db / 12);
            Add(new Moment { T = t, Kind = Kind.Drop, Flash = k, Punch = DropPunch * k, Shake = 0.15f * k });
        }

        /// <summary>
        /// A sim event: each death adds a kill to the hero's blow at its impact time (deaths land on
        /// their blow's impact), whose weight sets its flash, punch and shake; a structure break flashes,
        /// punches and shakes by its force.
        /// </summary>
        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Death when e.I0 >= 0:
                {
                    int i = Find(e.T, Kind.Blow);
                    if (i < 0) i = Add(new Moment { T = e.T, Kind = Kind.Blow });
                    ref var m = ref _ring[i];
                    m.Kills++;
                    float w = Weight(m.Kills);
                    m.Flash = BlowFlash * w;
                    m.Punch = w > 0 ? AccentPunch + BlowPunch * w : 0;
                    m.Shake = BlowShake * w;
                    break;
                }
                case SimEventKind.StructureBreak:
                {
                    float force = (float)Math.Max(0, Math.Min(1, e.AsStructureBreak.Force));
                    Add(new Moment { T = e.T, Kind = Kind.Break, Flash = 0.5f + 0.5f * force, Punch = BreakPunch, Shake = 0.15f + 0.25f * force });
                    break;
                }
            }
        }

        /// <summary>A blow's weight for its kills: 0 to 1, log-scaled between the crisp and heavy counts.</summary>
        public static float Weight(int kills)
        {
            double lo = Math.Log(1 + CrispKills), hi = Math.Log(1 + HeavyKills);
            return (float)Math.Max(0, Math.Min(1, (Math.Log(1 + kills) - lo) / (hi - lo)));
        }

        /// <summary>The flash at song time s: the strongest moment still decaying, as (1 - u)³ over a beat of <paramref name="period"/>.</summary>
        public float Flash(double s, double period)
        {
            float flash = 0;
            for (int i = 0; i < _count; i++)
            {
                ref readonly var m = ref _ring[i];
                double u = (s - m.T) / period;
                if (u < 0 || u >= 1 || m.Flash <= 0) continue;
                float k = (float)(1 - u);
                flash = Math.Max(flash, m.Flash * k * k * k);
            }
            return flash;
        }

        /// <summary>
        /// The view width's multiplier at s (below 1 is a zoom-in): each punch zooms in over a
        /// sixteenth and releases over a beat; overlapping punches take the deepest, so a new one never
        /// snaps the zoom back out (CameraFeel).
        /// </summary>
        public float Punch(double s, double period)
        {
            float factor = 1;
            double sixteenth = period / 4;
            for (int i = 0; i < _count; i++)
            {
                ref readonly var m = ref _ring[i];
                double u = s - m.T;
                if (u < 0 || m.Punch <= 0) continue;
                float f = 1;
                if (u < sixteenth) f = 1 - m.Punch * (float)(u / sixteenth);
                else if (u < sixteenth + period) f = 1 - m.Punch * (float)(1 - (u - sixteenth) / period);
                factor = Math.Min(factor, f);
            }
            return factor;
        }

        /// <summary>
        /// The shake's amplitude at s (m): the strongest moment's, decaying as (1 - u)² over a beat,
        /// capped at <see cref="ShakeCap"/> unless a break or drop is behind it.
        /// </summary>
        public float ShakeAmplitude(double s, double period)
        {
            float amp = 0, cap = ShakeCap;
            for (int i = 0; i < _count; i++)
            {
                ref readonly var m = ref _ring[i];
                double u = (s - m.T) / period;
                if (u < 0 || u >= 1 || m.Shake <= 0) continue;
                float k = (float)(1 - u);
                amp = Math.Max(amp, m.Shake * k * k);
                if (m.Kind == Kind.Break || m.Kind == Kind.Drop) cap = BreakShakeCap;
            }
            return Math.Min(amp, cap);
        }

        int Find(double t, Kind kind)
        {
            for (int i = 0; i < _count; i++)
                if (_ring[i].Kind == kind && _ring[i].T == t) return i;
            return -1;
        }

        /// <summary>Adds a moment, over the oldest added when the ring is full; returns its slot.</summary>
        int Add(in Moment m)
        {
            int slot;
            if (_count < Capacity) slot = _count++;
            else
            {
                slot = _next;
                _next = (_next + 1) % Capacity;
            }
            _ring[slot] = m;
            return slot;
        }

        /// <summary>Forgets moments more than the horizon before <paramref name="s"/>, keeping the ring's room for new ones.</summary>
        public void Forget(double s)
        {
            int w = 0;
            for (int i = 0; i < _count; i++)
                if (s - _ring[i].T < Horizon) _ring[w++] = _ring[i];
            _count = w;
            _next = 0;
        }
    }
}
