using System;

namespace Game.EditorTools
{
    /// <summary>Arguments for batch-mode task methods, from Unity's command line.</summary>
    public static class TaskArgs
    {
        public static string Get(string name, string fallback = null)
        {
            var raw = Environment.GetCommandLineArgs();
            for (int i = 0; i < raw.Length - 1; i++)
                if (raw[i] == name) return raw[i + 1];
            return fallback;
        }

        public static bool Has(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
    }
}
