namespace PS.Game.Sim
{
    /// <summary>
    /// Speeds that follow the tempo (unity-game.md, "Speeds Follow the Tempo"). On the grid the
    /// hero's speed is one tile per stride period, so every enemy's travel is measured against it:
    /// per hero stride, a role travels r tiles' worth, r being its speed against the hero's
    /// reference speed. Enemies therefore keep Vampire Survivors' speed ratios at any tempo.
    ///
    /// <code>
    /// skeleton: r = 1.45 / 5.19 = 0.28, a step every beat
    /// 100 BPM  2 strides per beat  hero 5.0 m/s  step 0.28 * 1.5 * 2 = 0.84 m
    /// 155 BPM  1 stride per beat   hero 3.9 m/s  step 0.28 * 1.5 * 1 = 0.42 m
    /// </code>
    ///
    /// Knockback and death launches are forced reactions and use fixed distances, not these.
    /// </summary>
    public static class Tempo
    {
        /// <summary>The hero's speed: one tile per stride period.</summary>
        public static double HeroSpeed(double tileM, double stridePeriod) => tileM / stridePeriod;

        /// <summary>A role's r: its <c>refSpeedMps</c> against the hero's reference speed (progression.json's hero <c>speedMps</c>).</summary>
        public static double Ratio(double roleSpeedMps, double heroReferenceMps) => roleSpeedMps / heroReferenceMps;

        /// <summary>
        /// The hero's strides per enemy step: the stride grid cuts each beat into 2^halvings steps,
        /// and a step may span several beats (a gait <c>x.</c> steps every other beat). The free
        /// pulse (halvings -1) gives 0: with no beat, enemies hold.
        /// </summary>
        public static int StridesPerStep(int halvings, int beatsPerStep = 1) =>
            halvings < 0 ? 0 : (1 << halvings) * beatsPerStep;

        /// <summary>How far an enemy travels in one step: r tiles per hero stride.</summary>
        public static double EnemyStepDistance(double ratio, double tileM, int stridesPerStep) =>
            ratio * tileM * stridesPerStep;
    }
}
