using System;
using Game.Client.Look;
using NUnit.Framework;
using PS.Game.Sim;
using UnityEngine;
using Is = NUnit.Framework.Is;

namespace Game.Tests
{
    /// <summary>The look: the fast lane, the post's beat feel, the moments' punch and shake, and the camera rig.</summary>
    public class LookTests
    {
        const double Fps = 60, Period = 0.5, Lead = 0.09;

        /// <summary>
        /// A sim fed 12 s of steady music, each entry known 2 s before it sounds: confident ticks every
        /// 50 ms at <paramref name="level"/>, committed beats every half second from 2 s, and whatever
        /// <paramref name="extra"/> adds.
        /// </summary>
        static Simulation Music(LoudnessLevel level = LoudnessLevel.Medium, Action<Action<StreamEntry>> extra = null)
        {
            var sim = new Simulation(new SimConfig());
            void Add(StreamEntry e) => sim.AddKnown(e, Math.Max(0, e.T - 2));
            for (int k = 0; k <= 240; k++) Add(StreamEntry.Tick(k * 0.05, 0, evidence: 0.9, energy: 0.6, level: level, kick: 0.1));
            for (int b = 0; b < 20; b++) Add(StreamEntry.Beat(2 + b * Period, 0, Period, 0.9));
            extra?.Invoke(Add);
            return sim;
        }

        static LaneFrame ReadAt(Simulation sim, FastLane lane, double s, FeelMoments moments = null)
        {
            sim.StepTo(Math.Max(sim.Now, s + Lead));
            return lane.Read(sim.Stream, sim.Beats, s, moments);
        }

