using System;

namespace Champions.Sim
{
    /// <summary>Why a spore ended (<see cref="SimEventKind.SporeEnd"/>).</summary>
    public enum SporeEnd : byte
    {
        /// <summary>It hit the hero (a dash's i-frames may still dodge it).</summary>
        Hit = 0,
        /// <summary>It flew its range.</summary>
        Range = 1,
        /// <summary>A structure stopped it.</summary>
        Blocked = 2,
        /// <summary>The world was held (the cards came up): every spore in flight goes.</summary>
        Held = 3,
    }

    /// <summary>A spore in flight (<see cref="EliteMoves.Spores"/>): where it is at <see cref="T"/>, its velocity and how far it has flown.</summary>
    public struct Spore
    {
        /// <summary>Its number this run (its events'), and the mushroom that fired it.</summary>
        public int Id, Owner;
        public double X, Y, T, Vx, Vy, Flown;
    }

    /// <summary>
    /// The elites' and the mushroom's telegraphed moves (design.md, "Threats"), one per type:
    ///
    /// - <b>Big ork, charge</b>: within <see cref="ChargeRules.TriggerM"/> of the hero it stops and a yellow
    ///   line toward the hero grows over <see cref="ChargeRules.TelegraphBeats"/> beats; on the beat after, it
    ///   charges <see cref="ChargeRules.LengthM"/> along the line (structures stop it). The hero anywhere on the
    ///   line then is hit.
    /// - <b>Troll, slam</b>: within <see cref="SlamRules.TriggerM"/> a yellow ring of <see cref="SlamRules.RadiusM"/>
    ///   fills round it over two beats and slams on the next, hitting the hero inside.
    /// - <b>Mushroom, spores</b>: within <see cref="SporeRules.RangeM"/> its cap flashes a beat before; on the
    ///   beat it fires a spore at where the hero is, flying straight at <see cref="SporeRules.SpeedMps"/> for
    ///   its range; then again every <see cref="SporeRules.EveryBars"/> bars.
    ///
    /// Telegraphs come from <see cref="Telegraphs"/> (whole beats landing on a beat; 0.5 s a beat beatless),
    /// and each move lands when its landing beat comes, as known then. A move's hit lands on its beat
    /// (<see cref="HeroHit.Beat"/>), so a dash through it on that beat is a perfect dodge; a spore's lands when
    /// it reaches the hero. A mob standing in a telegraph or charging takes no steps.
    /// </summary>
    public sealed class EliteMoves
    {
        readonly Simulation sim;
        readonly Horde horde;
        readonly EnemyRules rules;
        readonly BeatRules beat;
        readonly StructList<Spore> spores = new StructList<Spore>(64);

        internal EliteMoves(Simulation sim, Horde horde, EnemyRules rules, BeatRules beat)
        {
            this.sim = sim;
            this.horde = horde;
            this.rules = rules;
            this.beat = beat;
        }

        /// <summary>The spores in flight.</summary>
        public ReadOnlySpan<Spore> Spores => spores.AsSpan();

        /// <summary>Spores fired this run.</summary>
        public int Fired { get; private set; }

        internal void Resume(double elapsed)
        {
            for (int i = 0; i < spores.Count; i++)
            { var p = spores[i]; p.T += elapsed; spores[i] = p; }
        }

        internal void Tick(double end)
        {
            sim.HeroWas(end, out double hx, out double hy);
            var enemies = horde.Enemies;
            int n = enemies.Count;
            for (int id = 0; id < n; id++)
            {
                ref var m = ref horde.Edit(id);
                if (!m.Owned) continue;
                var type = rules.Cast[m.Kind];
                if (type.Move == EliteMove.None) continue;
                switch (m.Stage)
                {
                    case MoveStage.None:
                        // Between steps, or anywhere in a glide.
                        if (end >= m.NextMoveAt - 1e-9 && (m.Settled(end) || m.Motion == Motion.Glide)) Consider(id, ref m, type, end, hx, hy);
                        break;
                    case MoveStage.Telegraph:
                        double land = sim.Telegraphs.LandTime(m.Telegraph);
                        if (land <= end + 1e-9) Land(id, ref m, type, land);
                        break;
                    case MoveStage.Acting:
                        if (m.Settled(end)) m.Stage = MoveStage.None;
                        break;
                }
            }
            Fly(end);
        }

