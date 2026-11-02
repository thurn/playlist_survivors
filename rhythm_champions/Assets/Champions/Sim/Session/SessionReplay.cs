using System;

namespace Champions.Sim
{
    /// <summary>
    /// Replays a session (plan.md, "Sessions and replay") into a fresh sim on its config
    /// (<see cref="RunInputs.Build"/>): each entry is added when the live sim took it, with its recorded
    /// known-at time, and each input is given at its effective time, in the order the sim took them, so
    /// the replay steps through exactly the states the live run did and settles the same state hash, late
    /// entries included. At one time the entries go first: pending entries never change what an input does.
    ///
    /// The entries' contents come from the plugin's record: supplied in order as they are read
    /// (<see cref="Supply"/>), from <c>entries.jsonl</c> or the plugin's replay source, which may still be
    /// delivering them.
    /// <code>
    /// var replay = new SessionReplay(record);
    /// foreach (var e in SessionRecord.LoadEntries(dir)) replay.Supply(e);
    /// var sim = new Simulation(record.Run.Build(content));
    /// replay.StepTo(sim, double.PositiveInfinity);  // to the run's end
    /// replay.Matches(sim);                          // the recorded hash again
    /// </code>
    /// </summary>
    public sealed class SessionReplay
    {
        readonly SessionRecord record;
        readonly StreamEntry[] entries;
        int supplied, nextEntry, nextInput;

        public SessionReplay(SessionRecord record)
        {
            this.record = record ?? throw new ArgumentNullException(nameof(record));
            entries = new StreamEntry[record.Entries.Length];
        }

        public SessionRecord Record => record;

        /// <summary>The frontier the run settled to: a replay steps no further.</summary>
        public double End => record.End;

        /// <summary>How many of the run's entries have been supplied.</summary>
        public int Supplied => supplied;

        /// <summary>Every entry the run took has been supplied.</summary>
        public bool AllSupplied => supplied == entries.Length;

        /// <summary>
        /// The run's next entry, from the plugin's record. Entries past the ones the run took (the analysis
        /// ran on after the run ended) are ignored.
        /// </summary>
        public void Supply(in StreamEntry e)
        {
            if (supplied < entries.Length) entries[supplied++] = e;
        }

        /// <summary>
        /// Steps <paramref name="sim"/> to <paramref name="songTime"/>, but no further than the run's end,
        /// giving each input at its time. False if an entry needed by then hasn't been supplied: the sim then
        /// stops at the time it was taken, and a later call carries on.
        /// </summary>
        public bool StepTo(Simulation sim, double songTime)
        {
            double target = Math.Min(songTime, End);
            var inputs = record.Inputs;
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
                sim.Give(inputs[nextInput++]);
            }
            sim.StepTo(target);
            return true;
        }

        /// <summary>The replay reached the run's end with every entry and input given.</summary>
        public bool Done(Simulation sim) => sim.Now >= End && nextEntry == entries.Length && nextInput == record.Inputs.Length;

        /// <summary>The replay is done and settled the run's hash.</summary>
        public bool Matches(Simulation sim) => Done(sim) && sim.Hash == record.Hash && sim.EventCount == record.Events;
    }
}
