using System.Collections.Generic;
using PS.Game.Sim;

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
    /// It keeps the cursor into the sim's event log and frees what it has handed on.
    /// </summary>
    public sealed class EventDispatch
    {
        readonly List<IPresenter> _presenters = new List<IPresenter>();

        /// <summary>The sim's next event to dispatch.</summary>
        public int Cursor { get; private set; }

        public void Subscribe(IPresenter presenter) => _presenters.Add(presenter);

        public void Unsubscribe(IPresenter presenter) => _presenters.Remove(presenter);

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
            for (int i = 0; i < _presenters.Count; i++) _presenters[i].OnEvent(e);
        }

        public void Present(in PresentFrame frame)
        {
            for (int i = 0; i < _presenters.Count; i++) _presenters[i].Present(frame);
        }
    }
}
