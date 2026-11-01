using System;
using System.IO;
using Game.Client.Hero;
using NUnit.Framework;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using Is = NUnit.Framework.Is;

namespace Game.Tests
{
    /// <summary>The hero's motion: attacks retimed onto the sim's impacts, follow-ups, hops, swings on the walk and the blade's flare.</summary>
    public class HeroTests
    {
        const double Fps = 60;
        static readonly HeroClips Clips = HeroClips.Parse(File.ReadAllText(HeroClips.ContentPath));
        static readonly HeroClip[] Attacks = { HeroClip.SlashL, HeroClip.SlashR, HeroClip.Slash3, HeroClip.Spin, HeroClip.Slam };

        static SimEvent Attack(HeroAttackKind kind, double windupStart, double impact, bool heavy = false, double dirX = 1, double dirY = 0) =>
            SimEvent.SwordAttack(windupStart, 0, kind, windupStart, impact, dirX, dirY, heavy, 7.04);

        static SimEvent Step(double start, double end, double fromX, double toX, Direction facing, bool bump = false, bool pressed = false) =>
            SimEvent.HeroStep(start, 0, pressed, bump, false, facing, fromX, 0, toX, 0, start, end);

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

        /// <summary>
        /// A wind-up longer than the clip's at 0.75x (a free press landing on a far eighth) starts the clip at the wind-up
        /// anyway and coils, slowly, into the strike, which runs at 0.75x onto the impact: never a late start with
        /// nothing on screen.
        /// </summary>
        [Test]
        public void ALongWindupCoilsFromItsStartIntoTheStrike()
        {
            foreach (var clip in new[] { HeroClip.SlashL, HeroClip.SlashR })
            {
                var motion = new HeroMotion(Clips);
                if (clip == HeroClip.SlashR) motion.OnEvent(Attack(HeroAttackKind.SlashLeft, 0, 0.32));
                float impact = Clips[clip].impact;
                double windup = 2, at = windup + 2.5 * impact / HeroClips.MinSpeed;
                motion.OnEvent(Attack(clip == HeroClip.SlashL ? HeroAttackKind.SlashLeft : HeroAttackKind.SlashRight, windup, at));
                int swing = motion.SwingCount - 1;
                var first = motion.Evaluate(windup, 0, 0, Direction.Up);
                Assert.That(first.A.Clip, Is.EqualTo(clip));
                Assert.That(first.A.Time, Is.EqualTo(0f).Within(1e-5f), $"{clip}: from its first frame at the wind-up");
                Assert.That(first.Attack, Is.GreaterThan(0f), $"{clip}: showing on the wind-up's frame");
                float last = 0;
                for (double t = windup + 1 / Fps; t < at - 1e-9; t += 1 / Fps)
                {
                    motion.ClipTime(swing, t, out _, out float time);
                    Assert.That(time, Is.GreaterThan(last), $"{clip} at {t}: moving all the way");
                    Assert.That(time - last, Is.LessThanOrEqualTo(HeroClips.MinSpeed / Fps + 1e-4), $"{clip} at {t}: no faster than 0.75x");
                    last = time;
                }
                motion.ClipTime(swing, at, out _, out float landed);
                Assert.That(landed, Is.EqualTo(impact).Within(1e-4f), $"{clip}: on the impact");
                motion.ClipTime(swing, at - (1 - HeroClips.CoilShare) * impact / HeroClips.MinSpeed, out _, out float coiled);
                Assert.That(coiled, Is.EqualTo(HeroClips.CoilShare * impact).Within(1e-4f), $"{clip}: the strike at 0.75x from the coil");
            }
        }

