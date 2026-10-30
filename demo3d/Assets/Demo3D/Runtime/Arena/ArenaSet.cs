using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// Several environments resident in one player (trailer.md, Environments in one player). Each
    /// baked scene loads additively and sets itself up as it would alone (its lightmaps, light
    /// probes, static shadow map, post volume, flames and hero light); then all but one are hidden.
    /// <see cref="Switch"/> makes another the drawn one within the frame: each scene is on its own
    /// layer, so the camera's culling mask selects the new one's renderers; the old one's realtime
    /// lights and post volume go off, the new one's on, and the globals the new one's set-up wrote
    /// are put back (<see cref="ArenaRuntime.Apply"/>).
    /// The scenes overlap at the origin, as baked; each scene's own light probes are swapped in, since
    /// after additive loads Unity keeps one scene's probes for all (ps-hgs.1's evidence).
    /// </summary>
    public sealed class ArenaSet : IDisposable
    {
        /// <summary>Player frames to wait for every scene to set up (an additive load finishes on the next).</summary>
        const int LoadFrames = 30;
        /// <summary>The first resident scene's layer; the project names none from here (TagManager).</summary>
        public const int FirstLayer = 8;

        readonly ArenaRuntime[] _arenas;
        readonly UnityEngine.Camera _camera;
        readonly int _layers; // every resident scene's layer bit
        int _frames;
        bool _ready;

        public ArenaRuntime Active { get; private set; }
        public int Count => _arenas.Length;
        public ArenaRuntime this[int i] => _arenas[i];
        /// <summary>The last <see cref="Switch"/>'s CPU time, 0 if it kept the active arena.</summary>
        public double LastSwitchMs { get; private set; }

        /// <summary>A set of arenas loading or already set up (<see cref="Load"/>, or the editor's ArenaRuntime.Attach); the first is active once ready.</summary>
        public ArenaSet(ArenaRuntime[] arenas, UnityEngine.Camera camera)
        {
            _arenas = arenas;
            _camera = camera;
            for (int i = 0; i < arenas.Length; i++) _layers |= 1 << (FirstLayer + i);
        }

        /// <summary>Starts loading <paramref name="environments"/>, each with <paramref name="o"/>'s rung and density; the first is active once ready.</summary>
        public static ArenaSet Load(ArenaOptions o, IReadOnlyList<string> environments, UnityEngine.Camera camera, UnityEngine.Transform volumeParent)
        {
            var arenas = new ArenaRuntime[environments.Count];
            for (int i = 0; i < arenas.Length; i++)
            {
                var e = o.Clone();
                e.Environment = environments[i];
                arenas[i] = ArenaRuntime.Load(e, camera, volumeParent);
            }
            return new ArenaSet(arenas, camera);
        }

        /// <summary>
        /// True once every scene is set up, the first then active and the rest hidden. Throws, naming
        /// the environment, if one failed to set up or hasn't loaded after <see cref="LoadFrames"/> calls.
        /// </summary>
        public bool Poll()
        {
            if (_ready) return true;
            foreach (var a in _arenas)
                if (a.Failure != null)
                    throw new InvalidOperationException($"ArenaSet: {a.Options.Environment} ({a.SceneName}) failed to set up: {a.Failure.Message}", a.Failure);
            var waiting = new List<string>();
            foreach (var a in _arenas) if (!a.Ready) waiting.Add(a.Options.Environment);
            if (waiting.Count > 0)
            {
                if (++_frames > LoadFrames) throw new InvalidOperationException($"ArenaSet: {string.Join(", ", waiting)} not loaded after {LoadFrames} frames");
                return false;
            }
            _ready = true;
            for (int i = 0; i < _arenas.Length; i++)
            {
                _arenas[i].Resident(FirstLayer + i);
                _arenas[i].Show(false);
            }
            Switch(0);
            return true;
        }

        /// <summary>Makes arena <paramref name="i"/> the drawn one (after <see cref="Poll"/> returned true).</summary>
        public ArenaRuntime Switch(int i)
        {
            var next = _arenas[i];
            if (next == Active)
            {
                LastSwitchMs = 0;
                return next;
            }
            var watch = Stopwatch.StartNew();
            Active?.Show(false);
            next.Show(true);
            _camera.cullingMask = (_camera.cullingMask & ~_layers) | (1 << next.Layer);
            next.Apply();
            Active = next;
            LastSwitchMs = watch.Elapsed.TotalMilliseconds;
            return next;
        }

        public void Dispose()
        {
            foreach (var a in _arenas) a.Dispose();
        }
    }
}
