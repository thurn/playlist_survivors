using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// The bosses' telegraphs, charges and slams (unity-game.md, Enemies: "Bosses telegraph with an
    /// in-place pose and deliver charges and slams as steps on main beats"), for scheduled bosses of
    /// schedule.json's <c>bossMoves.roles</c>.
    ///
    /// From <see cref="BossMoveRules.FirstBeats"/> main beats after it lands, and then every
    /// <see cref="BossMoveRules.EveryBeats"/>, a boss telegraphs on a main beat's footplant, with the
    /// beat reliable and the delivery beat, <see cref="BossMoveRules.TelegraphBeats"/> later,
    /// already committed: an attack event (<see cref="EnemyAttackKind.Slam"/> with the hero within
    /// the slam's radius, else <see cref="EnemyAttackKind.Charge"/>) whose impact is that beat. It
    /// holds its pose in place until then, neither stepping nor lunging.
    /// - <b>A charge</b> is the delivery beat's step, decided at the step's usual time: up to
    ///   <see cref="BossMoveRules.ChargeM"/> along the line to where the hero stood at the
    ///   telegraph, stopped by structures and at contact with the hero. It hits the hero for the
    ///   role's power if the hero is in contact anywhere along it at the footplant.
    /// - <b>A slam</b> lands on the delivery beat: the hero within the radius takes the role's
    ///   power, and every structure in it is struck (<see cref="StructureHit.BossSlam"/>, which
    ///   breaks).
    /// A delivery beat that comes unreliable, frozen or with the boss knocked back fizzles, and the
    /// boss walks on; it telegraphs again a cycle later.
    ///
    /// The signature minotaur (<see cref="Signature"/>) moves by its own numbers: it only slams, on
    /// every <c>signature.slamBeats</c>th main beat from its landing, at the mark of where the hero
    /// stood as it telegraphed rather than around itself, for <c>slamDamage</c> within <c>slamRadiusM</c>.
    /// </summary>
    internal sealed class BossMoves
    {
        readonly BossMoveRules rules;
        readonly bool[] moverRole;
        // Per enemy slot: does it move, the next beat it may telegraph on, and its move in hand: its kind,
        // aim and slam centre. The signature's: slams only, at the hero's mark, its own cycle, radius and damage.
        readonly bool[] mover, marks;
        readonly int[] nextBeat, deliverBeat, every;
        readonly EnemyAttackKind[] move;
        readonly double[] aimX, aimY, slamX, slamY, radius, damage;

        public BossMoves(GameContent content, int cap)
        {
            rules = content.Schedule.BossMoves;
            moverRole = new bool[content.Roles.Length];
            foreach (string id in rules.Roles) moverRole[content.RoleIndex(id)] = true;
            mover = new bool[cap];
            marks = new bool[cap];
            nextBeat = new int[cap];
            deliverBeat = new int[cap];
            every = new int[cap];
            move = new EnemyAttackKind[cap];
            aimX = new double[cap];
            aimY = new double[cap];
            slamX = new double[cap];
            slamY = new double[cap];
            radius = new double[cap];
            damage = new double[cap];
        }

        /// <summary>Enemy <paramref name="i"/> spawned, a boss or not, on or after main beat <paramref name="beat"/>.</summary>
        public void Spawned(int i, int role, bool boss, int beat)
        {
            mover[i] = boss && moverRole[role];
            marks[i] = false;
            nextBeat[i] = beat + rules.FirstBeats;
            deliverBeat[i] = -1;
            every[i] = rules.EveryBeats;
            radius[i] = rules.SlamRadiusM;
            damage[i] = double.NaN;
        }

        /// <summary>Enemy <paramref name="i"/>, landed on main beat <paramref name="beat"/>, is the signature minotaur: it slams on every <c>slamBeats</c>th beat from it.</summary>
        public void Signature(int i, int beat, Signature signature)
        {
            mover[i] = true;
            marks[i] = true;
            every[i] = signature.SlamBeats;
            nextBeat[i] = beat + signature.SlamBeats - rules.TelegraphBeats;
            radius[i] = signature.SlamRadiusM;
            damage[i] = signature.SlamDamage;
        }

        /// <summary>Enemy <paramref name="i"/> moves no more (the minotaur's retreat); a move in hand fizzles.</summary>
        public void Stop(int i)
        {
            mover[i] = false;
            deliverBeat[i] = -1;
        }

        /// <summary>Is enemy <paramref name="i"/> holding a telegraph through main beat <paramref name="beat"/>, or delivering on it?</summary>
        public bool Busy(int i, int beat) => mover[i] && deliverBeat[i] >= beat;

        /// <summary>At main beat <paramref name="beat"/>'s footplant, with the beat reliable: bosses due telegraph.</summary>
        public void Telegraph(Simulation sim, Horde horde, int beat, double t, double hx, double hy)
        {
            var beats = sim.Stream.Beats;
            if (beat + rules.TelegraphBeats >= beats.Length) return;
            double impact = beats[beat + rules.TelegraphBeats].T;
            var enemies = sim.State.EnemyStore;
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                if (!e.Alive || !mover[i] || deliverBeat[i] >= beat || beat < nextBeat[i]) continue;
                e.PositionAt(t, out double x, out double y);
                double dx = hx - x, dy = hy - y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-9)
                {
                    dx = 1;
                    d = 1;
                }
                move[i] = marks[i] || d <= rules.SlamRadiusM ? EnemyAttackKind.Slam : EnemyAttackKind.Charge;
                aimX[i] = dx / d;
                aimY[i] = dy / d;
                slamX[i] = marks[i] ? hx : x;
                slamY[i] = marks[i] ? hy : y;
                deliverBeat[i] = beat + rules.TelegraphBeats;
                nextBeat[i] = beat + every[i];
                e.LungeBeat = -1;
                sim.Emit(SimEvent.EnemyAttack(t, i, move[i], t, impact, aimX[i], aimY[i], slamX[i], slamY[i]));
            }
        }

        /// <summary>
        /// At main beat <paramref name="beat"/>'s decision, with the beat reliable: charges due on it
        /// set off as its steps, from move start <paramref name="t"/> to the footplant.
        /// </summary>
        public void Decide(Simulation sim, Horde horde, int beat, double t, double footplant, double hx, double hy)
        {
            var enemies = sim.State.EnemyStore;
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                if (!e.Alive || !mover[i] || deliverBeat[i] != beat || move[i] != EnemyAttackKind.Charge) continue;
                if (e.Motion == EnemyMotion.Knock && e.MoveEnd > t + 1e-9) continue;
                e.PositionAt(t, out double x, out double y);
                // Along the aim, stopping at contact with the hero where the line passes it.
                double len = rules.ChargeM;
                double along = (hx - x) * aimX[i] + (hy - y) * aimY[i];
                double across = Math.Abs((hx - x) * aimY[i] - (hy - y) * aimX[i]);
                double contact = horde.ContactOf(e.Role);
                if (along > 0 && across < contact) len = Math.Min(len, Math.Max(0, along - Math.Sqrt(contact * contact - across * across)));
                double toX = x + aimX[i] * len, toY = y + aimY[i] * len;
                horde.Nav.Clamp(x, y, ref toX, ref toY);
                horde.StartStep(sim, i, beat, t, x, y, toX, toY, footplant);
            }
        }

        /// <summary>At main beat <paramref name="beat"/>'s footplant: moves due land (if the beat is still <paramref name="reliable"/>), and their bosses walk on.</summary>
        public void Land(Simulation sim, Horde horde, int beat, double t, bool reliable, double hx, double hy)
        {
            var enemies = sim.State.EnemyStore;
            var roles = horde.Roles;
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                if (!e.Alive || !mover[i] || deliverBeat[i] != beat) continue;
                deliverBeat[i] = -1;
                if (!reliable || (e.Motion == EnemyMotion.Knock && e.MoveEnd > t + 1e-9)) continue;
                double power = double.IsNaN(damage[i]) ? roles[e.Role].Power : damage[i];
                if (move[i] == EnemyAttackKind.Slam)
                {
                    sim.Structures.Strike(sim, t, slamX[i], slamY[i], radius[i], StructureHit.BossSlam, rules.SlamForceM);
                    double dx = hx - slamX[i], dy = hy - slamY[i], d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= radius[i] + Horde.HeroHurtRadiusM)
                        sim.Health.Hurt(sim, t, EnemyAttackKind.Slam, power, d > 1e-9 ? dx / d : aimX[i], d > 1e-9 ? dy / d : aimY[i]);
                }
                else if (e.SteppedBeat == beat &&
                         Horde.SegmentDistance(hx, hy, e.FromX, e.FromY, e.ToX, e.ToY) <= horde.ContactOf(e.Role) + Horde.BiteSlackM)
                {
                    sim.Health.Hurt(sim, t, EnemyAttackKind.Charge, power, aimX[i], aimY[i]);
                }
            }
        }
    }
}
