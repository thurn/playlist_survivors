using UnityEngine;
using UnityEngine.Rendering;

namespace StyleLab.Runtime
{
    /// <summary>
    /// One drawn character: the vendor rig, its own renderers hidden, drawing the baked merged
    /// style mesh with a treatment's materials; a shell renderer (a second skinned copy, for the
    /// fire's flames or the painted ink line); and its per-character shader values.
    /// </summary>
    public sealed class Figure
    {
        static readonly int HitId = Shader.PropertyToID("_Hit");
        static readonly int DeathId = Shader.PropertyToID("_Death");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int CenterId = Shader.PropertyToID("_Center");
        static readonly int HeightId = Shader.PropertyToID("_HeightM");

        public readonly TypeData Type;
        public readonly GameObject Root;
        public readonly Animator Animator;
        public Style Style { get; private set; }
        readonly SkinnedMeshRenderer _body, _shell;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly float _seed;

        public Figure(TypeData type, Transform parent, float seed)
        {
            Type = type;
            _seed = seed;
            Root = Rig.Spawn(type.prefab, type.weapons, parent);
            Animator = Root.GetComponentInChildren<Animator>();
            if (Animator != null) Animator.runtimeAnimatorController = type.controller;
            foreach (var r in Rig.Renderers(Root)) r.enabled = false;
            var bones = new Transform[type.bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = type.bones[i].Length == 0 ? Root.transform : Root.transform.Find(type.bones[i]);
                if (bones[i] == null) Debug.LogWarning($"Figure: {type.id} has no bone {type.bones[i]}");
            }
            _body = MakeRenderer("Body", bones, ShadowCastingMode.On);
            _shell = MakeRenderer("Shell", bones, ShadowCastingMode.Off);
            _shell.enabled = false;
            if (type.nativeHeight > 0.01f) Root.transform.localScale *= type.heightM / type.nativeHeight;
        }

        SkinnedMeshRenderer MakeRenderer(string name, Transform[] bones, ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            var r = go.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = Type.mesh;
            r.bones = bones;
            r.rootBone = Root.transform;
            r.updateWhenOffscreen = true;
            r.shadowCastingMode = shadows;
            return r;
        }

        public void Apply(Style style, MaterialBank bank)
        {
            Style = style;
            var mats = new Material[Type.albedo.Length];
            for (int k = 0; k < mats.Length; k++) mats[k] = bank.Body(style, Type, Type.albedo[k]);
            _body.sharedMaterials = mats;
            _body.shadowCastingMode = style == Style.Hologram ? ShadowCastingMode.Off : ShadowCastingMode.On;
            var shellMat = bank.Shell(style, Type);
            _shell.enabled = shellMat != null;
            if (shellMat != null)
            {
                var shellMats = new Material[mats.Length];
                for (int k = 0; k < shellMats.Length; k++) shellMats[k] = shellMat;
                _shell.sharedMaterials = shellMats;
            }
        }

        public void SetEffects(float hit, float death, Vector3 center)
        {
            _block.SetFloat(HitId, hit);
            _block.SetFloat(DeathId, death);
            _block.SetFloat(SeedId, _seed);
            _block.SetVector(CenterId, center);
            _block.SetFloat(HeightId, Type.heightM);
            _body.SetPropertyBlock(_block);
            if (_shell.enabled) _shell.SetPropertyBlock(_block);
        }

        public void SetVisible(bool visible)
        {
            if (Root.activeSelf != visible) Root.SetActive(visible);
        }
    }
}
