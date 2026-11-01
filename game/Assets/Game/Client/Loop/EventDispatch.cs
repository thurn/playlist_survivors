using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PS.Game.Sim;
using Debug = UnityEngine.Debug;

namespace Game.Client.Loop
{
    /// <summary>What a presenter draws a frame from.</summary>
    public struct PresentFrame
    {
        /// <summary>The song time the frame shows (frameSong).</summary>
        public double Song;
        /// <summary>The sim's frontier: every event at or before it has been dispatched.</summary>
        public double Frontier;
        /// <summary>The sim's state at <see cref="Song"/>, from the state history.</summary>
        public SimSnapshot State;
        /// <summary>
        /// True when frameSong was past the frontier, so <see cref="State"/> is the frontier's
        /// (display latency exceeds the lead; the overlay flags it).
        /// </summary>
        public bool AtFrontier;
    }

    /// <summary>
    /// A client system that shows the sim: the crowd, the hero, effects, sound, structures, the
    /// HUD. Events reach it once each, as soon as they settle, which is ahead of frameSong by
    /// about the lead, so it can schedule them at their song times; then it draws each frame.
    /// It never guesses future sim state (unity-game.md, Attacks and Steps Land on the Beat).
    /// </summary>
    public interface IPresenter
    {
        void OnEvent(in SimEvent e);
        void Present(in PresentFrame frame);
    }

    /// <summary>
    /// Hands settled events to every subscribed presenter, in order, then each frame to them.
    /// It keeps the cursor into the sim's event log and frees what it has handed on. A presenter
    /// that throws is contained: the others still get every event once and draw every frame, and
    /// the throw is logged once per presenter, call and exception type (a <see cref="PresenterFaultException"/>)
    /// and counted for the run's summary (<see cref="Faults"/>), so a headless check fails on it. When
    /// <see cref="Timed"/> (`--profile-out`, <see cref="FrameProfile"/>), it adds up the time the
    /// events' dispatch and each presenter's Present take, until <see cref="ResetTimes"/>.
    /// </summary>
    public sealed class EventDispatch
    {
        readonly List<IPresenter> _presenters = new List<IPresenter>();
        readonly List<Fault> _faults = new List<Fault>();
        long[] _presentTicks = new long[0];

        struct Fault
        {
            public IPresenter Presenter;
            public string Call;
            public Type Type;
            public int Count;
        }

        /// <summary>The sim's next event to dispatch.</summary>
        public int Cursor { get; private set; }

        /// <summary>Whether dispatch and presenting are timed (Stopwatch ticks).</summary>
        public bool Timed { get; set; }

        /// <summary>The events' dispatch to every presenter since <see cref="ResetTimes"/>, in Stopwatch ticks.</summary>
        public long EventTicks { get; private set; }

        public int Count => _presenters.Count;

        /// <summary>The presenters' throws so far, every one counted (each logged only the first time).</summary>
        public int Faults { get; private set; }

        /// <summary>The throws by presenter, call and exception type, for the run's summary: "OnEvent of CrowdView: InvalidOperationException x 3, ...".</summary>
        public string FaultReport()
        {
            var b = new StringBuilder();
            foreach (var f in _faults)
                b.Append(b.Length > 0 ? ", " : "").Append(f.Call).Append(" of ").Append(f.Presenter.GetType().Name)
                    .Append(": ").Append(f.Type.Name).Append(" x ").Append(f.Count);
            return b.ToString();
        }

        public IPresenter this[int i] => _presenters[i];

        /// <summary>Presenter <paramref name="i"/>'s Present since <see cref="ResetTimes"/>, in Stopwatch ticks.</summary>
        public long PresentTicks(int i) => _presentTicks[i];

        public void ResetTimes()
        {
            EventTicks = 0;
            Array.Clear(_presentTicks, 0, _presentTicks.Length);
        }

        public void Subscribe(IPresenter presenter)
        {
            _presenters.Add(presenter);
            Array.Resize(ref _presentTicks, _presenters.Count);
        }

        public void Unsubscribe(IPresenter presenter)
        {
            int i = _presenters.IndexOf(presenter);
            if (i < 0) return;
            _presenters.RemoveAt(i);
            Array.Copy(_presentTicks, i + 1, _presentTicks, i, _presentTicks.Length - i - 1);
            Array.Resize(ref _presentTicks, _presenters.Count);
        }

        /// <summary>Dispatches the sim's events since the cursor and discards them from its log.</summary>
        public void DrainSim(Simulation sim)
        {
            var events = sim.EventsSince(Cursor);
            for (int i = 0; i < events.Length; i++) Dispatch(events[i]);
            Cursor += events.Length;
            sim.DiscardEventsBefore(Cursor);
        }

        public void Dispatch(in SimEvent e)
        {
            long start = Timed ? Stopwatch.GetTimestamp() : 0;
            for (int i = 0; i < _presenters.Count; i++)
            {
                int tag = AllocationCensus.Tag(AllocationCensus.OnEvent(i));
                try
                {
                    _presenters[i].OnEvent(e);
                }
                catch (Exception x)
                {
                    Contain(_presenters[i], nameof(IPresenter.OnEvent), x);
                }
                AllocationCensus.Tag(tag);
            }
            if (Timed) EventTicks += Stopwatch.GetTimestamp() - start;
        }

        public void Present(in PresentFrame frame)
        {
            for (int i = 0; i < _presenters.Count; i++)
            {
                long start = Timed ? Stopwatch.GetTimestamp() : 0;
                int tag = AllocationCensus.Tag(AllocationCensus.Present(i));
                try
                {
                    _presenters[i].Present(frame);
                }
                catch (Exception x)
                {
                    Contain(_presenters[i], nameof(IPresenter.Present), x);
                }
                AllocationCensus.Tag(tag);
                if (Timed) _presentTicks[i] += Stopwatch.GetTimestamp() - start;
            }
        }

        void Contain(IPresenter presenter, string call, Exception x)
        {
            Faults++;
            for (int i = 0; i < _faults.Count; i++)
            {
                var f = _faults[i];
                if (f.Presenter != presenter || f.Call != call || f.Type != x.GetType()) continue;
                f.Count++;
                _faults[i] = f;
                return;
            }
            _faults.Add(new Fault { Presenter = presenter, Call = call, Type = x.GetType(), Count = 1 });
            Debug.LogException(new PresenterFaultException(presenter, call, x));
        }
    }

    /// <summary>A presenter's exception, contained by <see cref="EventDispatch"/>: the throw is its inner exception.</summary>
    public sealed class PresenterFaultException : Exception
    {
        public PresenterFaultException(IPresenter presenter, string call, Exception thrown)
            : base($"{presenter.GetType().Name}'s {call} threw {thrown.GetType().Name}: {thrown.Message} " +
                   "(contained: the other presenters go on, and its later throws of this kind are only counted)", thrown)
        {
        }
    }
}
