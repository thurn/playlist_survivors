using System;
using System.Globalization;
using UnityEngine;

namespace PS.Look
{
    /// <summary>
    /// `--hero-tune` and `--monster-tune` (<see cref="LookFlags"/>): a run's float values for a look's materials, to judge
    /// a change without a rebuild.
    /// </summary>
    public static class MaterialTune
    {
        /// <summary>
        /// Sets the shader's float property `_key` on <paramref name="m"/> for each `key=value` pair, or for a
        /// `part.key=value` pair only when <paramref name="part"/> is that part (one of <paramref name="parts"/>:
        /// `Edge=1.6`, `blade.Glow=2`). A key the shader lacks, or a part not in <paramref name="parts"/>, fails, so a typo
        /// doesn't pass for a look.
        /// </summary>
        public static void Apply(Material m, string pairs, string flag, string part, params string[] parts)
        {
            foreach (var pair in pairs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2 || !float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    throw new ArgumentException($"{flag}: bad pair '{pair}'");
                string key = kv[0].Trim();
                int dot = key.IndexOf('.');
                if (dot >= 0)
                {
                    string of = key.Substring(0, dot);
                    if (Array.IndexOf(parts, of) < 0) throw new ArgumentException($"{flag}: {of} is not one of {string.Join(", ", parts)}");
                    if (of != part) continue;
                    key = key.Substring(dot + 1);
                }
                if (!m.HasProperty("_" + key)) throw new ArgumentException($"{flag}: {m.shader.name} has no _{key}");
                m.SetFloat("_" + key, v);
            }
        }
    }
}
