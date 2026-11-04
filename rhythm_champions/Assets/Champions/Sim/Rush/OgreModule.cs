using System;

namespace Champions.Sim
{
    /// <summary>
    /// The framework's proof (ps-8z4.3): the main game's Fat Ogre rebuilt as a Boss Rush module from
    /// <see cref="BossKit"/>'s pieces and its own bosses.json moves (smash, boulders, stomp, charge, the dash-through wave), plus what the main
    /// game's boss lacks: a phase at half health with a two-beat untouchable roar, after which its telegraphs take its
    /// phase-two beats. Its shapes are the main game's (<see cref="BossKit.SmashLine"/> ...), so it lands where the
    /// Ogre's moves land. It is not one of the 25 (Decision 10): it stands in for unlanded fights and anchors the tests.
    /// </summary>
    public sealed class OgreModule : BossModule
    {
        public const string ModuleId = "fatOgre";

        BossType type;
        int turn, attack = -1;
        double nextMoveAt;

        public override string Id => ModuleId;
        public override double[] PhasesBelow => new[] { 0.5 };
        public override int TransitionBeats => 2;

        protected internal override void Begin(BossFight f, double t)
        {
            type = f.BossRules.Of(f.Bodies[0].Cast) ?? throw new InvalidOperationException($"{ModuleId}: no moves in {BossRules.File}");
            nextMoveAt = t;
        }

        protected internal override void PhaseBegins(BossFight f, int phase, double t, double until)
        {
            attack = -1;
            nextMoveAt = until;
        }

        protected internal override void Tick(BossFight f, double end)
        {
            if (attack >= 0)
            {
                if (f.Pending(attack) || f.Moving(0)) return;
                attack = -1;
                double beatS = f.BeatS;
                nextMoveAt = end + Math.Max(f.Sim.Dashes.Rules.CooldownS, (type.Rest(f.Phase) - 0.5) * beatS);
            }
            f.HeroAt(end, out double hx, out double hy);
            ref readonly var b = ref f.Bodies[0];
            double d = Math.Sqrt((hx - b.X) * (hx - b.X) + (hy - b.Y) * (hy - b.Y));
            f.Follow(0, end, type.KeepM);
            if (end < nextMoveAt - 1e-9) return;
            // The move in turn once the hero is in its reach; after a bar out of reach, the next in turn that reaches.
            bool patient = end < nextMoveAt + f.Sim.Beats.Rules.BeatsPerBar * f.BeatS - 1e-9;
            var moves = type.Moves;
            for (int k = 0; k < (patient ? 1 : moves.Length); k++)
            {
                var m = moves[(turn + k) % moves.Length];
                if (d > m.MaxM + 1e-9) continue;
                turn = (turn + k + 1) % moves.Length;
                attack = Use(f, m);
                return;
            }
        }

        int Use(BossFight f, BossMoveRules m)
        {
            int beats = m.Beats(f.Phase);
            switch (m.Move)
            {
                case BossMove.Smash: return BossKit.Smash(f, 0, m.LengthM, m.WidthM, m.Damage, beats);
                case BossMove.Boulders: return BossKit.Boulders(f, 0, m.Count, m.RadiusM, m.SpreadM, m.Damage, beats);
                case BossMove.Stomp: return BossKit.Stomp(f, 0, m.RadiusM, m.Damage, beats);
                case BossMove.Charge: return BossKit.Charge(f, 0, m.LengthM, m.DurationS, m.Damage, beats);
                case BossMove.Wave:
                {
                    // The main game's wave: from the hero's spot, 1.2 m thick, outrunning the walk.
                    f.HeroAt(f.Now, out double hx, out double hy);
                    return BossKit.Wave(f, 0, hx, hy, m.RadiusM, m.RadiusM / m.DurationS, 1.2, m.Damage, beats, fronts: m.Count);
                }
                default: throw new InvalidOperationException($"{ModuleId}: no piece for {m.Move}");
            }
        }
    }
}
