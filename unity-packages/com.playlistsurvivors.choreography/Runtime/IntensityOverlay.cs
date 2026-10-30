using System;
using System.Collections.Generic;

namespace PS.Choreography
{
    /// <summary>
    /// An intensity overlay (choreography.md, "Intensity overlays"): a JSON file merged over a level
    /// script before it is parsed, so one script plays at several intensities without being forked.
    /// <code>
    /// {
    ///   "name": "overdrive",
    ///   "multiply": { "envelope.onScreen": 3 },
    ///   "comboMaxSpeed": 2.2,
    ///   "loads": { "overdrive": { "deathsPerN": 0.25, "hitsPerDeath": 0.1 } },
    ///   "sections": {
    ///     "*": { "multiply": { "viewWidthM": 1.1 } },
    ///     "drop-1": { "load": "overdrive", "combo": ["slashL@beat", "slashR@beat"] }
    ///   }
    /// }
    /// </code>
    /// <list type="bullet">
    /// <item><c>multiply</c> scales numbers in the base script by dotted path; the number must be there.</item>
    /// <item>Every other top-level field is a global setting, merged over the script's: objects merge
    /// member by member, anything else replaces.</item>
    /// <item><c>sections</c> overrides sections by label, the same way, with an optional <c>multiply</c> of
    /// its own; <c>"*"</c> applies to every section first. A label the script lacks fails.</item>
    /// </list>
    /// Within one level, multipliers apply first, so a field both scaled and set takes the set value.
    /// </summary>
    public sealed class IntensityOverlay
    {
        public const string AllSections = "*";

        public string Name;
        readonly JsonValue root;

        IntensityOverlay(JsonValue root) { this.root = root; Name = root["name"].AsString(); }

        public static IntensityOverlay Parse(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            if (root.Type != JsonType.Object) throw new FormatException("intensity overlay: not an object");
            return new IntensityOverlay(root);
        }

        /// <summary>The level script's JSON with the overlay merged over it.</summary>
        public JsonValue Apply(JsonValue script)
        {
            JsonValue merged = Overlay(script, root, "sections", "name");
            JsonValue sections = root["sections"];
            if (sections.Type != JsonType.Object) return merged;
            var secs = new List<JsonValue>(merged["sections"].Type == JsonType.Array ? merged["sections"].Items : new List<JsonValue>());
            JsonValue all = sections[AllSections];
            foreach (KeyValuePair<string, JsonValue> kv in sections.Members)
            {
                if (kv.Key == AllSections) continue;
                if (secs.FindIndex(s => s["section"].AsString() == kv.Key) < 0)
                    throw new FormatException("intensity overlay '" + Name + "': the level script has no section '" + kv.Key + "'");
            }
            for (int i = 0; i < secs.Count; i++)
            {
                JsonValue s = secs[i];
                if (!all.IsNull) s = Overlay(s, all);
                JsonValue own = sections[s["section"].AsString() ?? ""];
                if (!own.IsNull) s = Overlay(s, own);
                secs[i] = s;
            }
            return Set(merged, "sections", JsonValue.OfItems(secs));
        }

        /// <summary><paramref name="over"/>'s multipliers, then its other fields (bar <paramref name="skip"/>), over <paramref name="target"/>.</summary>
        static JsonValue Overlay(JsonValue target, JsonValue over, params string[] skip)
        {
            if (over.Type != JsonType.Object) throw new FormatException("intensity overlay: expected an object, got " + over.Type);
            JsonValue mul = over["multiply"];
            if (mul.Type == JsonType.Object)
                foreach (KeyValuePair<string, JsonValue> kv in mul.Members)
                {
                    if (kv.Value.Type != JsonType.Number) throw new FormatException("intensity overlay: multiply '" + kv.Key + "' is not a number");
                    target = Multiply(target, kv.Key.Split('.'), 0, kv.Value.Number, kv.Key);
                }
            foreach (KeyValuePair<string, JsonValue> kv in over.Members)
            {
                if (kv.Key == "multiply" || Array.IndexOf(skip, kv.Key) >= 0) continue;
                target = Set(target, kv.Key, Merge(target[kv.Key], kv.Value));
            }
            return target;
        }

        /// <summary><paramref name="over"/> merged over <paramref name="b"/>: objects member by member, anything else replaced.</summary>
        public static JsonValue Merge(JsonValue b, JsonValue over)
        {
            if (b.Type != JsonType.Object || over.Type != JsonType.Object) return over;
            JsonValue r = b;
            foreach (KeyValuePair<string, JsonValue> kv in over.Members) r = Set(r, kv.Key, Merge(r[kv.Key], kv.Value));
            return r;
        }

        static JsonValue Multiply(JsonValue v, string[] path, int i, double k, string whole)
        {
            JsonValue child = v[path[i]];
            if (i == path.Length - 1)
            {
                if (child.Type != JsonType.Number) throw new FormatException("intensity overlay: multiply '" + whole + "' names no number in the level script");
                return Set(v, path[i], JsonValue.OfNumber(child.Number * k));
            }
            if (child.Type != JsonType.Object) throw new FormatException("intensity overlay: multiply '" + whole + "' names no number in the level script");
            return Set(v, path[i], Multiply(child, path, i + 1, k, whole));
        }

        /// <summary>A copy of object <paramref name="o"/> with <paramref name="key"/> set (appended if new).</summary>
        static JsonValue Set(JsonValue o, string key, JsonValue value)
        {
            var members = o.Type == JsonType.Object ? new List<KeyValuePair<string, JsonValue>>(o.Members) : new List<KeyValuePair<string, JsonValue>>();
            int at = members.FindIndex(m => m.Key == key);
            if (at >= 0) members[at] = new KeyValuePair<string, JsonValue>(key, value);
            else members.Add(new KeyValuePair<string, JsonValue>(key, value));
            return JsonValue.OfMembers(members);
        }
    }
}
