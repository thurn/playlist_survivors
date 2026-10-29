#if DEMO3D_VFX
using Demo3D.Runtime.Effects;
using UnityEngine;
using UnityEngine.VFX;

namespace Demo3D.Runtime.VfxSpike
{
    /// <summary>
    /// TD8's runtime spike, VFX Graph arm: one VisualEffect running the spike template
    /// (Editor/Effects/VfxSpikeTemplate.cs), and each spawn sent as an OnPlay event whose
    /// position attribute places its burst. Compiled only while com.unity.visualeffectgraph is in
    /// the project.
    /// </summary>
    public sealed class VfxSpikeSink : ISpawnSink
    {
        readonly VisualEffect _vfx;
        readonly VFXEventAttribute _attr;
        readonly int _onPlay = Shader.PropertyToID("OnPlay"), _position = Shader.PropertyToID("position");
        readonly System.Collections.Generic.List<Vector3> _pending = new System.Collections.Generic.List<Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => SpawnSinks.Register("vfx", (lib, cam) => new VfxSpikeSink());

        VfxSpikeSink()
        {
            var asset = Resources.Load<VisualEffectAsset>("SpikeBurst")
                ?? throw new System.InvalidOperationException("VfxSpikeSink: Assets/Baked/Spike/Resources/SpikeBurst.vfx is missing (demo3d unity VfxSpikeTemplate)");
            var go = new GameObject("VfxSpike");
            _vfx = go.AddComponent<VisualEffect>();
            _vfx.visualEffectAsset = asset;
            _vfx.initialEventName = "";
            _attr = _vfx.CreateVFXEventAttribute();
        }

        public bool Add(in EffectSystem.Spawn s)
        {
            _pending.Add(s.Position);
            return true;
        }

        public void Reset(double origin)
        {
            _pending.Clear();
            _vfx.Reinit();
        }

        public void Update(double now, float dt, Camera camera)
        {
            foreach (var p in _pending)
            {
                _attr.SetVector3(_position, p);
                _vfx.SendEvent(_onPlay, _attr);
            }
            _pending.Clear();
        }

        public int Particles => _vfx.aliveParticleCount;

        public void Dispose()
        {
            if (_vfx != null) Object.Destroy(_vfx.gameObject);
        }
    }
}
#endif