        /// <summary>Starts the move's telegraph when the hero is in its reach.</summary>
        void Consider(int id, ref Mob m, EnemyType type, double t, double hx, double hy)
        {
            ref readonly var e = ref horde.Enemies[id];
            double dx = hx - e.X, dy = hy - e.Y, d = Math.Sqrt(dx * dx + dy * dy);
            double trigger;
            int count;
            switch (type.Move)
            {
                case EliteMove.Charge:
                    trigger = rules.Charge.TriggerM;
                    count = rules.Charge.TelegraphBeats;
                    break;
                case EliteMove.Slam:
                    trigger = rules.Slam.TriggerM;
                    count = rules.Slam.TelegraphBeats;
                    break;
                default:
                    trigger = rules.Spores.RangeM;
                    count = rules.Spores.TelegraphBeats;
                    break;
            }
            if (d > trigger) return;
            var telegraph = sim.Telegraphs.Plan(t, count);
            m.Stage = MoveStage.Telegraph;
            m.Telegraph = telegraph;
            if (d > 1e-9)
            {
                m.AimX = dx / d;
                m.AimY = dy / d;
            }
            else
            {
                m.AimX = 1;
                m.AimY = 0;
            }
            m.FromX = m.ToX = e.X;
            m.FromY = m.ToY = e.Y;
            m.Start = m.End = t;
            m.Motion = Motion.Still;
            sim.Emit(SimEvent.EliteTelegraph(t, id, type.Move, telegraph, e.X, e.Y, m.AimX, m.AimY));
        }

        /// <summary>The move lands at <paramref name="t"/>, its telegraph's beat.</summary>
        void Land(int id, ref Mob m, EnemyType type, double t)
        {
            double x = m.ToX, y = m.ToY;
            sim.HeroWas(t, out double hx, out double hy);
            double hr = horde.HeroRadius;
            m.Stage = MoveStage.None;
            switch (type.Move)
            {
                case EliteMove.Charge:
                {
                    var c = rules.Charge;
                    double qx = x + m.AimX * c.LengthM, qy = y + m.AimY * c.LengthM;
                    var structures = sim.Structures;
                    if (structures != null)
                    {
                        structures.Move(x, y, type.RadiusM, qx - x, qy - y, out double nx, out double ny, out _);
                        qx = nx;
                        qy = ny;
                    }
                    if (HitShape.DistanceToSegment(hx, hy, x, y, qx, qy) <= type.RadiusM + hr + 1e-9)
                        Hurt(id, t, c.Damage, HitSource.Charge, x, y);
                    m.FromX = x;
                    m.FromY = y;
                    m.ToX = qx;
                    m.ToY = qy;
                    m.Start = t;
                    m.End = t + c.DurationS;
                    m.Motion = Motion.Charge;
                    m.Stage = MoveStage.Acting;
                    m.NextMoveAt = t + c.CooldownS;
                    sim.Emit(SimEvent.EliteMove(t, id, EliteMove.Charge, false, x, y, qx, qy));
                    break;
                }
                case EliteMove.Slam:
                {
                    var s = rules.Slam;
                    double dx = hx - x, dy = hy - y, reach = s.RadiusM + hr;
                    if (dx * dx + dy * dy <= reach * reach + 1e-9) Hurt(id, t, s.Damage, HitSource.Slam, x, y);
                    m.NextMoveAt = t + s.CooldownS;
                    sim.Emit(SimEvent.EliteMove(t, id, EliteMove.Slam, false, x, y, x, y));
                    break;
                }
                default:
                {
                    var s = rules.Spores;
                    double dx = hx - x, dy = hy - y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > 1e-9)
                    {
                        dx /= d;
                        dy /= d;
                    }
                    else
                    {
                        dx = m.AimX;
                        dy = m.AimY;
                    }
                    var spore = new Spore { Id = Fired++, Owner = id, X = x, Y = y, T = t, Vx = dx * s.SpeedMps, Vy = dy * s.SpeedMps };
                    spores.Add(spore);
                    sim.Emit(SimEvent.Spore(t, spore.Id, id, x, y, spore.Vx, spore.Vy, s.RangeM));
                    // The next lands every so many bars after this one: planned a little before, so its telegraph
                    // starts on the beat that makes it land there (beatless, exactly then).
                    var tel = m.Telegraph;
                    double beatS = tel.OnBeat && tel.Beats > 0 ? (t - tel.Start) / tel.Beats : beat.TelegraphBeatlessBeatS;
                    int every = s.EveryBars * beat.BeatsPerBar;
                    m.NextMoveAt = t + (every - s.TelegraphBeats - (tel.OnBeat ? 0.5 : 0)) * beatS;
                    sim.Emit(SimEvent.EliteMove(t, id, EliteMove.Spores, false, x, y, x + dx * s.RangeM, y + dy * s.RangeM));
                    break;
                }
            }
        }

