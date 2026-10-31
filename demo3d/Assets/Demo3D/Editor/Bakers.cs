using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Demo3D.EditorTools
{
    /// <summary>
    /// The bakers that write derived files into the gitignored Assets/Baked/. Each has a name and a
    /// method. The tools decide which are stale (setup.py's BAKER_INPUTS, the inputs each reads) and
    /// pass those as `-only`; every baker that finishes appends its name to <see cref="RanName"/>,
    /// which the tools read back to record the inputs it baked from.
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

        /// <summary>Runs the named bakers, or every one when <paramref name="only"/> is empty.</summary>
        public static void Run(List<string> only)
        {
            int ran = 0;
            Directory.CreateDirectory(BakedRoot);
            foreach (var b in Registry)
            {
                if (only.Count > 0 && !only.Contains(b.Name)) continue;
                Debug.Log("Bakers: running " + b.Name);
                b.Run();
                File.AppendAllText(Path.Combine(BakedRoot, RanName), b.Name + "\n");
                ran++;
            }
            Debug.Log($"Bakers: ran {ran} of {Registry.Count}");
            // When the last full run finished (dotfiles, so the importer skips them).
            if (only.Count == 0)
            {
                File.WriteAllText(Path.Combine(BakedRoot, StampName), DateTime.UtcNow.ToString("o") + "\n");
            }
        }

        public const string BakedRoot = "Assets/Baked";
        public const string StampName = ".bakeall";
        public const string RanName = ".bakeall-ran";
    }
}
