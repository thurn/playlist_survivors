using System;

namespace Champions.Sim
{
    /// <summary>Where a Boss Rush run is (<see cref="BossRush.Stage"/>, <see cref="SimEvent.RushStage"/>'s I0).</summary>
    public enum BossRushStage : byte
    {
        /// <summary>An act begins: its checkpoint is taken (an event only; forming follows at once).</summary>
        Act = 0,
        /// <summary>The boss's arena forms round the hero over <see cref="BossRushRules.FormBeats"/> beats.</summary>
        Forming = 1,
        /// <summary>The boss is called: it arrives on a downbeat (<see cref="Bosses"/>) and is fought to its death.</summary>
        Fighting = 2,
        /// <summary>It died: gems magnetised, full heal, the reward card, then the queued level-ups and the last gems.</summary>
        Reward = 3,
        /// <summary>Between acts: the next act's card, a few seconds, skippable with Enter (never Space: a dash pressed after the kill must not skip it).</summary>
        ActCard = 4,
        /// <summary>The last boss died: the run is won.</summary>
        Won = 5,
    }

    /// <summary>
    /// The Boss Rush's sequence (boss-rush.md, The Run > Flow; Content/bossrush.json): no horde, the bosses in their fixed
    /// order, each in turn: its arena forms (<see cref="BossRushStage.Forming"/>), it is called and fought
    /// (<see cref="BossRushStage.Fighting"/>), and its death brings the remaining XP magnetised, a full heal, the reward
    /// card and the queued level-ups (<see cref="BossRushStage.Reward"/>); between acts, an act card. Each act starts by
    /// taking its checkpoint (<see cref="Checkpoint"/>), which the client saves; a death ends the run as in the main game,
    /// and the client restarts the act on a new sim from that checkpoint. Its clocks are the run clock and the beats,
    /// so it holds with the world. Music never pauses for any of it.
    ///
    /// A slot whose own fight hasn't landed is fought by its stand-in (<see cref="BossRushBoss.Cast"/>), or passed over.
    /// </summary>
    public sealed class BossRush
    {
        readonly Simulation sim;
        readonly BossRushRules rules;
        readonly double beatlessBeatS;
        readonly BossRushCheckpoint start;
        int beatsLeft;
        double untilClock;

        internal BossRush(Simulation sim, SimConfig config)
        {
            this.sim = sim;
            rules = config.BossRush;
            beatlessBeatS = config.Beat.TelegraphBeatlessBeatS;
            start = config.BossRushStart ?? new BossRushCheckpoint();
            if (start.Boss < 0 || start.Boss >= rules.Count) throw new ArgumentException($"boss rush start: no boss {start.Boss + 1} of {rules.Count}");
            sim.Progression.Restore(sim, start.Level, start.Experience, start.HeldLevels, start.Fallbacks);
            Boss = rules.NextPlayed(start.Boss);
            if (Boss >= rules.Count)
            {
                Boss = rules.Count - 1;
                Checkpoint = Capture();
                Won(sim.Now);
                return;
            }
            BeginAct(sim.Now);
        }

        public BossRushRules Rules => rules;
        public BossRushStage Stage { get; private set; }

        /// <summary>The boss now (0-based): forming, fought, rewarded; through an act card, the next act's first.</summary>
        public int Boss { get; private set; }
        public int Act => rules.Bosses[Boss].Act;
        public BossRushBoss Slot => rules.Bosses[Boss];

        /// <summary>The checkpoint of the act now: what a death restarts it with.</summary>
        public BossRushCheckpoint Checkpoint { get; private set; }

        /// <summary>The XP the boss now has shed so far (the progression's shedding adds to it); its kill drops the rest.</summary>
        public double Shed { get; internal set; }

        /// <summary>The bosses killed this sim.</summary>
        public int Killed { get; private set; }

        /// <summary>The whole run's combat time so far (s): the attempts before this one, and this one's run clock.</summary>
        public double TotalTime => start.TimeBefore + sim.State.RunClock;

        /// <summary>The run's deaths per act before this attempt.</summary>
        public int[] Deaths => (int[])Checkpoint.Deaths.Clone();

        /// <summary>
        /// What restarts the act now (Retry act after a death, Restart act from the pause menu): its checkpoint, with the
        /// whole run's time so far and, after a <paramref name="death"/>, one more death in this act.
        /// </summary>
        public BossRushCheckpoint Retry(bool death)
        {
            var c = Checkpoint.Clone();
            c.TimeBefore = TotalTime;
            if (death) c.Deaths[Act]++;
            return c;
        }

        // ---- Driven by the sim ----

        /// <summary>A key pressed: Enter skips an act card, and is the card's (true).</summary>
        internal bool Press(Key key)
        {
            if (Stage != BossRushStage.ActCard || sim.State.Held || key != Key.Enter) return false;
            untilClock = sim.State.RunClock;
            return true;
        }

