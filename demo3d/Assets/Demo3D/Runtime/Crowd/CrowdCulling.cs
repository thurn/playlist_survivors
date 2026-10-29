using Unity.Mathematics;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The culling pass's arithmetic (CrowdCull.compute) on the CPU: the tests check the compute
    /// pass against it, and the stock baseline counts its on-screen monsters with it.
    /// </summary>
    public static class CrowdCulling
    {
        public const int Bins = 8;
        public const float LodHysteresis = 0.1f;

        /// <summary>The frustum's planes as (inward normal, distance): inside when n·p + d ≥ 0.</summary>
        public static void Planes(Camera camera, Plane[] scratch, float4[] output)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, scratch);
            for (int i = 0; i < 6; i++) output[i] = new float4(scratch[i].normal, scratch[i].distance);
        }

        /// <summary>Whether a sphere swept from c to c + d touches every plane's inner side.</summary>
        public static bool InsideSwept(float4[] planes, float3 c, float3 d, float r)
        {
            for (int i = 0; i < 6; i++)
            {
                var p = planes[i];
                float a = math.dot(p.xyz, c) + p.w;
                float b = math.dot(p.xyz, c + d) + p.w;
                if (math.max(a, b) < -r) return false;
            }
            return true;
        }

        /// <summary>The longest shadow's sweep for a sphere: along the light to the ground.</summary>
        public static float3 ShadowSweep(float3 lightDir, float sinElevation, float3 c, float r) =>
            lightDir * ((c.y + r) / sinElevation);

        /// <summary>The sun's elevation sine, kept away from 0 so a low sun's sweep stays finite.</summary>
        public static float SinElevation(float3 lightDir) => math.max(-lightDir.y, 0.1f);

        /// <summary>A monster's sphere (centre height, radius, height at scale 1): its clip's, or one
        /// holding both clips' while they crossfade.</summary>
        public static float3 ClipSphere(float3 a, float3 b, float blend)
        {
            if (blend >= 0.9999f) return a;
            float cy = 0.5f * (a.x + b.x);
            return new float3(cy, math.max(math.abs(cy - a.x) + a.y, math.abs(cy - b.x) + b.y), math.max(a.z, b.z));
        }

        public static int Bin(float depth, float near, float far) =>
            math.clamp((int)math.floor((depth - near) / math.max(far - near, 1e-3f) * Bins), 0, Bins - 1);

        static int RawLod(float px, float4 thresholds, int lodCount, float scale)
        {
            int l = 0;
            for (int i = 0; i + 1 < lodCount; i++)
                if (px < thresholds[i] * scale) l = i + 1;
            return l;
        }

        /// <summary>The LOD for a projected height in pixels, with 10% hysteresis around each threshold.</summary>
        public static int SelectLod(float px, int current, float4 thresholds, int lodCount)
        {
            int coarser = RawLod(px, thresholds, lodCount, 1 - LodHysteresis);
            int finer = RawLod(px, thresholds, lodCount, 1 + LodHysteresis);
            if (coarser > current) return coarser;
            if (finer < current) return finer;
            return math.min(current, lodCount - 1);
        }

        /// <summary>Metres per pixel at depth 1 for the camera: projected px = height / (depth × this).</summary>
        public static float PixelScale(Camera camera) =>
            2f * math.tan(math.radians(camera.fieldOfView) * 0.5f) / math.max(1, camera.pixelHeight);

        /// <summary>
        /// The depth range the bins span: the camera depths where the view's corner rays meet the
        /// ground and a plane `top` metres above it, padded by `pad`.
        /// </summary>
        public static Vector2 BinRange(Camera camera, float top, float pad)
        {
            var t = camera.transform;
            float near = float.MaxValue, far = 0;
            for (int i = 0; i < 4; i++)
            {
                var ray = camera.ViewportPointToRay(new Vector3(i & 1, i >> 1, 0));
                foreach (float h in new[] { 0f, top })
                {
                    float depth;
                    if (ray.direction.y < -1e-4f && ray.origin.y > h)
                    {
                        var hit = ray.origin + ray.direction * ((ray.origin.y - h) / -ray.direction.y);
                        depth = Vector3.Dot(hit - t.position, t.forward);
                    }
                    else depth = camera.farClipPlane;
                    near = Mathf.Min(near, depth);
                    far = Mathf.Max(far, depth);
                }
            }
            return new Vector2(Mathf.Max(camera.nearClipPlane, near - pad), Mathf.Min(camera.farClipPlane, far + pad));
        }
    }
}
