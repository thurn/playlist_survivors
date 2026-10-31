using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Game.Client.Arena
{
    /// <summary>
    /// The arena's static shadow map (unity-game.md, "The Arena"; demo3d's, copied and owned). URP
    /// has no cached shadow maps, so this renders the static set's depth from the moon's direction
    /// over the whole arena, 4096² (about 6 to 8 cm per texel over 240 m, by axis), through each
    /// material's own ShadowCaster pass (so alpha-clipped pieces cut out as in the cascades). The
    /// kit, the structures and the crowd and hero shaders sample it (StaticShadow.hlsl). The arena
    /// renders it at load and again after a structure breaks or regrows (ArenaRuntime.RefreshShadows).
    /// </summary>
    public sealed class StaticShadowMap : IDisposable
    {
        public const int DefaultSize = 4096;
        static readonly int MapId = Shader.PropertyToID("_GameStaticShadowMap");
        static readonly int MatrixId = Shader.PropertyToID("_GameStaticShadowMatrix");
        static readonly int ParamsId = Shader.PropertyToID("_GameStaticShadowParams");
        static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");
        static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        static readonly int LightPositionId = Shader.PropertyToID("_LightPosition");

        public RenderTexture Texture { get; private set; }
        public Matrix4x4 WorldToShadow { get; private set; }
        public Vector2 TexelM { get; private set; }
        public int Casters { get; private set; }
        public double RenderMs { get; private set; }

        /// <summary>
        /// Renders <paramref name="casters"/> (LOD 0 only; LOD 1+ renderers are skipped) from the
        /// light's direction over <paramref name="bounds"/> and publishes the map to the shaders.
        /// </summary>
        public static StaticShadowMap Render(IReadOnlyList<Renderer> casters, Quaternion lightRotation, Bounds bounds,
            int size = DefaultSize, float strength = 1f)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var map = new StaticShadowMap();
            Compute(lightRotation, bounds, out var view, out var proj, out var texel, size);
            map.TexelM = texel;
            map.WorldToShadow = ShadowTransform(proj, view);

            var desc = new RenderTextureDescriptor(size, size, GraphicsFormat.None, GraphicsFormat.D32_SFloat)
            {
                shadowSamplingMode = ShadowSamplingMode.CompareDepths,
                msaaSamples = 1,
                useMipMap = false,
            };
            map.Texture = new RenderTexture(desc) { name = "Game static shadow map", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            map.Texture.Create();

            var skip = Lod1Plus(casters);
            var cmd = new CommandBuffer { name = "Game static shadow map" };
            cmd.SetRenderTarget(map.Texture);
            cmd.ClearRenderTarget(true, false, Color.clear);
            cmd.SetViewProjectionMatrices(view, proj);
            Vector3 dir = -(lightRotation * Vector3.forward);
            // URP's caster-side bias (ShadowUtils.GetShadowBias): depth and normal bias in texels,
            // scaled for a soft-shadow kernel.
            float t = Mathf.Max(texel.x, texel.y);
            cmd.SetGlobalVector(ShadowBiasId, new Vector4(-1.0f * t * 2.5f, -1.0f * t * 2.5f, 0, 0));
            cmd.SetGlobalVector(LightDirectionId, new Vector4(dir.x, dir.y, dir.z, 0));
            cmd.SetGlobalVector(LightPositionId, Vector4.zero);
            int drawn = 0;
            foreach (var r in casters)
            {
                if (r == null || !r.enabled || skip.Contains(r)) continue;
                var mats = r.sharedMaterials;
                bool any = false;
                for (int s = 0; s < mats.Length; s++)
                {
                    var m = mats[s];
                    if (m == null) continue;
                    int pass = m.FindPass("ShadowCaster");
                    if (pass < 0) continue;
                    cmd.DrawRenderer(r, m, s, pass);
                    any = true;
                }
                if (any) drawn++;
            }
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            map.Casters = drawn;
            map.Publish(strength);
            map.RenderMs = sw.Elapsed.TotalMilliseconds;
            return map;
        }

        /// <summary>
        /// The light's view and orthographic projection fitted to <paramref name="bounds"/>: the
        /// bounds' eight corners in light space give the extent, so the map covers the whole arena
        /// at any moon angle. Texel sizes in metres come back per light-space axis.
        /// </summary>
        public static void Compute(Quaternion lightRotation, Bounds bounds, out Matrix4x4 view, out Matrix4x4 proj, out Vector2 texelM,
            int size = DefaultSize)
        {
            var lightToWorld = Matrix4x4.TRS(bounds.center, lightRotation, Vector3.one);
            var worldToLight = lightToWorld.inverse;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(i % 2 == 0 ? bounds.min.x : bounds.max.x, (i / 2) % 2 == 0 ? bounds.min.y : bounds.max.y,
                    i / 4 == 0 ? bounds.min.z : bounds.max.z);
                var l = worldToLight.MultiplyPoint3x4(c);
                min = Vector3.Min(min, l);
                max = Vector3.Max(max, l);
            }
            const float pad = 1f;
            // Camera convention (Unity's worldToCameraMatrix): looks down -z, so flip light-space z.
            view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * worldToLight;
            // Ortho's near and far are distances along the view (-z of the camera frame, +z of light
            // space, from the bounds' centre); the near one is negative, which an ortho allows.
            proj = Matrix4x4.Ortho(min.x - pad, max.x + pad, min.y - pad, max.y + pad, min.z - 10f, max.z + 10f);
            texelM = new Vector2((max.x - min.x + 2 * pad) / size, (max.y - min.y + 2 * pad) / size);
        }

        /// <summary>World to shadow texture (URP's ShadowUtils.GetShadowTransform).</summary>
        public static Matrix4x4 ShadowTransform(Matrix4x4 proj, Matrix4x4 view)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                proj.m20 = -proj.m20;
                proj.m21 = -proj.m21;
                proj.m22 = -proj.m22;
                proj.m23 = -proj.m23;
            }
            var worldToShadow = proj * view;
            var scaleBias = Matrix4x4.identity;
            scaleBias.m00 = 0.5f; scaleBias.m11 = 0.5f; scaleBias.m22 = 0.5f;
            scaleBias.m03 = 0.5f; scaleBias.m13 = 0.5f; scaleBias.m23 = 0.5f;
            return scaleBias * worldToShadow;
        }

        static HashSet<Renderer> Lod1Plus(IReadOnlyList<Renderer> casters)
        {
            var skip = new HashSet<Renderer>();
            var groups = new HashSet<LODGroup>();
            foreach (var r in casters)
            {
                if (r == null) continue;
                var g = r.GetComponentInParent<LODGroup>();
                if (g != null && groups.Add(g))
                {
                    var lods = g.GetLODs();
                    for (int i = 1; i < lods.Length; i++)
                        foreach (var lr in lods[i].renderers) if (lr != null) skip.Add(lr);
                }
            }
            // A renderer listed in LOD 0 too stays.
            foreach (var g in groups)
            {
                var lods = g.GetLODs();
                if (lods.Length > 0) foreach (var lr in lods[0].renderers) if (lr != null) skip.Remove(lr);
            }
            return skip;
        }

        public void Publish(float strength = 1f)
        {
            Shader.SetGlobalTexture(MapId, Texture);
            Shader.SetGlobalMatrix(MatrixId, WorldToShadow);
            Shader.SetGlobalVector(ParamsId, new Vector4(1, 1f / Texture.width, strength, 0));
        }

        /// <summary>Turns the map off in every shader (no arena).</summary>
        public static void Disable()
        {
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }

        public void Dispose()
        {
            Disable();
            if (Texture != null)
            {
                Texture.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
                else UnityEngine.Object.DestroyImmediate(Texture);
                Texture = null;
            }
        }
    }
}
