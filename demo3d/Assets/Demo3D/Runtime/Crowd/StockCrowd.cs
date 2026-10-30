using System.Collections.Generic;
using Demo3D.Runtime.Playback;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The stock baseline (crowd.md, Stock baseline): one GameObject per monster with an
    /// Animator and a SkinnedMeshRenderer (Unity's compute skinning, shadows and skinned motion
    /// vectors on). Each monster's Animator plays a playable graph whose two clip slots follow
    /// the instance's clips and times exactly, with the playables' speed at zero, so Unity's own
    /// animation update evaluates them (on its worker threads) at the timeline's times. That
    /// replaces CrossFade calls, which advance on game time and would drift from the song.
    /// Hit flash and tint go through a MaterialPropertyBlock; stock URP Lit has no dissolve, so
    /// a dissolving monster scales away instead. With <c>vendorMaterials</c> (a `--roster` set)
    /// the prefabs' materials draw as they are: no tint overrides their base colour, and the
    /// flash sets emission only while it lasts, so a pack's glow map shows the rest of the time.
    /// </summary>
    public sealed class StockCrowd : ICrowdRenderer
    {
        sealed class Slot
        {
            public GameObject Go;
            public PlayableGraph Graph;
            public AnimationMixerPlayable Mixer;
            public AnimationClipPlayable[] Clips;
            public Renderer[] Renderers;
            public Vector3 BaseScale;
            public uint LastA = uint.MaxValue, LastB = uint.MaxValue;
            public bool Fresh = true;
        }

        sealed class TypeState
        {
            public RosterAssets.TypeAssets Assets;
            public readonly List<Slot> Slots = new List<Slot>();
            public NativeArray<MonsterInstance> Instances;
            public int Count;
        }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly bool _vendorMaterials;
        TypeState[] _types;
        Transform _root;
        CrowdCounters _counters;

        public CrowdCounters Counters => _counters;

        public StockCrowd(bool vendorMaterials = false) { _vendorMaterials = vendorMaterials; }

        public void Initialize(string[] typeIds, int capacityPerType)
        {
            var roster = RosterAssets.Load();
            _root = new GameObject("StockCrowd").transform;
            _types = new TypeState[typeIds.Length];
            for (int i = 0; i < typeIds.Length; i++)
            {
                _types[i] = new TypeState
                {
                    Assets = roster.Find(typeIds[i]) ?? throw new System.ArgumentException("StockCrowd: no type " + typeIds[i]),
                    Instances = new NativeArray<MonsterInstance>(capacityPerType, Allocator.Persistent),
                };
            }
        }

        public NativeArray<MonsterInstance> Instances(int type, int count)
        {
            var t = _types[type];
            if (count > t.Instances.Length)
            {
                t.Instances.Dispose();
                t.Instances = new NativeArray<MonsterInstance>(count, Allocator.Persistent);
            }
            t.Count = count;
            return t.Instances.GetSubArray(0, count);
        }

        Slot Create(TypeState t)
        {
            var go = Object.Instantiate(t.Assets.prefab, _root);
            var animator = go.GetComponentInChildren<Animator>();
            var slot = new Slot { Go = go, Renderers = go.GetComponentsInChildren<Renderer>(), BaseScale = go.transform.localScale };
            foreach (var r in slot.Renderers)
                if (r is SkinnedMeshRenderer smr) { smr.skinnedMotionVectors = true; smr.updateWhenOffscreen = false; }
            slot.Graph = PlayableGraph.Create("stock");
            slot.Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(slot.Graph, "out", animator);
            slot.Mixer = AnimationMixerPlayable.Create(slot.Graph, t.Assets.clips.Length);
            slot.Clips = new AnimationClipPlayable[t.Assets.clips.Length];
            for (int c = 0; c < t.Assets.clips.Length; c++)
            {
                var p = AnimationClipPlayable.Create(slot.Graph, t.Assets.clips[c]);
                p.SetApplyFootIK(false);
                p.SetSpeed(0);
                slot.Graph.Connect(p, 0, slot.Mixer, c);
                slot.Mixer.SetInputWeight(c, 0);
                slot.Clips[c] = p;
            }
            output.SetSourcePlayable(slot.Mixer);
            slot.Graph.Play();
            return slot;
        }

        public void Draw(Camera camera)
        {
            int total = 0;
            long verts = 0;
            for (int ti = 0; ti < _types.Length; ti++)
            {
                var t = _types[ti];
                while (t.Slots.Count < t.Count) t.Slots.Add(Create(t));
                for (int i = 0; i < t.Slots.Count; i++)
                {
                    var s = t.Slots[i];
                    bool active = i < t.Count && (t.Instances[i].Flags & (uint)CrowdFlags.Hidden) == 0;
                    if (s.Go.activeSelf != active)
                    {
                        s.Go.SetActive(active);
                        s.Fresh |= active;
                    }
                    if (!active) continue;
                    Apply(s, t.Instances[i]);
                    if (s.Fresh)
                    {
                        // A new monster's graph is first evaluated on the next animation update,
                        // which would show its bind pose for a frame (after a seek, every monster).
                        s.Graph.Evaluate(0);
                        s.Fresh = false;
                    }
                    total++;
                }
                foreach (var r in t.Assets.prefab.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (r.sharedMesh != null) verts += (long)r.sharedMesh.vertexCount * t.Count;
            }
            _counters = new CrowdCounters { Instances = total, Visible = total, SkinnedVertices = verts, Draws = total, ShadowCasters = total };
        }

        void Apply(Slot s, in MonsterInstance m)
        {
            var tr = s.Go.transform;
            float dissolveShrink = 1f - Mathf.Clamp01(m.Dissolve);
            tr.SetPositionAndRotation(m.Position, Quaternion.Euler(0, m.Yaw * Mathf.Rad2Deg, 0));
            tr.localScale = s.BaseScale * (m.Scale <= 0 ? 1f : m.Scale) * dissolveShrink;
            int n = s.Clips.Length;
            uint a = m.ClipA < n ? m.ClipA : 0, b = m.ClipB < n ? m.ClipB : a;
            if (a != s.LastA || b != s.LastB)
            {
                if (s.LastA < n) s.Mixer.SetInputWeight((int)s.LastA, 0);
                if (s.LastB < n) s.Mixer.SetInputWeight((int)s.LastB, 0);
                s.LastA = a;
                s.LastB = b;
            }
            float blend = a == b ? 1f : Mathf.Clamp01(m.Blend);
            s.Mixer.SetInputWeight((int)a, blend);
            if (b != a) s.Mixer.SetInputWeight((int)b, 1f - blend);
            s.Clips[a].SetTime(m.TimeA);
            if (b != a) s.Clips[b].SetTime(m.TimeB);

            float flash = Mathf.Clamp01(m.HitFlash);
            if (_vendorMaterials)
            {
                _block.Clear();
                if (flash > 0) _block.SetColor(Emission, new Color(flash, flash, flash) * 2f);
                foreach (var r in s.Renderers) r.SetPropertyBlock(flash > 0 ? _block : null);
                return;
            }
            var tint = m.Tint == 0 ? Color.white : UnpackRgba(m.Tint);
            _block.Clear();
            _block.SetColor(BaseColor, tint);
            _block.SetColor(Emission, new Color(flash, flash, flash) * 2f);
            foreach (var r in s.Renderers) r.SetPropertyBlock(_block);
        }

        static Color UnpackRgba(uint c) =>
            new Color((c >> 24 & 0xFF) / 255f, (c >> 16 & 0xFF) / 255f, (c >> 8 & 0xFF) / 255f, (c & 0xFF) / 255f);

        public void Dispose()
        {
            if (_types == null) return;
            foreach (var t in _types)
            {
                foreach (var s in t.Slots)
                {
                    if (s.Graph.IsValid()) s.Graph.Destroy();
                    if (s.Go != null) Object.Destroy(s.Go);
                }
                if (t.Instances.IsCreated) t.Instances.Dispose();
            }
            if (_root != null) Object.Destroy(_root.gameObject);
            _types = null;
        }
    }
}