        /// <summary>A beat fired at <paramref name="t"/>: forming counts its beats.</summary>
        internal void Beat(double t)
        {
            if (Stage != BossRushStage.Forming || sim.State.Held || sim.State.Beat != Reliability.Reliable) return;
            if (--beatsLeft <= 0) Call(t);
        }

        /// <summary>The tick ending at <paramref name="end"/>: beatless forming, the reward's end, the act card's end.</summary>
        internal void Tick(double end)
        {
            if (sim.State.Held) return;
            switch (Stage)
            {
                case BossRushStage.Forming:
                    if (sim.State.Beat != Reliability.Reliable && sim.State.RunClock >= untilClock - 1e-9) Call(end);
                    break;
                case BossRushStage.Reward:
                    if (!sim.State.Offer.Up && sim.State.HeldLevels == 0 && sim.Pickups.FlyingCount == 0) Next(end);
                    break;
                case BossRushStage.ActCard:
                    if (sim.State.RunClock >= untilClock - 1e-9) BeginAct(end);
                    break;
            }
        }

        /// <summary>The boss died at <paramref name="t"/> at (<paramref name="x"/>, <paramref name="y"/>) (<see cref="Bosses"/>'s call).</summary>
        internal void BossDied(double t, double x, double y)
        {
            if (Stage != BossRushStage.Fighting) return;
            Killed++;
            double rest = Slot.Experience - Shed;
            Shed = Slot.Experience;
            if (rest > 0) sim.Pickups.Drop(sim, PickupKind.Experience, x, y, rest);
            sim.Pickups.AttractAll(sim);
            sim.Health.Heal(sim, sim.State.Hero.MaxHealth, HealReason.BossKill);
            if (Boss >= rules.LastPlayed)
            {
                Won(t);
                return;
            }
            Enter(BossRushStage.Reward, t);
            sim.Progression.Treasure(sim);
        }

        internal ulong FoldHash(ulong h)
        {
            h = Hashing.Fold(h, (ulong)Stage | (ulong)(uint)Boss << 8 | (ulong)(uint)beatsLeft << 40);
            h = Hashing.Fold(h, Hashing.Bits(untilClock));
            return Hashing.Fold(h, Hashing.Bits(Shed));
        }

        // ---- The sequence ----

        void BeginAct(double t)
        {
            Checkpoint = Capture();
            Enter(BossRushStage.Act, t);
            sim.Zones.Hold(sim, t, rules.Acts[Act].Zone);
            Form(t);
        }

        void Form(double t)
        {
            beatsLeft = rules.FormBeats;
            untilClock = sim.State.RunClock + rules.FormBeats * beatlessBeatS;
            Enter(BossRushStage.Forming, t, rules.FormBeats * BeatS());
        }

        void Call(double t)
        {
            Shed = 0;
            Enter(BossRushStage.Fighting, t);
            sim.Bosses.Call(Slot.Cast, t, Slot.Health);
        }

        void Next(double t)
        {
            int next = rules.NextPlayed(Boss + 1);
            bool newAct = rules.Bosses[next].Act != Act;
            Boss = next;
            if (!newAct)
            {
                Form(t);
                return;
            }
            untilClock = sim.State.RunClock + rules.ActCardS;
            Enter(BossRushStage.ActCard, t, rules.ActCardS);
        }

        void Won(double t)
        {
            Enter(BossRushStage.Won, t);
            sim.End(RunEnd.Victory);
        }

        void Enter(BossRushStage stage, double t, double forS = 0)
        {
            Stage = stage;
            sim.HeroWas(t, out double hx, out double hy);
            sim.Emit(SimEvent.RushStage(t, stage, Boss, Act, forS, hx, hy));
        }

        /// <summary>The state now as the act's checkpoint, carrying the run's totals from the start's.</summary>
        BossRushCheckpoint Capture()
        {
            var deaths = new int[rules.Acts.Length];
            Array.Copy(start.Deaths, deaths, Math.Min(deaths.Length, start.Deaths.Length));
            return new BossRushCheckpoint
            {
                Boss = Boss,
                Build = BossRushCheckpoint.CardsOf(sim.State.Build),
                Level = sim.State.Level,
                Experience = sim.State.Experience,
                HeldLevels = sim.State.HeldLevels,
                Fallbacks = sim.Progression.Fallbacks,
                TimeBefore = start.TimeBefore + sim.State.RunClock,
                Deaths = deaths,
            };
        }

        double BeatS()
        {
            int last = sim.State.LastBeat;
            return sim.State.Beat == Reliability.Reliable && last >= 0 ? sim.Beats.TempoAt(last) : beatlessBeatS;
        }
    }
}
