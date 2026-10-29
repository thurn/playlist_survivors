using UnityEngine;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The arena's own animation clock (flame flicker, the rune circle, mist cards): Time.time,
    /// unless the demo pins it to song time (<see cref="Override"/>), so fixed-time captures of the
    /// same song time come out the same (TD11's A/B checks: Unity's Time.time and the shaders' _Time
    /// follow wall time, which made two runs of one capture differ). Published each frame, before
    /// rendering, as the shader global <c>_Demo3DTime</c>. In fixed time the demo also pins every
    /// arena particle system to a fixed seed and steps it by song time.
    /// </summary>
    public sealed class ArenaClock : MonoBehaviour
    {
        static readonly int IdTime = Shader.PropertyToID("_Demo3DTime");

        /// <summary>Song time while the demo drives the arena; null for Time.time.</summary>
        public static double? Override;

        /// <summary>True in fixed-time runs: particle systems are reseeded and stepped by the clock.</summary>
        public static bool Deterministic;

        public static float Now => Override.HasValue ? (float)Override.Value : Time.time;

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
            Shader.SetGlobalFloat(IdTime, Now);
            if (!Deterministic || _particles == null) return;
            double now = Now;
            if (double.IsNaN(_last) || now < _last)
            {
                // From the clock's time, so a run that starts at song time t matches any other. Each system
                // is stepped alone (withChildren: false): _particles already holds every nested system, so
                // stepping with children stepped a child once per ancestor (the cold review's item 7).
                foreach (var ps in _particles) ps.Simulate((float)now, false, true, true);
            }
            else
            {
                float dt = (float)(now - _last);
                foreach (var ps in _particles) ps.Simulate(dt, false, false, true);
            }
            _last = now;
        }
    }
}
