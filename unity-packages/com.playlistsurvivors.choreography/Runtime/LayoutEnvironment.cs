using System;
using System.Collections.Generic;
using System.IO;

namespace PS.Choreography
{
    /// <summary>
    /// An environment: a layout file that <c>extends</c> another layout (demo3d's arena) and keeps
    /// its area plan, so the level script, the stress scenes and the beauty shots work unchanged
    /// in it, while its kit, ground, lighting and atmosphere are its own. <see cref="Resolve"/> turns
    /// one into a whole layout, which <see cref="ArenaLayout.Parse"/> and the client's readers take
    /// as they take the arena's.
    /// - The area plan (<see cref="PlanKeys"/>) comes from the extended layout only; an environment
    ///   that sets one of its keys fails.
    /// - Every other key merges over the extended layout's: objects key by key, at any depth (a
    ///   partial <c>lighting</c> keeps the rest of the arena's), arrays and values replace, and
    ///   null removes the key. The prop groups under <c>props</c> replace whole (a group is one
    ///   weighted list).
    /// - <c>pieces</c> {area name: [pieces]} replaces a named landmark's or structure's explicit
    ///   pieces, which stand at the same anchor.
    /// </summary>
    public static class LayoutEnvironment
    {
        /// <summary>The environment that is the extended layout itself (demo3d's moonlit forest).</summary>
        public const string Default = "forest";

        /// <summary>
        /// The area plan: the layout's name (which level and stress scripts name), the ground, the
        /// areas and their goals, the shots, the hero's start, the edge bands' width and the density
        /// the choreography is generated at.
        /// </summary>
        public static readonly string[] PlanKeys = { "name", "size", "decorationDensity", "edgeWidthM", "heroStart", "areas", "shots" };

        /// <summary>
        /// The whole layout of <paramref name="environmentJson"/>: its <c>extends</c> layout (by name,
        /// through <paramref name="layoutJson"/>, which returns null for an unknown one) with the
        /// environment merged over it. A layout without <c>extends</c> comes back as it is.
        /// <paramref name="name"/> names the environment in errors.
        /// </summary>
        public static string Resolve(string environmentJson, Func<string, string> layoutJson, string name = "environment")
        {
            JsonValue env = JsonValue.Parse(environmentJson);
            string extends = env["extends"].AsString();
            if (extends == null) return environmentJson;
            string baseJson = layoutJson(extends) ?? throw new FormatException($"environment {name}: no layout '{extends}' to extend");
            JsonValue under = JsonValue.Parse(Resolve(baseJson, layoutJson, extends));
            foreach (string key in PlanKeys)
                if (Has(env, key)) throw new FormatException($"environment {name}: '{key}' is the area plan's, which comes from {extends}");
            JsonValue pieces = env["pieces"];
            if (pieces.Type == JsonType.Object)
                foreach (KeyValuePair<string, JsonValue> kv in pieces.Members)
                {
                    JsonValue a = Area(under["areas"], kv.Key);
                    if (a == null || (a["type"].AsString() != "landmark" && a["type"].AsString() != "structure"))
                        throw new FormatException($"environment {name}: pieces for '{kv.Key}', which is no landmark or structure of {extends}");
                }

            var sw = new StringWriter();
            var o = new JsonOut(sw);
            o.BeginObject();
            foreach (KeyValuePair<string, JsonValue> kv in under.Members)
            {
                if (kv.Key == "areas" && pieces.Type == JsonType.Object) { o.Key(kv.Key); WriteAreas(o, kv.Value, pieces); continue; }
                if (!Has(env, kv.Key)) { o.Key(kv.Key); Write(o, kv.Value); continue; }
                JsonValue over = env[kv.Key];
                if (over.IsNull) continue;
                o.Key(kv.Key);
                Merge(o, kv.Value, over, kv.Key == "props" ? 1 : int.MaxValue);
            }
            foreach (KeyValuePair<string, JsonValue> kv in env.Members)
            {
                if (kv.Key == "extends" || kv.Key == "pieces" || Has(under, kv.Key) || kv.Value.IsNull) continue;
                o.Key(kv.Key);
                Write(o, kv.Value);
            }
            o.EndObject();
            return sw.ToString();
        }

        /// <summary>True if <paramref name="obj"/> has <paramref name="key"/>, even as null (which removes it).</summary>
        static bool Has(JsonValue obj, string key)
        {
            if (obj.Type != JsonType.Object) return false;
            foreach (KeyValuePair<string, JsonValue> kv in obj.Members) if (kv.Key == key) return true;
            return false;
        }

        static JsonValue Area(JsonValue areas, string name)
        {
            for (int i = 0; i < areas.Count; i++) if (areas[i]["name"].AsString() == name) return areas[i];
            return null;
        }

        /// <summary>Writes <paramref name="over"/> merged over <paramref name="under"/>, objects key by key down to <paramref name="depth"/> levels.</summary>
        static void Merge(JsonOut o, JsonValue under, JsonValue over, int depth)
        {
            if (depth <= 0 || under.Type != JsonType.Object || over.Type != JsonType.Object) { Write(o, over); return; }
            o.BeginObject();
            foreach (KeyValuePair<string, JsonValue> kv in under.Members)
            {
                if (!Has(over, kv.Key)) { o.Key(kv.Key); Write(o, kv.Value); continue; }
                JsonValue v = over[kv.Key];
                if (v.IsNull) continue;
                o.Key(kv.Key);
                Merge(o, kv.Value, v, depth - 1);
            }
            foreach (KeyValuePair<string, JsonValue> kv in over.Members)
            {
                if (Has(under, kv.Key) || kv.Value.IsNull) continue;
                o.Key(kv.Key);
                Write(o, kv.Value);
            }
            o.EndObject();
        }

        static void WriteAreas(JsonOut o, JsonValue areas, JsonValue pieces)
        {
            o.BeginArray();
            for (int i = 0; i < areas.Count; i++)
            {
                JsonValue a = areas[i];
                string name = a["name"].AsString();
                if (name == null || !Has(pieces, name)) { Write(o, a); continue; }
                o.BeginObject();
                foreach (KeyValuePair<string, JsonValue> kv in a.Members)
                    if (kv.Key != "pieces") { o.Key(kv.Key); Write(o, kv.Value); }
                o.Key("pieces");
                Write(o, pieces[name]);
                o.EndObject();
            }
            o.EndArray();
        }

        static void Write(JsonOut o, JsonValue v)
        {
            switch (v.Type)
            {
                case JsonType.Null: o.Value((string)null); break;
                case JsonType.Bool: o.Value(v.Bool); break;
                case JsonType.Number: o.Value(v.Number, 9); break;
                case JsonType.String: o.Value(v.String); break;
                case JsonType.Array:
                    o.BeginArray();
                    foreach (JsonValue item in v.Items) Write(o, item);
                    o.EndArray();
                    break;
                default:
                    o.BeginObject();
                    foreach (KeyValuePair<string, JsonValue> kv in v.Members) { o.Key(kv.Key); Write(o, kv.Value); }
                    o.EndObject();
                    break;
            }
        }
    }
}
