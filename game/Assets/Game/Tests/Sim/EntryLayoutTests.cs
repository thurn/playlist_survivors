using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// <see cref="StreamEntry"/> is the one C# mirror of ps_entry (unity-game.md, "The C Interface"):
    /// the client polls the plugin straight into it, so its layout and kinds must be the header's.
    /// </summary>
    public class EntryLayoutTests
    {
        [Test]
        public void EntryKindIsThePluginHeadersKindTable()
        {
            var header = new Dictionary<string, int>();
            foreach (string line in File.ReadLines(Checkout.File("native", "crates", "plugin", "include", "ps_plugin.h")))
            {
                var m = Regex.Match(line, @"^#define PS_KIND_(\w+)\s+(-?\d+)");
                if (m.Success) header[Pascal(m.Groups[1].Value)] = int.Parse(m.Groups[2].Value);
            }
            var kinds = new Dictionary<string, int>();
            foreach (EntryKind k in Enum.GetValues(typeof(EntryKind)))
                if (k != EntryKind.None) kinds[k.ToString()] = (int)k;
            Assert.That(header, Is.Not.Empty, "PS_KIND_* in ps_plugin.h");
            CollectionAssert.AreEquivalent(header, kinds);
        }

        [Test]
        public void StreamEntryHasPsEntrysLayout()
        {
            Assert.AreEqual(120, Marshal.SizeOf<StreamEntry>());
            Assert.AreEqual(0, (int)Marshal.OffsetOf<StreamEntry>(nameof(StreamEntry.Kind)));
            Assert.AreEqual(8, (int)Marshal.OffsetOf<StreamEntry>(nameof(StreamEntry.T)));
            Assert.AreEqual(16, (int)Marshal.OffsetOf<StreamEntry>(nameof(StreamEntry.Stamp)));
            Assert.AreEqual(24, (int)Marshal.OffsetOf<StreamEntry>(nameof(StreamEntry.V0)));
            Assert.AreEqual(112, (int)Marshal.OffsetOf<StreamEntry>(nameof(StreamEntry.V11)));
        }

        /// <summary>HINDSIGHT_BEAT as HindsightBeat.</summary>
        static string Pascal(string snake)
        {
            var b = new StringBuilder();
            foreach (string word in snake.Split('_'))
                if (word.Length > 0) b.Append(word[0]).Append(word.Substring(1).ToLowerInvariant());
            return b.ToString();
        }
    }
}
