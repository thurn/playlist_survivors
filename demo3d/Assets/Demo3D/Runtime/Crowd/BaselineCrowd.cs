using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// TD5's minimal stock baseline (crowd.md, Stock baseline), behind the same contract: one
    /// GameObject per monster from the baker's stock prefab, with an Animator driven by
    /// CrossFadeInFixedTime when a monster's clip changes, and one SkinnedMeshRenderer (the
    /// combined mesh, Unity's GPU skinning, shadows on). Transforms are written by a Burst
    /// transform job; hit flash is a property block on the renderer while it flashes. URP Lit has
    /// no dissolve, so a dissolving monster hides at half-way. Biases against the bone-texture
    /// path: it ignores ClipB, TimeB and Blend (the Animator crossfades on its own from the clip
    /// change, then plays at its own rate) and Tint (a property block per renderer would break
    /// batching). TD4's StockCrowd is the client's timeline-exact stock crowd; this is "baseline".
    /// Hidden slots (stable-slot gaps) hide their monster and restart it for the next owner.
    /// </summary>
    public sealed class BaselineCrowd : ICrowdRenderer
    {
        public float CrossFadeSeconds = 0.2f;

        static readonly int IdEmission = Shader.PropertyToID("_EmissionColor");

        readonly CrowdLibrary _lib;
        readonly Transform _root;
        readonly bool _ownsRoot;
        readonly float4[] _planes = new float4[6];
        readonly Plane[] _planeScratch = new Plane[6];
        TypeState[] _types = Array.Empty<TypeState>();
        CrowdCounters _counters;
        int _capacity;
        MaterialPropertyBlock _flash;

        public CrowdCounters Counters => _counters;
        public int OnScreenAlive { get; private set; }
        public int OnScreenDying { get; private set; }

        sealed class Entity
        {
            public GameObject Go;
            public Animator Animator;
            public SkinnedMeshRenderer Renderer;
            public uint Clip = uint.MaxValue;
            public bool Flashing, Visible = true;
        }

        sealed class TypeState
        {
            public CrowdType Type;
            public int[] StateHashes;
            public float3[] Spheres;
            public readonly List<Entity> Pool = new List<Entity>();
            public NativeArray<MonsterInstance> Data;
            public TransformAccessArray Transforms;
            public int Count;
            public bool TransformsDirty = true;
        }

        /// <summary>Without a root, it makes its own and destroys it on Dispose.</summary>
        public BaselineCrowd(CrowdLibrary lib, Transform root = null)
        {
            _lib = lib;
            _ownsRoot = root == null;
            _root = root != null ? root : new GameObject("BaselineCrowd").transform;
        }

        public void Initialize(string[] typeIds, int capacityPerType)
        {
            Clear();
            _capacity = capacityPerType;
            _flash = new MaterialPropertyBlock();
            _types = new TypeState[typeIds.Length];
            for (int i = 0; i < typeIds.Length; i++)
            {
                int at = _lib.IndexOf(typeIds[i]);
                if (at < 0) throw new ArgumentException($"BaselineCrowd: no baked type '{typeIds[i]}'");
                var t = _lib.Types[at];
                if (t.StockPrefab == null) throw new InvalidOperationException($"BaselineCrowd: {t.Id} has no stock prefab");
                var s = new TypeState
                {
                    Type = t,
                    StateHashes = new int[t.Clips.Length],
                    Data = new NativeArray<MonsterInstance>(capacityPerType, Allocator.Persistent),
                };
                for (int c = 0; c < t.Clips.Length; c++) s.StateHashes[c] = Animator.StringToHash(t.Clips[c].Role);
                s.Spheres = t.Clips.Select(c => new float3(c.CenterY, c.Radius, c.HeightM)).ToArray();
                _types[i] = s;
            }
        }

        public NativeArray<MonsterInstance> Instances(int type, int count)
        {
            var s = _types[type];
            if (count > _capacity) throw new ArgumentOutOfRangeException(nameof(count));
            while (s.Pool.Count < count)
            {
                var go = UnityEngine.Object.Instantiate(s.Type.StockPrefab, _root);
                s.Pool.Add(new Entity
                {
                    Go = go, Animator = go.GetComponentInChildren<Animator>(), Renderer = go.GetComponentInChildren<SkinnedMeshRenderer>(),
                });
                s.TransformsDirty = true;
            }
            for (int i = count; i < s.Count; i++) s.Pool[i].Go.SetActive(false);
            for (int i = s.Count; i < count; i++)
            {
                s.Pool[i].Go.SetActive(true);
                s.Pool[i].Clip = uint.MaxValue;
            }
            if (count != s.Count) s.TransformsDirty = true;
            s.Count = count;
            return s.Data.GetSubArray(0, count);
        }

        [BurstCompile]
        struct PlaceJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<MonsterInstance> Data;

            public void Execute(int i, TransformAccess t)
            {
                var m = Data[i];
                t.SetPositionAndRotation(m.Position, quaternion.RotateY(m.Yaw));
                t.localScale = new float3(m.Scale);
            }
        }

        public void Draw(Camera camera)
        {
            CrowdCulling.Planes(camera, _planeScratch, _planes);
            var handles = new NativeArray<JobHandle>(_types.Length, Allocator.Temp);
            for (int ti = 0; ti < _types.Length; ti++)
            {
                var s = _types[ti];
                if (s.TransformsDirty)
                {
                    if (s.Transforms.isCreated) s.Transforms.Dispose();
                    var list = new Transform[s.Count];
                    for (int i = 0; i < s.Count; i++) list[i] = s.Pool[i].Go.transform;
                    s.Transforms = new TransformAccessArray(list);
                    s.TransformsDirty = false;
                }
                handles[ti] = new PlaceJob { Data = s.Data }.Schedule(s.Transforms);
            }
            JobHandle.ScheduleBatchedJobs();

            int alive = 0, dying = 0, instances = 0, casters = 0;
            long skinned = 0;
            var white = new Color(2.5f, 2.4f, 2.2f);
            foreach (var s in _types)
            {
                instances += s.Count;
                int visible = 0;
                for (int i = 0; i < s.Count; i++)
                {
                    var m = s.Data[i];
                    var e = s.Pool[i];
                    bool show = (m.Flags & (uint)CrowdFlags.Hidden) == 0 && m.Dissolve < 0.5f;
                    if (show != e.Visible)
                    {
                        e.Renderer.enabled = show;
                        e.Visible = show;
                    }
                    if ((m.Flags & (uint)CrowdFlags.Hidden) != 0)
                    {
                        e.Clip = uint.MaxValue; // the slot's next owner starts afresh
                        continue;
                    }
                    if (m.ClipA != e.Clip && m.ClipA < s.StateHashes.Length)
                    {
                        // A new clip starts: crossfade into it at its time.
                        if (e.Clip == uint.MaxValue) e.Animator.PlayInFixedTime(s.StateHashes[m.ClipA], 0, m.TimeA);
                        else e.Animator.CrossFadeInFixedTime(s.StateHashes[m.ClipA], CrossFadeSeconds, 0, m.TimeA);
                        e.Clip = m.ClipA;
                    }
                    bool flash = m.HitFlash > 0.001f;
                    if (flash || e.Flashing)
                    {
                        if (flash)
                        {
                            _flash.SetColor(IdEmission, white * m.HitFlash);
                            e.Renderer.SetPropertyBlock(_flash);
                        }
                        else e.Renderer.SetPropertyBlock(null);
                        e.Flashing = flash;
                    }
                    if (!show) continue;
                    casters++;
                    var sphere = CrowdCulling.ClipSphere(s.Spheres[m.ClipA], s.Spheres[m.ClipB], m.Blend);
                    float3 c = (float3)m.Position + new float3(0, sphere.x * m.Scale, 0);
                    if (CrowdCulling.InsideSwept(_planes, c, float3.zero, sphere.y * m.Scale))
                    {
                        visible++;
                        if ((m.Flags & (uint)CrowdFlags.Dying) != 0) dying++;
                        else alive++;
                    }
                }
                skinned += (long)visible * s.Type.Vertices;
            }
            JobHandle.CompleteAll(handles);
            handles.Dispose();
            OnScreenAlive = alive;
            OnScreenDying = dying;
            _counters = new CrowdCounters
            {
                Instances = instances, Visible = alive + dying, SkinnedVertices = skinned, Draws = casters, ShadowCasters = casters,
            };
        }

        public void Dispose()
        {
            Clear();
            if (_ownsRoot && _root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
                else UnityEngine.Object.DestroyImmediate(_root.gameObject);
            }
        }

        void Clear()
        {
            foreach (var s in _types)
            {
                if (s.Transforms.isCreated) s.Transforms.Dispose();
                if (s.Data.IsCreated) s.Data.Dispose();
                foreach (var e in s.Pool)
                    if (e.Go != null)
                    {
                        if (Application.isPlaying) UnityEngine.Object.Destroy(e.Go);
                        else UnityEngine.Object.DestroyImmediate(e.Go);
                    }
            }
            _types = Array.Empty<TypeState>();
        }
    }
}
