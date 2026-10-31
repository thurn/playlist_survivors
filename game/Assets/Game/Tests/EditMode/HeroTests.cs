using System;
using System.IO;
using Game.Client.Hero;
using NUnit.Framework;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using Is = NUnit.Framework.Is;

namespace Game.Tests
{
    /// <summary>The hero's motion: attacks retimed onto the sim's impacts, follow-ups, hops and the blade's flare.</summary>
    public class HeroTests
    {
        const double Fps = 60;
        static readonly HeroClips Clips = HeroClips.Parse(File.ReadAllText(HeroClips.ContentPath));
        static readonly HeroClip[] Attacks = { HeroClip.SlashL, HeroClip.SlashR, HeroClip.Slash3, HeroClip.Spin, HeroClip.Slam };

        static SimEvent Attack(HeroAttackKind kind, double windupStart, double impact, bool heavy = false, double dirX = 1, double dirY = 0) =>
            SimEvent.SwordAttack(windupStart, 0, kind, windupStart, impact, dirX, dirY, heavy, 7.04);

        static SimEvent Step(double start, double end, double fromX, double toX, Direction facing, bool bump = false, bool late = false) =>
            SimEvent.HeroStep(start, 0, late, bump, false, facing, fromX, 0, toX, 0, start, end);

        /// <summary>
        /// Every attack clip, with the time from the wind-up to the impact asking for 0.5x to 2x (past
        /// the 0.75x-1.33x range both ways), at frame phases across a frame: the clip's own impact frame
        /// shows on the first frame at or after the sim's impact and not the frame before, with the
        /// attack whole on the upper body.
        /// </summary>
        [Test]
        public void RetimingLandsTheImpactOnTheImpactsFrameAcrossTheSpeedRange()
        {
            foreach (var clip in Attacks)
            {
                float annotated = Clips[clip].impact;
                foreach (double asked in new[] { 0.5, 0.75, 0.9, 1.0, 1.15, 1.33, 1.6, 2.0 })
                    foreach (double phase in new[] { 0, 0.3, 0.71 })
                    {
                        double impact = 10 + phase / Fps;
                        double windup = annotated / asked;
                        foreach (bool heavy in new[] { false, true })
                        {
                            var motion = new HeroMotion(Clips);
                            // A heavy main strike spins, a section change slams; the light clips play from a combo.
                            if (clip == HeroClip.SlashR) motion.OnEvent(Attack(HeroAttackKind.SlashLeft, 5, 5.32));
                            if (clip == HeroClip.Slash3) motion.OnEvent(Attack(HeroAttackKind.SlashRight, 5, 5.32));
                            var kind = clip == HeroClip.SlashL ? HeroAttackKind.SlashLeft : clip == HeroClip.Spin ? HeroAttackKind.Spin
                                : clip == HeroClip.Slam ? HeroAttackKind.Slam : HeroAttackKind.FollowUp;
                            motion.OnEvent(Attack(kind, impact - windup, impact, heavy));
                            int swing = motion.SwingCount - 1;

                            double frame = Math.Ceiling(impact * Fps - 1e-9) / Fps;
                            Assert.That(motion.ClipTime(swing, frame, out var played, out float at), Is.True);
                            Assert.That(played, Is.EqualTo(clip));
                            Assert.That(at, Is.GreaterThanOrEqualTo(annotated - 1e-4f), $"{clip} at {asked}x, phase {phase}: impact frame");
                            motion.ClipTime(swing, frame - 1 / Fps, out _, out float before);
                            Assert.That(before, Is.LessThan(annotated), $"{clip} at {asked}x, phase {phase}: the frame before");
                            motion.ClipTime(swing, impact, out _, out float exact);
                            Assert.That(exact, Is.EqualTo(annotated).Within(1e-4f), $"{clip} at {asked}x: at the impact");
                            Assert.That(at - before, Is.LessThanOrEqualTo(HeroClips.MaxSpeed / Fps + 1e-4), "a frame's step at most 1.33x");

                            var pose = motion.Evaluate(frame, 0, 0, Direction.Up);
                            Assert.That(pose.A.Clip, Is.EqualTo(clip));
                            Assert.That(pose.Attack, Is.EqualTo(1f).Within(1e-5f));
                            Assert.That(pose.AShare, Is.EqualTo(1f).Within(1e-5f));
                        }
                    }
            }
        }

