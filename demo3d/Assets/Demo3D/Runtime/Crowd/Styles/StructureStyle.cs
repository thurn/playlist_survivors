using System;
using System.Collections.Generic;
using Demo3D.Runtime.Arena;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The arena's structures in a monster style's treatment, `--structure-style <name>` (environment.md, Structure
    /// styles; epic ps-suy): a look only, beside <see cref="MonsterStyle"/>. The names are the monster styles' but mix,
    /// plus match, which takes `--monster-style`'s (mix: baseline). baseline (the default) leaves the arena exactly as it
    /// is. Any other style swaps the materials of <see cref="ArenaRuntime.Structures"/> for its structure shader
    /// (Arena/Shaders/Structures/: the style's own forward code on static meshes) once the arena is ready, gives each
    /// renderer its piece values, adds fire's flames as a second renderer, and brings its post look (when the horde has
    /// none) and the beat globals (<see cref="StyleBeat"/>). It owns what it makes and puts the kit back on dispose.
    /// </summary>
    public sealed class StructureStyle : IDisposable
    {
        public const string Flag = "--structure-style";

        /// <summary>
        /// `--structure-tune key=value,...`: the structures' own tune flag, with `--style-tune`'s keys (gain, peak and the
        /// post look's), so the set can be tuned without touching the horde's values. The post keys tune the structure
        /// style's post look, which applies only when the horde has none.
        /// </summary>
        public const string TuneFlag = "--structure-tune";

        public const string Match = "match";

        /// <summary>Every name the flag takes: the monster styles' but mix, and match.</summary>
        public static readonly string[] Names = { "baseline", "painted", "fire", "crystal", "spectrum", "hologram", "chrome", "void", Match };

        /// <summary>The resolved style (never match).</summary>
        public readonly string Name;
        public bool IsBaseline => Name == "baseline";

        /// <summary>Crystal refracts the camera's opaque texture, which URP's asset leaves off (DemoMain turns it on).</summary>
        public bool NeedsOpaqueTexture => Name == "crystal";

        readonly Dictionary<string, float> _tune = new Dictionary<string, float>();
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly List<(Renderer renderer, Material[] materials, bool block)> _restore = new List<(Renderer, Material[], bool)>();

        static readonly int PieceId = Shader.PropertyToID("_SL_Piece");
        static readonly int PieceFxId = Shader.PropertyToID("_SL_PieceFx");
        static readonly int HeightId = Shader.PropertyToID("_HeightM");
        static readonly int LickId = Shader.PropertyToID("_Lick");
        static readonly int ThicknessId = Shader.PropertyToID("_Thickness");

        StructureStyle(string name) { Name = name; }

        public static StructureStyle Baseline() => new StructureStyle("baseline");

        /// <summary>
        /// A style by name (null: baseline); match follows <paramref name="horde"/> (mix, a style per type, has no one
        /// style for the set: baseline). Any other name fails, listing the known ones.
        /// </summary>
        public static StructureStyle Parse(string name, MonsterStyle horde = null)
        {
            if (name == null) return Baseline();
            if (Array.IndexOf(Names, name) < 0)
                throw new ArgumentException($"{Flag}: unknown style '{name}' (one of {string.Join(", ", Names)})");
            if (name == Match) name = horde == null || horde.Name == "mix" ? "baseline" : horde.Name;
            return new StructureStyle(name);
        }

        /// <summary>The style command-line arguments ask for: `--structure-style name`, else baseline; and any `--structure-tune`.</summary>
        public static StructureStyle FromArgs(IReadOnlyList<string> args, MonsterStyle horde)
        {
            StructureStyle style = null;
            string tune = null;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] != Flag && args[i] != TuneFlag) continue;
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException(args[i] == Flag
                        ? $"{Flag} needs a style (one of {string.Join(", ", Names)})"
                        : $"{TuneFlag} needs key=value pairs (keys {string.Join(", ", MonsterStyle.TuneKeys)})");
                if (args[i] == Flag) style = Parse(args[i + 1], horde);
                else tune = args[i + 1];
            }
            style ??= Baseline();
            if (tune != null)
            {
                if (style.IsBaseline) throw new ArgumentException($"{TuneFlag} tunes a {Flag} treatment; baseline has none");
                MonsterStyle.ParseTune(TuneFlag, tune, style._tune);
            }
            return style;
        }

        public static StructureStyle FromCommandLine(MonsterStyle horde) => FromArgs(Environment.GetCommandLineArgs(), horde);

        /// <summary>
        /// The scale on a structure's forward colour (_Gain, SL_Grade). The set sits under the horde (environment.md,
        /// Value structure: effects, then the horde, then the set); values and why: evidence/structures/looks/README.md.
        /// </summary>
        public float Gain
        {
            get
            {
                if (_tune.TryGetValue("gain", out float g)) return g;
                switch (Name)
                {
                    case "painted": return 1.0f;
                    case "chrome": return 0.8f;   // dark mirrors: the reflected grid floor and horizon need it
                    case "void": return 0.5f;
                    case "fire": return 0.7f;
                    case "crystal": return 0.16f; // a dark sapphire: crypt's dark ruins by the horde must stay dark (README)
                    case "spectrum": return 0.45f; // a wall of film reads as coloured paint: keep the lacquer dark under the wires
                    case "hologram": return 0.3f; // its light over dim glass, under the horde beside it
                    default: return 0.8f;
                }
            }
        }

        /// <summary>
        /// The soft limit on a structure's luminance after the gain (_Peak, SL_Grade), below each style's horde peak
        /// (<see cref="MonsterStyle.Peak"/>), with the structure shaders' own limit on the brightest channel under the
        /// bloom's knee (StyleCommon.hlsl, SL_STATIC_CHANNEL_MAX): a structure blooms only on PostFeel's pulses.
        /// </summary>
        public float Peak
        {
            get
            {
                if (_tune.TryGetValue("peak", out float p)) return p;
                switch (Name)
                {
                    case "fire": return 0.1f;
                    case "hologram": return 0.07f;
                    case "crystal":
                    case "spectrum": return 0.2f;
                    default: return 0.3f;
                }
            }
        }

        /// <summary>
        /// The post stack the structure style wants as PostFeel's base over the night's own (<paramref name="night"/>,
        /// read from the volume: <see cref="PostLook.From"/>), or null under baseline. The horde it frames is the
        /// baseline's, whose colours and readability are tuned to the night's grade, and the monster styles' stylized
        /// grade (contrast, saturation, their full fringe) took up to 10 L* off its contrast with the ground
        /// (evidence/structures/looks/README.md). So the set keeps the night's tonemapper, exposure, contrast, saturation,
        /// threshold and vignette, and takes the style's accents only: its bloom (relative to the stylized default's) and
        /// an eighth of its chromatic fringe. `--structure-tune`'s post keys go over it.
        /// </summary>
        public PostLook? PostOver(PostLook night)
        {
            if (IsBaseline) return null;
            var style = MonsterStyle.PostOf(Name);
            var look = night;
            look.bloom = night.bloom * style.bloom / PostLook.Stylized().bloom;
            look.chroma = Mathf.Max(night.chroma, style.chroma / 8f);
            return look.With(_tune);
        }

        /// <summary>
        /// The one post look for the night volume: the horde's if `--monster-style` sets one, else the structures' over
        /// the night's (null: the night's own).
        /// </summary>
        public static PostLook? PostFor(MonsterStyle horde, StructureStyle structures, PostLook night) =>
            horde?.Post ?? structures?.PostOver(night);

        static string ShaderOf(string name)
        {
            switch (name)
            {
                case "painted": return "Demo3D/Structures/Painted";
                case "fire": return "Demo3D/Structures/Fire";
                case "chrome": return "Demo3D/Structures/Chrome";
                case "void": return "Demo3D/Structures/Void";
                case "crystal": return "Demo3D/Structures/Crystal";
                case "spectrum": return "Demo3D/Structures/Spectrum";
                case "hologram": return "Demo3D/Structures/Hologram";
                default: throw new InvalidOperationException(name);
            }
        }

        /// <summary>The structure shader's name for this style (null under baseline).</summary>
        public string ShaderName => IsBaseline ? null : ShaderOf(Name);

        /// <summary>SL_StaticFacets' cells per metre, for the styles that read a style mesh's barycentrics.</summary>
        float CellScale
        {
            get
            {
                switch (Name)
                {
                    case "crystal": return 1.8f;   // a dozen cut faces across a wall section
                    case "hologram": return 2f;    // a fine projected grid
                    case "spectrum": return 1f;    // a metre lattice, like a synth's panel
                    default: return 1f;
                }
            }
        }

        static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c.linear : Color.magenta;
        static Color H(string hex, float intensity) { var c = C(hex) * intensity; c.a = 1; return c; }

        /// <summary>
        /// Restyles the arena's structures (<see cref="ArenaRuntime.Structures"/>); call once it is ready. Returns how many
        /// renderers it styled. Baseline does nothing.
        /// </summary>
        public int Apply(ArenaRuntime arena, CrowdLibrary lib)
        {
            if (IsBaseline) return 0;
            var shader = lib.StructureShader(ShaderOf(Name));
            var flameShader = Name == "fire" ? lib.StructureShader("Demo3D/Structures/FireFlames") : null;
            Texture2D ramp = null;
            if (Name == "painted")
            {
                ramp = MonsterStyle.RampTexture(new[] { C("#161d31"), C("#2f4264"), C("#617692"), C("#a9a594") }, "Ramp structures");
                _owned.Add(ramp);
            }
            var bodies = new Dictionary<Material, Material>();
            var flames = new Dictionary<Material, Material>();
            var block = new MaterialPropertyBlock();
            int styled = 0, burning = 0;
            foreach (var r in arena.Structures)
            {
                if (r == null) continue;
                var kit = r.sharedMaterials;
                bool hadBlock = r.HasPropertyBlock();
                _restore.Add((r, kit, hadBlock));
                var mats = new Material[kit.Length];
                for (int k = 0; k < kit.Length; k++)
                {
                    if (!bodies.TryGetValue(kit[k], out var m))
                    {
                        m = From(kit[k], shader, arena.Options.Prepass);
                        Look(m, ramp);
                        bodies[kit[k]] = m;
                    }
                    mats[k] = m;
                }
                r.sharedMaterials = mats;
                var b = r.bounds;
                block.Clear();
                if (hadBlock) r.GetPropertyBlock(block);
                SetPiece(block, b);
                r.SetPropertyBlock(block);
                styled++;
                if (flameShader != null && r.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    AddFlames(r, filter.sharedMesh, kit, flameShader, flames, block, b);
                    burning++;
                }
            }
            Debug.Log($"StructureStyle: {Flag} {Name} on {styled} of {arena.Static.Count} kit renderers " +
                      $"({bodies.Count} materials{(flameShader != null ? $", {burning} burning" : "")})");
            return styled;
        }

        /// <summary>The piece values a structure shader reads per renderer: its base and seed, height, hit and death.</summary>
        static void SetPiece(MaterialPropertyBlock block, Bounds b)
        {
            float seed = Mathf.Repeat(Mathf.Sin(b.center.x * 12.9898f + b.center.z * 78.233f) * 43758.5453f, 1f);
            block.SetVector(PieceId, new Vector4(b.center.x, b.min.y, b.center.z, seed));
            block.SetVector(PieceFxId, Vector4.zero);
            block.SetFloat(HeightId, Mathf.Max(b.size.y, 0.1f));
        }

        /// <summary>
        /// A structure material on <paramref name="shader"/> from the kit material it replaces: its albedo, colour, cull,
        /// alpha clip and canopy fade, and the style's grade. Out of the depth prepass when the arena's kit is (E6).
        /// </summary>
        Material From(Material kit, Shader shader, bool prepass)
        {
            var m = new Material(shader) { name = $"{kit.name} ({Name})", enableInstancing = false };
            var albedo = kit.HasProperty("_BaseMap") ? kit.GetTexture("_BaseMap") : null;
            if (albedo != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (kit.HasProperty("_BaseColor") && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", kit.GetColor("_BaseColor"));
            bool clip = albedo != null && kit.IsKeywordEnabled("_ALPHATEST_ON") && kit.HasProperty("_Cutoff");
            m.SetTexture("_SL_ClipMap", albedo != null ? albedo : Texture2D.whiteTexture);
            if (albedo != null)
            {
                Vector2 scale = kit.GetTextureScale("_BaseMap"), offset = kit.GetTextureOffset("_BaseMap");
                m.SetVector("_SL_ClipMap_ST", new Vector4(scale.x, scale.y, offset.x, offset.y));
            }
            else m.SetVector("_SL_ClipMap_ST", new Vector4(1, 1, 0, 0));
            m.SetFloat("_SL_Cutoff", clip ? Mathf.Max(kit.GetFloat("_Cutoff"), 1e-3f) : 0f);
            m.SetFloat("_SL_Canopy", kit.IsKeywordEnabled("_CANOPY_FADE") || kit.shader.name == "Demo3D/LitCanopy" ? 1f : 0f);
            m.SetFloat("_Cull", kit.HasProperty("_Cull") ? kit.GetFloat("_Cull") : (float)CullMode.Back);
            m.SetFloat("_SL_CellScale", CellScale);
            m.SetFloat("_Gain", Gain);
            m.SetFloat("_Peak", Peak);
            if (!prepass)
            {
                m.SetShaderPassEnabled("DepthNormals", false);
                m.SetShaderPassEnabled("DepthOnly", false);
            }
            _owned.Add(m);
            return m;
        }

        /// <summary>The style's own colours and settings for the set (structures are larger and dimmer than monsters).</summary>
        void Look(Material m, Texture2D ramp)
        {
            switch (Name)
            {
                case "painted":
                    // Moonlit stone in the horde's lacquer: slate to a muted bone (a canopy's lit facets stay under the horde), violet
                    // shadows, a warm rim, sparse teal runes that flare as the beat wave passes.
                    m.SetTexture("_Ramp", ramp);
                    m.SetVector("_LumRange", new Vector4(0.04f, 0.5f, 0, 0));
                    m.SetVector("_ShadowColor", C("#4a4a9c"));
                    m.SetVector("_RimColor", H("#ffb070", 0.8f));
                    m.SetVector("_SpotColor", H("#4fe8ff", 1.8f));
                    m.SetFloat("_SpotScale", 3.2f);
                    m.SetFloat("_SpotAmount", 0.84f);
                    m.SetFloat("_Gloss", 28f);
                    m.SetFloat("_Sheen", 0.45f);
                    break;
                case "fire":
                    SetFire(m);
                    m.SetFloat("_CrackScale", 1.1f);  // cells a structure's size, not a monster's
                    m.SetFloat("_CrackWidth", 0.06f);
                    break;
                case "chrome":
                    m.SetVector("_Tint", C("#c8d0e6"));
                    m.SetFloat("_Ripple", 0.006f);
                    break;
                case "void":
                    m.SetVector("_RimColor", H("#7a5cff", 2.2f));
                    m.SetFloat("_RimPower", 3f);
                    m.SetVector("_NebulaA", C("#3a0d5c"));
                    m.SetVector("_NebulaB", C("#00465c"));
                    break;
                case "crystal":
                    m.SetVector("_Tint", C("#4f86ff"));
                    m.SetFloat("_Refraction", 0.05f);
                    m.SetFloat("_Facets", 0.85f);
                    break;
                case "spectrum":
                    m.SetFloat("_Hue", 0.55f);
                    m.SetFloat("_WireGlow", 1.4f);
                    m.SetFloat("_Bulge", 0.015f);
                    break;
                case "hologram":
                    m.SetVector("_Color", H("#38c8ff", 1.6f));
                    m.SetFloat("_Glitch", 0.15f);
                    break;
            }
        }

        static void SetFire(Material m)
        {
            m.SetVector("_Ember", H("#8a1600", 1f));
            m.SetVector("_Flame", H("#ff7a1a", 2.4f));
            m.SetVector("_Core", H("#ffd27a", 4f));
        }

        /// <summary>
        /// A burning structure's flames: a second renderer on its mesh (the FireShell approach, crowd.md), a child at the
        /// same transform, additive, casting and receiving no shadow, its tongues sized to the piece.
        /// </summary>
        void AddFlames(Renderer body, Mesh mesh, Material[] kit, Shader shader, Dictionary<Material, Material> flames,
                       MaterialPropertyBlock block, Bounds b)
        {
            var go = new GameObject("Flames") { layer = body.gameObject.layer };
            go.transform.SetParent(body.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            var mats = new Material[kit.Length];
            for (int k = 0; k < kit.Length; k++)
            {
                if (!flames.TryGetValue(kit[k], out var m))
                {
                    m = From(kit[k], shader, true);
                    SetFire(m);
                    // Canopies are acres of leaf cards: their flames burn lower, as the dragon's wings do.
                    m.SetFloat("_Intensity", m.GetFloat("_SL_Canopy") > 0.5f ? 0.25f : 0.5f);
                    flames[kit[k]] = m;
                }
                mats[k] = m;
            }
            r.sharedMaterials = mats;
            block.Clear();
            SetPiece(block, b);
            block.SetFloat(LickId, Mathf.Clamp(0.18f * b.size.y, 0.35f, 1.4f));
            block.SetFloat(ThicknessId, Mathf.Clamp(0.03f * b.size.y, 0.06f, 0.2f));
            r.SetPropertyBlock(block);
            _owned.Add(go);
        }

        public void Dispose()
        {
            foreach (var (r, mats, block) in _restore)
            {
                if (r == null) continue;
                r.sharedMaterials = mats;
                if (!block) r.SetPropertyBlock(null);
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
