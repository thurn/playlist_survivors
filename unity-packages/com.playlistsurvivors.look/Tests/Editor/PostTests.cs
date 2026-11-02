using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PS.Look.Tests
{
    /// <summary>The post axis's options and the stack built from them: what a player's volume draws.</summary>
    public class PostTests
    {
        static Palette Ion => Palette.Parse(LookLibrary.Shared.PaletteJson("ion"));
        /// <summary>Ion with an amber spark, so a flash's tint shows.</summary>
        static Palette AmberSpark => Palette.Parse(Regex.Replace(LookLibrary.Shared.PaletteJson("ion"), @"""spark"":\s*""#ffffff""", @"""spark"": ""#ff8000"""));

        [Test]
        public void EveryPresetsPostOptionIsBuiltAndEveryBuiltOptionParses()
        {
            var library = LookLibrary.Shared;
            Assert.That(library.Posts, Is.EquivalentTo(LookAxes.Post.Built), "a file for every built option, and no other");
            foreach (var name in LookAxes.Post.Built) Assert.That(PostOption.Parse(library.PostJson(name)).Name, Is.EqualTo(name));
            foreach (var preset in library.Presets)
            {
                if (preset == LookAxis.Classic) continue; // the night post, the player's own
                var look = ResolvedLook.Resolve(new LookFlags { Look = preset });
                Assert.That(PostStack.OptionOf(look), Is.Not.Null, $"{preset} draws its post option");
            }
        }

        [Test]
        public void AnOptionNotBuiltDrawsNoOption()
        {
            var flags = new LookFlags { Look = "edge" };
            flags.Overrides[LookAxes.Post] = "vaporwave";
            Assert.That(PostStack.OptionOf(ResolvedLook.Resolve(flags)), Is.Null);
            Assert.That(PostStack.OptionOf(null), Is.Null);
        }

        [Test]
        public void ATypoFailsRatherThanDrawingTheDefault()
        {
            Assert.Throws<FormatException>(() => PostOption.Parse(@"{ ""name"": ""x"", ""blom"": { ""intensity"": 1 } }"));
            Assert.Throws<FormatException>(() => PostOption.Parse(@"{ ""name"": ""x"", ""bloom"": { ""intensty"": 1 } }"));
            Assert.Throws<FormatException>(() => PostOption.Parse(@"{ ""name"": ""x"", ""tonemap"": ""reinhard"" }"));
            Assert.Throws<FormatException>(() => PostOption.Parse(@"{ ""name"": ""x"", ""feel"": { ""beat"": { ""exposure"": ""lots"" } } }"));
            var o = PostOption.Parse(@"{ ""name"": ""x"", ""halo"": { ""intensity"": 0.7 }, ""void"": { ""knee"": 0.6 }, ""feel"": { ""hit"": { ""flash"": 0.6 }, ""flash"": { ""calm"": 0.3 } } }");
            Assert.That(o.HaloIntensity, Is.EqualTo(0.7f));
            Assert.That((o.VoidKnee, o.VoidPower), Is.EqualTo((0.6f, 2f)), "the knee set, the power its default");
            Assert.That(o.Feel.HitFlash, Is.EqualTo(0.6f));
            Assert.That(o.Feel.FlashCalm, Is.EqualTo(0.3f), "a calm moment's flash: the special-moment tier");
            Assert.That(PostOption.Parse(@"{ ""name"": ""x"" }").Feel.FlashCalm, Is.EqualTo(1f), "left out: every flash in full");
            Assert.That(o.Tonemap, Is.EqualTo(PostOption.Tonemaps.Filmic), "left out: the default");
        }

        [Test]
        public void TheProfileGradesInThePalettesColoursAndLeavesAFilmicTonemapToTheLooksPass()
        {
            var option = PostOption.Parse(@"{ ""name"": ""x"", ""tonemap"": ""filmic"", ""colour"": { ""exposure"": 0.5 }, ""bloom"": { ""threshold"": 0.9, ""intensity"": 0.8 },
                ""lens"": { ""vignette"": 0.3, ""grain"": 0.4, ""dirt"": 6 } }");
            var profile = PostStack.CreateProfile(option, Ion, grainScale: 2);
            try
            {
                Assert.That(profile.TryGet(out Tonemapping tone) && tone.mode.value == TonemappingMode.None, "URP draws no tonemap: the look's pass did");
                Assert.That(profile.TryGet(out ColorAdjustments color) && color.postExposure.value == 0f, "the exposure is the look's pass's, before its tonemap");
                Assert.That(profile.TryGet(out Vignette vignette) && vignette.color.value == Ion.VoidFloor, "the vignette darkens toward the void");
                Assert.That(profile.TryGet(out Bloom bloom) && bloom.intensity.value == 0.8f && bloom.dirtTexture.value == LensDirt.Texture);
                Assert.That(profile.TryGet(out FilmGrain grain) && grain.intensity.value == 0.8f, "grain raised by the supersampling");

                var aces = PostStack.CreateProfile(PostOption.Parse(@"{ ""name"": ""y"", ""tonemap"": ""aces"" }"), Ion);
                Assert.That(aces.TryGet(out Tonemapping acesTone) && acesTone.mode.value == TonemappingMode.ACES);
                Assert.That(aces.TryGet(out FilmGrain _), Is.False, "no grain unless the option has some");
                UnityEngine.Object.DestroyImmediate(aces);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void TheFeelMovesTheStackOverItsBaseAndTintsTheBloomTowardThePalette()
        {
            var option = PostOption.Parse(@"{ ""name"": ""x"", ""colour"": { ""contrast"": 20 }, ""bloom"": { ""intensity"": 0.8 }, ""feel"": { ""beat"": { ""tint"": 0.5 } } }");
            var go = new GameObject("volumes");
            try
            {
                var plain = PostStack.CreateVolume(go.transform, option, Ion);
                plain.sharedProfile.TryGet(out ColorAdjustments color);
                plain.sharedProfile.TryGet(out Bloom bloom);
                new PostBinding().Apply(plain, new PostOffsets { Contrast = 5, Bloom = 0.3f, Pulse = 1, Flash = 1 });
                Assert.That(color.contrast.value, Is.EqualTo(25f).Within(1e-5), "offsets over the base");
                Assert.That(bloom.intensity.value, Is.EqualTo(1.1f).Within(1e-6));
                Assert.That(bloom.tint.value, Is.EqualTo(Color.white), "no tints: the bloom's tint left alone");

                var tinted = PostStack.CreateVolume(go.transform, option, Ion);
                tinted.sharedProfile.TryGet(out bloom);
                var binding = new PostBinding { Tints = PostStack.Tints(option, AmberSpark) };
                binding.Apply(tinted, new PostOffsets { Pulse = 1 });
                Assert.That(bloom.tint.value, Is.EqualTo(Color.Lerp(Color.white, Ion.GridPulse, 0.5f)), "a full pulse leans the glow toward the grid's pulse");
                binding.Apply(tinted, new PostOffsets { Flash = 1 });
                Assert.That(bloom.tint.value, Is.EqualTo(AmberSpark.Spark), "a full flash takes the glow to the spark");
                binding.Apply(tinted, default);
                Assert.That(bloom.tint.value, Is.EqualTo(Color.white), "at rest the glow is the emitters' own");
                UnityEngine.Object.DestroyImmediate(plain.sharedProfile);
                UnityEngine.Object.DestroyImmediate(tinted.sharedProfile);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// A filter offset (the crystal cut's palette flash) leans the colour filter toward its colour and back to the
        /// volume's own at rest; a volume that never takes one keeps its filter's override off.
        /// </summary>
        [Test]
        public void AFilterOffsetLeansTheColourFilterAndRestsOnTheVolumesOwn()
        {
            var option = PostOption.Parse(@"{ ""name"": ""x"", ""colour"": { ""contrast"": 20 } }");
            var go = new GameObject("volumes");
            try
            {
                var volume = PostStack.CreateVolume(go.transform, option, Ion);
                volume.sharedProfile.TryGet(out ColorAdjustments color);
                bool before = color.colorFilter.overrideState;
                Color own = before ? color.colorFilter.value : Color.white;
                var binding = new PostBinding();
                binding.Apply(volume, new PostOffsets { Contrast = 1 });
                Assert.That(color.colorFilter.overrideState, Is.EqualTo(before), "no filter offset: the filter left alone");
                var hue = new Color(0.2f, 1f, 0.6f);
                binding.Apply(volume, new PostOffsets { Filter = hue, FilterShare = 0.5f });
                Assert.That(color.colorFilter.overrideState, Is.True);
                Assert.That(color.colorFilter.value, Is.EqualTo(Color.Lerp(own, hue, 0.5f)), "half way to the hue");
                binding.Apply(volume, default);
                Assert.That(color.colorFilter.value, Is.EqualTo(own), "at rest the volume's own filter");
                UnityEngine.Object.DestroyImmediate(volume.sharedProfile);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
