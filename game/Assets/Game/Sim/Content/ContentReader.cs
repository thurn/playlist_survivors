using System;
using System.Collections.Generic;

namespace PS.Game.Sim.Content
{
    /// <summary>Bad content: a file that doesn't parse, a field that is missing, unknown or of the wrong
    /// type, or a reference to something that doesn't exist. The message names the file and the path.</summary>
    public sealed class ContentException : Exception
    {
        public ContentException(string message) : base(message) { }
        public ContentException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// A strict reader over one JSON object: every field read must be present (null only where the
    /// reader allows it), and <see cref="End"/> refuses any field nobody read, so the content's shape
    /// is exactly what the loaders know.
    /// </summary>
    sealed class ContentReader
    {
        readonly List<KeyValuePair<string, JsonValue>> members;
        readonly HashSet<string> read = new HashSet<string>();
        public readonly string Path;

        public ContentReader(JsonValue value, string path)
        {
            Path = path;
            if (value.Type != JsonType.Object) throw Fail(path, "expected an object");
            members = value.Members;
            var seen = new HashSet<string>();
            foreach (var m in members)
                if (!seen.Add(m.Key)) throw Fail(Child(m.Key), "duplicate field");
        }

        public static ContentException Fail(string path, string what) => new ContentException(path + ": " + what);
        public ContentException Fail(string what) => Fail(Path, what);
        public string Child(string key) => Path + "." + key;

        JsonValue Get(string key)
        {
            read.Add(key);
            foreach (var m in members) if (m.Key == key) return m.Value;
            throw Fail(Child(key), "missing");
        }

        public double Number(string key)
        {
            JsonValue v = Get(key);
            if (v.Type != JsonType.Number) throw Fail(Child(key), "expected a number");
            return v.Number;
        }

        /// <summary>A number or null (NaN).</summary>
        public double NumberOrNull(string key) => Get(key).IsNull ? double.NaN : Number(key);

        public int Int(string key)
        {
            double n = Number(key);
            if (n != Math.Floor(n) || Math.Abs(n) > int.MaxValue) throw Fail(Child(key), "expected an integer");
            return (int)n;
        }

        /// <summary>An integer or null (-1).</summary>
        public int IntOrNull(string key) => Get(key).IsNull ? -1 : Int(key);

        public bool Bool(string key)
        {
            JsonValue v = Get(key);
            if (v.Type != JsonType.Bool) throw Fail(Child(key), "expected true or false");
            return v.Bool;
        }

        public string String(string key)
        {
            JsonValue v = Get(key);
            if (v.Type != JsonType.String) throw Fail(Child(key), "expected a string");
            return v.String;
        }

        public string StringOrNull(string key) => Get(key).IsNull ? null : String(key);

        public ContentReader Object(string key) => new ContentReader(Get(key), Child(key));

        public ContentReader ObjectOrNull(string key) => Get(key).IsNull ? null : Object(key);

        /// <summary>The object's fields in file order, for maps keyed by name.</summary>
        public IEnumerable<string> Keys()
        {
            foreach (var m in members) yield return m.Key;
        }

        public List<JsonValue> Array(string key)
        {
            JsonValue v = Get(key);
            if (v.Type != JsonType.Array) throw Fail(Child(key), "expected an array");
            return v.Items;
        }

        /// <summary>Each item of an array of objects, read by <paramref name="read"/>.</summary>
        public T[] Objects<T>(string key, Func<ContentReader, T> read)
        {
            List<JsonValue> items = Array(key);
            var result = new T[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                var r = new ContentReader(items[i], Child(key) + "[" + i + "]");
                result[i] = read(r);
                r.End();
            }
            return result;
        }

        public string[] Strings(string key) => Strings(Array(key), Child(key));

        public static string[] Strings(List<JsonValue> items, string path)
        {
            var result = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Type != JsonType.String) throw Fail(path + "[" + i + "]", "expected a string");
                result[i] = items[i].String;
            }
            return result;
        }

        public double[] Numbers(string key)
        {
            List<JsonValue> items = Array(key);
            var result = new double[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Type != JsonType.Number) throw Fail(Child(key) + "[" + i + "]", "expected a number");
                result[i] = items[i].Number;
            }
            return result;
        }

        /// <summary>Refuses any field that wasn't read.</summary>
        public void End()
        {
            foreach (var m in members)
                if (!read.Contains(m.Key)) throw Fail(Child(m.Key), "unknown field");
        }
    }
}
