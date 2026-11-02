using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>
    /// The impulse field: rings set out on their frames, fade, merge in a mass kill and keep the strongest; calm rings
    /// draw at a share of their strength and special ones light the special-moment tier.
    /// </summary>
    public class ImpulseTests
    {
        // Special unless asked, so the strengths handed to shaders are the rings' own.
        static Impulse Ring(double t, float x = 0, float strength = 1, ImpulseKind kind = ImpulseKind.Blow, bool special = true) => new Impulse
        {
            Kind = kind, At = new Vector2(x, 0), Start = t, Strength = strength, Speed = 20, Reach = 10, Width = 1, Colour = Color.white,
            Special = special,
        };

        [Test]
        public void ARingSetsOutOnTheFirstFrameThatReachesItsTime()
        {
            var field = new ImpulseField();
            var shown = new List<double>();
            field.Shown = (kind, t) => shown.Add(t);
            field.Add(Ring(1.0));
            field.Publish(1.0 - 1.0 / 60);
            Assert.That((field.Published, shown.Count), Is.EqualTo((0, 0)));
            field.Publish(1.0);
            Assert.That(field.Published, Is.EqualTo(1));
            Assert.That(shown, Is.EqualTo(new[] { 1.0 }), "marked once, with its own time");
            Assert.That(field.PublishedRing(0).z, Is.EqualTo(0f), "the front starts at the centre");
            Assert.That(field.PublishedColour(0).w, Is.EqualTo(1f), "full strength on its first frame");
            field.Publish(1.1);
            Assert.That(shown.Count, Is.EqualTo(1));
            Assert.That(field.PublishedRing(0).z, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void ARingFadesOverItsRunAndIsForgottenAfter()
        {
            var field = new ImpulseField();
            field.Add(Ring(0));
            field.Publish(0.25);
            Assert.That(field.PublishedColour(0).w, Is.EqualTo(0.25f).Within(1e-5f), "half its run left: a quarter of its strength");
            field.Publish(0.5);
            Assert.That((field.Published, field.Count), Is.EqualTo((0, 0)));
        }

        [Test]
        public void SameInstantDeathsNearbyAreOneRing()
        {
            var field = new ImpulseField();
            for (int i = 0; i < 50; i++) field.Add(Ring(2.0, x: (i % 5) * 0.5f, strength: 0.35f, kind: ImpulseKind.Death));
            field.Add(Ring(2.0, x: 30, strength: 0.35f, kind: ImpulseKind.Death));
            field.Add(Ring(2.0, x: 0, kind: ImpulseKind.Blow));
            field.Add(Ring(2.5, x: 0, strength: 0.35f, kind: ImpulseKind.Death));
            Assert.That(field.Count, Is.EqualTo(4), "the clump, the far death, the blow, the later death");
            Assert.That(field[0].Strength, Is.EqualTo(ImpulseField.MergedCap), "the clump's strength capped");
            Assert.That(field[0].At.x, Is.EqualTo(1f).Within(1e-4f), "at the clump's weighted centre");
        }

        [Test]
        public void TheStrongestRingsAreHandedToShaders()
        {
            var field = new ImpulseField();
            for (int i = 0; i < ImpulseField.Max + 8; i++) field.Add(Ring(0, x: i * 10, strength: 0.1f + i * 0.01f));
            field.Publish(0);
            Assert.That(field.Published, Is.EqualTo(ImpulseField.Max));
            float weakest = float.MaxValue;
            for (int i = 0; i < ImpulseField.Max; i++) weakest = Mathf.Min(weakest, field.PublishedColour(i).w);
            Assert.That(weakest, Is.EqualTo(0.1f + 8 * 0.01f).Within(1e-5f), "the 8 weakest left out");
            Assert.That(field.Strongest.w, Is.EqualTo(0.1f + (ImpulseField.Max + 7) * 0.01f).Within(1e-5f));
            Assert.That(field.Strongest.x, Is.EqualTo((ImpulseField.Max + 7) * 10f));
        }

        [Test]
        public void RingsAddedOutOfOrderSetOutInOrder()
        {
            var field = new ImpulseField();
            var shown = new List<double>();
            field.Shown = (kind, t) => shown.Add(t);
            field.Add(Ring(0.3, x: 0));
            field.Add(Ring(0.1, x: 50));
            field.Add(Ring(0.2, x: 100));
            for (double t = 0; t <= 0.31; t += 0.05) field.Publish(t);
            Assert.That(shown, Is.EqualTo(new[] { 0.1, 0.2, 0.3 }));
        }

        [Test]
        public void ACalmRingDrawsAtItsShareAndOnlySpecialRingsLightTheTier()
        {
            var field = new ImpulseField();
            field.Add(Ring(0, x: 0, special: false));
            field.Publish(0);
            Assert.That(field.PublishedColour(0).w, Is.EqualTo(ImpulseField.CalmRing).Within(1e-6f), "a calm ring at its share");
            Assert.That(field.Special, Is.EqualTo(0f), "no special moment");
            field.Add(Ring(0.1, x: 50));
            field.Publish(0.1);
            Assert.That(field.Special, Is.EqualTo(1f), "full on the special moment's first frame");
            field.Publish(0.1 + ImpulseField.SpecialSeconds / 2);
            Assert.That(field.Special, Is.EqualTo(0.25f).Within(1e-5f), "falling as the square of what is left");
            Assert.That(ImpulseField.SpecialAt(ImpulseField.SpecialSeconds), Is.EqualTo(0f));
            Assert.That(ImpulseField.SpecialAt(-0.01), Is.EqualTo(0f), "nothing before it");
        }

        [Test]
        public void SlamsAndBreaksAreSpecialMomentsAndStrikesAndDeathsAreNot()
        {
            var c = Color.white;
            Assert.That(ImpulseField.Shatter(Vector2.zero, 0, 2f, c).Special, Is.True, "a structure break");
            Assert.That(ImpulseField.Blow(Vector2.zero, 0, 5f, 1f, c).Special, Is.True, "a slam");
            Assert.That(ImpulseField.Blow(Vector2.zero, 0, 5f, 0.7f, c).Special, Is.False, "a heavy strike");
            Assert.That(ImpulseField.Death(Vector2.zero, 0, c).Special, Is.False, "a death");
            Assert.That(ImpulseField.Beat(Vector2.zero, 0, true, c).Special, Is.False, "a beat");
        }

        [Test]
        public void ARingFromAPaletteRoleIsLinear()
        {
            var field = new ImpulseField();
            var role = new Color(0.5f, 0.25f, 1f);
            field.Add(ImpulseField.Shatter(Vector2.zero, 0, 3f, role));
            field.Publish(0);
            var c = field.PublishedColour(0);
            Assert.That(c.x / c.w, Is.EqualTo(role.linear.r).Within(1e-5f));
        }
    }
}
