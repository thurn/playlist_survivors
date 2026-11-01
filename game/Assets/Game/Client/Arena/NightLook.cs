using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Client.Arena
{
    /// <summary>
    /// The arena's night (unity-game.md, "The Look"), demo3d's NightLook copied and owned: ambient and
    /// fog from the lighting and the camera's clear colour. The post stack is the look's (PostStack,
    /// neon-look.md), which replaced the night's grade. Nothing is baked or vendor-derived.
    /// </summary>
    public static class NightLook
    {
        static ArenaLighting _fogLighting;
        // Made once: a zone's ease applies the render settings every frame, and a method group makes a new delegate each time.
        static readonly Action<ScriptableRenderContext, Camera> FitFog = FitFogToCamera;

        public static void ApplyRenderSettings(ArenaLighting l)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = l.AmbientSky;
            RenderSettings.ambientEquatorColor = l.AmbientEquator;
            RenderSettings.ambientGroundColor = l.AmbientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = l.FogMode;
            RenderSettings.fogColor = l.FogColor;
            RenderSettings.fogDensity = l.FogDensity;
            var range = FogRange(l, NominalFocusM);
            RenderSettings.fogStartDistance = range.x;
            RenderSettings.fogEndDistance = range.y;
            _fogLighting = l;
            RenderPipelineManager.beginCameraRendering -= FitFog;
            if (l.FogMode == FogMode.Linear) RenderPipelineManager.beginCameraRendering += FitFog;
            RenderSettings.skybox = null;
        }

        /// <summary>The focus distance of a 30 m view at the game camera's pitch, for cameras that look at no ground.</summary>
        public const float NominalFocusM = 31.5f;

        /// <summary>Linear fog's start and end view depths for a camera focused <paramref name="focusM"/> away.</summary>
        public static Vector2 FogRange(ArenaLighting l, float focusM) =>
            new Vector2(l.FogStartAtFocus * focusM, l.FogEndAtFocus * focusM);

        /// <summary>The distance along a camera's view axis to the ground (y = 0), or the nominal focus if it looks at none.</summary>
        public static float FocusDistance(Camera camera)
        {
            var t = camera.transform;
            float down = -t.forward.y;
            return down > 0.05f && t.position.y > 0 ? t.position.y / down : NominalFocusM;
        }

        static void FitFogToCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || _fogLighting == null || RenderSettings.fogMode != FogMode.Linear) return;
            var range = FogRange(_fogLighting, FocusDistance(camera));
            RenderSettings.fogStartDistance = range.x;
            RenderSettings.fogEndDistance = range.y;
        }

        /// <summary>The camera clears to the fog's colour, so the arena's far edge fades into it, and draws the look's post stack.</summary>
        public static void ApplyCamera(Camera camera, ArenaLighting l)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = l.FogColor;
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = true;
        }
    }
}
