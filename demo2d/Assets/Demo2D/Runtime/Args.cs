using System;
using System.Globalization;

namespace Demo2D.Runtime
{
    /// <summary>Reads `--name value` and `--flag` arguments from the command line.</summary>
    public static class Args
    {
        static string[] Raw => Environment.GetCommandLineArgs();

        public static bool Has(string name)
        {
            foreach (var a in Raw)
                if (a == name) return true;
            return false;
        }

        public static string Get(string name, string fallback = null)
        {
            var raw = Raw;
            for (int i = 0; i < raw.Length - 1; i++)
                if (raw[i] == name) return raw[i + 1];
            return fallback;
        }

        public static float GetFloat(string name, float fallback)
        {
            var s = Get(name);
            return s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : fallback;
        }

        public static int GetInt(string name, int fallback)
        {
            var s = Get(name);
            return s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? v
                : fallback;
        }
    }
}
