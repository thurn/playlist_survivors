namespace Champions.Sim
{
    /// <summary>Why the zone key changed (<see cref="SimEvent.Zone"/>'s I2).</summary>
    public enum ZoneReason
    {
        None = 0,
        /// <summary>A strong section change in the music.</summary>
        Section = 1,
        /// <summary><see cref="ZoneRules.EveryS"/> of run clock without one.</summary>
        Timer = 2,
        /// <summary>The dragon's entrance: the warm key, for good.</summary>
        Warm = 3,
    }

    /// <summary>
    /// The floor's zone key (design.md, The World &gt; Zones, Decision 23). A strong section change, once known,
    /// moves the key on to the next of <see cref="ZoneRules.Cycle"/> on the next beat, crossfading over that beat
    /// (the client sweeps it outward from the hero with the beat's ring); with no reliable beat it changes at once
    /// over <see cref="ZoneRules.BeatlessFadeS"/>. Without a section change for <see cref="ZoneRules.EveryS"/> of
    /// run clock the key moves on the same way. The dragon's entrance turns it warm and holds it there
    /// (<see cref="Warm"/>). Keys change only while the run plays; changes heard before it starts are passed over.
    /// Each change is a <see cref="SimEvent.Zone"/> at the moment its crossfade starts.
    /// </summary>
    public sealed class Zones
    {
        readonly ZoneRules rules;
        int changesSeen, cycleAt;
        bool due;
        ZoneReason dueReason;
        double lastClock;

        internal Zones(ZoneRules rules)
        {
            this.rules = rules;
            Key = rules.Cycle[0];
        }

        /// <summary>The key now (an index into the palette's zones).</summary>
        public int Key { get; private set; }

        /// <summary>The warm key holds: nothing moves it on any more.</summary>
        public bool Locked { get; private set; }

        /// <summary>A change is waiting for the next beat.</summary>
        public bool Due => due;

        /// <summary>Takes new section changes and the timer at the tick's end; with no reliable beat a due change happens now.</summary>
        internal void Tick(Simulation sim, double end)
        {
            var stream = sim.Stream;
            int count = stream.Count(EntryKind.Change);
            bool playing = sim.State.Phase == RunPhase.Playing;
            double clock = (sim.Dashes.Rules.LegacyForms ? sim.State.RunClock : sim.State.HordeClock);
            for (; changesSeen < count; changesSeen++)
            {
                if (!playing || Locked || due) continue;
                var change = stream.Get(EntryKind.Change, changesSeen);
                if (change.V0 < rules.SectionMinStrength || clock - lastClock < rules.SectionMinGapS) continue;
                Plan(ZoneReason.Section);
            }
            if (!playing || Locked) return;
            if (!due && clock - lastClock >= rules.EveryS) Plan(ZoneReason.Timer);
            if (due && sim.State.Beat != Reliability.Reliable) Change(sim, end, rules.BeatlessFadeS, Next(), dueReason);
        }

        /// <summary>A beat fired at <paramref name="t"/>: a due change crossfades over it while the beat is reliable.</summary>
        internal void Beat(Simulation sim, double t, double period)
        {
            if (due && !Locked && sim.State.Beat == Reliability.Reliable) Change(sim, t, period, Next(), dueReason);
        }

        /// <summary>
        /// The dragon's entrance at <paramref name="t"/>: the warm key, crossfading over a beat (the beatless fallback
        /// without one), held for the rest of the run.
        /// </summary>
        internal void Warm(Simulation sim, double t)
        {
            if (Locked) return;
            double fade = sim.State.Beat == Reliability.Reliable && sim.State.LastBeat >= 0
                ? sim.Beats.PeriodOf(sim.State.LastBeat)
                : rules.BeatlessFadeS;
            Change(sim, t, fade, rules.Warm, ZoneReason.Warm);
            Locked = true;
        }

        void Plan(ZoneReason reason)
        {
            due = true;
            dueReason = reason;
        }

        int Next()
        {
            cycleAt = (cycleAt + 1) % rules.Cycle.Length;
            return rules.Cycle[cycleAt];
        }

        void Change(Simulation sim, double t, double fadeS, int key, ZoneReason reason)
        {
            due = false;
            lastClock = (sim.Dashes.Rules.LegacyForms ? sim.State.RunClock : sim.State.HordeClock);
            int previous = Key;
            Key = key;
            sim.State.Zone = key;
            sim.Emit(SimEvent.Zone(t, key, previous, reason, fadeS));
        }
    }
}
