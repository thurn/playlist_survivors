using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// The signature minotaur (unity-game.md, Music-Driven Events: "a scripted mini-boss that lands on a
    /// reliable beat at 90 s and slams every 8 beats"; native's engine/signature.rs; schedule.json's
    /// <c>signature</c>).
    ///
    /// - Once in a run, it lands on the first reliable main beat once the run clock reaches <c>fromS</c>
    ///   with no scheduled boss waiting: a boss (the sword favours it; its health is level-scaled, with no
    ///   chest) just inside the view, as a scheduled boss lands. It takes the stage from a set piece in
    ///   play and makes its own room.
    /// - It pursues as the horde does, and slams on every <c>slamBeats</c>th main beat from its landing
    ///   (<see cref="BossMoves.Signature"/>): it telegraphs <c>bossMoves.telegraphBeats</c> before, marking
    ///   the ground under the hero, and the blow lands there, hurting the hero within <c>slamRadiusM</c> for
    ///   <c>slamDamage</c> and breaking the structures in it. Leaving the mark in time is the dodge.
    /// - <c>retreatS</c> after it landed it stops slamming and walks away from the hero at twice its pace,
    ///   leaving once out of sight, or <see cref="LeaveS"/> later wherever it is.
    /// </summary>
    internal sealed class SignatureBoss
    {
        /// <summary>A retreating minotaur leaves this long after it turned, if it is still in sight.</summary>
        public const double LeaveS = 4;
        /// <summary>Its retreat's pace, in its role's steps.</summary>
        public const double RetreatScale = 2;

        readonly MusicEvents m;
        readonly BossMoves bosses;
        readonly Signature rules;
        readonly int role;
        // Its group while it is on the field: the group object is reused once its id changes.
        MusicGroup group;
        int groupId = -1;
        bool landed, forced;
        double landedRun, retreatRun = double.NaN;

        public SignatureBoss(MusicEvents m, BossMoves bosses)
        {
            this.m = m;
            this.bosses = bosses;
            rules = m.Schedule.Signature;
            role = m.Content.RoleIndex(rules.Role);
        }

        bool OnField => group != null && group.Id == groupId;

        /// <summary>The minotaur on the next reliable main beat, unless it is on the field.</summary>
        public void Force()
        {
            if (OnField) return;
            landed = false;
            forced = true;
        }

        public void Land(Simulation sim, int beat, double t, bool reliable, double hx, double hy)
        {
            if (landed || !reliable || m.Spawner.BossesWaiting > 0) return;
            if (!forced && !(m.Natural && m.RunClock >= rules.FromS - 1e-9)) return;
            m.Pieces.Retire(sim, t, hx, hy);
            if (m.Horde.MakeRoom(sim, t, 1, hx, hy) == 0) return;
            var g = m.Begin(SetPieceKind.Signature, double.PositiveInfinity);
            if (g == null) return;
            m.Spawner.InViewPoint(sim.Random, hx, hy, out double x, out double y);
            int i = m.Horde.Spawn(sim, t, role, x, y, boss: true);
            m.Add(g, i);
            bosses.Signature(i, beat, rules);
            group = g;
            groupId = g.Id;
            landed = true;
            forced = false;
            landedRun = m.RunClock;
            retreatRun = double.NaN;
            sim.Emit(SimEvent.SetPiece(t, SetPieceKind.Signature, 1, t, t));
        }

        public void Tick(Simulation sim, double end, double hx, double hy)
        {
            if (!OnField) return;
            if (!m.Member(group, 0, out int i))
            {
                group.Id = -1; // it died
                return;
            }
            if (double.IsNaN(retreatRun))
            {
                if (m.RunClock < landedRun + rules.RetreatS - 1e-9) return;
                retreatRun = m.RunClock;
                bosses.Stop(i);
                m.State.EnemyStore[i].PositionAt(end, out double x, out double y);
                double dx = x - hx, dy = y - hy, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-9)
                {
                    dx = 1;
                    d = 1;
                }
                m.Horde.Scripts.Line(i, dx / d, dy / d, RetreatScale, flier: false);
                return;
            }
            m.State.EnemyStore[i].PositionAt(end, out double ex, out double ey);
            if (m.Spawner.OutOfSight(ex, ey, hx, hy) || m.RunClock >= retreatRun + LeaveS - 1e-9)
            {
                m.Horde.Leave(sim, i, end);
                group.Id = -1;
            }
        }
    }
}
