using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PS.Look
{
    /// <summary>
    /// The post-processing stack built from a look's post option (neon-look.md, Post-processing stack), the one
    /// stack every shot and every zone is graded by:
    /// - the look's own pass before URP's post (<see cref="LookPostPass"/>, while <see cref="Use"/> holds), on the
    ///   scene's light: the exposure, the wide soft halo layer, and the filmic tonemap that keeps saturated emitters
    ///   coloured;
    /// - URP's volume (<see cref="CreateProfile"/>) on what that pass drew: colour adjustments, the bloom's tight core
    ///   layer with the lens dirt on it, vignette toward the palette's void, chromatic aberration, film grain, and the
    ///   exposure, white balance and tint the post feel moves (<see cref="PostBinding"/>). URP grades in LDR as the
    ///   projects always have (its builds strip HDR grading's variants), its tonemapper off under a filmic option and
    ///   URP's neutral or ACES otherwise;
    /// - the look's retro pass after URP's post: the scanlines and CRT mask.
    /// </summary>
    public static class PostStack
    {
        /// <summary>The volume's priority: over an environment's own volumes (10), under a trailer's black (1000).</summary>
        public const int Priority = 20;

        /// <summary>
        /// The option the look's post axis draws, or null when it asks for one not built (today's post, which
        /// ResolvedLook warns of) or <see cref="LookAxis.Classic"/>, both the player's own night post, or there is no look.
        /// </summary>
        public static PostOption OptionOf(ResolvedLook look, LookLibrary library = null)
        {
            var name = look?.Option(LookAxes.Post);
            return name != null ? PostOption.Parse((library ?? LookLibrary.Shared).PostJson(name)) : null;
        }

        /// <summary>
        /// URP's part of <paramref name="option"/>'s stack in <paramref name="palette"/>'s colours. Grain is a pixel at
        /// the render resolution, which a supersampled frame's downsample averages away: <paramref name="grainScale"/>
        /// (the supersampling factor) raises it to match. The option's exposure is the look's pass's, before the tonemap:
        /// the profile's is 0, for the post feel's offsets.
        /// </summary>
        public static VolumeProfile CreateProfile(PostOption option, Palette palette, float grainScale = 1f)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "LookPost " + option.Name;
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(option.BloomThreshold);
            bloom.intensity.Override(option.BloomIntensity);
            bloom.scatter.Override(option.BloomScatter);
            bloom.tint.Override(Color.white);
            if (option.Dirt > 0)
            {
                bloom.dirtTexture.Override(LensDirt.Texture);
                bloom.dirtIntensity.Override(option.Dirt);
            }
            profile.Add<Tonemapping>(true).mode.Override(option.Tonemap switch
            {
                PostOption.Tonemaps.Neutral => TonemappingMode.Neutral,
                PostOption.Tonemaps.Aces => TonemappingMode.ACES,
                _ => TonemappingMode.None, // the look's pass tonemapped
            });
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0f);
            color.contrast.Override(option.Contrast);
            color.saturation.Override(option.Saturation);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(option.Vignette);
            vignette.smoothness.Override(option.VignetteSmoothness);
            vignette.color.Override(palette.VoidFloor);
            profile.Add<WhiteBalance>(true).temperature.Override(0f);
            profile.Add<ChromaticAberration>(true).intensity.Override(option.Chromatic);
            if (option.Grain > 0)
            {
                var grain = profile.Add<FilmGrain>(true);
                grain.type.Override(FilmGrainLookup.Thin2);
                grain.intensity.Override(Mathf.Min(1f, option.Grain * grainScale));
                grain.response.Override(0.8f);
            }
            return profile;
        }

        /// <summary>A global volume with <see cref="CreateProfile"/>'s stack, under <paramref name="parent"/>.</summary>
        public static Volume CreateVolume(Transform parent, PostOption option, Palette palette, float grainScale = 1f)
        {
            var go = new GameObject("LookPost");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = Priority;
            volume.sharedProfile = CreateProfile(option, palette, grainScale);
            return volume;
        }

        /// <summary>The bloom tints the post feel leans toward: the grid's pulse on the beat, the spark on a flash.</summary>
        public static PostBinding.BloomTints Tints(PostOption option, Palette palette) =>
            new PostBinding.BloomTints { Beat = palette.GridPulse, BeatShare = option.Feel.BeatTint, Flash = palette.Spark };

        static LookPostPass _scene, _retro;
        static float _sceneExposure;

        /// <summary>The option the look's passes draw, or null when none is in use.</summary>
        public static PostOption Active => _scene?.Option;

        /// <summary>
        /// The scene's own exposure in stops, added to the option's before the tonemap: a world lit dimmer than the void
        /// (game's baked arena) sets it, not the grade.
        /// </summary>
        public static float SceneExposure
        {
            get => _sceneExposure;
            set
            {
                _sceneExposure = value;
                if (_scene != null) _scene.SceneExposure = value;
            }
        }

        /// <summary>
        /// Draws <paramref name="option"/>'s own passes on every game camera that runs post-processing, until
        /// <see cref="Release"/>. A camera without post-processing (an ID render) is left alone.
        /// </summary>
        public static void Use(PostOption option)
        {
            if (_scene == null)
            {
                var shader = Resources.Load<Shader>(LookPostPass.ShaderPath) ?? throw new InvalidOperationException($"PostStack: no shader Resources/{LookPostPass.ShaderPath}");
                _scene = new LookPostPass(CoreUtils.CreateEngineMaterial(shader), LookPostPass.Stage.Scene);
                _retro = new LookPostPass(CoreUtils.CreateEngineMaterial(shader), LookPostPass.Stage.Retro);
                RenderPipelineManager.beginCameraRendering += Enqueue;
            }
            _scene.Option = _retro.Option = option;
            _scene.SceneExposure = _sceneExposure;
        }

        /// <summary>Stops the look's passes.</summary>
        public static void Release()
        {
            if (_scene == null) return;
            RenderPipelineManager.beginCameraRendering -= Enqueue;
            _scene.Dispose();
            _retro.Dispose();
            _scene = _retro = null;
        }

        static void Enqueue(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || !camera.TryGetComponent(out UniversalAdditionalCameraData data) || !data.renderPostProcessing) return;
            if (_scene.Draws(_scene.Option)) data.scriptableRenderer.EnqueuePass(_scene);
            if (_retro.Draws(_retro.Option)) data.scriptableRenderer.EnqueuePass(_retro);
        }
    }
}
