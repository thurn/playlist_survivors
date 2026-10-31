using UnityEngine;

namespace Game.Client.Arena
{
    /// <summary>
    /// The arena's own animation clock (the rune circles and mist cards), published each frame before
    /// rendering as the shader global <c>_GameTime</c>: Unity's time, which `--fixed-time` steps
    /// exactly. In fixed time it also pins every arena particle system (the kit's candle flames) to a
    /// seed from its position and steps it by that time, so two captures of one frame match.
    /// </summary>
    public sealed class ArenaClock : MonoBehaviour
    {
        static readonly int IdTime = Shader.PropertyToID("_GameTime");

        /// <summary>True in fixed-time runs: particle systems are reseeded and stepped by the clock.</summary>
        public static bool Deterministic;

        ParticleSystem[] _particles;
        double _last = double.NaN;

        void Start()
        {
            _particles = GetComponentsInChildren<ParticleSystem>(true);
            if (!Deterministic) return;
            for (int i = 0; i < _particles.Length; i++)
            {
                var ps = _particles[i];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false;
                var p = ps.transform.position;
                ps.randomSeed = (uint)(Mathf.Abs(p.x * 73.1f + p.z * 19.7f + i) * 977f);
                ps.Pause(true); // stepped by hand below
            }
        }

        void LateUpdate()
        {
            double now = Time.timeAsDouble;
            Shader.SetGlobalFloat(IdTime, (float)now);
            if (!Deterministic || _particles == null) return;
            // Each system alone (withChildren: false): _particles already holds every nested one.
            if (double.IsNaN(_last) || now < _last)
                foreach (var ps in _particles) ps.Simulate((float)now, false, true, true);
            else
                foreach (var ps in _particles) ps.Simulate((float)(now - _last), false, false, true);
            _last = now;
        }
    }
}
