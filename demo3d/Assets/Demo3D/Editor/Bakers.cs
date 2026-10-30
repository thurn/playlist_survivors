using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Demo3D.EditorTools
{
    /// <summary>
    /// The bakers that write derived files into the gitignored Assets/Baked/. Each has a name, the
    /// inputs that make its output stale, and a method. Setup caches each baker's output per
    /// machine, keyed by its inputs' hash, and runs only the stale ones.
    /// </summary>
    public static class Bakers
    {
        public sealed class Baker
        {
            public string Name;
            public Action Run;
        }

        static readonly List<Baker> Registry = new List<Baker>();

        public static IReadOnlyList<Baker> All => Registry;

        public static void Register(string name, Action run) =>
            Registry.Add(new Baker { Name = name, Run = run });

        public static void RunStale(List<string> only)
        {
            int ran = 0;
            foreach (var b in Registry)
            {
                if (only.Count > 0 && !only.Contains(b.Name)) continue;
                Debug.Log("Bakers: running " + b.Name);
                b.Run();
                ran++;
            }
            Debug.Log($"Bakers: ran {ran} of {Registry.Count}");
            // `demo3d build` runs BakeAll first in a worktree without this stamp (a dotfile, so
            // the importer skips it).
            if (only.Count == 0)
            {
                Directory.CreateDirectory(BakedRoot);
                File.WriteAllText(Path.Combine(BakedRoot, StampName), DateTime.UtcNow.ToString("o") + "\n");
            }
        }

        public const string BakedRoot = "Assets/Baked";
        public const string StampName = ".bakeall";
    }
}