        [Test]
        public void TheBeatPulseLandsOnTheBeatFrameAndDecaysOverTheBeat()
        {
            var sim = Music();
            var lane = new FastLane();
            const double beat = 6.0; // a frame time at 60 fps, well after the lock
            var before = ReadAt(sim, lane, beat - 1 / Fps);
            var on = ReadAt(sim, lane, beat);
            var after = ReadAt(sim, lane, beat + 1 / Fps);
            var half = ReadAt(sim, lane, beat + Period / 2);

            Assert.That(on.Pulse, Is.EqualTo(1).Within(1e-9));
            Assert.That(before.Pulse, Is.LessThan(0.01));
            Assert.That(after.Pulse, Is.LessThan(on.Pulse).And.GreaterThan(0.9));
            Assert.That(half.Pulse, Is.EqualTo(0.25).Within(1e-6));
            Assert.That(on.SinceBeat, Is.EqualTo(0).Within(1e-9));
            Assert.That(on.Period, Is.EqualTo(Period).Within(1e-9));
            Assert.That(on.Level, Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void NoPulseBeforeTheBeatIsReliableOrAfterItStops()
        {
            var sim = Music();
            var lane = new FastLane();
            Assert.That(ReadAt(sim, lane, 1.0).Pulse, Is.EqualTo(0)); // no beat yet
            var silent = new Simulation(new SimConfig());
            Assert.That(new FastLane().Read(silent.Stream, silent.Beats, 1.0).Level, Is.EqualTo(-1)); // no loudness known
            // The last beat is at 11.5; it continues 1.5 periods, then nothing is known ahead.
            Assert.That(ReadAt(sim, lane, 11.5 + 1.6 * Period).Pulse, Is.EqualTo(0));
        }

        [Test]
        public void AKickIsAnticipatedAbout90MsAheadAndADropBuildsOverTheSecondsBeforeIt()
        {
            var sim = Music(extra: add =>
            {
                add(StreamEntry.Onset(7.0, 0, strength: 1, strong: true, band: (int)DrumBand.Kick));
                add(StreamEntry.Jump(10.0, 0, db: 12));
            });
            var lane = new FastLane();
            var moments = new FeelMoments();
            Assert.That(ReadAt(sim, lane, 7.0 - 0.1, moments).KickPre, Is.EqualTo(0));
            double mid = ReadAt(sim, lane, 7.0 - 0.045, moments).KickPre;
            Assert.That(mid, Is.EqualTo(0.25).Within(1e-6)); // (1 - 0.045 / 0.09)²
            var on = ReadAt(sim, lane, 7.0, moments);
            Assert.That(on.Kick, Is.EqualTo(1).Within(1e-9));
            Assert.That(ReadAt(sim, lane, 7.0 + 0.11, moments).Kick, Is.EqualTo(Math.Exp(-1)).Within(1e-6));

            Assert.That(ReadAt(sim, lane, 10.0 - 1.7, moments).Rise, Is.EqualTo(0));
            double early = ReadAt(sim, lane, 10.0 - 1.2, moments).Rise, late = ReadAt(sim, lane, 10.0 - 0.1, moments).Rise;
            Assert.That(early, Is.GreaterThan(0).And.LessThan(late));
            Assert.That(late, Is.GreaterThan(0.95));
            Assert.That(ReadAt(sim, lane, 10.0 + 1 / Fps, moments).Rise, Is.EqualTo(0));

            // The accent and the drop became moments, flashing from when they sound.
            Assert.That(moments.Flash(7.0 - 1 / Fps, Period), Is.EqualTo(0));
            Assert.That(moments.Flash(7.0, Period), Is.EqualTo(1).Within(1e-6));
            Assert.That(moments.Flash(10.0, Period), Is.EqualTo(1).Within(1e-6));
        }

        [Test]
        public void TheLaneNeverShowsAnEntryBeforeItIsKnown()
        {
            // An onset known only after it sounds shows from when it became known, not back-dated.
            var sim = new Simulation(new SimConfig());
            sim.AddKnown(StreamEntry.Onset(3.0, 0, 1, strong: true, band: (int)DrumBand.Kick), 3.05);
            var lane = new FastLane();
            var moments = new FeelMoments();
            Assert.That(ReadAt(sim, lane, 3.0, moments).Kick, Is.EqualTo(0)); // revealed (the frontier passed 3.05) but not yet known
            Assert.That(moments.Flash(3.0, Period), Is.EqualTo(0));
            Assert.That(moments.Flash(3.05, Period), Is.EqualTo(1).Within(1e-6));
        }

        [Test]
        public void ThePostPulsesOnTheBeatAndFlashesOnAccentsWithAberrationOnlyThen()
        {
            var sim = Music(extra: add => add(StreamEntry.Onset(8.0, 0, 1, strong: true)));
            var lane = new FastLane();
            var moments = new FeelMoments();
            var post = new PostFeel();
            PostFeel.Offsets At(double s) => post.At(s, ReadAt(sim, lane, s, moments), moments);

            var rest = At(6.0 - 1 / Fps);
            var beat = At(6.0);
            Assert.That(beat.Exposure - rest.Exposure, Is.EqualTo(PostFeel.BeatExposure).Within(0.01));
            Assert.That(beat.Bloom, Is.GreaterThan(rest.Bloom + 0.3f));
            Assert.That(beat.Vignette, Is.LessThan(rest.Vignette));
            Assert.That(beat.Chromatic, Is.EqualTo(0));
            Assert.That(rest.Chromatic, Is.EqualTo(0));

            var accent = At(8.0);
            Assert.That(accent.Chromatic, Is.EqualTo(PostFeel.FlashChromatic).Within(1e-6));
            Assert.That(accent.Saturation, Is.EqualTo(PostFeel.FlashSaturation).Within(1e-4));
            Assert.That(accent.Exposure, Is.EqualTo(PostFeel.BeatExposure + PostFeel.FlashExposure).Within(0.01));
            Assert.That(At(8.0 + Period).Chromatic, Is.EqualTo(0)); // gone within a beat
        }

        [Test]
        public void AZoneGradeEasesInOverTwoBarsFromItsChange()
        {
            var post = new PostFeel();
            post.SetZone("lava", from: 10, barS: 1);
            var lava = PostFeel.GradeOf("lava");
            Assert.That(post.Grade(10).Temperature, Is.EqualTo(0));
            Assert.That(post.Grade(11).Temperature, Is.EqualTo(lava.Temperature / 2).Within(1e-4));
            Assert.That(post.Grade(12).Temperature, Is.EqualTo(lava.Temperature));
            // A change mid-ease starts from where the grade is.
            post.SetZone("crypt", from: 11, barS: 1);
            Assert.That(post.Grade(11).Temperature, Is.EqualTo(lava.Temperature / 2).Within(1e-4));
            Assert.That(post.Grade(13).Temperature, Is.EqualTo(PostFeel.GradeOf("crypt").Temperature));
        }

        [Test]
        public void ABreakFlashesAndShakesByItsThrowAgainstAHeavyStrikes()
        {
            // The sim's force is the hit's throw in metres: a heavy strike's 4.63 m, a cleave band's 1.85 m.
            var heavy = new FeelMoments();
            heavy.OnEvent(SimEvent.StructureBreak(6.0, 3, StructureHit.HeavyStrike, 0, 0, 1, 0, force: FeelMoments.HeavyThrowM));
            var band = new FeelMoments();
            band.OnEvent(SimEvent.StructureBreak(6.0, 3, StructureHit.Cleave, 0, 0, 1, 0, force: 1.85));
            Assert.That(heavy.ShakeAmplitude(6.0, Period), Is.EqualTo(0.4f).Within(1e-5));
            Assert.That(heavy.Flash(6.0, Period), Is.EqualTo(1f).Within(1e-5));
            Assert.That(band.ShakeAmplitude(6.0, Period), Is.EqualTo(0.15f + 0.25f * 1.85f / FeelMoments.HeavyThrowM).Within(1e-5));
            Assert.That(band.Flash(6.0, Period), Is.LessThan(0.75f));
        }

        [Test]
        public void HeavyStrikesPunchAndShakeByTheirKillsWithinBounds()
        {
            var moments = new FeelMoments();
            for (int i = 0; i < FeelMoments.CrispKills; i++) moments.OnEvent(SimEvent.Death(5.0, i, 0, 0, 0, 1, 0, 1));
            Assert.That(moments.Punch(5.1, Period), Is.EqualTo(1)); // a crisp slash: no punch
            for (int i = 0; i < 100; i++) moments.OnEvent(SimEvent.Death(5.0, i, 0, 0, 0, 1, 0, 1));
            float sixteenth = (float)(Period / 4);
            Assert.That(moments.Punch(5.0 + sixteenth, Period), Is.EqualTo(1 - FeelMoments.AccentPunch - FeelMoments.BlowPunch).Within(1e-5));
            Assert.That(moments.Punch(5.0 + sixteenth + Period, Period), Is.EqualTo(1));
            Assert.That(moments.ShakeAmplitude(5.0, Period), Is.EqualTo(FeelMoments.BlowShake).Within(1e-6));
            Assert.That(moments.Flash(5.0, Period), Is.EqualTo(FeelMoments.BlowFlash).Within(1e-6));

            // A break shakes past the hits' cap, but never past its own.
            moments.OnEvent(SimEvent.StructureBreak(6.0, 3, StructureHit.HeavyStrike, 0, 0, 1, 0, force: 5));
            Assert.That(moments.ShakeAmplitude(6.0, Period), Is.EqualTo(FeelMoments.BreakShakeCap).Within(1e-6));
            for (int k = 0; k < 600; k++)
                Assert.That(CameraRig.Shake(6 + k / Fps, 0.4f).magnitude, Is.LessThanOrEqualTo(0.4f + 1e-5f));
        }

        [Test]
        public void TheCameraFollowsAStridingHeroSmoothlyAtAFixed60Fps()
        {
            var rig = new CameraRig();
            double px = 0, py = 0, pvx = 0, pvy = 0, maxSpeed = 0, maxAccel = 0, maxHeroGap = 0;
            int frames = (int)(4 * SyntheticHero.Side * SyntheticHero.StepS * Fps) * 2; // two laps
            for (int n = 0; n <= frames; n++)
            {
                double s = n / Fps;
                SyntheticHero.At(s, out double hx, out double hy, out var facing);
                CameraRig.Heading(facing, out double dx, out double dy);
                rig.Step(s, n == 0 ? 0 : 1 / Fps, hx, hy, dx, dy, level: 1, Period);
                double vx = (rig.FocusX - px) * Fps, vy = (rig.FocusY - py) * Fps;
                if (n >= 2)
                {
                    maxSpeed = Math.Max(maxSpeed, Math.Sqrt(vx * vx + vy * vy));
                    maxAccel = Math.Max(maxAccel, Math.Sqrt((vx - pvx) * (vx - pvx) + (vy - pvy) * (vy - pvy)) * Fps);
                }
                if (n > Fps) maxHeroGap = Math.Max(maxHeroGap, Math.Sqrt((rig.FocusX - hx) * (rig.FocusX - hx) + (rig.FocusY - hy) * (rig.FocusY - hy)));
                px = rig.FocusX; py = rig.FocusY; pvx = vx; pvy = vy;
            }
            // The hero moves in 1.5 m strides of 0.11 s (13.6 m/s while striding, 6 m/s on average);
            // the focus glides near its average speed, with no frame-to-frame jumps, and keeps the
            // hero within the footprint's slack.
            Assert.That(maxSpeed, Is.LessThan(9), "speed");
            Assert.That(maxAccel, Is.LessThan(60), "acceleration");
            Assert.That(maxHeroGap, Is.LessThan(CameraRig.FocusSlackM), "gap");

            // Standing, it settles on the lead ahead of the hero.
            for (int n = 0; n < 10 * Fps; n++) rig.Step(100 + n / Fps, 1 / Fps, 0, 0, 0, 1, 1, Period);
            Assert.That(rig.FocusX, Is.EqualTo(0).Within(1e-3));
            Assert.That(rig.FocusY, Is.EqualTo(CameraRig.LeadM).Within(1e-3));
        }

        [Test]
        public void TheViewWidthFollowsLoudnessEasedOverTwoBars()
        {
            var rig = new CameraRig();
            void StepAt(double s, double level) => rig.Step(s, 1 / Fps, 0, 0, 0, 1, level, Period);
            StepAt(0, -1); // no loudness known yet: holds the medium view
            Assert.That(rig.Level, Is.EqualTo(1));
            Assert.That(rig.Width(0), Is.EqualTo(CameraRig.MediumWidthM));
            StepAt(1, 1.6); // not clearly louder: holds
            Assert.That(rig.Level, Is.EqualTo(1));
            StepAt(2, 1.9);
            Assert.That(rig.Level, Is.EqualTo(2));
            double bars = 2 * 2 * Period;
            Assert.That(rig.Width(2), Is.EqualTo(CameraRig.MediumWidthM));
            Assert.That(rig.Width(2 + bars / 2), Is.EqualTo((CameraRig.MediumWidthM + CameraRig.LoudWidthM) / 2).Within(1e-4));
            Assert.That(rig.Width(2 + bars - 1 / Fps), Is.LessThan(CameraRig.LoudWidthM));
            Assert.That(rig.Width(2 + bars), Is.EqualTo(CameraRig.LoudWidthM));
            float previous = 0;
            for (int n = 0; n <= bars * Fps; n++)
            {
                float w = rig.Width(2 + n / Fps);
                Assert.That(w, Is.GreaterThanOrEqualTo(previous));
                previous = w;
            }
            StepAt(4, 0.1);
            Assert.That(rig.Level, Is.EqualTo(0));
            Assert.That(rig.Width(4 + bars), Is.EqualTo(CameraRig.QuietWidthM));
        }

        [Test]
        public void TheFramingShowsTheWidthAcrossTheGroundAtTheGamesPitch()
        {
            const float aspect = 16f / 9;
            CameraRig.Pose(new Vector3(10, 0, 20), 30, aspect, out var position, out var rotation);
            Assert.That(Vector3.Angle(rotation * Vector3.forward, Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up)), Is.EqualTo(CameraRig.PitchDeg).Within(1e-3));
            float distance = Vector3.Distance(position, new Vector3(10, 0, 20));
            float halfWidth = distance * Mathf.Tan(CameraRig.FovDeg * 0.5f * Mathf.Deg2Rad) * aspect;
            Assert.That(2 * halfWidth, Is.EqualTo(30).Within(1e-3));
        }

        [Test]
        public void TheFootprintIsAConvexTrapezoidAroundTheLoudViewWithSlack()
        {
            var p = CameraRig.Footprint(16f / 9);
            Assert.That(p.Length, Is.EqualTo(8));
            for (int i = 0; i < 4; i++)
            {
                double ax = p[2 * i], ay = p[2 * i + 1];
                double bx = p[(2 * i + 2) % 8], by = p[(2 * i + 3) % 8], cx = p[(2 * i + 4) % 8], cy = p[(2 * i + 5) % 8];
                Assert.That((bx - ax) * (cy - by) - (by - ay) * (cx - bx), Is.GreaterThan(0), "counter-clockwise and convex");
            }
            double near = p[2] - p[0], far = p[4] - p[6];
            Assert.That(far, Is.GreaterThan(near)); // the north edge is further away, so wider
            Assert.That(p[1], Is.LessThan(0)); Assert.That(p[5], Is.GreaterThan(0));
            // Through the focus, the view is the loud width plus the slack each side.
            double t = -p[1] / (p[7] - p[1]);
            double left = p[0] + (p[6] - p[0]) * t, right = p[2] + (p[4] - p[2]) * t;
            Assert.That(right - left, Is.EqualTo(CameraRig.LoudWidthM + 2 * CameraRig.FocusSlackM).Within(0.6));
        }
    }
}
