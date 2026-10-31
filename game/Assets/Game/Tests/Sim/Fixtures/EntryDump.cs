using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PS.Game.Sim.Content;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// A real belief stream: a dump written by native's <c>ps entries</c> (unity-game.md, "Stream
    /// Entries and What the Sim May Know"), JSON lines of a header and then one ps_entry per line
    /// (<c>kind</c>, <c>t</c>, <c>known_at</c> and the kind's <c>v</c> slots), in emission order.
    /// The dump's <c>known_at</c> is the plugin's stamp; the sim adds B.
    /// </summary>
    public static class EntryDump
    {
        /// <summary>The first 20 s of house-128 (128 BPM), native/fixtures/entries.</summary>
        public const string House128 = "house-128-20s.jsonl";

        public static List<StreamEntry> Load(string name, out double duration)
        {
            var entries = new List<StreamEntry>();
            duration = double.NaN;
            int line = 0;
            foreach (string text in File.ReadLines(Checkout.File("native", "fixtures", "entries", name)))
            {
                line++;
                if (text.Length == 0) continue;
                var json = JsonValue.Parse(text);
                if (line == 1)
                {
                    Assert.AreEqual("ps-entries", Field(json, "format").String, "the dump's header");
                    Assert.AreEqual(1, Field(json, "version").Number, "the dump's version");
                    duration = Field(json, "duration").Number;
                    continue;
                }
                var e = new StreamEntry
                {
                    Kind = (EntryKind)(int)Field(json, "kind").Number,
                    T = Field(json, "t").Number,
                    Stamp = Field(json, "known_at").Number,
                };
                if (!Enum.IsDefined(typeof(EntryKind), e.Kind) || e.Kind == EntryKind.None)
                    throw new FormatException($"{name}:{line}: unknown kind {(int)e.Kind}");
                var v = Field(json, "v").Items;
                if (v.Count > StreamEntry.Slots) throw new FormatException($"{name}:{line}: {v.Count} slots");
                for (int i = 0; i < v.Count; i++) e[i] = v[i].Number;
                entries.Add(e);
            }
            return entries;
        }

        static JsonValue Field(JsonValue obj, string key)
        {
            foreach (var m in obj.Members)
                if (m.Key == key) return m.Value;
            throw new FormatException("a dump line without " + key);
        }
    }
}
