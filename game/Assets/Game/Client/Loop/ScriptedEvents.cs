using System;
using System.Collections.Generic;
using System.IO;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Loop
{
    /// <summary>
    /// A synthetic event source (`--events FILE`), so presenters can make stills and tests before
    /// the sim's rules emit their events. The file is JSON lines, one <see cref="SimEvent"/> each,
    /// with the slots of its kind's factory (SimEvent.cs) and absent ones 0:
    /// <code>
    /// {"kind": "Spawn", "t": 1.0, "i": [0, 2], "d": [3.0, 4.5]}
    /// {"kind": "EnemyStep", "t": 1.5, "i": [0], "d": [3.0, 4.5, 3.0, 6.0, 1.5, 1.75]}
    /// </code>
    /// Lines starting with `#` are comments. Each event is dispatched once the frontier reaches
    /// its <c>t</c>, as the sim would emit it; events at one time keep the file's order.
    /// </summary>
    public sealed class ScriptedEvents
    {
        [Serializable]
        struct Line
        {
            public string kind;
            public double t;
            public int[] i;
            public double[] d;
        }

        readonly SimEvent[] _events;
        int _next;

        public ScriptedEvents(SimEvent[] events)
        {
            // Sorted by time, keeping the given order at one time.
            var order = new int[events.Length];
            for (int k = 0; k < order.Length; k++) order[k] = k;
            Array.Sort(order, (a, b) => events[a].T != events[b].T ? events[a].T.CompareTo(events[b].T) : a.CompareTo(b));
            _events = new SimEvent[events.Length];
            for (int k = 0; k < order.Length; k++) _events[k] = events[order[k]];
        }

        public int Count => _events.Length;
        public int Remaining => _events.Length - _next;

        public static ScriptedEvents Load(string path) => Parse(File.ReadAllLines(path), path);

        public static ScriptedEvents Parse(IReadOnlyList<string> lines, string name = "events")
        {
            var events = new List<SimEvent>();
            for (int n = 0; n < lines.Count; n++)
            {
                string text = lines[n].Trim();
                if (text.Length == 0 || text[0] == '#') continue;
                var line = JsonUtility.FromJson<Line>(text);
                if (!Enum.TryParse(line.kind, out SimEventKind kind) || kind == SimEventKind.None)
                    throw new FormatException($"{name}:{n + 1}: unknown event kind '{line.kind}'");
                int[] ints = line.i ?? Array.Empty<int>();
                double[] doubles = line.d ?? Array.Empty<double>();
                if (ints.Length > 3 || doubles.Length > 6)
                    throw new FormatException($"{name}:{n + 1}: an event has at most 3 ints and 6 doubles");
                var e = new SimEvent { Kind = kind, T = line.t };
                if (ints.Length > 0) e.I0 = ints[0];
                if (ints.Length > 1) e.I1 = ints[1];
                if (ints.Length > 2) e.I2 = ints[2];
                if (doubles.Length > 0) e.D0 = doubles[0];
                if (doubles.Length > 1) e.D1 = doubles[1];
                if (doubles.Length > 2) e.D2 = doubles[2];
                if (doubles.Length > 3) e.D3 = doubles[3];
                if (doubles.Length > 4) e.D4 = doubles[4];
                if (doubles.Length > 5) e.D5 = doubles[5];
                events.Add(e);
            }
            return new ScriptedEvents(events.ToArray());
        }

        /// <summary>Dispatches every event due at or before `frontier`.</summary>
        public void DispatchUpTo(double frontier, EventDispatch dispatch)
        {
            while (_next < _events.Length && _events[_next].T <= frontier) dispatch.Dispatch(_events[_next++]);
        }
    }
}
