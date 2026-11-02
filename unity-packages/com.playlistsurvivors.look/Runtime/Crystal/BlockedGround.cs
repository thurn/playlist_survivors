using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PS.Look
{
    /// <summary>
    /// The void world's impassable ground (neon-look.md, The Axes &gt; World): the tiles a level blocks with no structure
    /// on them (an arena's dressing and its edge), which the void world would otherwise leave as invisible walls. It is
    /// marked twice:
    /// - its rim: every blocked tile beside walkable ground, diagonals too, carries a low crystal hedge
    ///   (<see cref="CrystalKit.Wall"/> along the longest straight runs of such tiles, at most <see cref="MaxRun"/>
    ///   long, a <see cref="CrystalKit.Shard"/> on a tile left alone) in a structure look's
    ///   <see cref="StructureLook.Terrain"/>; it never breaks, so its pieces merge into one mesh per
    ///   <see cref="Chunk"/> tiles square;
    /// - the floor: <see cref="Mask"/>, a texel a tile (1 walkable, 0 blocked), for <see cref="GridFloor.SetBlocked"/>,
    ///   whose lines fade out over blocked ground.
    /// Tiles are a level's: column x east, row y north, tile (0, 0)'s south-west corner at <see cref="Origin"/> on the
    /// ground (x, z). Every piece stands within its tiles, so nothing stands on walkable ground. It owns what it makes.
    /// </summary>
    public sealed class BlockedGround : IDisposable
    {
        /// <summary>The longest hedge (tiles), and the side of the square of tiles whose pieces share a mesh.</summary>
        public const int MaxRun = 5, Chunk = 16;

        static readonly int PieceId = Shader.PropertyToID("_SL_Piece");
        static readonly int PieceFxId = Shader.PropertyToID("_SL_PieceFx");
        static readonly int TintId = Shader.PropertyToID("_SL_Tint");
        static readonly int HeightId = Shader.PropertyToID("_HeightM");

        public readonly int Width, Height;
        public readonly Vector2 Origin;
        public readonly float TileM;
        /// <summary>The floor's mark: <see cref="Width"/> by <see cref="Height"/> texels, R 1 on a walkable tile, 0 on a blocked one.</summary>
        public readonly Texture2D Mask;
        /// <summary>The rim's merged meshes' renderers, a chunk each.</summary>
        public readonly List<Renderer> Renderers = new List<Renderer>();
        /// <summary>How many pieces stand on the rim, and on how many tiles.</summary>
        public int Pieces { get; private set; }
        public int RimTiles { get; private set; }

        readonly GameObject _root;
        readonly List<Mesh> _meshes = new List<Mesh>();

        /// <summary>
        /// The ground <paramref name="blocked"/> marks (row-major, <c>y * width + x</c>; everything outside the
        /// rectangle counts as blocked), its rim in <paramref name="material"/> under <paramref name="parent"/> on
        /// <paramref name="layer"/>. The same tiles and <paramref name="seed"/> give the same pieces.
        /// </summary>
        public BlockedGround(Transform parent, Material material, bool[] blocked, int width, int height, Vector2 origin, float tileM,
            int seed, int layer = 0)
        {
            if (blocked == null || blocked.Length != width * height)
                throw new ArgumentException("BlockedGround: expected width * height tiles");
            Width = width;
            Height = height;
            Origin = origin;
            TileM = tileM;

            var open = new byte[width * height];
            for (int i = 0; i < open.Length; i++) open[i] = blocked[i] ? (byte)0 : (byte)255;
            Mask = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = "Blocked ground", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            Mask.SetPixelData(open, 0);
            Mask.Apply(false, false);

            _root = new GameObject("BlockedGround") { layer = layer };
            if (parent != null) _root.transform.SetParent(parent, false);
            var chunks = new Dictionary<int, List<CombineInstance>>();
            var made = new List<StructureShards.Set>();
            var rng = new System.Random(seed);
            float U(float a, float b) => a + (b - a) * (float)rng.NextDouble();
            void Stand(StructureShards.Set set, Vector2 tile, float yaw)
            {
                made.Add(set);
                int key = (int)(tile.y / Chunk) * ((width + Chunk - 1) / Chunk) + (int)(tile.x / Chunk);
                if (!chunks.TryGetValue(key, out var list)) chunks[key] = list = new List<CombineInstance>();
                var at = new Vector3(origin.x + (tile.x + 0.5f) * tileM, 0f, origin.y + (tile.y + 0.5f) * tileM);
                list.Add(new CombineInstance { mesh = set.Source, transform = Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one) });
                Pieces++;
            }

            var free = Rim(blocked, width, height);
            foreach (bool f in free) if (f) RimTiles++;
            // The longest straight run of rim tiles left takes a hedge (cut into near-equal lengths of at most MaxRun), until only lone tiles are left.
            while (Longest(free, width, height, out int x0, out int y0, out bool alongX, out int length))
            {
                int parts = (length + MaxRun - 1) / MaxRun, done = 0;
                for (int p = 0; p < parts; p++)
                {
                    int n = (length - done) / (parts - p);
                    for (int k = done; k < done + n; k++) free[alongX ? y0 * width + x0 + k : (y0 + k) * width + x0] = false;
                    float mid = done + 0.5f * (n - 1);
                    var tile = alongX ? new Vector2(x0 + mid, y0) : new Vector2(x0, y0 + mid);
                    Stand(CrystalKit.Wall(0.92f * n * tileM, U(1.4f, 1.9f), rng.Next()), tile, alongX ? 0f : 90f);
                    done += n;
                }
            }
            for (int i = 0; i < free.Length; i++)
                if (free[i]) Stand(CrystalKit.Shard(0.3f * tileM, U(1.3f, 1.9f), rng.Next()), new Vector2(i % width, i / width), U(0f, 360f));

            var block = new MaterialPropertyBlock();
            foreach (var kv in chunks)
            {
                int vertices = 0;
                foreach (var c in kv.Value) vertices += c.mesh.vertexCount;
                var mesh = new Mesh { name = "Blocked ground " + kv.Key, indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                _meshes.Add(mesh);
                var go = new GameObject("Chunk " + kv.Key) { layer = layer };
                go.transform.SetParent(_root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                var b = r.bounds;
                block.Clear();
                block.SetVector(PieceId, new Vector4(b.center.x, 0f, b.center.z, 0f));
                block.SetVector(PieceFxId, Vector4.zero);
                block.SetVector(TintId, Vector4.zero); // the material's tint
                block.SetFloat(HeightId, Mathf.Max(b.max.y, 0.1f));
                r.SetPropertyBlock(block);
                Renderers.Add(r);
            }
            foreach (var set in made) CrystalKit.Destroy(set);
        }

        /// <summary>The blocked tiles beside a walkable one, diagonals too (outside the rectangle counts as blocked).</summary>
        public static bool[] Rim(bool[] blocked, int width, int height)
        {
            var rim = new bool[blocked.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (!blocked[y * width + x]) continue;
                for (int dy = -1; dy <= 1 && !rim[y * width + x]; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < width && ny < height && !blocked[ny * width + nx]) { rim[y * width + x] = true; break; }
                }
            }
            return rim;
        }

        /// <summary>The longest straight run of <paramref name="free"/> tiles, along a row or a column, if one is two or more long.</summary>
        static bool Longest(bool[] free, int width, int height, out int x0, out int y0, out bool alongX, out int length)
        {
            x0 = y0 = length = 0;
            alongX = true;
            for (int y = 0; y < height; y++)
                for (int x = 0, run = 0; x <= width; x++)
                {
                    if (x < width && free[y * width + x]) { run++; continue; }
                    if (run > length) { length = run; x0 = x - run; y0 = y; alongX = true; }
                    run = 0;
                }
            for (int x = 0; x < width; x++)
                for (int y = 0, run = 0; y <= height; y++)
                {
                    if (y < height && free[y * width + x]) { run++; continue; }
                    if (run > length) { length = run; x0 = x; y0 = y - run; alongX = false; }
                    run = 0;
                }
            return length >= 2;
        }

        public void Dispose()
        {
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
                else UnityEngine.Object.DestroyImmediate(_root);
            }
            foreach (var o in new List<UnityEngine.Object>(_meshes) { Mask })
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
            }
            _meshes.Clear();
            Renderers.Clear();
        }
    }
}
