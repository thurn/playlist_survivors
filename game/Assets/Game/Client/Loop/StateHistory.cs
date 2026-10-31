using System;
using PS.Game.Sim;

namespace Game.Client.Loop
{
    /// <summary>The hero as one snapshot holds it (<see cref="HeroState"/>).</summary>
    public struct HeroSnapshot
    {
        public int TileX, TileY;
        public double X, Y;
        public Direction Facing;
        public double FromX, FromY, MoveStart, MoveEnd;

        public static HeroSnapshot Of(HeroState h) => new HeroSnapshot
        {
            TileX = h.TileX,
            TileY = h.TileY,
            X = h.X,
            Y = h.Y,
            Facing = h.Facing,
            FromX = h.FromX,
            FromY = h.FromY,
            MoveStart = h.MoveStart,
            MoveEnd = h.MoveEnd,
        };

        /// <summary>Where the hero is at song time `t`: along its latest move, linearly (presenters shape the hop).</summary>
        public void PositionAt(double t, out double x, out double y)
        {
            double span = MoveEnd - MoveStart;
            double a = t >= MoveEnd || span <= 0 ? 1 : t <= MoveStart ? 0 : (t - MoveStart) / span;
            x = FromX + (X - FromX) * a;
            y = FromY + (Y - FromY) * a;
        }
    }

    /// <summary>
    /// The sim's state at one frontier, copied for the client (unity-game.md, The Sim Lead). The
    /// rule beads' state (enemies, structures, items) joins it as the client needs it.
    /// </summary>
    public struct SimSnapshot
    {
        /// <summary>The frontier it was taken at.</summary>
        public double Now;
        public double RunClock;
        public bool Frozen;
        public Reliability Beat;
        public int LastBeat, Bar;
        public bool Downbeat;
        public double StrideStep, StridePeriod;
        public int StrideHalvings;
        public HeroSnapshot Hero;

        public static SimSnapshot Of(SimState s) => new SimSnapshot
        {
            Now = s.Now,
            RunClock = s.RunClock,
            Frozen = s.Frozen,
            Beat = s.Beat,
            LastBeat = s.LastBeat,
            Bar = s.Bar,
            Downbeat = s.Downbeat,
            StrideStep = s.StrideStep,
            StridePeriod = s.StridePeriod,
            StrideHalvings = s.StrideHalvings,
            Hero = HeroSnapshot.Of(s.Hero),
        };
    }

    /// <summary>
    /// A short history of the sim's snapshots, one per frame's frontier, to show frameSong, which
    /// trails the frontier by the lead (unity-game.md, The Sim Lead). <see cref="Sample"/> answers
    /// at a song time between two snapshots: continuous values are interpolated, and discrete
    /// state is the earlier snapshot's, except a move that has started by then. Its capacity
    /// covers a lead of a second at 120 fps.
    /// </summary>
    public sealed class StateHistory
    {
        public const int Capacity = 128;

        readonly SimSnapshot[] _ring = new SimSnapshot[Capacity];
        int _head, _count;

        public int Count => _count;

        /// <summary>The latest snapshot: the frontier's.</summary>
        public ref readonly SimSnapshot Latest => ref _ring[(_head + Capacity - 1) % Capacity];

        /// <summary>Keeps the state at the sim's new frontier; one at the same frontier replaces the last.</summary>
        public void Record(SimState state)
        {
            if (_count > 0 && Latest.Now >= state.Now) _head = (_head + Capacity - 1) % Capacity;
            else if (_count < Capacity) _count++;
            _ring[_head] = SimSnapshot.Of(state);
            _head = (_head + 1) % Capacity;
        }

        public void Clear() => _head = _count = 0;

        /// <summary>
        /// The state at song time `t`. Returns false, with the latest snapshot, when `t` is past
        /// the frontier (display latency exceeds the lead); before the oldest it gives the oldest.
        /// </summary>
        public bool Sample(double t, out SimSnapshot state)
        {
            if (_count == 0) throw new InvalidOperationException("StateHistory: nothing recorded");
            if (t > Latest.Now)
            {
                state = Latest;
                return false;
            }
            if (t < At(0).Now)
            {
                state = At(0);
                return true;
            }
            int lo = 0, hi = _count - 1; // the last snapshot at or before t, by age from the oldest
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (At(mid).Now <= t) lo = mid;
                else hi = mid - 1;
            }
            ref readonly var a = ref At(lo);
            if (lo == _count - 1 || a.Now == t)
            {
                state = a;
                return true;
            }
            ref readonly var b = ref At(lo + 1);
            double f = (t - a.Now) / (b.Now - a.Now);
            state = a;
            state.Now = t;
            state.RunClock = a.RunClock + (b.RunClock - a.RunClock) * f;
            if (b.Hero.MoveStart <= t) state.Hero = b.Hero;
            return true;
        }

        ref readonly SimSnapshot At(int age) => ref _ring[(_head + Capacity - _count + age) % Capacity];
    }
}