        [Test]
        public void AClipStartsAtTheWindupInsideTheSpeedRangeAndIsEnteredPartWayPastIt()
        {
            var motion = new HeroMotion(Clips);
            float impact = Clips[HeroClip.SlashL].impact;
            motion.OnEvent(Attack(HeroAttackKind.SlashLeft, 2, 2 + impact / 1.2));
            motion.ClipTime(0, 2, out _, out float atWindup);
            Assert.That(atWindup, Is.EqualTo(0f).Within(1e-5f), "1.2x starts at the wind-up");
            Assert.That(motion.Evaluate(1.99, 0, 0, Direction.Up).Attack, Is.EqualTo(0f), "nothing before the wind-up");

            motion.OnEvent(Attack(HeroAttackKind.SlashRight, 4, 4 + 0.1));
            var pose = motion.Evaluate(4, 0, 0, Direction.Up);
            float expected = Clips[HeroClip.SlashR].impact - 0.1f * HeroClips.MaxSpeed;
            Assert.That(pose.A.Clip, Is.EqualTo(HeroClip.SlashR));
            Assert.That(pose.A.Time, Is.EqualTo(expected).Within(1e-4f), "a short wind-up enters the clip part-way, at 1.33x");
        }

        /// <summary>A subdivision strike's follow-up crossfades in from the swing before without taking its impact.</summary>
        [Test]
        public void AFollowUpContinuesFromTheSwingBeforeWhichKeepsItsImpact()
        {
            var motion = new HeroMotion(Clips);
            motion.OnEvent(Attack(HeroAttackKind.SlashLeft, 0.68, 1.0));
            motion.OnEvent(Attack(HeroAttackKind.FollowUp, 1.0, 1.15));

            var atFirst = motion.Evaluate(1.0 - 1e-6, 0, 0, Direction.Up);
            Assert.That(atFirst.A.Clip, Is.EqualTo(HeroClip.SlashL));
            Assert.That(atFirst.A.Time, Is.EqualTo(Clips[HeroClip.SlashL].impact).Within(1e-3f));
            Assert.That(atFirst.AShare, Is.EqualTo(1f));

            var between = motion.Evaluate(1.03, 0, 0, Direction.Up);
            Assert.That(between.A.Clip, Is.EqualTo(HeroClip.SlashR), "the combo's next swing");
            Assert.That(between.B.Clip, Is.EqualTo(HeroClip.SlashL));
            Assert.That(between.AShare, Is.InRange(0.1f, 0.9f), "crossfading from the follow-through");
            Assert.That(between.Attack, Is.EqualTo(1f).Within(1e-5f), "the upper body stays in the attack");

            var atSecond = motion.Evaluate(1.15, 0, 0, Direction.Up);
            Assert.That(atSecond.A.Time, Is.EqualTo(Clips[HeroClip.SlashR].impact).Within(1e-4f));
            Assert.That(atSecond.AShare, Is.EqualTo(1f));
        }

        [Test]
        public void SlashesAlternateFollowUpsRunTheComboHeavyStrikesSpinAndSectionChangesSlam()
        {
            Assert.That(HeroMotion.ClipFor(HeroAttackKind.SlashLeft, null), Is.EqualTo(HeroClip.SlashL));
            Assert.That(HeroMotion.ClipFor(HeroAttackKind.SlashRight, HeroClip.SlashL), Is.EqualTo(HeroClip.SlashR));
            Assert.That(HeroMotion.ClipFor(HeroAttackKind.FollowUp, HeroClip.SlashL), Is.EqualTo(HeroClip.SlashR));
            Assert.That(HeroMotion.ClipFor(HeroAttackKind.FollowUp, HeroClip.SlashR), Is.EqualTo(HeroClip.Slash3));
            Assert.That(HeroMotion.ClipFor(HeroAttackKind.FollowUp, HeroClip.Slash3), Is.EqualTo(HeroClip.SlashR));
            Assert.That(HeroMotion.ClipFor(HeroAttackKind.FollowUp, HeroClip.Spin), Is.EqualTo(HeroClip.SlashL));
            Assert.That(HeroMotion.ClipFor(HeroAttackKind.Spin, HeroClip.SlashR), Is.EqualTo(HeroClip.Spin));
            Assert.That(HeroMotion.ClipFor(HeroAttackKind.Slam, HeroClip.SlashR), Is.EqualTo(HeroClip.Slam));
        }

