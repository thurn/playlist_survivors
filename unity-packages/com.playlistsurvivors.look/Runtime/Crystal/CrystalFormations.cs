using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PS.Look
{
    /// <summary>
    /// A shot's crystal formations on screen (trailer.md, "Crystal formations"): each a cluster of glowing hexagonal
    /// spires from <see cref="CrystalKit.Formation"/>, no vendor model, the tallest its <see cref="Spot.HeightM"/> and
    /// their bases within its footprint, cut as they are built into the shards a shatter flies when a blow smashes one.
    /// They draw in the material given: a structure look's (<see cref="StructureLook"/>) or the caller's own. A formation
    /// stands only while its shot is on screen (<see cref="Show"/>).
    /// </summary>
    public sealed class CrystalFormations : IDisposable
    {
        /// <summary>The formations' gem colour before the palettes (sRGB #ffe7b0, linear): their tint and their break's bursts outside a structure look.</summary>
        public static Color Gem => ColorUtility.TryParseHtmlString("#ffe7b0", out var c) ? c.linear : Color.white;

        /// <summary>Where one formation stands: its placement's id, its centre on the ground (x, z), its footprint's radius and its height (m).</summary>
        public struct Spot
        {
            public int Placement;
            public string Name;
            public Vector2 Position;
            public float RadiusM, HeightM;
        }

        /// <summary>One formation: its spot, its renderer while it stands and the shards it breaks into.</summary>
        public sealed class Formation
        {
            public Spot Spot;
            public MeshRenderer Renderer;
            public StructureShards.Set Set;
        }

        readonly GameObject _root;
        readonly List<StructureShards.Set> _owned = new List<StructureShards.Set>();
        readonly Dictionary<int, Formation> _byPlacement = new Dictionary<int, Formation>();

        public IReadOnlyCollection<Formation> All => _byPlacement.Values;

        static readonly int PieceId = Shader.PropertyToID("_SL_Piece");
        static readonly int PieceFxId = Shader.PropertyToID("_SL_PieceFx");
        static readonly int HeightId = Shader.PropertyToID("_HeightM");

        /// <summary>The formations of <paramref name="spots"/>, hidden until <see cref="Show"/>, under <paramref name="parent"/>, in <paramref name="material"/>.</summary>
        public CrystalFormations(IReadOnlyList<Spot> spots, Transform parent, Material material)
        {
            _root = new GameObject("CrystalFormations");
            _root.transform.SetParent(parent, false);
            _root.SetActive(false);
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < spots.Count; i++)
            {
                var p = spots[i];
                int seed = Mathf.RoundToInt(p.Position.x * 73.1f) * 7919 + Mathf.RoundToInt(p.Position.y * 37.9f) * 104729 + i;
                var set = CrystalKit.Formation(p.RadiusM, p.HeightM, seed);
                set.Source.name = p.Name;
                _owned.Add(set);
                var go = new GameObject(p.Name);
                go.transform.SetParent(_root.transform, false);
                go.transform.SetPositionAndRotation(new Vector3(p.Position.x, 0f, p.Position.y), Quaternion.Euler(0f, (seed & 0xffff) % 360, 0f));
                go.AddComponent<MeshFilter>().sharedMesh = set.Source;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                var b = r.bounds;
                block.Clear();
                block.SetVector(PieceId, new Vector4(b.center.x, 0f, b.center.z, Mathf.Repeat(seed * 0.618034f, 1f)));
                block.SetVector(PieceFxId, Vector4.zero);
                block.SetFloat(HeightId, p.HeightM);
                r.SetPropertyBlock(block);
                _byPlacement[p.Placement] = new Formation { Spot = p, Renderer = r, Set = set };
            }
        }

        /// <summary>The formation at <paramref name="placement"/>, or null if none stands there.</summary>
        public Formation Of(int placement) => _byPlacement.TryGetValue(placement, out var f) ? f : null;

        /// <summary>Shows the formations (their shot is on screen) or hides them.</summary>
        public void Show(bool on)
        {
            if (_root.activeSelf != on) _root.SetActive(on);
        }

        public void Dispose()
        {
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
                else UnityEngine.Object.DestroyImmediate(_root);
            }
            foreach (var set in _owned) CrystalKit.Destroy(set);
            _owned.Clear();
        }
    }
}
