using System;
using UnityEngine;

namespace PS.Look.Effects
{
    /// <summary>Where a player sends effect spawns: the GPU runtime (<see cref="EffectSystem"/>), or demo3d's VFX Graph spike arm.</summary>
    public interface ISpawnSink : IDisposable
    {
        bool Add(in EffectSystem.Spawn s);
        void Reset(double origin);
        void Update(double now, float dt, Camera camera);
        int Particles { get; }
    }
}
