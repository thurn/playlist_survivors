using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// Replays a session (unity-game.md, "Sessions and Replay") into a fresh sim on its config
    /// (<see cref="RunInputs.Build"/>): each entry is added when the live sim took it, with its
    /// recorded known-at time, and each key change, pointer target, debug command and run control is given at its
    /// effective time, in the order the sim took them, so the replay steps through exactly the states the live run
    /// did and settles the same event log, late entries included. At one time the entries go first:
    /// pending entries never change what an input does.
    ///
    /// The entries' contents come from the plugin's record: supplied in order as they are read
    /// (<see cref="Supply"/>), from <c>entries.jsonl</c> or the plugin's Replay source, which may
    /// still be delivering them.
    /// <code>
    /// var replay = new SessionReplay(record);
    /// foreach (var e in SessionRecord.LoadEntries(dir)) replay.Supply(e);
    /// var sim = new Simulation(record.Run.Build(content, arena));
    /// replay.StepTo(sim, double.PositiveInfinity);  // to the run's end
    /// replay.Matches(sim);                          // the settled hash again
    /// </code>
    /// </summary>
    public sealed class SessionReplay
    {
        readonly SessionRecord record;
        readonly StreamEntry[] entries;
        readonly Input[] inputs;
        int supplied, nextEntry, nextInput;

        struct Input
        {
            public double Effective;
            public int Order;
            public byte Type; // 0 a key change, 1 a debug command, 2 a run control, 3 a pointer target
            public KeyRecord Key;
            public TargetRecord Target;
            public DebugRecord Command;
            public RunControl Control;
        }

        public SessionReplay(SessionRecord record)
        {
            this.record = record ?? throw new ArgumentNullException(nameof(record));
            entries = new StreamEntry[record.Entries.Length];
            inputs = new Input[record.Keys.Length + record.Targets.Length + record.Commands.Length + record.Controls.Length];
            int n = 0;
            foreach (var k in record.Keys) inputs[n++] = new Input { Effective = k.Effective, Order = k.Order, Type = 0, Key = k };
            foreach (var t in record.Targets) inputs[n++] = new Input { Effective = t.Effective, Order = t.Order, Type = 3, Target = t };
            foreach (var c in record.Commands) inputs[n++] = new Input { Effective = c.Effective, Order = c.Order, Type = 1, Command = c };
            foreach (var c in record.Controls) inputs[n++] = new Input { Effective = c.Effective, Order = c.Order, Type = 2, Control = c.Control };
            Array.Sort(inputs, (a, b) => a.Order.CompareTo(b.Order));
        }

        public SessionRecord Record => record;

        /// <summary>The frontier the run settled to: a replay steps no further.</summary>
        public double End => record.End;

        /// <summary>How many of the run's entries have been supplied.</summary>
        public int Supplied => supplied;

        /// <summary>Every entry the run took has been supplied.</summary>
        public bool AllSupplied => supplied == entries.Length;

        /// <summary>When the run took the first entry not yet supplied (infinity once all are).</summary>
        public double NextUnsupplied => supplied < entries.Length ? record.Entries[supplied].Taken : double.PositiveInfinity;

        /// <summary>
        /// The run's next entry, from the plugin's record. Entries past the ones the run took (the
        /// analysis ran on after the run ended) are ignored.
        /// </summary>
        public void Supply(in StreamEntry e)
        {
            if (supplied < entries.Length) entries[supplied++] = e;
        }

        /// <summary>
        /// Steps <paramref name="sim"/> to <paramref name="songTime"/>, but no further than the run's
        /// end, giving each input at its time. False if an entry needed by then hasn't been supplied:
        /// the sim then stops at the time it was taken, and a later call carries on.
        /// </summary>
        public bool StepTo(Simulation sim, double songTime)
        {
            double target = Math.Min(songTime, End);
            while (true)
            {
                double entryAt = nextEntry < entries.Length ? record.Entries[nextEntry].Taken : double.PositiveInfinity;
                double inputAt = nextInput < inputs.Length ? inputs[nextInput].Effective : double.PositiveInfinity;
                double at = Math.Min(entryAt, inputAt);
                if (at > target) break;
                sim.StepTo(at);
                if (entryAt == at)
                {
                    if (nextEntry >= supplied) return false;
                    sim.AddKnown(entries[nextEntry], record.Entries[nextEntry].KnownAt);
                    nextEntry++;
                    continue;
                }
                ref readonly var input = ref inputs[nextInput++];
                if (input.Type == 3) sim.Target(input.Target.X, input.Target.Y, input.Target.Heard);
                else if (input.Type == 2) sim.Control(input.Control);
                else if (input.Type == 1) sim.Command(input.Command.Command, input.Command.Arg);
                else if (input.Key.Down) sim.Press(input.Key.Key, input.Key.Heard);
                else sim.Release(input.Key.Key, input.Key.Heard);
            }
            sim.StepTo(target);
            return true;
        }

        /// <summary>The replay reached the run's end with every input given.</summary>
        public bool Done(Simulation sim) => sim.Now >= End && nextEntry == entries.Length && nextInput == inputs.Length;

        /// <summary>The replay is done and settled the run's hash.</summary>
        public bool Matches(Simulation sim) => Done(sim) && sim.Hash == record.Hash && sim.EventCount == record.Events;
    }
}