        [Test]
        public void AStrideHopsAlongItsMoveFacingItsTravelAndABumpHopsInPlace()
        {
            var motion = new HeroMotion(Clips);
            Assert.That(motion.Evaluate(1.0, 0.75, 0.75, Direction.Down).X, Is.EqualTo(0.75), "before any step: the snapshot");
            motion.OnEvent(Step(2.0, 2.11, 0, 1.5, Direction.Right));
            var start = motion.Evaluate(2.0, 0, 0, Direction.Up);
            var mid = motion.Evaluate(2.055, 0, 0, Direction.Up);
            var end = motion.Evaluate(2.11, 0, 0, Direction.Up);
            Assert.That(start.Lift, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(mid.Lift, Is.EqualTo(HeroMotion.HopM).Within(1e-4f));
            Assert.That(end.Lift, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(mid.X, Is.EqualTo(0.75).Within(1e-9));
            Assert.That(end.X, Is.EqualTo(1.5).Within(1e-9));
            Assert.That(mid.Yaw, Is.EqualTo((float)(Math.PI / 2)).Within(1e-5f), "facing east");
            Assert.That(mid.Run, Is.EqualTo(1f), "the hop on the legs");
            Assert.That(motion.Evaluate(2.11 + HeroMotion.HopOut, 0, 0, Direction.Up).Run, Is.EqualTo(0f), "standing again");

            // The next stride carries the hop's weight on, from the other foot.
            motion.OnEvent(Step(2.2, 2.31, 1.5, 3.0, Direction.Right));
            var next = motion.Evaluate(2.2, 0, 0, Direction.Up);
            Assert.That(next.Run, Is.GreaterThan(0.05f));
            Assert.That(next.RunTime, Is.EqualTo(Clips[HeroClip.Run].length * 0.5f).Within(1e-5f));

            motion.OnEvent(Step(3.0, 3.07, 3.0, 3.0, Direction.Left, bump: true, late: true));
            var bump = motion.Evaluate(3.035, 0, 0, Direction.Up);
            Assert.That(bump.X, Is.EqualTo(3.0), "a bump stays on its tile");
            Assert.That(bump.Lift, Is.EqualTo(HeroMotion.BumpHopM).Within(1e-4f));
            Assert.That(bump.Yaw, Is.EqualTo((float)(-Math.PI / 2)).Within(1e-5f));
        }

        [Test]
        public void TheHeroTurnsToItsSwingAndTheBladeFlaresOnEachImpact()
        {
            var motion = new HeroMotion(Clips);
            motion.OnEvent(Step(0.5, 0.61, 0, 1.5, Direction.Right));
            motion.OnEvent(Attack(HeroAttackKind.SlashLeft, 0.7, 1.0, dirX: 0, dirY: -1));
            motion.OnEvent(Attack(HeroAttackKind.Spin, 1.18, 1.5, heavy: true, dirX: 0, dirY: -1));
            Assert.That(motion.Evaluate(0.69, 0, 0, Direction.Up).Yaw, Is.EqualTo((float)(Math.PI / 2)).Within(1e-5f), "its travel before the swing");
            Assert.That(Math.Abs(motion.Evaluate(0.8, 0, 0, Direction.Up).Yaw), Is.EqualTo((float)Math.PI).Within(1e-5f), "south, at the swing");
            Assert.That(motion.Evaluate(0.999, 0, 0, Direction.Up).Flare, Is.EqualTo(0f));
            Assert.That(motion.Evaluate(1.0, 0, 0, Direction.Up).Flare, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(motion.Evaluate(1.0 + HeroMotion.FlareS / 2, 0, 0, Direction.Up).Flare, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(motion.Evaluate(1.5, 0, 0, Direction.Up).Flare, Is.EqualTo(HeroMotion.HeavyFlare).Within(1e-5f), "a heavy blow flares more");
            Assert.That(motion.Evaluate(3.5, 0, 0, Direction.Up).Yaw, Is.EqualTo((float)(Math.PI / 2)).Within(1e-5f), "back to its travel after the follow-through");
        }

        [Test]
        public void OnlyTheSwordsAttacksSwingTheKnight()
        {
            var motion = new HeroMotion(Clips);
            // Content's magic wand (weapon 4, a missile): its kind 0 is no sword strike, whatever HeroAttackKind 0 is.
            motion.OnEvent(SimEvent.HeroAttack(0.7, 4, ShapeKind.Missile, 0, 0.7, 1.0, 1, 0, false, 0));
            Assert.That(motion.SwingCount, Is.EqualTo(0), "another weapon plays no sword clip");
            Assert.That(motion.Evaluate(1.0, 0, 0, Direction.Up).Attack, Is.EqualTo(0f));
            Assert.That(motion.Evaluate(1.0, 0, 0, Direction.Up).Flare, Is.EqualTo(0f));
            motion.OnEvent(Attack(HeroAttackKind.SlashLeft, 1.2, 1.5));
            Assert.That(motion.SwingCount, Is.EqualTo(1));
            Assert.That(motion.ClipTime(0, 1.5, out var clip, out _), Is.True);
            Assert.That(clip, Is.EqualTo(HeroClip.SlashL));
        }
    }
}
