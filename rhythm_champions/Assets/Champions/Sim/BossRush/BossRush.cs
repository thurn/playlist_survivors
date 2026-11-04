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
        // Shedding (boss-rush.md, Progression): the thresholds the boss has passed, those still to burst on the next beat,
        // and the run clock the oldest of those passed at (the beatless fallback's).
        int shedPassed, shedPending;
        double shedSince;

        internal BossRush(Simulation sim, SimConfig config)
        {
            this.sim = sim;
            rules = config.BossRush;
            beatlessBeatS = config.Beat.TelegraphBeatlessBeatS;
            start = config.BossRushStart ?? new BossRushCheckpoint();
            if (start.Boss < 0 || start.Boss >= rules.Count) throw new ArgumentException($"boss rush start: no boss {start.Boss + 1} of {rules.Count}");
            sim.Progression.Restore(sim, start.Level, start.Experience, start.HeldLevels, start.Fallbacks);
            // A landed fight sheds by its own thresholds and gem spots (BossFight.Shed); a stand-in by the content's shape.
            sim.Fight.Shed += FightShed;
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

        /// <summary>The XP the boss now has shed so far (its bursts and its adds); its kill drops the rest.</summary>
        public double Shed { get; internal set; }

        /// <summary>The bursts the boss now has shed (one per <see cref="BossRushRules.ShedStep"/> of health lost).</summary>
        public int Bursts { get; private set; }

        /// <summary>
        /// The boss's health share now (1 full, 0 dead): what its thresholds are read from. The main game's stand-ins'
        /// own, or a landed fight's (<see cref="BossFight.HealthShare"/>, every body together).
        /// </summary>
        public double HealthShare => Slot.Fight && sim.Fight.InFight ? sim.Fight.HealthShare
            : sim.Bosses.InDuel && sim.Bosses.MaxHealth > 0 ? Math.Max(0, sim.Bosses.Health / sim.Bosses.MaxHealth) : 1;

        /// <summary>
        /// A boss's add (or any other mechanic of the boss's) wants <paramref name="want"/> XP: what of the boss's budget is
        /// left for it, now counted shed. Adds draw from the same fixed budget (boss-rush.md, Progression), so the kill's
        /// drop shrinks by as much; past the budget an add yields nothing.
        /// </summary>
        internal double Draw(double want)
        {
            if (Stage != BossRushStage.Fighting || !(want > 0)) return 0;
            double got = Math.Min(want, Math.Max(0, Slot.Experience - Shed));
            Shed += got;
            return got;
        }

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

        /// <summary>A beat fired at <paramref name="t"/>: forming counts its beats; a fight's passed thresholds burst.</summary>
        internal void Beat(double t)
        {
            if (sim.State.Held || sim.State.Beat != Reliability.Reliable) return;
            if (Stage == BossRushStage.Fighting && shedPending > 0) BurstPending(t);
            if (Stage != BossRushStage.Forming) return;
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
                case BossRushStage.Fighting:
                    PassThresholds();
                    // Without a reliable beat a passed threshold bursts one fallback beat after it was passed.
                    if (shedPending > 0 && sim.State.Beat != Reliability.Reliable && sim.State.RunClock >= shedSince + beatlessBeatS - 1e-9) BurstPending(end);
                    break;
                case BossRushStage.Reward:
                    if (!sim.State.Offer.Up && sim.State.HeldLevels == 0 && sim.Pickups.FlyingCount == 0) Next(end);
                    break;
                case BossRushStage.ActCard:
                    if (sim.State.RunClock >= untilClock - 1e-9) BeginAct(end);
                    break;
            }
        }

        /// <summary>The boss died at <paramref name="t"/> at (<paramref name="x"/>, <paramref name="y"/>) (<see cref="Bosses"/>'s or <see cref="BossFight"/>'s call).</summary>
        internal void BossDied(double t, double x, double y)
        {
            if (Stage != BossRushStage.Fighting) return;
            Killed++;
            shedPending = 0;
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
            h = Hashing.Fold(h, (ulong)(uint)shedPassed | (ulong)(uint)shedPending << 16 | (ulong)(uint)Bursts << 32);
            h = Hashing.Fold(h, Hashing.Bits(shedSince));
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
            // The slot's arena draws itself in round the hero (BossArena; the previous one goes as it does).
            if (sim.Arena != null) sim.Arena.FormAroundHero(sim.Arena.Rules.Find(Slot.Arena), t);
        }

        void Call(double t)
        {
            Shed = 0;
            shedPassed = shedPending = Bursts = 0;
            shedSince = 0;
            Enter(BossRushStage.Fighting, t);
            // A landed fight runs its module (BossFight); a slot still waiting for one is fought by its stand-in duel.
            if (Slot.Fight) sim.Fight.Start(Slot.Id, t, new FightSetup { Act = Act + 1, Health = Slot.Health });
            else sim.Bosses.Call(Slot.Cast, t, Slot.Health);
        }

        // ---- Shedding (boss-rush.md, Progression > Shedding) ----

        /// <summary>The thresholds the boss's health has fallen past since the last tick, each queued for the next beat.</summary>
        void PassThresholds()
        {
            if (Slot.Fight) return;
            int thresholds = Thresholds;
            double share = HealthShare;
            while (shedPassed < thresholds && share <= 1 - (shedPassed + 1) * rules.ShedStep + 1e-9)
            {
                shedPassed++;
                if (shedPending++ == 0) shedSince = sim.State.RunClock;
            }
        }

        /// <summary>The bursts a fight sheds before its kill: one per step down to the last step (9 at 10%).</summary>
        int Thresholds => Math.Max(0, (int)Math.Round(1 / rules.ShedStep) - 1);

        void BurstPending(double t)
        {
            if (!sim.Bosses.InDuel) { shedPending = 0; return; }
            ref readonly var b = ref sim.Enemies[sim.Bosses.Id];
            while (shedPending > 0)
            {
                shedPending--;
                double value = Math.Min(Slot.Experience * rules.ShedStep, Math.Max(0, Slot.Experience - Shed));
                Bursts++;
                Shed += value;
                Burst(sim, t, Bursts, Slot.Shed, b.X, b.Y, sim.Bosses.Facing, value);
                sim.Emit(SimEvent.RushShed(t, Bursts, Boss, value, b.X, b.Y));
            }
        }

        /// <summary>
        /// A landed fight's burst: an equal step of the slot's budget (one of its module's thresholds plus the kill's),
        /// dropped at the module's authored spots by their shares. The kill's drop (<see cref="BossDied"/>) takes the rest.
        /// </summary>
        void FightShed(Simulation s, BossFight fight, int burst, System.Collections.Generic.IReadOnlyList<GemSpot> spots)
        {
            if (Stage != BossRushStage.Fighting || !Slot.Fight || spots.Count == 0) return;
            double value = Math.Min(Slot.Experience / (fight.Module.ShedThresholds + 1), Math.Max(0, Slot.Experience - Shed));
            if (!(value > 0)) return;
            double shares = 0;
            foreach (var g in spots) shares += g.Share;
            Bursts++;
            Shed += value;
            foreach (var g in spots) sim.Pickups.Drop(sim, PickupKind.Experience, g.X, g.Y, value * g.Share / shares);
            ref readonly var lead = ref fight.Lead;
            sim.Emit(SimEvent.RushShed(fight.ShedAt, Bursts, Boss, value, lead.X, lead.Y));
        }

        /// <summary>
        /// Burst <paramref name="index"/> (1 up) of <paramref name="value"/> XP from (<paramref name="x"/>, <paramref name="y"/>),
        /// the boss facing <paramref name="facing"/>: <paramref name="shape"/>'s gems, each an equal share (an arena keeps them
        /// inside its walls once it has them).
        /// </summary>
        internal static void Burst(Simulation sim, double t, int index, BossShedShape shape, double x, double y, double facing, double value)
        {
            if (!(value > 0)) return;
            int n = shape.Gems;
            double each = value / n;
            for (int i = 0; i < n; i++)
            {
                Spot(shape, index, i, facing, out double dx, out double dy);
                sim.Pickups.Drop(sim, PickupKind.Experience, x + dx, y + dy, each);
            }
        }

        /// <summary>Gem <paramref name="i"/> of burst <paramref name="index"/>'s offset from the boss.</summary>
        internal static void Spot(BossShedShape shape, int index, int i, double facing, out double dx, out double dy)
        {
            int n = shape.Gems;
            switch (shape.Kind)
            {
                case BossShedKind.Spray:
                {
                    // A fan ahead, alternating near and far rows so neighbours never share a pickup cell.
                    double spread = shape.SpreadDeg * Math.PI / 180;
                    double a = facing + (n == 1 ? 0 : (i / (double)(n - 1) - 0.5) * spread);
                    double r = shape.RadiusM * (i % 2 == 0 ? 1 : 0.65);
                    dx = Math.Cos(a) * r; dy = Math.Sin(a) * r;
                    return;
                }
                case BossShedKind.Lane:
                {
                    // A line across the boss's front, RadiusM ahead of it.
                    double along = n == 1 ? 0 : (i / (double)(n - 1) - 0.5) * shape.LengthM;
                    double fx = Math.Cos(facing), fy = Math.Sin(facing);
                    dx = fx * shape.RadiusM - fy * along; dy = fy * shape.RadiusM + fx * along;
                    return;
                }
                default:
                {
                    // Evenly round it, each burst turned by half a gap from the last.
                    double a = (i + 0.5 * index) * 2 * Math.PI / n;
                    dx = Math.Cos(a) * shape.RadiusM; dy = Math.Sin(a) * shape.RadiusM;
                    return;
                }
            }
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
