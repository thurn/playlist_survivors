using UnityEngine;

namespace Champions.Render
{
    /// <summary>
    /// The hero's weapon effects, one per moment the design draws (design.md, Attacks, Dashes, Mods; The Look &gt; Weapon
    /// effects). Each is a recipe of layers in Content/effects.json (<see cref="EffectRecipes"/>). Orbit Blades' ring is
    /// continuous (<see cref="IEffectCommands.Orbit"/>), not a cue.
    /// </summary>
    public enum EffectCue
    {
        /// <summary>Sword Slash's arc at the hero, aimed; <see cref="EffectShot.Mirrored"/> the other hand's swing.</summary>
        SwordSlash,
        /// <summary>Sword Slash's rank-3 spin: the whole circle.</summary>
        SwordSpin,
        /// <summary>An extra strike drawn spectral (Twin Strike's echo, Tempo's extra slash): dimmer than the hero.</summary>
        SpectralSlash,
        /// <summary>Tempest, Sword Slash's evolution: the crescent wave each slash throws (from At to To).</summary>
        Tempest,
        /// <summary>Blade Rain's sword dropping on its spot (At), landing at the shot's time.</summary>
        BladeRain,
        /// <summary>Sword Storm, Blade Rain's evolution: eight swords bursting down round At.</summary>
        SwordStorm,
        /// <summary>Crescent's wave rolling from At to To over the shot's duration.</summary>
        Crescent,
        /// <summary>Full Moon, Crescent's evolution: a ring wave out from At.</summary>
        FullMoon,
        /// <summary>A phantom at At repeating a slash: a dimmer white figure and its spectral arc.</summary>
        Phantom,
        /// <summary>Legion, Phantom's evolution: three phantoms round At, slashing.</summary>
        Legion,
        /// <summary>The plain Dash's stride smear from At to To.</summary>
        Dash,
        /// <summary>Blade Rush's fast slashes along its path.</summary>
        BladeRush,
        /// <summary>Thousand Cuts, Blade Rush's evolution: afterimage blades slashing along the path.</summary>
        ThousandCuts,
        /// <summary>Leap Slam's landing shockwave at At.</summary>
        LeapSlam,
        /// <summary>Leap Slam's ring rolling on, or its aftershock, out from At.</summary>
        LeapRing,
        /// <summary>Skyfall, Leap Slam's evolution: the landing that shatters everything near and its ring.</summary>
        Skyfall,
        /// <summary>Shield Charge: the shield's front driving from At to To, enemies thrown to its sides.</summary>
        ShieldCharge,
        /// <summary>Shield Charge's rank-3 bash: a cone at To.</summary>
        ShieldBash,
        /// <summary>Juggernaut, Shield Charge's evolution: the longer charge.</summary>
        Juggernaut,
        /// <summary>Blink Strike's slash burst at one end (At).</summary>
        BlinkBurst,
        /// <summary>Phase Walk, Blink Strike's evolution: the phantom's repeated burst at At.</summary>
        PhaseWalk,
        /// <summary>Momentum's slash trail along a dash's path.</summary>
        MomentumTrail,
        /// <summary>Afterimage: a white afterimage repeating a dash from At to To.</summary>
        Afterimage,
        /// <summary>Shatterburst: a kill's shards cutting round At.</summary>
        Shatterburst,
        /// <summary>Encore: the charge an on-beat dash leaves on the hero at At.</summary>
        Encore,
    }

    /// <summary>How strong an effect draws (design.md, The Look &gt; Weapon effects): on the beat bigger and brighter; special moments the brightest.</summary>
    public enum EffectPower
    {
        Plain,
        OnBeat,
        Special,
    }

    /// <summary>
    /// One cue's moment. Positions are ground metres (the sim's x, y: Unity's x, z); times are song times. A field left at
    /// zero takes the recipe's own value.
    /// </summary>
    public struct EffectShot
    {
        /// <summary>When it lands (an impact's time; a dash's or wave's start).</summary>
        public double T;
        /// <summary>Where: an arc's centre, a path's start, a burst's point.</summary>
        public Vector2 At;
        /// <summary>A path's end (dashes, trails, waves); unused by the rest.</summary>
        public Vector2 To;
        /// <summary>The way it faces on the ground (a unit vector): a slash's aim. Paths face along themselves.</summary>
        public Vector2 Facing;
        /// <summary>Its reach (m): a slash's, a shockwave's, a burst's; the sim's, sized for the beat.</summary>
        public float Radius;
        /// <summary>A slash's arc (degrees).</summary>
        public float ArcDeg;
        /// <summary>A path's span (s): a dash's duration, a wave's travel.</summary>
        public float Duration;
        public EffectPower Power;
        /// <summary>The other hand's swing: the sweep runs the other way (Sword Slash alternates).</summary>
        public bool Mirrored;
    }

    /// <summary>
    /// The effects presenter's commands (plan.md, The Client: presenters built against small command interfaces; the
    /// assembly's EventDispatch maps the sim's events onto them). Every effect of the hero's is white.
    /// </summary>
    public interface IEffectCommands
    {
        /// <summary>Fires a cue's effect, as soon as its moment is known.</summary>
        void Fire(EffectCue cue, in EffectShot shot);

        /// <summary>
        /// Orbit Blades' ring from <paramref name="t"/>: <paramref name="swords"/> spectral swords round the hero (none:
        /// gone), the first at <paramref name="angle"/> (radians from +x on the ground) turning at <paramref name="speed"/>
        /// (rad/s), at <paramref name="radius"/> m (a change eases to it). <paramref name="evolved"/>: Blade Halo's brighter ring.
        /// </summary>
        void Orbit(double t, int swords, double angle, double speed, float radius, bool evolved = false);
    }
}
