namespace PS.Game.Sim
{
    /// <summary>How an enemy is moving: standing, on a main-beat step, or knocked back.</summary>
    public enum EnemyMotion : byte { Still, Step, Knock }

    /// <summary>An enemy's attacks, the kind in its attack and hit events.</summary>
    public enum EnemyAttackKind
    {
        /// <summary>A contact lunge: a notice pose, contact on the main beat, a return.</summary>
        Lunge = 0,
    }

    /// <summary>
    /// One enemy slot as the sim settled it at the frontier (<see cref="SimState.Enemies"/>). A
    /// slot's index is the enemy id in events; a dead slot is reused by a later spawn. While
    /// <see cref="Motion"/> is not <see cref="EnemyMotion.Still"/>, the enemy travels in a straight
    /// line from (<see cref="FromX"/>, <see cref="FromY"/>) at <see cref="MoveStart"/> to
    /// (<see cref="ToX"/>, <see cref="ToY"/>) at <see cref="MoveEnd"/>; a step's end is its
    /// footplant, on the beat. The rest of the time it stands still.
    /// </summary>
    public struct EnemyState
    {
        public bool Alive { get; internal set; }
        /// <summary>Its role's index in the content's roles.</summary>
        public int Role { get; internal set; }
        public double X { get; internal set; }
        public double Y { get; internal set; }
        public double Health { get; internal set; }
        public EnemyMotion Motion { get; internal set; }
        public double FromX { get; internal set; }
        public double FromY { get; internal set; }
        public double ToX { get; internal set; }
        public double ToY { get; internal set; }
        public double MoveStart { get; internal set; }
        public double MoveEnd { get; internal set; }
        /// <summary>The main beat it has noticed the hero for and will lunge on, or -1.</summary>
        public int LungeBeat { get; internal set; }
        /// <summary>The latest main beat it stepped on, or -1.</summary>
        public int SteppedBeat { get; internal set; }

        /// <summary>Where it stands at song time <paramref name="t"/>, from its current motion.</summary>
        public void PositionAt(double t, out double x, out double y)
        {
            if (Motion == EnemyMotion.Still || t >= MoveEnd)
            {
                x = Motion == EnemyMotion.Still ? X : ToX;
                y = Motion == EnemyMotion.Still ? Y : ToY;
                return;
            }
            double u = t <= MoveStart ? 0 : (t - MoveStart) / (MoveEnd - MoveStart);
            x = FromX + (ToX - FromX) * u;
            y = FromY + (ToY - FromY) * u;
        }
    }
}
