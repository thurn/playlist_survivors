using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render
{
    /// <summary>
    /// The endless grid (design.md, The World &gt; The infinite arena), ported by review from the look package's grid
    /// floor: one flat patch of <see cref="PatchM"/> metres, a vertex every <see cref="VertexM"/>, kept centred under
    /// where the camera looks (<see cref="Follow"/>), so it follows the hero forever; its vertices sit on fixed world
    /// points, so the rings' lift doesn't swim as it moves. GridFloor.shader draws it from the world's globals
    /// (World.hlsl): quiet lines in the zone key's hue, the beat wave, the event rings.
    /// </summary>
    public sealed class GridFloor : IDisposable
    {
        /// <summary>The floor's material in Champions.Render's Resources (it carries the shader into the build).</summary>
        public const string MaterialPath = "World/GridFloor";
        /// <summary>The patch (m): the duel's eased-out view sees about 50 m across and 25 m up the screen, plus the lead.</summary>
        public const float PatchM = 120f, VertexM = 1.5f;
        /// <summary>The patch snaps to this (m): a whole number of vertices and of the grid's major cells.</summary>
        public const float SnapM = 6f;

        public readonly Material Material;
        readonly GameObject _go;
        readonly Mesh _mesh;

        public GameObject GameObject => _go;

        public GridFloor(int layer = 0)
        {
            var shared = Resources.Load<Material>(MaterialPath)
                         ?? throw new InvalidOperationException($"GridFloor: no material at Resources/{MaterialPath}");
            Material = new Material(shared) { name = "Grid floor" };
            _mesh = Patch();
            _go = new GameObject("Grid floor") { layer = layer };
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        /// <summary>Centres the patch under where <paramref name="camera"/> looks at the ground.</summary>
        public void Follow(Camera camera)
        {
            if (camera == null) return;
            var t = camera.transform;
            Vector3 p = t.position, f = t.forward;
            Vector3 at = f.y < -1e-3f ? p - f * (p.y / f.y) : p;
            _go.transform.position = new Vector3(Mathf.Round(at.x / SnapM) * SnapM, 0f, Mathf.Round(at.z / SnapM) * SnapM);
        }

        static Mesh Patch()
        {
            int n = Mathf.RoundToInt(PatchM / VertexM), side = n + 1;
            var vertices = new Vector3[side * side];
            float half = PatchM * 0.5f;
            for (int j = 0; j < side; j++)
            for (int i = 0; i < side; i++)
                vertices[j * side + i] = new Vector3(i * VertexM - half, 0f, j * VertexM - half);
            var indices = new int[n * n * 6];
            int k = 0;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * side + i, b = a + 1, c = a + side, d = c + 1;
                indices[k++] = a; indices[k++] = c; indices[k++] = b;
                indices[k++] = b; indices[k++] = c; indices[k++] = d;
            }
            var mesh = new Mesh { name = "Grid floor", indexFormat = side * side > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices;
            mesh.triangles = indices;
            // The rings' lift raises it a little; the bounds keep it drawn.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(PatchM, 2f, PatchM));
            return mesh;
        }

        public void Dispose()
        {
            foreach (var o in new UnityEngine.Object[] { _go, _mesh, Material })
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
