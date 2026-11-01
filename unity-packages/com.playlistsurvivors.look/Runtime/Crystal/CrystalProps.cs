using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PS.Look
{
    /// <summary>
    /// The void world's structures on the ground: each structure site a set of crystal kit pieces standing on its tiles
    /// (<see cref="CrystalKit.Fill"/>) in a structure look's material, with their shards. <see cref="Sites"/> and
    /// <see cref="Sets"/> are what <see cref="StructureShatter.Attach"/> takes, in the order the sites were added. Each
    /// renderer carries the piece values the shatter and the shader read (_SL_Piece: its base and seed; _HeightM).
    /// It owns what it makes.
    /// </summary>
    public sealed class CrystalProps : IDisposable
    {
        static readonly int PieceId = Shader.PropertyToID("_SL_Piece");
        static readonly int PieceFxId = Shader.PropertyToID("_SL_PieceFx");
        static readonly int TintId = Shader.PropertyToID("_SL_Tint");
        static readonly int HeightId = Shader.PropertyToID("_HeightM");

        readonly GameObject _root;
        readonly Material _material;
        readonly int _layer;
        readonly List<StructureShards.Set> _owned = new List<StructureShards.Set>();
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        /// <summary>Each site's renderers, one per piece, and the shards of each, by the site's index.</summary>
        public readonly List<Renderer[]> Sites = new List<Renderer[]>();
        public readonly List<StructureShards.Set[]> Sets = new List<StructureShards.Set[]>();

        /// <summary>Every piece's renderer.</summary>
        public readonly List<Renderer> Renderers = new List<Renderer>();

        /// <summary>Props under <paramref name="parent"/> in <paramref name="material"/>, on <paramref name="layer"/> (a resident scene's).</summary>
        public CrystalProps(Transform parent, Material material, int layer = 0)
        {
            _layer = layer;
            _root = new GameObject("CrystalProps") { layer = layer };
            if (parent != null) _root.transform.SetParent(parent, false);
            _material = material;
        }

        /// <summary>
        /// Adds a site standing on <paramref name="tiles"/> (their centres on the ground, metres, a tile
        /// <paramref name="tileM"/> square), as <see cref="CrystalKit.Fill"/> fits it, and returns its index.
        /// </summary>
        public int Add(IReadOnlyList<Vector2> tiles, float tileM, bool large, int seed)
        {
            var placed = CrystalKit.Fill(tiles, tileM, large, seed);
            var site = new GameObject("Site " + Sites.Count) { layer = _layer };
            site.transform.SetParent(_root.transform, false);
            var renderers = new Renderer[placed.Count];
            var sets = new StructureShards.Set[placed.Count];
            for (int i = 0; i < placed.Count; i++)
            {
                var p = placed[i];
                _owned.Add(p.Set);
                var go = new GameObject(p.Set.Source.name) { layer = _layer };
                go.transform.SetParent(site.transform, false);
                go.transform.SetPositionAndRotation(p.Position, Quaternion.Euler(0f, p.YawDeg, 0f));
                go.AddComponent<MeshFilter>().sharedMesh = p.Set.Source;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                var b = r.bounds;
                _block.Clear();
                _block.SetVector(PieceId, new Vector4(b.center.x, 0f, b.center.z, Mathf.Repeat((seed + i) * 0.618034f, 1f)));
                _block.SetVector(PieceFxId, Vector4.zero);
                _block.SetVector(TintId, Vector4.zero); // the material's gem
                _block.SetFloat(HeightId, Mathf.Max(p.HeightM, 0.1f));
                r.SetPropertyBlock(_block);
                renderers[i] = r;
                sets[i] = p.Set;
                Renderers.Add(r);
            }
            Sites.Add(renderers);
            Sets.Add(sets);
            return Sites.Count - 1;
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
            Sites.Clear();
            Sets.Clear();
            Renderers.Clear();
        }
    }
}
