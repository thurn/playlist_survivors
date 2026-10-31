using UnityEngine;

namespace Game.Client.Crowd
{
    /// <summary>
    /// The culling pass's camera-side arithmetic (CrowdCull.compute takes its results): the
    /// frustum's planes, the depth bins' range and the projected-height scale. demo3d's
    /// CrowdCulling, copied and owned.
    /// </summary>
    public static class CrowdCulling
    {
        public const int Bins = 8;

        /// <summary>The frustum's planes as (inward normal, distance): inside when n·p + d ≥ 0.</summary>
        public static void Planes(Camera camera, Plane[] scratch, Vector4[] output)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, scratch);
            for (int i = 0; i < 6; i++) output[i] = new Vector4(scratch[i].normal.x, scratch[i].normal.y, scratch[i].normal.z, scratch[i].distance);
        }

        /// <summary>The sun's elevation sine, kept away from 0 so a low sun's shadow sweep stays finite.</summary>
        public static float SinElevation(Vector3 lightDir) => Mathf.Max(-lightDir.y, 0.1f);

        /// <summary>Metres per pixel at depth 1 for the camera: projected px = height / (depth × this).</summary>
        public static float PixelScale(Camera camera) =>
            2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) / Mathf.Max(1, camera.pixelHeight);

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
                for (int k = 0; k < 2; k++)
                {
                    float h = k == 0 ? 0f : top;
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
