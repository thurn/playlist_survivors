using System.Collections.Generic;
using System.IO;
using PS.Choreography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.EditorTools.Arena
{
    /// <summary>
    /// TD15's additions to a generated arena scene (environment.md, Art direction and Lighting the
    /// night), applied by <see cref="ArenaBaker"/> after it has placed the kit:
    /// - the canopy fade: at the lit rungs the trees' materials become Demo3D/LitCanopy twins, which
    ///   dither out pixels between the camera and the hero (E0 stays stock);
    /// - the mist: the layout's <c>atmosphere.mist</c> cards, merged into one mesh with our
    ///   Demo3D/MistCard material (no shadows, not part of the static set).
    /// Everything it writes goes to the environment's directory under the gitignored Assets/Baked/Arena/.
    /// </summary>
    public static class ArenaDressing
    {
        public static void Dress(ArenaEnvironment env, Transform root, Transform stat, bool lit)
        {
            Twins.Clear(); // each scene refreshes its twins from the converted materials (a warm editor keeps statics)
            var json = env.Root;
            int trees = lit ? Canopies(env, stat) : 0;
            int cards = Mist(env, root, json["atmosphere"]["mist"]);
            int runes = Runes(env, root, json["atmosphere"]["runes"]);
            int pools = Pools(env, root, stat, json["lighting"]["glow"], json["lighting"]["flames"]["props"]);
            Debug.Log($"ArenaDressing: {trees} tree renderers fade near the hero, {cards} mist cards, {runes} rune circles, {pools} glow pools");
        }

        /// <summary>
        /// A converted kit material's base colour with the layout's <c>lighting.kitValue</c>
        /// (<paramref name="values"/>) applied (environment.md, Value structure): the first entry whose
        /// key starts the vendor material's name scales its RGB (alpha, and so alpha clipping,
        /// untouched). The packs' pale stone and lilac atlases otherwise outshine the horde under the
        /// moon. The converter applies <c>lighting.kitEmission</c> to emission the same way.
        /// </summary>
        public static Color KitValue(JsonValue values, Color c, string materialName)
        {
            if (values.Type != JsonType.Object) return c;
            foreach (var kv in values.Members)
                if (materialName.StartsWith(kv.Key, System.StringComparison.Ordinal))
                {
                    float k = kv.Value.AsFloat(1f);
                    return new Color(c.r * k, c.g * k, c.b * k, c.a);
                }
            return c;
        }

        /// <summary>True for the kit's trees (canopied pieces), not their logs and stumps.</summary>
        public static bool IsTree(string pieceName) =>
            pieceName.Contains("Tree") && !pieceName.Contains("TreeLog") && !pieceName.Contains("TreeStump");

        static readonly Dictionary<Material, Material> Twins = new Dictionary<Material, Material>();

        static int Canopies(ArenaEnvironment env, Transform stat)
        {
            var shader = Shader.Find("Demo3D/LitCanopy") ?? throw new System.Exception("ArenaDressing: no Demo3D/LitCanopy");
            int n = 0;
            foreach (Transform group in stat)
            foreach (Transform piece in group)
            {
                if (!IsTree(piece.name)) continue;
                foreach (var r in piece.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null || mats[i].shader == null || mats[i].shader.name != "Demo3D/LitStaticShadow") continue;
                        mats[i] = Twin(env, mats[i], shader);
                        changed = true;
                    }
                    if (changed) { r.sharedMaterials = mats; n++; }
                }
            }
            return n;
        }

        static Material Twin(ArenaEnvironment env, Material m, Shader shader)
        {
            if (Twins.TryGetValue(m, out var t) && t != null) return t; // already refreshed for this scene
            var path = $"{env.MaterialDir}/{Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m))}_Canopy.mat";
            t = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = t == null;
            if (create) t = new Material(m) { name = m.name + " canopy" };
            else t.CopyPropertiesFromMaterial(m);
            t.shader = shader;
            t.shaderKeywords = m.shaderKeywords;
            t.EnableKeyword("_CANOPY_FADE");
            t.renderQueue = m.renderQueue;
            if (create) AssetDatabase.CreateAsset(t, path);
            else EditorUtility.SetDirty(t);
            Twins[m] = t;
            return t;
        }

        /// <summary>
        /// The mist cards, one merged mesh: each card a horizontal quad at its height, its uv the
        /// card's own 0..1 (the shader's elliptical edge), its vertex alpha its opacity.
        /// </summary>
        static int Mist(ArenaEnvironment env, Transform root, JsonValue mist)
        {
            var cards = Items(mist["cards"]);
            float height = mist["heightM"].AsFloat(0.45f);
            return Cards(env, root, "Mist", cards, "Demo3D/MistCard", c =>
                (new Vector3(c["at"][0].AsFloat(), c["heightM"].AsFloat(height), c["at"][1].AsFloat()),
                 new Vector2(c["size"][0].AsFloat(10), c["size"][1].AsFloat(6)), c["yawDeg"].AsFloat(0),
                 new Color(1, 1, 1, c["opacity"].AsFloat(0.6f))),
                mat =>
                {
                    var color = mist["color"];
                    if (color.Count >= 3) mat.SetColor("_MistColor", new Color(color[0].AsFloat(), color[1].AsFloat(), color[2].AsFloat()));
                    mat.SetFloat("_NoiseScale", mist["noiseScale"].AsFloat(0.12f));
                    mat.SetFloat("_SoftM", mist["softM"].AsFloat(1.2f));
                });
        }

        /// <summary>
        /// The rune circles, one merged mesh of ground quads (Demo3D/RuneCircle): the vertex colour is
        /// the circle's linear tint, its alpha the intensity.
        /// </summary>
        static int Runes(ArenaEnvironment env, Transform root, JsonValue runes)
        {
            var cards = Items(runes["circles"]);
            return Cards(env, root, "Runes", cards, "Demo3D/RuneCircle", c =>
                {
                    float d = 2 * c["radiusM"].AsFloat(6);
                    var col = c["color"];
                    return (new Vector3(c["at"][0].AsFloat(), 0.04f, c["at"][1].AsFloat()), new Vector2(d, d), c["yawDeg"].AsFloat(0),
                        new Color(col[0].AsFloat(0.4f), col[1].AsFloat(0.8f), col[2].AsFloat(1f), c["intensity"].AsFloat(0.5f)));
                },
                mat => mat.SetFloat("_Turn", runes["turnsPerS"].AsFloat(0.01f)));
        }

        /// <summary>
        /// A pool under each placed glowing or flaming piece (found by name in the static set): the
        /// glow or flame entry's colour (linear) and <c>pool {radiusM, intensity}</c>, centred under it.
        /// </summary>
        static int Pools(ArenaEnvironment env, Transform root, Transform stat, params JsonValue[] sources)
        {
            var list = new List<(Vector3 at, float radius, Color color)>();
            foreach (var glow in sources)
            if (glow.Type == JsonType.Object)
                foreach (var kv in glow.Members)
                {
                    var pool = kv.Value["pool"];
                    if (pool.Type != JsonType.Object) continue;
                    string piece = kv.Key.Substring(kv.Key.IndexOf('/') + 1);
                    var col = kv.Value["color"];
                    var color = new Color(col[0].AsFloat(1), col[1].AsFloat(1), col[2].AsFloat(1), pool["intensity"].AsFloat(0.3f));
                    foreach (Transform group in stat)
                    foreach (Transform t in group)
                        if (t.name == piece) list.Add((new Vector3(t.position.x, 0.03f, t.position.z), pool["radiusM"].AsFloat(4f) * t.localScale.x, color));
                }
            return Cards(env, root, "GlowPools", list, "Demo3D/GroundGlow",
                p => (p.at, new Vector2(2 * p.radius, 2 * p.radius), 0f, p.color), mat => { });
        }

        static List<JsonValue> Items(JsonValue array)
        {
            var list = new List<JsonValue>();
            for (int i = 0; i < array.Count; i++) list.Add(array[i]);
            return list;
        }

        /// <summary>
        /// Horizontal quads, merged into one mesh (the environment's NAME.asset) and drawn with one
        /// material of our own shader (its Materials/NAME.mat).
        /// </summary>
        static int Cards<T>(ArenaEnvironment env, Transform root, string name, List<T> cards, string shaderName,
            System.Func<T, (Vector3 at, Vector2 size, float yawDeg, Color color)> card, System.Action<Material> configure)
        {
            if (cards.Count == 0) return 0;
            string meshPath = $"{env.Dir}/{name}.asset", matPath = $"{env.MaterialDir}/{name}.mat";
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color>();
            var idx = new List<int>();
            for (int i = 0; i < cards.Count; i++)
            {
                var (at, size, yawDeg, color) = card(cards[i]);
                var rot = Quaternion.Euler(0, yawDeg, 0);
                int b = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    float u = k == 1 || k == 2 ? 1 : 0, s = k >= 2 ? 1 : 0;
                    v.Add(at + rot * new Vector3((u - 0.5f) * size.x, 0, (s - 0.5f) * size.y));
                    uv.Add(new Vector2(u, s));
                    col.Add(color);
                }
                idx.AddRange(new[] { b, b + 3, b + 1, b + 1, b + 3, b + 2 });
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Arena" + name };
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            var shader = Shader.Find(shaderName) ?? throw new System.Exception("ArenaDressing: no " + shaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "Arena" + name };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.shader = shader;
            configure(mat);
            EditorUtility.SetDirty(mat);

            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return cards.Count;
        }
    }
}
