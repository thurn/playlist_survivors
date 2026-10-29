using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demo3D.Runtime.Effects
{
    /// <summary>Where an <see cref="EffectDriver"/> sends its spawns: our GPU runtime, or the runtime spike's VFX Graph arm.</summary>
    public interface ISpawnSink : IDisposable
    {
        bool Add(in EffectSystem.Spawn s);
        void Reset(double origin);
        void Update(double now, float dt, Camera camera);
        int Particles { get; }
    }

    /// <summary>
    /// Alternative sinks by name. The spike's VFX Graph arm (Runtime/VfxSpike, compiled only
    /// while com.unity.visualeffectgraph is in the project) registers "vfx" here.
    /// </summary>
    public static class SpawnSinks
    {
        static readonly Dictionary<string, Func<EffectLibrary, Camera, ISpawnSink>> Factories = new Dictionary<string, Func<EffectLibrary, Camera, ISpawnSink>>();

        public static void Register(string name, Func<EffectLibrary, Camera, ISpawnSink> factory) => Factories[name] = factory;

        public static ISpawnSink Create(string name, EffectLibrary lib, Camera camera) =>
            Factories.TryGetValue(name, out var f) ? f(lib, camera)
                : throw new ArgumentException($"no effect runtime '{name}' (VFX Graph needs com.unity.visualeffectgraph in Packages/manifest.json)");
    }
}
