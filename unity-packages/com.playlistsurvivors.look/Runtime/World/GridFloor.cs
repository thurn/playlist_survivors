using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PS.Look
{
    /// <summary>
    /// The reactive grid floor (neon-look.md, The Axes &gt; World and The impulse field): the world kit's floor every
    /// world option carries, drawn by GridFloor.shader in the applied palette. It bends and brightens as the impulse
    /// field's rings pass and breathes on the beat; between impulses it is steady and dark. One flat patch of
    /// <see cref="PatchM"/> metres, a vertex every <see cref="VertexM"/>, kept centred under the camera's view
    /// (<see cref="Follow"/>), so it covers any framing; its vertices sit on fixed world points, so the lift doesn't
    /// swim as it moves. It replaces the arena's ground and its light decals (<see cref="Replace"/>).
    /// The world options it draws:
    /// - `void-grid`: the grid as the style guide sets it;
    /// - `pure-void`: a fainter grid (its lines at half the material's L* after the post);
    /// - `hued-zones`: the grid in the palette's first zone's hue (<see cref="SetZoneTint"/> for another).
    /// In every option a level's impassable ground (<see cref="SetBlocked"/>) shows no grid: the lines fade to
    /// <see cref="BlockedLines"/> over it and a kerb outlines the walkable floor, so the gridded floor is where the hero
    /// can walk.
    /// </summary>
    public sealed class GridFloor : IDisposable
    {
        /// <summary>The floor's material in the package's Resources (it carries the shader into both builds).</summary>
        public const string MaterialPath = "Look/GridFloor";
        public const float PatchM = 240f, VertexM = 1.5f;
        /// <summary>The patch snaps to this (m): a whole number of vertices and of the grid's major cells.</summary>
        public const float SnapM = 6f;

        public const string VoidGrid = "void-grid", HuedZones = "hued-zones", PureVoid = "pure-void";
        /// <summary>The arenas' floor decals beside their ground, which paint light on the floor (the style guide: no light pools).</summary>
        public static readonly string[] Decals = { "Runes", "GlowPools" };

        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int LineLId = Shader.PropertyToID("_LineL");
        static readonly int BlockMaskId = Shader.PropertyToID("_BlockMask");
        static readonly int BlockRectId = Shader.PropertyToID("_BlockRect");
        static readonly int BlockedLinesId = Shader.PropertyToID("_BlockedLines");

        /// <summary>The lines' brightness over blocked ground, against the walkable floor's.</summary>
        public const float BlockedLines = 0.1f;

        public readonly string Option;
        public readonly Material Material;
        readonly GameObject _go;
        readonly Mesh _mesh;

        /// <summary>The floor's object (on <see cref="GridFloor(string, Palette, int)"/>'s layer).</summary>
        public GameObject GameObject => _go;

        /// <summary>The floor for <paramref name="look"/>'s world option, or null when it names none built (today's ground).</summary>
        public static GridFloor For(ResolvedLook look, int layer = 0)
        {
            string option = look?.Option(LookAxes.World);
            return option == null ? null : new GridFloor(option, look.Palette, layer);
        }

        public GridFloor(string option, Palette palette, int layer = 0)
        {
            Option = option;
            var shared = Resources.Load<Material>(MaterialPath)
                         ?? throw new InvalidOperationException($"GridFloor: no material at Resources/{MaterialPath} in the look package");
            Material = new Material(shared) { name = "Grid floor (" + option + ")" };
            if (option == PureVoid) Material.SetFloat(LineLId, 0.5f * Material.GetFloat(LineLId));
            if (option == HuedZones && palette != null && palette.Zones.Length > 0) SetZoneTint(palette.Zones[0].Tint, 1f);
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

        /// <summary>A zone's tint (sRGB) replacing the lines' hue by <paramref name="amount"/> (0 to 1), keeping their brightness.</summary>
        public void SetZoneTint(Color tint, float amount)
        {
            var linear = tint.linear;
            Material.SetVector(TintId, new Vector4(linear.r, linear.g, linear.b, Mathf.Clamp01(amount)));
        }

        /// <summary>
        /// Marks <paramref name="ground"/>'s blocked tiles, and everything outside its rectangle, as impassable: the lines
        /// fade out over them and a kerb outlines the walkable floor. The floor draws from the mask, so it must outlive
        /// the floor's use of it.
        /// </summary>
        public void SetBlocked(BlockedGround ground)
        {
            Material.SetTexture(BlockMaskId, ground.Mask);
            Material.SetVector(BlockRectId, new Vector4(ground.Origin.x, ground.Origin.y,
                1f / (ground.Width * ground.TileM), 1f / (ground.Height * ground.TileM)));
            Material.SetFloat(BlockedLinesId, BlockedLines);
        }

        /// <summary>Hides an arena's ground and the floor decals beside it (<see cref="Decals"/>): the floor is the ground now.</summary>
        public static void Replace(Renderer ground)
        {
            if (ground == null) return;
            ground.enabled = false;
            var root = ground.transform.parent;
            if (root == null) return;
            foreach (var name in Decals)
            {
                var decal = root.Find(name);
                if (decal != null) foreach (var r in decal.GetComponentsInChildren<Renderer>()) r.enabled = false;
            }
        }

        /// <summary>Centres the patch under where <paramref name="camera"/> looks at the ground (its position where it looks up).</summary>
        public void Follow(Camera camera)
        {
            if (camera == null) return;
            var t = camera.transform;
            Vector3 p = t.position, f = t.forward;
            Vector3 at = f.y < -1e-3f ? p - f * (p.y / f.y) : p;
            // Far along a shallow view, the patch's middle goes partway out, so its near edge stays behind the camera.
            var delta = new Vector2(at.x - p.x, at.z - p.z);
            float reach = PatchM * 0.35f;
            if (delta.sqrMagnitude > reach * reach) delta = delta.normalized * reach;
            float x = Mathf.Round((p.x + delta.x) / SnapM) * SnapM, z = Mathf.Round((p.z + delta.y) / SnapM) * SnapM;
            _go.transform.position = new Vector3(x, 0f, z);
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
            // The lift raises it a little; the bounds keep it drawn.
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
