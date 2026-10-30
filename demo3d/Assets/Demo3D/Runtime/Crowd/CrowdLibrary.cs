using System;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The bone-texture baker's output (crowd.md, Bone-texture path), one asset in the gitignored
    /// Assets/Baked/Crowd/Resources/ so a player loads it with <see cref="Load"/>. It holds every
    /// drawn roster type in roster order, plus the shaders the crowd needs, so a build includes
    /// them.
    /// </summary>
    public sealed class CrowdLibrary : ScriptableObject
    {
        public const string ResourcePath = "CrowdLibrary";
        /// <summary>The default clip rate; 30 Hz is a cut (TD11, <c>clips30</c>), baked beside it.</summary>
        public const float ClipRate = 60f;
        public const float CutClipRate = 30f;

        public CrowdType[] Types = Array.Empty<CrowdType>();
        public Shader CrowdShader;
        public ComputeShader CullShader;
        public ComputeShader PaletteShader;
        public Material Ground;
        public Shader BufferDebug;
        /// <summary>The `--monster-style` shaders (Shaders/Styles/), by their names (<see cref="StyleShader"/>).</summary>
        public Shader[] StyleShaders = Array.Empty<Shader>();

        public static CrowdLibrary Load() =>
            Resources.Load<CrowdLibrary>(ResourcePath)
            ?? throw new InvalidOperationException(
                "CrowdLibrary: Assets/Baked/Crowd/Resources/CrowdLibrary.asset is missing (run `demo3d unity BakeCrowd`)");

        public Shader StyleShader(string name) =>
            Array.Find(StyleShaders, s => s != null && s.name == name)
            ?? throw new InvalidOperationException($"CrowdLibrary: no style shader {name} (re-run `demo3d unity BakeCrowd`)");

        public int IndexOf(string id)
        {
            for (int i = 0; i < Types.Length; i++)
                if (Types[i].Id == id) return i;
            return -1;
        }
    }

    /// <summary>One baked roster type.</summary>
    [Serializable]
    public sealed class CrowdType
    {
        public string Id;
        public string Kind;
        /// <summary>The combined mesh at demo scale: bone indices in UV1, weights in UV2 (float4).</summary>
        public Mesh Mesh;
        /// <summary>
        /// TD11's decimated LODs of <see cref="Mesh"/> (Unity's MeshLodUtility, the same vertices and
        /// streams): LOD1 about half its triangles, LOD2 about a quarter. Null if not baked.
        /// </summary>
        public Mesh Lod1, Lod2;
        public int Lod1Triangles, Lod2Triangles;
        /// <summary>
        /// The style mesh of <see cref="Mesh"/>, <see cref="Lod1"/> and <see cref="Lod2"/>
        /// (CrowdBaker.Unweld): unwelded, with the face normal in the tangent and barycentrics plus
        /// a value per triangle in UV3. Only the `--monster-style` treatments that need it draw it
        /// (<see cref="MonsterStyle.UsesStyleMesh"/>).
        /// </summary>
        public Mesh StyleMesh, StyleLod1, StyleLod2;
        /// <summary>One material per submesh (one per albedo texture).</summary>
        public Material[] Materials;
        /// <summary>Skinning matrices: bone b's row r of frame f is texel (3b + r, f).</summary>
        public Texture2D Bones;
        /// <summary>The same clips baked at 30 Hz (TD11's clips30 cut): rows from each clip's FirstRow30.</summary>
        public Texture2D Bones30;
        public int BoneCount;
        public CrowdClip[] Clips;
        /// <summary>Yaw-invariant bounds at instance scale 1: a sphere around (0, CenterY, 0).</summary>
        public float CenterY, Radius, HeightM;
        /// <summary>
        /// The albedo's linear luminance under the mesh's vertices, its 4th to 97th percentile: the
        /// painted style stretches it over the type's colour strip (MonsterStyle).
        /// </summary>
        public Vector2 ValueRange = new Vector2(0, 1);
        public float RadiusM; // separation radius from the roster
        public float HoverM;  // the client adds it to Position.y (bats)
        /// <summary>
        /// The type's signature colour (ps-e8r.8, roster.json): its hue in HSV degrees (negative for
        /// none, as the hero), its saturation, and how far the shader dyes its low-saturation albedo
        /// toward it (0..1).
        /// </summary>
        public float Hue = -1, HueSaturation, Dye;
        /// <summary>Degrees the shader first turns the whole albedo round the luma axis (a saturated hide on another type's hue).</summary>
        public float HueRotate;
        public int Vertices, Triangles;
        /// <summary>The stock baseline's prefab: one SkinnedMeshRenderer and an Animator.</summary>
        public GameObject StockPrefab;

        /// <summary>
        /// `--classic-monster-colors`: the monsters' colours before the signature colours (ps-e8r.8),
        /// the packs' own albedo with no dye and no hue rotation, for comparing the two palettes.
        /// </summary>
        public static bool ClassicColors => Args.Has("--classic-monster-colors");

        /// <summary>
        /// The signature colour for CrowdSkinned's <c>_Demo3DTypeColor</c>: linear rgb at full value,
        /// alpha the dye; all zero for a type without one or under <see cref="ClassicColors"/>.
        /// `--type-dye d` and `--type-saturation s` override the roster's, for tuning in a player.
        /// </summary>
        public Vector4 SignatureColor()
        {
            if (Hue < 0 || ClassicColors) return Vector4.zero;
            float s = Args.GetFloat("--type-saturation", HueSaturation);
            var c = Color.HSVToRGB(Mathf.Repeat(Hue / 360f, 1f), Mathf.Clamp01(s), 1f).linear;
            return new Vector4(c.r, c.g, c.b, Mathf.Clamp01(Args.GetFloat("--type-dye", Dye)));
        }

        /// <summary>CrowdSkinned's <c>_Demo3DTypeRotate</c>: (cos, sin) of <see cref="HueRotate"/> (`--hue-rotate-ID` overrides it).</summary>
        public Vector4 HueRotation()
        {
            if (ClassicColors) return new Vector4(1, 0, 0, 0);
            float r = Args.GetFloat("--hue-rotate-" + Id, HueRotate) * Mathf.Deg2Rad;
            return new Vector4(Mathf.Cos(r), Mathf.Sin(r), 0, 0);
        }

        public int Clip(string role)
        {
            for (int i = 0; i < Clips.Length; i++)
                if (Clips[i].Role == role) return i;
            return -1;
        }
    }

    /// <summary>One clip in a type's bone texture, in roster order.</summary>
    [Serializable]
    public struct CrowdClip
    {
        public string Role;
        public string Name;
        public int FirstRow;
        public int Frames;
        /// <summary>The clip's rows in the 30 Hz texture (<see cref="CrowdType.Bones30"/>).</summary>
        public int FirstRow30;
        public int Frames30;
        public float Length;
        public bool Loop;
        /// <summary>From the clip annotations (TD2); negative where absent.</summary>
        public float Impact, End, StrideM;
        /// <summary>Horizontal root travel removed from a looping clip, in metres.</summary>
        public float DriftRemovedM;
        /// <summary>This clip's own bounding sphere at instance scale 1 (see CrowdType).</summary>
        public float CenterY, Radius, HeightM;

        /// <summary>Frames a clip of this length bakes to at 60 Hz (or <paramref name="rate"/>): both ends included.</summary>
        public static int FrameCount(float length, float rate = CrowdLibrary.ClipRate) => Math.Max(2, (int)Math.Ceiling(length * rate - 1e-3f) + 1);

        /// <summary>The sample time of frame f at 60 Hz (or <paramref name="rate"/>; the last frame is the clip's end).</summary>
        public float FrameTime(int f, float rate = CrowdLibrary.ClipRate) => Math.Min(Length, f / rate);
    }
}
