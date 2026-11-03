using System;
using System.IO;
using System.Linq;
using Champions.Render;
using UnityEditor;
using UnityEngine;

namespace Champions.EditorTools.Effects
{
    /// <summary>
    /// Writes the weapon effects' library (<see cref="EffectLibrary"/>) into the ignored Assets/Baked/Effects/Resources/:
    /// Hovl's slash meshes and noise texture from the synced vendor files (manifest.json's Hovl prefabs bring them),
    /// referenced and never copied, each mesh's frame measured from its vertices, and our own shaders. Without the vendor
    /// files there is no library, and the game draws no weapon effects. <see cref="Tasks.BuildPlayer"/> runs it before
    /// every build, so the library always matches the vendor files and the shaders.
    /// </summary>
    public static class EffectBaker
    {
        public const string Dir = "Assets/Baked/Effects/Resources";
        public const string HovlDir = "Assets/Vendor/Hovl Studio/HSFiles";
        public const string ShaderDir = "Assets/Champions/Render/Effects/Shaders/";
        /// <summary>Hovl's crescent and ring meshes, and the magic sword the spectral swords draw with.</summary>
        public static readonly string[] Rings = { "Slash1", "Slash2", "Slash3", "Slash4" };
        public const string Sword = "MagicSword";
        /// <summary>Hovl's sword slash's dissolve noise (its SwordSlash material's), the streaks and the fade.</summary>
        public const string Noise = "Noise43b";

        public static string AssetPath => $"{Dir}/{EffectLibrary.ResourcePath}.asset";

        /// <summary>Bakes the library; false, with nothing written, when the vendor files are missing.</summary>
        public static bool Bake()
        {
            var meshes = Rings.Append(Sword).Select(n => (Name: n, Mesh: LoadMesh($"{HovlDir}/Models/{n}.fbx"))).ToArray();
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>($"{HovlDir}/Textures/{Noise}.png");
            var missing = meshes.Where(m => m.Mesh == null).Select(m => m.Name).ToList();
            if (noise == null) missing.Add(Noise);
            if (missing.Count > 0)
            {
                Debug.LogWarning($"EffectBaker: no {string.Join(", ", missing)} under {HovlDir} (run `champions setup`); no effect library");
                return false;
            }
            var lib = ScriptableObject.CreateInstance<EffectLibrary>();
            lib.Meshes = meshes.Select(m => Measure(m.Name, m.Mesh, solid: m.Name == Sword)).ToArray();
            lib.Noise = noise;
            lib.Slash = Load<Shader>("Slash.shader");
            lib.Spark = Load<Shader>("Spark.shader");
            lib.Sparks = Load<ComputeShader>("Sparks.compute");
            Directory.CreateDirectory(Dir);
            AssetDatabase.DeleteAsset(AssetPath);
            AssetDatabase.CreateAsset(lib, AssetPath);
            AssetDatabase.SaveAssets();
            MeleeBaker.Bake();
            Debug.Log($"EffectBaker: {string.Join(", ", lib.Meshes.Select(m => $"{m.Name} ({m.Mesh.GetIndexCount(0) / 3} triangles, inner {m.Inner:F2})"))} -> {AssetPath}");
            return true;
        }

        static T Load<T>(string file) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(ShaderDir + file) ?? throw new Exception($"EffectBaker: no {ShaderDir}{file}");

        static Mesh LoadMesh(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();

        /// <summary>
        /// A mesh's frame (<see cref="EffectMesh.Frame"/>). A ring or crescent: its thinnest axis becomes +y and its origin
        /// the centre, scaled to an outer radius of 1, a crescent turned so its middle points along +x. A solid: its
        /// longest axis becomes +x with the tip (the end farther from its origin) at 0 and the other end at -1, its
        /// thinnest +y.
        /// </summary>
        public static EffectMesh Measure(string name, Mesh mesh, bool solid)
        {
            var v = mesh.vertices;
            var b = mesh.bounds;
            int thin = Smallest(b.size), longest = Largest(b.size);
            var normal = Axis(thin);
            Vector3 x;
            if (solid)
            {
                float lo = v.Min(p => p[longest]), hi = v.Max(p => p[longest]);
                bool tipHigh = Mathf.Abs(hi) >= Mathf.Abs(lo);
                x = Axis(longest) * (tipHigh ? 1 : -1);
                var z = Vector3.Cross(x, normal);
                float length = hi - lo, tip = tipHigh ? hi : lo;
                var r = Rows(x, normal, z, 1 / length);
                // The tip at 0: subtract its point along the axis.
                r.SetColumn(3, new Vector4(-Mathf.Abs(tip) / length, 0, 0, 1));
                return new EffectMesh { Name = name, Mesh = mesh, Frame = r, Inner = 0 };
            }
            x = Axis(thin == 0 ? 1 : 0);
            var zr = Vector3.Cross(x, normal);
            // A crescent's middle along +x: the vertices' mean direction in the plane.
            Vector2 mean = Vector2.zero;
            foreach (var p in v) mean += new Vector2(Vector3.Dot(p, x), Vector3.Dot(p, zr)).normalized;
            bool ring = mean.magnitude < 0.2f * v.Length;
            if (!ring)
            {
                float a = Mathf.Atan2(mean.y, mean.x);
                var x2 = x * Mathf.Cos(a) + zr * Mathf.Sin(a);
                zr = Vector3.Cross(x2, normal);
                x = x2;
            }
            float outer = 0, inner = float.MaxValue;
            foreach (var p in v)
            {
                float d = new Vector2(Vector3.Dot(p, x), Vector3.Dot(p, zr)).magnitude;
                outer = Mathf.Max(outer, d);
                inner = Mathf.Min(inner, d);
            }
            return new EffectMesh { Name = name, Mesh = mesh, Frame = Rows(x, normal, zr, 1 / outer), Inner = inner / outer };
        }

        static Matrix4x4 Rows(Vector3 x, Vector3 y, Vector3 z, float scale)
        {
            var m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(x.x, x.y, x.z, 0) * scale);
            m.SetRow(1, new Vector4(y.x, y.y, y.z, 0) * scale);
            m.SetRow(2, new Vector4(z.x, z.y, z.z, 0) * scale);
            m.SetRow(3, new Vector4(0, 0, 0, 1));
            return m;
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
        static int Smallest(Vector3 s) => s.x <= s.y && s.x <= s.z ? 0 : s.y <= s.z ? 1 : 2;
        static int Largest(Vector3 s) => s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;
    }
}
