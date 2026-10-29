using System;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Effects
{
    /// <summary>
    /// The arena's ambient particles (environment.md, Atmosphere: fireflies on the effect runtime):
    /// every <see cref="Period"/> seconds a few emitters of the selection's <c>client.ambient</c>
    /// effects, scattered over the view at that moment and living two periods, so the view is
    /// always populated without a field of emitters over the whole arena. The demo's EffectDriver
    /// and S0 (ArenaScene) both use it. Deterministic: positions and seeds come from the period's
    /// index.
    /// </summary>
    public sealed class AmbientEffects
    {
        public const double Period = 2.0;
        readonly ISpawnSink _fx;
        readonly int[] _effects;
        readonly int _perPeriod;
        double _next;
        public bool Any => _effects.Length > 0;

        public AmbientEffects(EffectLibrary lib, ISpawnSink fx)
        {
            _fx = fx;
            var client = JsonValue.Parse(string.IsNullOrEmpty(lib.Selection) ? "{}" : lib.Selection)["client"];
            var arr = client["ambient"];
            var list = new System.Collections.Generic.List<int>();
            for (int i = 0; i < arr.Count; i++) { int k = lib.IndexOf(arr[i].AsString()); if (k >= 0) list.Add(k); }
            _effects = list.ToArray();
            _perPeriod = Math.Max(1, (int)client["ambientPerPeriod"].AsFloat(6));
        }

        /// <summary>After a seek: the next spawn is the period still alive at <paramref name="from"/>.</summary>
        public void Reset(double from) => _next = Math.Floor(from / Period);

        /// <summary>Spawns every period that has started by <paramref name="now"/>; framing gives the view's ground focus and width at a time.</summary>
        public int Spawn(double now, Func<double, (Vector2 focus, float width)> framing)
        {
            int n = 0;
            if (_effects.Length == 0) return 0;
            while ((_next + 1) * Period <= now)
            {
                _next++;
                double t = _next * Period;
                var (focus, width) = framing(Math.Max(0, t));
                uint k = (uint)_next;
                for (int j = 0; j < _perPeriod; j++)
                {
                    uint seed = EffectSystem.Hash(k, (uint)j);
                    float x = (EffectSystem.Rand(seed, 1) - 0.5f) * width, z = (EffectSystem.Rand(seed, 2) - 0.4f) * width * 0.6f;
                    _fx.Add(new EffectSystem.Spawn
                    {
                        Effect = _effects[j % _effects.Length], Position = new Vector3(focus.x + x, 0.3f, focus.y + z), Yaw = 0, Scale = 1f,
                        Seed = seed, Time = t, Velocity = Vector3.zero, YawRate = 0, Duration = (float)(Period * 2),
                    });
                    n++;
                }
            }
            return n;
        }
    }
}
