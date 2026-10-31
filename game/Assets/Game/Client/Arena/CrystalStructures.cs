using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Client.Arena
{
    /// <summary>
    /// The crystal swap (unity-game.md, "The Look": rainbow crystal), demo3d's StructureStyle under
    /// `--structure-style crystal`, copied and owned: once the arena is ready it swaps every structure
    /// site's kit materials for Game/StructureCrystal, one material per kit material, and gives each
    /// renderer its piece values and its site's gem colour (<see cref="Hue"/>: hues across the
    /// spectrum around the arena's centre). Crystal refracts the camera's opaque texture, so it turns
    /// that on for the camera. It owns what it makes and puts the kit back on dispose.
    /// </summary>
    public sealed class CrystalStructures : IDisposable
    {
        /// <summary>The set sits under the horde (a dark gem: demo3d's evidence/structures/looks): the scale on its colour and its soft peak.</summary>
        public const float Gain = 0.16f, Peak = 0.2f;
        /// <summary>The facets' cells per metre: a dozen cut faces across a wall section.</summary>
        public const float CellScale = 1.8f;
        public const float Refraction = 0.05f, Facets = 0.85f;
        /// <summary>The gems' saturation and value (HSV, sRGB): demo3d's sapphire #4f86ff at every hue.</summary>
        public const float Saturation = 0.69f, Value = 1f;

        static readonly int PieceId = Shader.PropertyToID("_SL_Piece");
        static readonly int PieceFxId = Shader.PropertyToID("_SL_PieceFx");
        static readonly int TintId = Shader.PropertyToID("_SL_Tint");
        static readonly int HeightId = Shader.PropertyToID("_HeightM");

        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly List<(Renderer renderer, Material[] materials)> _restore = new List<(Renderer, Material[])>();

        /// <summary>The materials it made, one per kit material.</summary>
        public int Materials { get; private set; }

        /// <summary>
        /// A site's gem colour (linear) from where it stands: its hue the angle around the arena's centre,
        /// starting from blue in the south and turning through the spectrum, jittered a little by
        /// <paramref name="seed"/> (0..1), so neighbouring sites read as one family and the arena holds
        /// every hue.
        /// </summary>
        public static Color Hue(Vector3 centre, float seed)
        {
            float turn = Mathf.Atan2(centre.x, -centre.z) / (2 * Mathf.PI);
            float h = Mathf.Repeat(0.62f + turn + 0.08f * (seed - 0.5f), 1f);
            var c = Color.HSVToRGB(h, Saturation, Value).linear;
            c.a = 1;
            return c;
        }

        /// <summary>Restyles <paramref name="arena"/>'s structure sites; call once it is ready. Returns how many renderers it styled.</summary>
        public int Apply(ArenaRuntime arena, Camera camera)
        {
            var shader = arena.CrystalShader != null ? arena.CrystalShader : throw new InvalidOperationException("CrystalStructures: the bake has no crystal shader");
            if (camera != null && camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                data.requiresColorOption = CameraOverrideOption.On;
            var bodies = new Dictionary<Material, Material>();
            var block = new MaterialPropertyBlock();
            int styled = 0;
            foreach (var site in arena.Sites)
            {
                if (site.Length == 0) continue;
                var siteBounds = site[0].bounds;
                foreach (var r in site) siteBounds.Encapsulate(r.bounds);
                var tint = Hue(siteBounds.center, Seed(siteBounds.center));
                foreach (var r in site)
                {
                    var kit = r.sharedMaterials;
                    _restore.Add((r, kit));
                    var mats = new Material[kit.Length];
                    for (int k = 0; k < kit.Length; k++)
                    {
                        if (!bodies.TryGetValue(kit[k], out var m)) bodies[kit[k]] = m = From(kit[k], shader);
                        mats[k] = m;
                    }
                    r.sharedMaterials = mats;
                    var b = r.bounds;
                    block.Clear();
                    block.SetVector(PieceId, new Vector4(b.center.x, b.min.y, b.center.z, Seed(b.center)));
                    block.SetVector(PieceFxId, Vector4.zero);
                    block.SetVector(TintId, tint);
                    block.SetFloat(HeightId, Mathf.Max(b.size.y, 0.1f));
                    r.SetPropertyBlock(block);
                    styled++;
                }
            }
            Materials = bodies.Count;
            Debug.Log($"CrystalStructures: {styled} renderers of {arena.Sites.Count} sites in crystal ({bodies.Count} materials)");
            return styled;
        }

        static float Seed(Vector3 p) => Mathf.Repeat(Mathf.Sin(p.x * 12.9898f + p.z * 78.233f) * 43758.5453f, 1f);

        /// <summary>A crystal material from the kit material it replaces: its albedo for alpha clip, its cull and canopy fade.</summary>
        Material From(Material kit, Shader shader)
        {
            var m = new Material(shader) { name = kit.name + " (crystal)", enableInstancing = false };
            var albedo = kit.HasProperty("_BaseMap") ? kit.GetTexture("_BaseMap") : null;
            bool clip = albedo != null && kit.IsKeywordEnabled("_ALPHATEST_ON") && kit.HasProperty("_Cutoff");
            m.SetTexture("_SL_ClipMap", albedo != null ? albedo : Texture2D.whiteTexture);
            if (albedo != null)
            {
                Vector2 scale = kit.GetTextureScale("_BaseMap"), offset = kit.GetTextureOffset("_BaseMap");
                m.SetVector("_SL_ClipMap_ST", new Vector4(scale.x, scale.y, offset.x, offset.y));
            }
            else m.SetVector("_SL_ClipMap_ST", new Vector4(1, 1, 0, 0));
            m.SetFloat("_SL_Cutoff", clip ? Mathf.Max(kit.GetFloat("_Cutoff"), 1e-3f) : 0f);
            m.SetFloat("_SL_Canopy", kit.IsKeywordEnabled("_CANOPY_FADE") ? 1f : 0f);
            m.SetFloat("_Cull", kit.HasProperty("_Cull") ? kit.GetFloat("_Cull") : (float)CullMode.Back);
            m.SetFloat("_SL_CellScale", CellScale);
            m.SetFloat("_Gain", Gain);
            m.SetFloat("_Peak", Peak);
            m.SetFloat("_Refraction", Refraction);
            m.SetFloat("_Facets", Facets);
            _owned.Add(m);
            return m;
        }

        public void Dispose()
        {
            foreach (var (r, mats) in _restore)
            {
                if (r == null) continue;
                r.sharedMaterials = mats;
                r.SetPropertyBlock(null);
            }
            _restore.Clear();
            foreach (var o in _owned)
                if (o != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                    else UnityEngine.Object.DestroyImmediate(o);
                }
            _owned.Clear();
        }
    }
}
