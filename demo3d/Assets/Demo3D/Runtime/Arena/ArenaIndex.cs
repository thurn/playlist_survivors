using System;
using System.Globalization;
using UnityEngine;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The baked arena scenes the player can load, written by the editor's ArenaBaker into the
    /// gitignored Assets/Baked/Arena/Resources/ (the scenes hold vendor-derived content).
    /// </summary>
    public sealed class ArenaIndex : ScriptableObject
    {
        public const string ResourcePath = "Demo3DArenaIndex";

        /// <summary>The arena's rungs (environment.md, Environment rendering) a baked scene serves.</summary>
        public const string E0 = "e0", E2 = "e2";

        [Serializable]
        public sealed class Entry
        {
            public string scene;        // the scene's name, as SceneManager loads it
            public string rung;         // "e0": stock, realtime only; "e2": baked lightmaps, static shadow map
            public float density;
            public int placements;
            public int renderers;       // kit renderers (the static set), the ground excluded
            public int lights;          // realtime point lights
            public long triangles;      // LOD 0
        }

        public Entry[] entries = new Entry[0];

        public Entry Find(string rung, float density) =>
            Array.Find(entries, e => e.rung == rung && Mathf.Abs(e.density - density) < 1e-3f);

        public static string SceneName(string rung, float density) =>
            "Arena-" + rung.ToUpperInvariant() + "-" + density.ToString("0.###", CultureInfo.InvariantCulture) + "x";

        public static ArenaIndex Load() => Resources.Load<ArenaIndex>(ResourcePath);
    }
}
