using System;
using NUnit.Framework;
using PS.Look.EditorTools.Effects;
using PS.Look.Effects;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PS.Look.Tests
{
    /// <summary>The vfx axis's procedural effects (neon-look.md, The Axes &gt; VFX): arcs, light streaks and the selection's variants.</summary>
    public class EffectVfxTests
    {
        static (EffectEntry entry, Vector4[] emitters, EmitterEmission[] emission) Convert(string json)
        {
            var go = new GameObject("built");
            try
            {
                var entry = EffectConverter.ConvertOne(go, json, out var emitters, out var emission, out var errors);
                Assert.That(errors, Is.Empty);
                return (entry, emitters, emission);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static EmitterSlots.Flags Flags(Vector4[] emitters, int e) =>
            (EmitterSlots.Flags)(uint)BitConverter.SingleToInt32Bits(emitters[e * EmitterSlots.Stride + EmitterSlots.Time].w);

        static bool Has(EmitterSlots.Flags flags, EmitterSlots.Flags flag) => (flags & flag) != 0;

        static Vector4 Slot(Vector4[] emitters, int e, int slot) => emitters[e * EmitterSlots.Stride + slot];

        [Test]
        public void AnArcIsOneParticleBentRoundTheSharedStrip()
        {
            var (entry, emitters, emission) = Convert(@"{ ""arcs"": [
                { ""radius"": 2.4, ""width"": 0.3, ""sweep"": 160, ""centre"": -135, ""tilt"": 10, ""life"": 0.3, ""head"": 0.38, ""tail"": 0.4, ""core"": 3.5, ""soft"": 1.8,
                  ""hdr"": 1.6, ""heat"": { ""edge"": ""hero.trail"", ""mid"": ""hero.trail"", ""gain"": 0.25 } },
                { ""radius"": 2, ""sweep"": 140, ""delay"": 0.03, ""life"": 0.24, ""grow"": [0.5, 1] } ] }");
            Assert.That(entry.EmitterCount, Is.EqualTo(2));
            for (int e = 0; e < 2; e++)
            {
                var flags = Flags(emitters, e);
                Assert.That(Has(flags, EmitterSlots.Flags.Arc), Is.True);
                Assert.That(Has(flags, EmitterSlots.Flags.LocalSpace), Is.True, "an arc moves with its spawn");
                Assert.That(Slot(emitters, e, EmitterSlots.Render0).x, Is.EqualTo((float)EmitterSlots.RenderMode.Mesh));
                Assert.That(Slot(emitters, e, EmitterSlots.Mat1).x, Is.EqualTo((float)EmitterSlots.Blend.Additive));
                Assert.That(emission[e].Bursts.Length, Is.EqualTo(1));
                Assert.That(emission[e].Bursts[0].CountMax, Is.EqualTo(1f), "one particle draws the whole arc");
            }
            Assert.That(Slot(emitters, 0, EmitterSlots.Render1).w, Is.EqualTo(Slot(emitters, 1, EmitterSlots.Render1).w), "every arc bends the one strip: one draw bucket");
            var a0 = Slot(emitters, 0, EmitterSlots.Arc0);
            Assert.That(a0.x, Is.EqualTo(160 * Mathf.Deg2Rad).Within(1e-5f));
            Assert.That(a0.y, Is.EqualTo(-135 * Mathf.Deg2Rad).Within(1e-5f));
            Assert.That(a0.z, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(Slot(emitters, 0, EmitterSlots.Arc1), Is.EqualTo(new Vector4(0.38f, 0.4f, 3.5f, 1.8f)));
            Assert.That(Slot(emitters, 0, EmitterSlots.SizeMax).x, Is.EqualTo(2.4f), "the radius is the particle's size");
            Assert.That(Slot(emitters, 0, EmitterSlots.Mat0).x, Is.EqualTo(1.6f).Within(1e-5f), "the layer's hdr");
            Assert.That(Slot(emitters, 0, EmitterSlots.Heat0).w, Is.GreaterThan(0), "its heat ramp is on");
            Assert.That(Has(Flags(emitters, 1), EmitterSlots.Flags.SizeOverLife), Is.True, "grow scales the radius over its life");
            Assert.That(emission[1].DelayMin, Is.EqualTo(0.03f).Within(1e-5f));
            Assert.That(entry.Lifetime, Is.EqualTo(0.3f).Within(1e-4f));
        }

        [Test]
        public void AStreakTrailsItsSpawnForItsDuration()
        {
            var (entry, emitters, _) = Convert(@"{ ""streaks"": [ { ""size"": 0.2, ""trail"": 0.07, ""head"": 0.5, ""hdr"": 3 } ] }");
            Assert.That(entry.EmitterCount, Is.EqualTo(2), "the streak and its head");
            var streak = Flags(emitters, 0);
            Assert.That(Has(streak, EmitterSlots.Flags.Beam), Is.True, "it lives as long as its spawn asks");
            Assert.That(Has(streak, EmitterSlots.Flags.LocalSpace), Is.False, "in the world, so it stretches behind its velocity");
            Assert.That(Slot(emitters, 0, EmitterSlots.RotMin).w, Is.EqualTo(1f), "the spawn's velocity inherited");
            Assert.That(Slot(emitters, 0, EmitterSlots.Render0), Is.EqualTo(new Vector4((float)EmitterSlots.RenderMode.Stretch, 3, 0, 0.07f)), "stretched behind its particle by 0.07 s of travel");
            Assert.That(Has(Flags(emitters, 1), EmitterSlots.Flags.LocalSpace), Is.True, "the head rides the spawn");
            Assert.That(Slot(emitters, 1, EmitterSlots.Render0).x, Is.EqualTo((float)EmitterSlots.RenderMode.Billboard));
        }

        [Test]
        public void AVfxOptionDrawsItsVariants()
        {
            var lib = ScriptableObject.CreateInstance<EffectLibrary>();
            try
            {
                lib.Effects = new[] { new EffectEntry { Name = "slash" }, new EffectEntry { Name = "streak-slash" }, new EffectEntry { Name = "prism-slash" } };
                lib.Variants = new[]
                {
                    new VfxVariant { Option = "light-streaks", Name = "slash", Effect = "streak-slash" },
                    new VfxVariant { Option = "prism-streaks", Name = "slash", Effect = "prism-slash" },
                };
                Assert.That(lib.IndexOf("slash", null), Is.EqualTo(0), "no option: the effect itself");
                Assert.That(lib.IndexOf("slash", "light-streaks"), Is.EqualTo(1));
                Assert.That(lib.IndexOf("slash", "prism-streaks"), Is.EqualTo(2));
                Assert.That(lib.IndexOf("spin", "light-streaks"), Is.EqualTo(-1), "an effect the library lacks");
                Assert.That(lib.IndexOf("slash", "light-streaks", special: true), Is.EqualTo(1), "no special variant: the everyday one");
                lib.Effects = new[] { lib.Effects[0], lib.Effects[1], lib.Effects[2], new EffectEntry { Name = "streak-slash-big" } };
                lib.Variants = new[] { lib.Variants[0], lib.Variants[1], new VfxVariant { Option = "light-streaks", Name = "slash@special", Effect = "streak-slash-big" } };
                Assert.That(lib.IndexOf("slash", "light-streaks", special: true), Is.EqualTo(3), "a special moment's bright variant");
                Assert.That(lib.IndexOf("slash", "light-streaks"), Is.EqualTo(1), "the everyday one otherwise");
                Assert.That(lib.IndexOf("slash", null, special: true), Is.EqualTo(0), "no option: the effect itself");
            }
            finally { Object.DestroyImmediate(lib); }
        }

        [Test]
        public void EveryPresetsVfxIsBuilt()
        {
            var library = LookLibrary.Shared;
            foreach (var name in library.Presets)
            {
                if (name == "classic") continue;
                var look = ResolvedLook.Resolve(new LookFlags { Look = name });
                Assert.That(look.Option(LookAxes.Vfx), Is.Not.Null, $"preset {name}'s vfx ({look.Requested(LookAxes.Vfx)}) draws");
            }
        }
    }
}
