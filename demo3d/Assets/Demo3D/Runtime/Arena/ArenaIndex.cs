using System;
using System.Globalization;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The baked arena scenes the player can load, written by the editor's ArenaBaker into the
    /// gitignored Assets/Baked/Arena/Resources/ (the scenes hold vendor-derived content): the
    /// forest's, and each other environment's (ps-bop).
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
            public string environment;  // ps-bop: "forest" (or empty, from before environments), "crypt", ...
            public string rung;         // "e0": stock, realtime only; "e2": baked lightmaps, static shadow map
            public float density;
            public int placements;
            public int renderers;       // kit renderers (the static set), the ground excluded
            public int lights;          // realtime point lights
            public long triangles;      // LOD 0
            public bool merged;         // TD14's E1 variant: small props merged into one mesh per cell and material
            public int mergedRenderers; // merged scenes: the renderers the small props became
        }

        public Entry[] entries = new Entry[0];

        public Entry Find(string environment, string rung, float density, bool merged = false) =>
            Array.Find(entries, e => EnvironmentOf(e) == Environment(environment) && e.rung == rung && Mathf.Abs(e.density - density) < 1e-3f && e.merged == merged);

        public static string EnvironmentOf(Entry e) => Environment(e.environment);

        static string Environment(string name) => string.IsNullOrEmpty(name) ? LayoutEnvironment.Default : name;

        /// <summary>The scene's name: the forest's "Arena-E2-2x" as before environments, another's "Arena-crypt-E2-2x".</summary>
        public static string SceneName(string environment, string rung, float density, bool merged = false) =>
            "Arena-" + (Environment(environment) == LayoutEnvironment.Default ? "" : environment + "-") + rung.ToUpperInvariant() + "-" +
            density.ToString("0.###", CultureInfo.InvariantCulture) + "x" + (merged ? "-merged" : "");

        public static ArenaIndex Load() => Resources.Load<ArenaIndex>(ResourcePath);
    }
}
