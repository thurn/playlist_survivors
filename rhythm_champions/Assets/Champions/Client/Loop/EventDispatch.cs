using System;
using System.Collections.Generic;
using System.Text;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Loop
{
    /// <summary>What a presenter draws a frame from.</summary>
    public struct PresentFrame
    {
        /// <summary>The song time the frame shows (frameSong).</summary>
        public double Song;
        /// <summary>The sim's frontier: every event at or before it has been handed out.</summary>
        public double Frontier;
        /// <summary>Real seconds since the last frame.</summary>
        public float Dt;
        /// <summary>The hero's feet at <see cref="Song"/> (the sim's ground x, y).</summary>
        public Vector2 Hero;
        public Simulation Sim;
        public Camera Camera;
    }

    /// <summary>
    /// The one place sim events meet the presenters (plan.md, The Client: the `EventDispatch`): each presenter subscribes by
    /// name with what it does with an event, at the end of a frame's events, and with a frame; the loop hands it every
    /// settled event once, in log order, then the frame. A presenter that throws is contained (as game/ does since
    /// ps-msz.8.7): the others still get every event and draw every frame, the throw is logged once per presenter, call and
    /// exception type and counted (<see cref="Faults"/>, <see cref="FaultReport"/>), so a headless check fails on it.
    /// </summary>
    public sealed class EventDispatch
    {
        public delegate void Taker(in SimEvent e);
        public delegate void Presenter(in PresentFrame frame);

        sealed class Entry
        {
            public string Name;
            public Taker Take;
            public Action Flush;
            public Presenter Present;
        }

        struct Fault
        {
            public string Name, Call;
            public Type Type;
            public int Count;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly List<Fault> _faults = new List<Fault>();

        /// <summary>The sim's next event to hand out.</summary>
        public int Cursor { get; private set; }

        /// <summary>Every throw contained so far (each logged only the first time).</summary>
        public int Faults { get; private set; }

        public int Count => _entries.Count;

        /// <summary>
        /// Subscribes <paramref name="name"/>: <paramref name="take"/> for each event, <paramref name="flush"/> after a
        /// frame's events, <paramref name="present"/> each frame (any may be null). Frames present in subscription order.
        /// </summary>
        public void Add(string name, Taker take, Action flush = null, Presenter present = null) =>
            _entries.Add(new Entry { Name = name, Take = take, Flush = flush, Present = present });

        /// <summary>
        /// Hands out the sim's events since the cursor, then frees them from its log; with <paramref name="only"/>, to those
        /// presenters alone (a scene running ahead hands its state's keepers what it skips drawing).
        /// </summary>
        public void Drain(Simulation sim, ICollection<string> only = null)
        {
            var events = sim.EventsSince(Cursor);
            for (int i = 0; i < events.Length; i++) Dispatch(events[i], only);
            Cursor += events.Length;
            sim.DiscardEventsBefore(Cursor);
            foreach (var x in _entries)
            {
                if (x.Flush == null || only != null && !only.Contains(x.Name)) continue;
                try { x.Flush(); }
                catch (Exception ex) { Contain(x.Name, "Flush", ex); }
            }
        }

        public void Dispatch(in SimEvent e, ICollection<string> only = null)
        {
            foreach (var x in _entries)
            {
                if (x.Take == null || only != null && !only.Contains(x.Name)) continue;
                try { x.Take(e); }
                catch (Exception ex) { Contain(x.Name, "Take", ex); }
            }
        }

        public void Present(in PresentFrame frame)
        {
            foreach (var x in _entries)
            {
                if (x.Present == null) continue;
                try { x.Present(frame); }
                catch (Exception ex) { Contain(x.Name, "Present", ex); }
            }
        }

        /// <summary>The throws by presenter, call and type: "Take of crowd: InvalidOperationException x 3, ...".</summary>
        public string FaultReport()
        {
            var b = new StringBuilder();
            foreach (var f in _faults)
                b.Append(b.Length > 0 ? ", " : "").Append(f.Call).Append(" of ").Append(f.Name).Append(": ").Append(f.Type.Name).Append(" x ").Append(f.Count);
            return b.Length > 0 ? b.ToString() : "none";
        }

        void Contain(string name, string call, Exception x)
        {
            Faults++;
            for (int i = 0; i < _faults.Count; i++)
            {
                var f = _faults[i];
                if (f.Name != name || f.Call != call || f.Type != x.GetType()) continue;
                f.Count++;
                _faults[i] = f;
                return;
            }
            _faults.Add(new Fault { Name = name, Call = call, Type = x.GetType(), Count = 1 });
            Debug.LogException(new PresenterFaultException(name, call, x));
        }
    }

    /// <summary>A presenter's exception, contained by <see cref="EventDispatch"/>: the throw is its inner exception.</summary>
    public sealed class PresenterFaultException : Exception
    {
        public PresenterFaultException(string presenter, string call, Exception thrown)
            : base($"the {presenter} presenter's {call} threw {thrown.GetType().Name}: {thrown.Message} " +
                   "(contained: the other presenters go on, and its later throws of this kind are only counted)", thrown)
        {
        }
    }
}
