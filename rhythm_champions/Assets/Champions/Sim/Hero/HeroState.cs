namespace Champions.Sim
{
    /// <summary>
    /// The hero as the sim last settled it. Positions are ground-plane metres. It holds the chord of the
    /// tick ahead: from where the hero is at the tick's start to where it will be at its end, so
    /// <see cref="PositionAt"/> answers for any time in that tick.
    /// </summary>
    public sealed class HeroState
    {
        /// <summary>Where the hero will be at the tick ahead's end.</summary>
        public double X { get; internal set; }
        public double Y { get; internal set; }

        /// <summary>Where the tick ahead's chord starts, and its start and end times.</summary>
        public double FromX { get; internal set; }
        public double FromY { get; internal set; }
        public double MoveStart { get; internal set; } = double.NegativeInfinity;
        public double MoveEnd { get; internal set; } = double.NegativeInfinity;

        /// <summary>The way the hero faces, a unit vector: its latest travel's, down the screen at the start.</summary>
        public double FacingX { get; internal set; }
        public double FacingY { get; internal set; } = -1;

        /// <summary>Health now, and full health.</summary>
        public double Health { get; internal set; }
        public double MaxHealth { get; internal set; }
        /// <summary>The latest hit that hurt the hero (song time; -infinity before any): it can't be hurt again for <see cref="HeroRules.HurtInvulnerableS"/>.</summary>
        public double HurtAt { get; internal set; } = double.NegativeInfinity;
        public bool Dead { get; internal set; }

        /// <summary>The equipped dash: its index in <see cref="DashRules.Kinds"/> and its rank (4: the evolution).</summary>
        public int Dash { get; internal set; }
        public int DashRank { get; internal set; } = 1;
        /// <summary>Dashes made this run.</summary>
        public int Dashes { get; internal set; }
        /// <summary>The latest dash's span (-infinity before any) and whether it was on the beat.</summary>
        public double DashStart { get; internal set; } = double.NegativeInfinity;
        public double DashEnd { get; internal set; } = double.NegativeInfinity;
        public bool DashOnBeat { get; internal set; }
        /// <summary>When the next dash may start (the cooldown's end), and the cooldown it ran.</summary>
        public double DashReadyAt { get; internal set; } = double.NegativeInfinity;
        public double DashCooldownS { get; internal set; }
        /// <summary>A press in the cooldown's last moments waits to fire when the dash is ready.</summary>
        public bool DashWaiting { get; internal set; }
        /// <summary>The latest dash's i-frames end: the hero can't be hurt from its start to this.</summary>
        public double IFramesUntil { get; internal set; } = double.NegativeInfinity;

        /// <summary>Where the hero is at song time <paramref name="t"/> in the tick ahead (or at its end after it).</summary>
        public void PositionAt(double t, out double x, out double y)
        {
            double u = t >= MoveEnd || MoveEnd <= MoveStart ? 1 : System.Math.Max(0, (t - MoveStart) / (MoveEnd - MoveStart));
            x = FromX + (X - FromX) * u;
            y = FromY + (Y - FromY) * u;
        }
    }
}
