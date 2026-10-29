using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Effects
{
    /// <summary>
    /// Effect lights (effects.md, Effect lights): each spawned effect's light cue lives for its
    /// duration; every frame the active cues are clustered on a 1.5 m world grid (intensities
    /// summed, the position their intensity-weighted centroid, the range the largest), clusters
    /// whose range sphere misses the view are culled, and the pooled unshadowed point lights take
    /// the strongest of the rest by screen contribution (intensity times the screen area of the
    /// range sphere). The rest are dropped, weakest first, and their share of the frame's visible
    /// cue intensity is recorded.
    /// </summary>
    public sealed class EffectLights : IDisposable
    {
        public const int DefaultPool = 128;
        public const float DefaultCell = 1.5f;
        /// <summary>The pass rule's limit on the p99 of the dropped share (effects.md, Drops).</summary>
        public const double DropLimit = 0.02;

        struct Cue
        {
            public Vector3 Position, Velocity;
            public Color Color;
            public float Range, Start, Scale;
            public LightCue Source;
        }

        struct Cluster
        {
            public Vector3 Weighted;
            public Vector3 Color;
            public float Intensity, Range, Score;
            public Vector3 Position => Intensity > 0 ? Weighted / Intensity : Weighted;
        }

        readonly List<Cue> _cues = new List<Cue>(4096);
        readonly Dictionary<long, int> _cells = new Dictionary<long, int>(4096);
        Cluster[] _clusters = new Cluster[1024];
        int[] _order = new int[1024];
        float[] _keys = new float[1024];
        readonly Plane[] _planes = new Plane[6];
        readonly Light[] _pool;
        readonly GameObject _root;
        double _origin;

        public float Cell = DefaultCell;
        public int PoolSize => _pool.Length;
        /// <summary>Active cues, visible clusters, lights used and the dropped share of visible intensity, this frame.</summary>
        public int ActiveCues { get; private set; }
        public int Clusters { get; private set; }
        public int VisibleClusters { get; private set; }
        public int LightsUsed { get; private set; }
        public double DropShare { get; private set; }
        /// <summary>Global multiplier on cue intensities (URP's point light units).</summary>
        public float IntensityScale = 1f;

        public EffectLights(int poolSize)
        {
            _root = new GameObject("EffectLights");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_root);
            // URP 17.6's Forward+ limit on visible additional lights (256 on desktop, ShaderConfig.cs).
            int limit = UnityEngine.Rendering.Universal.UniversalRenderPipeline.maxVisibleAdditionalLights;
            Debug.Log($"EffectLights: pool {poolSize}, Forward+ visible additional light limit {limit}");
            if (poolSize > limit) Debug.LogWarning($"EffectLights: the pool ({poolSize}) exceeds the Forward+ limit ({limit}); URP drops the excess");
            _pool = new Light[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject("EffectLight" + i);
                go.transform.SetParent(_root.transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                l.enabled = false;
                _pool[i] = l;
            }
        }

        public void Clear()
        {
            _cues.Clear();
            foreach (var l in _pool) l.enabled = false;
            LightsUsed = 0;
        }

        public void Add(Vector3 position, in LightCue cue, double time, float scale, Vector3 velocity)
        {
            _cues.Add(new Cue
            {
                Position = position, Velocity = velocity, Color = cue.Color, Range = cue.Range * Mathf.Sqrt(Mathf.Max(0.05f, scale)),
                Start = (float)(time - _origin), Scale = scale, Source = cue,
            });
        }

        public void Update(double now, Camera camera)
        {
            float t = (float)(now - _origin);
            // Evaluate and expire cues, clustering the live ones.
            _cells.Clear();
            int n = 0;
            int w = 0;
            for (int i = 0; i < _cues.Count; i++)
            {
                var c = _cues[i];
                float age = t - c.Start;
                if (age >= c.Source.Duration) continue;
                _cues[w++] = c;
                if (age < 0) continue;
                float intensity = c.Source.At(age) * IntensityScale;
                if (intensity <= 0) continue;
                Vector3 p = c.Position + c.Velocity * age;
                long key = Key(p);
                if (!_cells.TryGetValue(key, out int k))
                {
                    if (n == _clusters.Length) Grow();
                    k = n++;
                    _cells[key] = k;
                    _clusters[k] = default;
                }
                ref var cl = ref _clusters[k];
                cl.Intensity += intensity;
                cl.Weighted += p * intensity;
                cl.Color += new Vector3(c.Color.r, c.Color.g, c.Color.b) * intensity;
                cl.Range = Mathf.Max(cl.Range, c.Range);
            }
            _cues.RemoveRange(w, _cues.Count - w);
            ActiveCues = w;
            Clusters = n;

            // Cull by the view, score by screen contribution.
            int visible = 0;
            double total = 0;
            if (camera != null)
            {
                GeometryUtility.CalculateFrustumPlanes(camera, _planes);
                var camPos = camera.transform.position;
                float tanHalf = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                for (int k = 0; k < n; k++)
                {
                    ref var cl = ref _clusters[k];
                    var p = cl.Position;
                    if (!SphereVisible(p, cl.Range)) continue;
                    float dist = Mathf.Max(0.01f, Vector3.Distance(p, camPos));
                    float rScreen = cl.Range / (dist * tanHalf); // fraction of the half view height
                    float area = Mathf.Min(1f, rScreen * rScreen);
                    cl.Score = cl.Intensity * area;
                    _order[visible] = k;
                    _keys[visible] = -cl.Score;
                    visible++;
                    total += cl.Intensity;
                }
            }
            VisibleClusters = visible;
            Array.Sort(_keys, _order, 0, visible);
            int used = Mathf.Min(visible, _pool.Length);
            double dropped = 0;
            for (int i = used; i < visible; i++) dropped += _clusters[_order[i]].Intensity;
            DropShare = total > 0 ? dropped / total : 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                var l = _pool[i];
                if (i >= used)
                {
                    if (l.enabled) l.enabled = false;
                    continue;
                }
                ref var cl = ref _clusters[_order[i]];
                var col = cl.Color / Mathf.Max(1e-5f, cl.Intensity);
                l.transform.position = cl.Position;
                l.color = new Color(col.x, col.y, col.z);
                l.intensity = cl.Intensity;
                l.range = cl.Range;
                if (!l.enabled) l.enabled = true;
            }
            LightsUsed = used;
        }

        bool SphereVisible(Vector3 p, float r)
        {
            for (int i = 0; i < 6; i++)
                if (_planes[i].GetDistanceToPoint(p) < -r) return false;
            return true;
        }

        long Key(Vector3 p)
        {
            long x = Mathf.FloorToInt(p.x / Cell) & 0x1FFFFF, y = Mathf.FloorToInt(p.y / Cell) & 0x1FFFFF, z = Mathf.FloorToInt(p.z / Cell) & 0x1FFFFF;
            return (x << 42) | (y << 21) | z;
        }

        void Grow()
        {
            Array.Resize(ref _clusters, _clusters.Length * 2);
            Array.Resize(ref _order, _clusters.Length);
            Array.Resize(ref _keys, _clusters.Length);
        }

        public void Dispose()
        {
            if (_root == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root); else UnityEngine.Object.DestroyImmediate(_root);
        }
    }
}
