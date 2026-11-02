using UnityEngine;

namespace Champions.Client.Hero
{
    /// <summary>The kinds of swing the hero's body plays for Sword Slash (design.md, The Hero &gt; Animation).</summary>
    public enum SwingKind
    {
        /// <summary>A slash: LightCombo01A and LightCombo01B in turn.</summary>
        Slash,
        /// <summary>Rank 3's spin: HeavyFlourish01.</summary>
        Spin,
    }

    /// <summary>The dash types' poses (design.md, Dashes), in dashes.json's order.</summary>
    public enum DashPose
    {
        /// <summary>The plain Dash: a long low stride held (the forward dodge's).</summary>
        Dash,
        /// <summary>HeavyStab01's thrust through the crowd.</summary>
        BladeRush,
        /// <summary>LightLeaping01's leap, landing on the dash's end.</summary>
        LeapSlam,
        /// <summary>The forward parry held, shield up front.</summary>
        ShieldCharge,
        /// <summary>Gone at the start, back at the end.</summary>
        BlinkStrike,
    }

    /// <summary>
    /// What the hero presenter is told (plan.md, The Client): the EventDispatch maps the sim's events onto these, so the
    /// hero can be drawn before and apart from the systems that move it. Times are song times; ground vectors are the
    /// sim's (x right, y up the screen). Commands come ahead of the times they show, by the sim's lead.
    /// </summary>
    public interface IHeroCommands
    {
        /// <summary>The hero walks at <paramref name="velocity"/> (m/s) from <paramref name="start"/> (HeroMove: D0, D3/D4); zero stands.</summary>
        void Walk(double start, Vector2 velocity);

        /// <summary>
        /// Sword Slash's swing (Swing: D0, D1/D2, I0): one clip whose impact frame lands at <paramref name="impact"/>,
        /// aimed along <paramref name="aim"/>, the aim held through it.
        /// </summary>
        void Swing(SwingKind kind, double impact, Vector2 aim);

        /// <summary>
        /// A dash (HeroDash: I1's type, D0, D1, D4/D5; rank 4 the evolution) from <paramref name="start"/> to
        /// <paramref name="end"/> along <paramref name="direction"/>.
        /// </summary>
        void Dash(DashPose pose, double start, double end, Vector2 direction, bool evolved);

        /// <summary>A hit took health at <paramref name="t"/> (HeroHit with damage): the hero flashes once.</summary>
        void Hurt(double t);

        /// <summary>The hero died at <paramref name="t"/> (HeroDeath), the killing blow from <paramref name="from"/>: it shatters.</summary>
        void Death(double t, Vector2 from);
    }
}
