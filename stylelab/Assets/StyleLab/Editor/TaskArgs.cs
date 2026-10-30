using System;
using System.Collections.Generic;

namespace StyleLab.EditorTools
{
    /// <summary>
    /// Arguments for batch-mode task methods. A cold launch passes them on Unity's command line;
    /// the warm editor sets <see cref="Override"/> for the duration of one job.
    /// </summary>
    public static class TaskArgs
    {
        internal static string[] Override;

        static string[] Raw => Override ?? Environment.GetCommandLineArgs();

        public static string Get(string name, string fallback = null)
        {
            var raw = Raw;
            for (int i = 0; i < raw.Length - 1; i++)
                if (raw[i] == name) return raw[i + 1];
            return fallback;
        }

        public static bool Has(string name) => Array.IndexOf(Raw, name) >= 0;

        public static List<string> All(string name)
        {
            var list = new List<string>();
            var raw = Raw;
            for (int i = 0; i < raw.Length - 1; i++)
                if (raw[i] == name) list.Add(raw[i + 1]);
            return list;
        }
    }
}
