using System;

namespace Champions.Sim
{
    public sealed partial class Bosses
    {
        // Finite opportunities in active encounter time, never a renewable spawn loop.
        public const int ReinforcementGroups = 3, ReinforcementGroupSize = 3;
        public const double ReinforcementFirstS = 12, ReinforcementIntervalS = 16, ReinforcementWarningS = 2;
        double arrivalClock;
        int reinforcementSlot;
        bool reinforcing;
        BossMark reinforcement;
        readonly int[] reinforcementIds = { -1, -1, -1 };
        readonly StructList<int> blockedSpawn = new StructList<int>(8);

        /// <summary>A harmless, fixed-position group warning, separate from damaging boss marks.</summary>
        public BossMark? ReinforcementWarning => reinforcing ? reinforcement : (BossMark?)null;

        // Retire the entire old field, shattering its visible part. With no old mobs left outside,
        // walking or dashing beyond the original camera cannot bypass encounter isolation.
        // No Progression.Killed call: neither XP nor healing rolls nor hero kill credit.
        int ClearForEncounter(double t, double hx, double hy)
        {
            sim.Horde.Moves.CallOff(t);
            int shattered = 0;
            for (int id = 0, n = sim.Enemies.Count; id < n; id++)
            {
                ref readonly var e = ref sim.Enemies[id];
                if (!e.Alive || e.Boss) continue;
                sim.Horde.PositionAt(id, t, out double ex, out double ey);
                double sx = (ex - hx) / rules.ScreenHalfWidthM, sy = (ey - hy) / rules.ScreenHalfHeightM;
                bool visible = Math.Abs(sx) <= 1 && Math.Abs(sy) <= 1;
                if (!visible && sim.Horde.Owns(id)) sim.Horde.Despawn(id, t);
                else
                {
                    sim.Enemies.Kill(id);
                    if (visible)
                    {
                        double dx = ex - x, dy = ey - y, d = Math.Sqrt(dx * dx + dy * dy);
                        sim.Emit(SimEvent.Kill(t, id, BlowSource.Boss, BlowFlags.None,
                            d > 1e-9 ? dx / d : 0, d > 1e-9 ? dy / d : -1, rules.EntrancePower, ex, ey));
                        shattered++;
                    }
                    else sim.Emit(SimEvent.EnemyGone(t, id, ex, ey));
                }
            }
            sim.Horde.Moved();
            return shattered;
        }

        // Only Walking calls this: attack fronts have finished, and choosing another attack waits
        // for the whole warning plus a two-second approach. Three weak bodies occupy one sector.
        bool Reinforce(double t)
        {
            // Boss Rush adds are a boss's own mechanic, inside its XP budget (boss-rush.md): no main-game reinforcements.
            if (sim.Dashes.Rules.LegacyForms || sim.BossRush != null) return false;
            if (reinforcing)
            {
                if (sim.Telegraphs.LandTime(reinforcement.Telegraph) > t + 1e-9) return true;
                sim.HeroWas(t, out double hx, out double hy);
                int kind = cast.IndexOf("skeleton");
                for (int i = 0; i < ReinforcementGroupSize; i++)
                {
                    double px = reinforcement.Shape.X + (i - 1) * 1.2, py = reinforcement.Shape.Y;
                    double dx = px - hx, dy = py - hy;
                    blockedSpawn.Clear();
                    sim.Structures?.Reach(HitShape.Circle(px, py, cast.Cast[kind].RadiusM), t, blockedSpawn);
                    // A player may deliberately walk onto a warning. Skip that spawn, never relocate
                    // it onto a different lane or inside a structure. A skipped slot is spent.
                    if (dx * dx + dy * dy < 36 || blockedSpawn.Count > 0) continue;
                    reinforcementIds[i] = sim.Horde.Spawn(kind, px, py);
                }
                sim.Emit(SimEvent.BossMove(t, reinforcement, false, x, y, x, y, 0, 0));
                reinforcing = false;
                nextMoveAt = Math.Max(nextMoveAt, t + 2);
                return true;
            }
            if (reinforcementSlot >= ReinforcementGroups ||
                sim.State.RunClock - arrivalClock < ReinforcementFirstS + reinforcementSlot * ReinforcementIntervalS) return false;
            reinforcementSlot++;
            // No stacking live groups, even if a player keeps one alive for the whole fight.
            foreach (int id in reinforcementIds) if (sim.Horde.Owns(id)) return false;
            Array.Fill(reinforcementIds, -1);
            sim.HeroWas(t, out double heroX, out double heroY);
            double side = (reinforcementSlot & 1) == 0 ? -1 : 1;
            reinforcement = new BossMark
            {
                Id = nextMark++, Move = BossMove.Reinforcements,
                Telegraph = new Telegraph(t, t + ReinforcementWarningS, -1, -1, true),
                Shape = HitShape.Circle(heroX + side * 10, heroY + 8, 2),
            };
            reinforcing = true;
            sim.Emit(SimEvent.BossTelegraph(reinforcement));
            return true;
        }

        void CancelReinforcement(double t)
        {
            if (reinforcing) sim.Emit(SimEvent.BossMove(t, reinforcement, true, x, y, x, y, 0, 0));
            reinforcing = false;
            Array.Fill(reinforcementIds, -1);
        }
    }
}