        /// <summary>
        /// A free press (game-modes.md, Attacking on the beat): its attack event arrives as the sim takes it, its wind-up
        /// from when it was heard and its impact on an eighth. On the key-down frame, at the heard time, the hero is
        /// already in the attack, in the clip's wind-up.
        /// </summary>
        [Test]
        public void AFreePressShowsItsWindupOnTheKeyDownFrame()
        {
            foreach (double gap in new[] { 0.12, 0.25, 0.37, 0.62 })
            {
                var motion = new HeroMotion(Clips);
                double heard = 10;
                motion.OnEvent(SimEvent.SwordAttack(heard + 0.03, 0, HeroAttackKind.SlashLeft, heard, heard + gap, 1, 0, false, 7.04));
                var pose = motion.Evaluate(heard, 0, 0, Direction.Up);
                Assert.That(pose.Attack, Is.GreaterThan(0f), $"{gap} s to the impact: in the attack on the key-down frame");
                Assert.That(pose.A.Time, Is.LessThan(Clips[HeroClip.SlashL].impact), $"{gap} s: in the wind-up");
                Assert.That(pose.Yaw, Is.EqualTo((float)(Math.PI / 2)).Within(1e-5f), $"{gap} s: turned to the swing");
                Assert.That(motion.Evaluate(heard + 1 / Fps, 0, 0, Direction.Up).A.Time, Is.GreaterThan(pose.A.Time), $"{gap} s: and moving");
                motion.ClipTime(0, heard + gap, out _, out float landed);
                Assert.That(landed, Is.EqualTo(Clips[HeroClip.SlashL].impact).Within(1e-4f), $"{gap} s: on the impact");
            }
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
        public void AGlideWalksAtItsVelocityAndAHeardInputsGlideReplacesThoseAfterIt()
        {
            var motion = new HeroMotion(Clips);
            motion.OnEvent(SimEvent.HeroGlide(1.0, 1.0, 2, 3, 0, 0, Direction.Down));
            motion.OnEvent(SimEvent.HeroGlide(2.0, 2.0, 2, 3, 6, 0, Direction.Right));
            var start = motion.Evaluate(2.0, 0, 0, Direction.Up);
            var walking = motion.Evaluate(2.5, 0, 0, Direction.Up);
            Assert.That(start.X, Is.EqualTo(2).Within(1e-9));
            Assert.That(start.Run, Is.EqualTo(0f), "the run's weight comes up from standing");
            Assert.That(walking.X, Is.EqualTo(5).Within(1e-9));
            Assert.That(walking.Y, Is.EqualTo(3).Within(1e-9));
            Assert.That(walking.Lift, Is.EqualTo(0f), "a walk, not hops");
            Assert.That(walking.Run, Is.EqualTo(1f));
            Assert.That(walking.Yaw, Is.EqualTo((float)(Math.PI / 2)).Within(1e-5f), "facing east");
            Assert.That(motion.Evaluate(2.25, 0, 0, Direction.Up).RunTime, Is.EqualTo(Clips[HeroClip.Run].length * (1.5 / HeroMotion.CycleM)).Within(1e-4f),
                "a cycle each CycleM walked");

            // A turn north heard at 2.4, emitted at 2.45 after the sim had already emitted a stop at 2.42.
            motion.OnEvent(SimEvent.HeroGlide(2.42, 2.42, 4.52, 3, 0, 0, Direction.Right));
            motion.OnEvent(SimEvent.HeroGlide(2.45, 2.4, 4.4, 3, 0, 6, Direction.Up));
            var turned = motion.Evaluate(2.5, 0, 0, Direction.Up);
            Assert.That(turned.X, Is.EqualTo(4.4).Within(1e-9), "the stop it replaced is gone");
            Assert.That(turned.Y, Is.EqualTo(3.6).Within(1e-9));
            Assert.That(turned.Run, Is.EqualTo(1f), "still walking");

            motion.OnEvent(SimEvent.HeroGlide(3.0, 3.0, 4.4, 6.6, 0, 0, Direction.Up));
            var stopped = motion.Evaluate(3.0 + HeroMotion.HopOut, 0, 0, Direction.Down);
            Assert.That(stopped.Run, Is.EqualTo(0f), "standing again");
            Assert.That(stopped.Yaw, Is.EqualTo(0f).Within(1e-5f), "facing the way it last went");
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

            motion.OnEvent(Step(3.0, 3.07, 3.0, 3.0, Direction.Left, bump: true, pressed: true));
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

        static double Off(double a, double b) => Math.IEEERemainder(a - b, 2 * Math.PI);

        static double Deg(double d) => d * Math.PI / 180;

        /// <summary>A swing aimed at <paramref name="yaw"/> (radians clockwise from north) while the hero walks.</summary>
        static SimEvent AimedAttack(double windupStart, double impact, double yaw) =>
            Attack(HeroAttackKind.SlashLeft, windupStart, impact, dirX: Math.Sin(yaw), dirY: Math.Cos(yaw));

        /// <summary>
        /// Walking east, a swing to the north (game-modes.md, Swinging on the move): the legs keep running east and the
        /// upper body twists to the swing, eased in so it is on the aim before the impact, and back out after the
        /// follow-through; up to 120 degrees either way the twist alone reaches the aim.
        /// </summary>
        [Test]
        public void WalkingTheLegsKeepToTheTravelAndTheUpperBodyTwistsToTheSwing()
        {
            double east = Math.PI / 2, windup = 2, impact = 2.3;
            var motion = new HeroMotion(Clips);
            motion.OnEvent(SimEvent.HeroGlide(1.0, 1.0, 0, 0, 6, 0, Direction.Right));
            motion.OnEvent(AimedAttack(windup, impact, 0));

            var before = motion.Evaluate(windup - 0.01, 0, 0, Direction.Up);
            Assert.That(before.Twist, Is.EqualTo(0f));
            Assert.That(before.Yaw, Is.EqualTo((float)east).Within(1e-5f));
            Assert.That(before.Run, Is.EqualTo(1f), "running");
            Assert.That(before.Hips, Is.EqualTo(1f), "the walk carries the hips");

            var entry = motion.Evaluate(windup, 0, 0, Direction.Up);
            Assert.That(entry.Attack, Is.GreaterThan(0f), "the swing shows on its first frame");
            Assert.That(entry.Twist, Is.EqualTo(0f).Within(1e-5f), "the twist eases in");
            float easing = motion.Evaluate(windup + 0.3 * HeroMotion.TwistLead * (impact - windup), 0, 0, Direction.Up).Twist;
            Assert.That(easing, Is.InRange(-east + 0.1, -0.05), "twisting left, part of the way");
            var led = motion.Evaluate(windup + HeroMotion.TwistLead * (impact - windup), 0, 0, Direction.Up);
            Assert.That(led.Twist, Is.EqualTo((float)-east).Within(1e-5f), "on the aim before the impact: the torso leads");

            var atImpact = motion.Evaluate(impact, 0, 0, Direction.Up);
            Assert.That(atImpact.Yaw, Is.EqualTo((float)east).Within(1e-5f), "the legs face the travel");
            Assert.That(atImpact.Twist, Is.EqualTo((float)-east).Within(1e-5f), "the upper body faces the swing");
            Assert.That(atImpact.Run, Is.EqualTo(1f).Within(1e-5f), "still running forward");
            Assert.That(atImpact.Hips, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(atImpact.Attack, Is.EqualTo(1f).Within(1e-5f));

            var after = motion.Evaluate(4, 0, 0, Direction.Up);
            Assert.That(after.Twist, Is.EqualTo(0f).Within(1e-5f), "unwound after the follow-through");
            Assert.That(after.Yaw, Is.EqualTo((float)east).Within(1e-5f));

            foreach (double offset in new[] { -119.0, -90, -45, 10, 60, 100, 119 })
            {
                var m = new HeroMotion(Clips);
                m.OnEvent(SimEvent.HeroGlide(1.0, 1.0, 0, 0, 6, 0, Direction.Right));
                m.OnEvent(AimedAttack(windup, impact, east + Deg(offset)));
                var p = m.Evaluate(impact, 0, 0, Direction.Up);
                Assert.That(p.Yaw, Is.EqualTo((float)east).Within(1e-5f), $"{offset} degrees: the legs on the travel");
                Assert.That(p.Twist, Is.EqualTo((float)Deg(offset)).Within(1e-5f), $"{offset} degrees: the twist reaches the aim");
                Assert.That(p.Run, Is.EqualTo(1f).Within(1e-5f), $"{offset} degrees: the forward run");
            }
        }

        /// <summary>A swing entering before the one before has followed through holds the twist: it never lets go between them.</summary>
        [Test]
        public void AChainOfSwingsHoldsTheTwist()
        {
            var motion = new HeroMotion(Clips);
            motion.OnEvent(SimEvent.HeroGlide(1.0, 1.0, 0, 0, 6, 0, Direction.Right));
            motion.OnEvent(AimedAttack(2, 2.3, 0));
            motion.OnEvent(AimedAttack(2.6, 2.9, 0));
            for (double t = 2 + HeroMotion.TwistLead * 0.3; t <= 3.0; t += 1 / Fps)
                Assert.That(motion.Evaluate(t, 0, 0, Direction.Up).Twist, Is.EqualTo((float)(-Math.PI / 2)).Within(1e-4f), $"at {t}");

            // A swing turning the other way crossfades the twist across, from the one before's aim to its own.
            motion.OnEvent(AimedAttack(3.0, 3.3, Math.PI * 0.9));
            float last = motion.Evaluate(3.0, 0, 0, Direction.Up).Twist;
            Assert.That(last, Is.EqualTo((float)(-Math.PI / 2)).Within(1e-4f));
            for (double t = 3.0 + 1 / Fps; t <= 3.0 + HeroMotion.TwistLead * 0.3; t += 1 / Fps)
            {
                float twist = motion.Evaluate(t, 0, 0, Direction.Up).Twist;
                Assert.That(Math.Abs(twist - last), Is.LessThan(0.5f), $"at {t}: no jump");
                last = twist;
            }
            Assert.That(motion.Evaluate(3.0 + HeroMotion.TwistLead * 0.3, 0, 0, Direction.Up).Twist, Is.EqualTo((float)(Math.PI * 0.4)).Within(1e-4f),
                "on the new aim");
        }

        /// <summary>
        /// A swing entering long after an earlier one has unwound eases straight in toward its own aim: the old swing's aim
        /// (here behind the walk, on the other side) never bends its way in round the back.
        /// </summary>
        [Test]
        public void AFreshSwingEasesInFromTheTravelNotFromAnOldSwingsAim()
        {
            double east = Math.PI / 2;
            var motion = new HeroMotion(Clips);
            motion.OnEvent(SimEvent.HeroGlide(1.0, 1.0, 0, 0, 6, 0, Direction.Right));
            motion.OnEvent(AimedAttack(2, 2.3, east + Deg(170)));
            motion.OnEvent(AimedAttack(5, 5.3, 0));
            Assert.That(motion.Evaluate(4.9, 0, 0, Direction.Up).Twist, Is.EqualTo(0f), "the old swing has unwound");
            float last = 0;
            for (double t = 5; t <= 5.3; t += 1 / Fps)
            {
                float twist = motion.Evaluate(t, 0, 0, Direction.Up).Twist;
                Assert.That(twist, Is.LessThanOrEqualTo(last + 1e-5f), $"at {t}: twisting left toward the aim, never round the back");
                last = twist;
            }
            Assert.That(last, Is.EqualTo((float)-east).Within(1e-5f));
        }

        /// <summary>
        /// The walk turns mid-swing (east to north, toward the strike), the twist already on the aim: the drawn legs ease
        /// round under an upper body that stays on the strike, rather than the torso swinging off the aim with the legs' lag.
        /// </summary>
        [Test]
        public void TurningTheWalkMidSwingKeepsTheDrawnUpperBodyOnTheAim()
        {
            var motion = new HeroMotion(Clips);
            motion.OnEvent(SimEvent.HeroGlide(1.0, 1.0, 0, 0, 6, 0, Direction.Right));
            motion.OnEvent(AimedAttack(2, 2.3, 0));
            motion.OnEvent(SimEvent.HeroGlide(2.25, 2.25, 7.5, 0, 0, 6, Direction.Up));
            var turn = new HeroTurn();
            double turned = 0;
            for (double t = 1.5, last = t; t <= 2.4; last = t, t += 1 / Fps)
            {
                var p = motion.Evaluate(t, 0, 0, Direction.Up);
                float meant = p.Yaw + p.Twist;
                turn.Ease(ref p, t - last);
                if (t > 2.24) Assert.That(Math.Abs(Off(turn.Legs + p.Twist, meant)), Is.LessThan(0.05), $"at {t}: the upper body on the strike");
                if (t < 2) Assert.That(p.Twist, Is.EqualTo(0f), $"at {t}: no swing, no twist");
                if (t > 2.25) turned = Math.Max(turned, Math.Abs(Off(turn.Legs, Math.PI / 2)));
            }
            Assert.That(turned, Is.GreaterThan(1.0), "the legs turned to the new walk");
        }

        /// <summary>
        /// Past 120 degrees the twist unwinds and the whole body turns, the legs coming off the forward run: square to
        /// the travel they take the attack's footwork, and a strike straight behind backpedals with no twist, whichever
        /// side it is on.
        /// </summary>
        [Test]
        public void PastTheClampTheBodyTurnsAndTheLegsComeOffTheForwardRun()
        {
            double windup = 2, impact = 2.3;
            HeroPose At(double offset)
            {
                var m = new HeroMotion(Clips);
                m.OnEvent(SimEvent.HeroGlide(1.0, 1.0, 0, 0, 0, 6, Direction.Up));
                m.OnEvent(AimedAttack(windup, impact, Deg(offset)));
                var p = m.Evaluate(impact, 0, 0, Direction.Up);
                Assert.That(Off(p.Yaw + p.Twist, Deg(offset)), Is.EqualTo(0).Within(1e-5), $"{offset} degrees: the upper body on the aim");
                Assert.That(Math.Abs(p.Twist), Is.LessThanOrEqualTo(HeroMotion.TwistMax + 1e-5), $"{offset} degrees: within the clamp");
                return p;
            }

            foreach (double sign in new[] { 1.0, -1.0 })
            {
                var past = At(sign * 130);
                Assert.That(past.Twist, Is.EqualTo((float)(sign * Deg(100))).Within(1e-4f), "10 degrees past: a sixth unwound");
                Assert.That(past.Yaw, Is.EqualTo((float)(sign * Deg(30))).Within(1e-4f), "the legs turned off the travel");
                Assert.That(past.Run, Is.EqualTo((float)Math.Cos(Deg(30))).Within(1e-4f), "off the forward run");
                Assert.That(past.Back, Is.EqualTo(0f));
                Assert.That(past.Hips, Is.EqualTo(past.Run).Within(1e-5f));

                var square = At(sign * 150);
                Assert.That(square.Yaw, Is.EqualTo((float)(sign * Deg(90))).Within(1e-4f), "square to the travel");
                Assert.That(square.Run + square.Back, Is.LessThan(1e-4f), "the legs and hips on the attack's footwork");
                Assert.That(square.Hips, Is.LessThan(1e-4f));
                Assert.That(square.Attack, Is.EqualTo(1f).Within(1e-5f));
            }

            var behindLeft = At(-179.9);
            var behindRight = At(179.9);
            foreach (var behind in new[] { behindLeft, behindRight })
            {
                Assert.That(Math.Abs(behind.Twist), Is.LessThan(0.01f), "straight behind: no twist");
                Assert.That(Math.Abs(Off(behind.Yaw, Math.PI)), Is.LessThan(0.01), "the legs on the aim");
                Assert.That(behind.Back, Is.EqualTo(1f).Within(1e-4f), "backpedalling");
                Assert.That(behind.Run, Is.EqualTo(0f));
            }
            Assert.That(Math.Abs(Off(behindLeft.Yaw, behindRight.Yaw)), Is.LessThan(0.02), "either side of straight behind, the same pose");

            // The backpedal is the run clip played backward: as the walk goes on, its time goes back.
            var walk = new HeroMotion(Clips);
            walk.OnEvent(SimEvent.HeroGlide(1.0, 1.0, 0, 0, 0, 6, Direction.Up));
            var a = walk.Evaluate(1.4, 0, 0, Direction.Up);
            var b = walk.Evaluate(1.4 + 1 / Fps, 0, 0, Direction.Up);
            Assert.That(b.RunTime - a.RunTime, Is.GreaterThan(0f));
            Assert.That(b.BackTime - a.BackTime, Is.EqualTo(-(b.RunTime - a.RunTime)).Within(1e-4f));
        }

        /// <summary>Standing, a swing turns the whole body as it always has: no twist, the walk's run and hips gone.</summary>
        [Test]
        public void StandingASwingTurnsTheWholeBody()
        {
            var motion = new HeroMotion(Clips);
            motion.OnEvent(SimEvent.HeroGlide(1.0, 1.0, 0, 0, 6, 0, Direction.Right));
            motion.OnEvent(SimEvent.HeroGlide(1.5, 1.5, 3, 0, 0, 0, Direction.Right));
            motion.OnEvent(AimedAttack(2, 2.3, Math.PI));
            var p = motion.Evaluate(2.3, 0, 0, Direction.Up);
            Assert.That(Math.Abs(p.Yaw), Is.EqualTo((float)Math.PI).Within(1e-5f), "turned to the swing");
            Assert.That(p.Twist, Is.EqualTo(0f));
            Assert.That(p.Run + p.Back + p.Hips, Is.EqualTo(0f));
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
