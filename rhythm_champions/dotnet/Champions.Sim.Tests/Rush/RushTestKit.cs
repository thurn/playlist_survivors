namespace Champions.Sim.Tests
{
    /// <summary>
    /// Helpers for tests of the Boss Rush run whichever of its fights have landed (boss-rush.md, Fights): a slot is fought by
    /// its own <see cref="BossFight"/> once its module is registered, else by its stand-in duel (<see cref="Bosses"/>).
    /// </summary>
    static class RushTestKit
    {
        /// <summary>
        /// Every slot fought by its stand-in duel, as before any fight landed: for the shell's and the progression's own
        /// rules (forming, rewards, the stand-ins' shedding), so landing a fight changes none of their tests.
        /// </summary>
        public static void StandIns(BossRushRules rush, EnemyRules cast)
        {
            foreach (var b in rush.Bosses)
            {
                if (!b.Fight) continue;
                b.Fight = b.Landed = false;
                b.Cast = cast.IndexOf(b.StandIn);
            }
        }

        /// <summary>The run's boss is in its fight: a landed slot's <see cref="BossFight"/>, or the stand-in's duel.</summary>
        public static bool Fighting(Simulation s) =>
            s.BossRush.Stage == BossRushStage.Fighting && (s.BossRush.Slot.Fight ? s.Fight.InFight : s.Bosses.InDuel);

        /// <summary>The run's boss (the fight's first standing body, or the duel's boss) as an enemy id.</summary>
        public static int Boss(Simulation s) => s.BossRush.Slot.Fight ? s.Fight.Lead.Enemy : s.Bosses.Id;

        /// <summary>A sword blow of <paramref name="damage"/> on the run's boss now.</summary>
        public static void Strike(Simulation s, double damage)
        {
            ref readonly var b = ref s.Enemies[Boss(s)];
            s.Strikes.Land(s, new Blow { Shape = HitShape.Circle(b.X, b.Y, 0.1), T = s.Now, Damage = damage,
                Tags = HitTags.Strike, Source = BlowSource.SwordSlash, Owner = -1, Part = -1 });
        }
    }
}