        void Hurt(int id, double t, double damage, HitSource source, double x, double y)
        {
            sim.Health.Hurt(sim, new HeroHit { T = t, Damage = damage, Source = source, Owner = id, Beat = t, FromX = x, FromY = y });
        }

        /// <summary>Every spore flies on to <paramref name="end"/>: hitting the hero, a structure, or the end of its range.</summary>
        void Fly(double end)
        {
            if (spores.Count == 0) return;
            var s = rules.Spores;
            sim.HeroWas(end, out double hx, out double hy);
            double reach = s.RadiusM + horde.HeroRadius;
            var structures = sim.Structures;
            int k = 0;
            for (int i = 0; i < spores.Count; i++)
            {
                var p = spores[i];
                double span = end - p.T;
                double dx = p.Vx * span, dy = p.Vy * span, step = Math.Sqrt(dx * dx + dy * dy);
                if (p.Flown + step > s.RangeM && step > 1e-12)
                {
                    double keep = (s.RangeM - p.Flown) / step;
                    dx *= keep;
                    dy *= keep;
                    step *= keep;
                }
                double nx = p.X + dx, ny = p.Y + dy;
                SporeEnd? ended = null;
                if (HitShape.DistanceToSegment(hx, hy, p.X, p.Y, nx, ny) <= reach + 1e-9)
                {
                    ended = SporeEnd.Hit;
                    sim.Health.Hurt(sim, new HeroHit
                    {
                        T = end, Damage = s.Damage, Source = HitSource.Spore, Owner = p.Owner, Beat = double.NaN, FromX = p.X, FromY = p.Y,
                    });
                }
                else if (structures != null)
                {
                    structures.Move(p.X, p.Y, s.RadiusM, dx, dy, out double bx, out double by, out int blocker);
                    if (blocker >= 0)
                    {
                        ended = SporeEnd.Blocked;
                        nx = bx;
                        ny = by;
                    }
                }
                p.X = nx;
                p.Y = ny;
                p.T = end;
                p.Flown += step;
                if (ended == null && p.Flown >= s.RangeM - 1e-9) ended = SporeEnd.Range;
                if (ended != null)
                {
                    sim.Emit(SimEvent.SporeEnd(end, p.Id, ended.Value, p.X, p.Y));
                    continue;
                }
                spores[k++] = p;
            }
            while (spores.Count > k) spores.RemoveLast();
        }

        /// <summary>Mob <paramref name="id"/> is gone (killed, left behind): its telegraph, if any, is called off.</summary>
        internal void Dropped(int id, double t)
        {
            ref var m = ref horde.Edit(id);
            if (m.Stage == MoveStage.Telegraph) CallOff(id, ref m, t);
            m.Stage = MoveStage.None;
        }

        /// <summary>The world is held: every telegraph is called off and every spore goes.</summary>
        internal void CallOff(double t)
        {
            var enemies = horde.Enemies;
            int n = enemies.Count;
            for (int id = 0; id < n; id++)
            {
                ref var m = ref horde.Edit(id);
                if (!m.Owned) continue;
                if (m.Stage == MoveStage.Telegraph) CallOff(id, ref m, t);
                m.Stage = MoveStage.None;
            }
            for (int i = 0; i < spores.Count; i++)
                sim.Emit(SimEvent.SporeEnd(t, spores[i].Id, SporeEnd.Held, spores[i].X, spores[i].Y));
            spores.Clear();
        }

        void CallOff(int id, ref Mob m, double t)
        {
            sim.Emit(SimEvent.EliteMove(t, id, rules.Cast[m.Kind].Move, true, m.ToX, m.ToY, m.ToX, m.ToY));
            m.Stage = MoveStage.None;
            m.NextMoveAt = Math.Max(m.NextMoveAt, t);
        }
    }
}
