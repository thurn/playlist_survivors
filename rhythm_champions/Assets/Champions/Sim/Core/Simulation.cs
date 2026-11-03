using System;

namespace Champions.Sim
{
    /// <summary>
    /// Rhythm Champions' deterministic simulation (plan.md, "The Sim"): fixed 1/120 s ticks, no wall
    /// clock, no Unity, no unseeded randomness. The client feeds it stream entries, inputs and time, and
    /// reads its state and event log; it never calls the client.
    ///
    /// <code>
    /// sim.Add(entry);                 // held until its known_at
    /// sim.Press(Key.Space, heard);    // inputs carry the song time they were heard at; recorded
    /// sim.Target(x, y, heard);        // a click target on the ground
    /// sim.Control(RunControl.Pause, heard);
    /// sim.StepTo(frontier);           // whole ticks
    /// sim.EventsSince(cursor); sim.Snapshot();
    /// </code>
    ///
    /// Only <see cref="Add"/>, <see cref="AddKnown"/>, <see cref="Press"/>, <see cref="Release"/>,
    /// <see cref="Target"/>, <see cref="Control"/>, <see cref="StepTo"/> and <see cref="DiscardEventsBefore"/>
    /// change the sim. Everything else public here and on what it exposes (<see cref="State"/>,
    /// <see cref="Stream"/>, <see cref="Beats"/>, <see cref="Judgement"/>, <see cref="Telegraphs"/>,
    /// <see cref="Cadence"/>, <see cref="Quiet"/>, <see cref="Progression"/>) is a read the client may make at any
    /// time without changing the run.
    ///
    /// Tick k covers song time (k/120, (k+1)/120]. Inside it, moments with exact times fire at those
    /// times, earliest first, and at one time in a fixed order: entries known then are revealed, then a
    /// beat fires (the attack cadence takes it), then the cadence's seconds pulse. The beat's state is
    /// updated before each of those and at the tick's end, after which the per-tick systems run: quiet
    /// moments, the run clock, the hero's walk, its dash, its health, then the attacks (<see cref="Arsenal"/>,
    /// which takes the tick's beats and strikes in time order), the horde (<see cref="Sim.Horde"/>, which
    /// acts on the tick's beats in time order), the bosses (<see cref="Sim.Bosses"/>, which a downbeat brings), the
    /// structures' chunks (<see cref="Sim.StructureField"/>, loaded round the hero), the zone key
    /// (<see cref="Sim.Zones"/>, which a beat moves on) and the progression (<see cref="Sim.Progression"/>: held levels
    /// show, the run starts after the first offer). Inputs take effect when given, at the frontier, judged from when
    /// they were heard (<see cref="HeroWalk"/>, <see cref="Dashes"/>, <see cref="Judgement"/>); while cards are up, key
    /// presses are theirs. Hits on the hero settle once the inputs heard by then are in (<see cref="HeroHealth"/>).
    /// Nothing simulated is ever revised.
    /// </summary>
    public sealed class Simulation
    {
        public const int TicksPerSecond = 120;
        public const double TickSeconds = 1.0 / TicksPerSecond;

        readonly StructList<InputRecord> inputs;
        readonly EventLog events;
        readonly HeroWalk hero;
        readonly Dashes dashes;
        readonly HeroHealth health;
        readonly RunRules run;
        readonly Horde horde;
        readonly double knownAtBudget;
        double cursor;

        public Simulation(SimConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            knownAtBudget = config.KnownAtBudget;
            Pickups = new Pickups(config.Progression);
            Random = new SimRandom(config.Seed);
            Stream = new BeliefLog(config);
            Beats = new BeatServices(Stream, config.Beat, config.TickCapacity);
            Judgement = new Judgement(Beats);
            Telegraphs = new Telegraphs(Beats);
            Cadence = new Cadence(Beats, config.Beat.CadenceTargetS);
            Quiet = new QuietMoments(Stream, config.Beat);
            events = new EventLog(config.EventCapacity);
            inputs = new StructList<InputRecord>(config.InputCapacity);
            State = new SimState { Tick = (long)Math.Floor(config.StartTime * TicksPerSecond + 1e-9) };
            State.Now = State.Tick * TickSeconds;
            cursor = State.Now;
            run = new RunRules(config, State);
            if (State.Phase == RunPhase.Playing) Emit(SimEvent.RunStart(State.Now));
            hero = new HeroWalk(State.Hero, config.Hero, State.Now);
            Strikes = new Strikes(config.TargetCapacity);
            health = new HeroHealth(State.Hero, config.Hero, config.Beat);
            dashes = new Dashes(config.Dashes, State, hero, health, Strikes, Beats, config.Beat);
            Cadence.Begin(State.Now);
            Quiet.Begin(State.Now);
            Enemies = new Enemies(config.EnemyCapacity);
            Strikes.Add(Enemies);
            State.Build = new WeaponBuild(config.Combat);
            if (config.SurvivorLoadout.Length == 0) State.Build.Set(WeaponKind.Sword, 1);
            foreach (var card in config.SurvivorLoadout)
            {
                if (!config.SurvivorWeapons) throw new ArgumentException("survivor preset needs survivor weapons");
                if (card.Kind == CardKind.Weapon && State.Build.Level(card.Weapon) == 0) State.Build.Set(card.Weapon, card.Rank);
                else if (card.Kind == CardKind.Support && State.Build.Level(card.Support) == 0) State.Build.Set(card.Support, card.Rank);
                else throw new ArgumentException("invalid or duplicate survivor preset item");
            }
            if (config.SurvivorWeapons && State.Build.Level(WeaponKind.Sword) == 0) State.Build.Set(WeaponKind.Sword, 1);
            Weapons = new WeaponScheduler(this);
            Arsenal = new Arsenal(config.Attacks, config.Mods, Enemies, Beats, config.EnemyCapacity);
            Arsenal.Begin(this);
            foreach (var u in config.Loadout) Arsenal.Set(this, u);
            foreach (var d in config.Dummies) Enemies.Spawn(this, d.X, d.Y, d.Radius, d.Health);
            if (config.Structures != null)
            {
                StructureField = new StructureField(this, config.Structures, config.Seed);
                UseStructures(StructureField);
            }
            horde = new Horde(this, config, Enemies);
            Zones = new Zones(config.Zones);
            State.Zone = Zones.Key;
            Bosses = new Bosses(this, config);
            Progression = new Progression(this, config);
            if (config.SurvivorWeapons)
            {
                Melee = new MeleeWeapons(this);
                Ranged = new RangedWeapons(this);
                Constructs = new ConstructWeapons(this);
                Magic = new MagicWeapons(this);
            }
        }

        public SimState State { get; }
        /// <summary>The run's one generator: the sim's alone, since every draw changes the rest of the run.</summary>
        internal SimRandom Random { get; }
        public BeliefLog Stream { get; }
        public BeatServices Beats { get; }
        public Judgement Judgement { get; }
        public Telegraphs Telegraphs { get; }
        /// <summary>The attack cadence: Sword Slash's, aiming at <see cref="BeatRules.CadenceTargetS"/>.</summary>
        public Cadence Cadence { get; }
        public QuietMoments Quiet { get; }
        /// <summary>The hero's blows' one way into the world (<see cref="Sim.Strikes"/>).</summary>
        public Strikes Strikes { get; }
        /// <summary>Every enemy, and the one grid over them.</summary>
        public Enemies Enemies { get; }
        /// <summary>The horde: its mobs' motion, elite moves, spores and, with a level, the spawner.</summary>
        public Horde Horde => horde;

        /// <summary>The floor's zone key (the dragon's entrance turns it warm).</summary>
        public Zones Zones { get; }

        /// <summary>The bosses: their calls, entrances and duels.</summary>
        public Bosses Bosses { get; }

        /// <summary>The hero's dash (the progression equips, the mods tune it).</summary>
        internal Dashes Dashes => dashes;

        /// <summary>The hero's health (the horde and bosses hurt it, the progression heals it).</summary>
        internal HeroHealth Health => health;

        /// <summary>The structures the hero meets, once installed (<see cref="UseStructures"/>); null: nothing blocks.</summary>
        internal IStructures Structures { get; private set; }

        /// <summary>The world's structures in their chunks, with a level (<see cref="SimConfig.Structures"/>); null without.</summary>
        public StructureField StructureField { get; }
        /// <summary>The hero's attacks and mods, and every blow it deals.</summary>
        public Arsenal Arsenal { get; }

        public WeaponScheduler Weapons { get; }
        public MeleeWeapons Melee { get; }
        public RangedWeapons Ranged { get; }
        public ConstructWeapons Constructs { get; }
        public MagicWeapons Magic { get; }

        /// <summary>Experience, held levels, the offers and the card moment, and treasure.</summary>
        public Progression Progression { get; }
        public Pickups Pickups { get; }

        /// <summary>The frontier: the song time simulated to.</summary>
        public double Now => State.Now;

        /// <summary>The cursor after the latest event.</summary>
        public int EventCount => events.Count;

        /// <summary>The hash of the settled event log alone.</summary>
        public ulong EventHash => events.Hash;

        /// <summary>
        /// The run's state hash: the settled event log's hash folded with the state at the frontier. A
        /// session records it; a replay must reproduce it.
        /// </summary>
        public ulong Hash
        {
            get
            {
                ulong h = events.Hash;
                h = Hashing.Fold(h, (ulong)State.Tick);
                h = Hashing.Fold(h, (ulong)State.Phase | (ulong)State.End << 8 | (ulong)(uint)State.Holds << 16);
                h = Hashing.Fold(h, Hashing.Bits(State.RunClock));
                if (!Dashes.Rules.LegacyForms && Bosses.Called > 0) h = Hashing.Fold(h, Hashing.Bits(State.HordeClock));
                h = Hashing.Fold(h, (ulong)State.Beat | (ulong)State.Quiet << 8 | (ulong)(uint)State.CadenceBeats << 16);
                h = Hashing.Fold(h, (ulong)(uint)State.LastBeat);
                h = Hashing.Fold(h, Hashing.Bits(State.Hero.X));
                h = Hashing.Fold(h, Hashing.Bits(State.Hero.Y));
                h = Hashing.Fold(h, Hashing.Bits(State.Hero.Health));
                h = Hashing.Fold(h, (ulong)(uint)State.Hero.Dashes | (ulong)(uint)Strikes.Struck << 32);
                h = Hashing.Fold(h, Hashing.Bits(State.Hero.DashReadyAt));
                h = Hashing.Fold(h, (ulong)(uint)Enemies.Alive | (ulong)(uint)Arsenal.Kills << 32);
                h = Hashing.Fold(h, (ulong)(uint)horde.Spawned);
                h = Hashing.Fold(h, (ulong)(uint)inputs.Count);
                h = Hashing.Fold(h, (ulong)(uint)Stream.Accepted.Length);
                if (StructureField != null) h = Hashing.Fold(h, (ulong)(uint)StructureField.Standing | (ulong)(uint)StructureField.Breaks << 32);
                // Only once a boss was called, so the sessions before bosses keep their hashes.
                if (Bosses.Called > 0)
                {
                    h = Hashing.Fold(h, (ulong)Bosses.Stage | (ulong)(uint)Bosses.Phase << 8 | (ulong)(uint)Bosses.Marks.Length << 16 | (ulong)(uint)Bosses.Called << 32);
                    h = Hashing.Fold(h, Hashing.Bits(Bosses.Break));
                    h = Hashing.Fold(h, Hashing.Bits(Bosses.Health));
                }
                // Only once the progression has moved, so the sessions before it keep their hashes.
                if (State.Experience > 0 || State.Level > 1 || Progression.Picks > 0 || State.Offer.Up)
                {
                    h = Hashing.Fold(h, (ulong)(uint)State.Level | (ulong)(uint)State.HeldLevels << 32);
                    h = Hashing.Fold(h, Hashing.Bits(State.Experience));
                    h = Hashing.Fold(h, (ulong)(uint)Progression.Picks | (ulong)State.Offer.Kind << 32 | (ulong)(uint)(State.Offer.Focus + 1) << 40);
                }
                if (Progression.Fallbacks > 0) h = Hashing.Fold(h, (ulong)(uint)Progression.Fallbacks);
                if (Pickups.ExperienceDropped > 0 || Pickups.StoredCount > 0 || Pickups.FlyingCount > 0) h = Pickups.Fold(h);
                if (Constructs != null) h = Constructs.FoldHash(h);
                h = Weapons.FoldHash(h);
                if (Ranged != null) h = Ranged.FoldHash(h);
                return Magic == null ? h : Magic.FoldHash(h);
            }
        }

        // ---- Entries ----

        /// <summary>
        /// Accepts an arriving entry. It is known from <c>max(Stamp + B, frontier)</c>, the frontier being
        /// where the next <see cref="StepTo"/> takes the sim, the end of the frame it arrived in.
        /// </summary>
        public void Add(in StreamEntry e) => Stream.Arrive(e);

        /// <summary>Accepts a replayed entry with its recorded known-at time.</summary>
        public void AddKnown(in StreamEntry e, double knownAt) => Stream.AddKnown(e, knownAt, Now);

        // ---- Inputs ----

        /// <summary>Every input taken, in order: a session records them.</summary>
        public ReadOnlySpan<InputRecord> Inputs => inputs.AsSpan();

        int pressedKeys, blockedKeys;
        /// <summary>A key pressed, heard at song time <paramref name="heard"/>; it takes effect now. While cards are up it is theirs.</summary>
        public void Press(Key key, double heard)
        {
            inputs.Add(new InputRecord { Kind = InputKind.Press, Code = (byte)key, Heard = heard, Effective = Now });
            health.Took(heard, Now);
            int bit = 1 << (int)key;
            if (!dashes.Rules.LegacyForms)
            {
                if (key == Key.F && (pressedKeys & bit) != 0) return;
                pressedKeys |= bit;
                if ((blockedKeys & bit) != 0) return;
            }
            if (Progression.Press(this, key))
            {
                if (!dashes.Rules.LegacyForms) blockedKeys |= bit;
                return;
            }
            hero.Press(this, key, heard);
            if (key == Key.Space || key == Key.F) dashes.Press(this, heard, key == Key.F);
        }

        /// <summary>A key released, heard at song time <paramref name="heard"/>; it takes effect now.</summary>
        public void Release(Key key, double heard)
        {
            inputs.Add(new InputRecord { Kind = InputKind.Release, Code = (byte)key, Heard = heard, Effective = Now });
            health.Took(heard, Now);
            pressedKeys &= ~(1 << (int)key); blockedKeys &= ~(1 << (int)key);
            hero.Release(this, key, heard);
        }

        /// <summary>A click target, the ground point (<paramref name="x"/>, <paramref name="y"/>), heard at song time <paramref name="heard"/>.</summary>
        public void Target(double x, double y, double heard)
        {
            inputs.Add(new InputRecord { Kind = InputKind.Target, X = x, Y = y, Heard = heard, Effective = Now });
            health.Took(heard, Now);
            hero.Target(this, x, y, heard);
        }

        /// <summary>A run control (<see cref="RunControl"/>), heard at song time <paramref name="heard"/>; it takes effect now.</summary>
        public void Control(RunControl control, double heard)
        {
            inputs.Add(new InputRecord { Kind = InputKind.Control, Code = (byte)control, Heard = heard, Effective = Now });
            health.Took(heard, Now);
            bool held = State.Held;
            run.Control(this, control, Now);
            if (State.Held != held) HeldChanged();
        }

        /// <summary>Gives a recorded input again (a replay's), at the sim's time now.</summary>
        public void Give(in InputRecord input)
        {
            switch (input.Kind)
            {
                case InputKind.Press: Press(input.Key, input.Heard); break;
                case InputKind.Release: Release(input.Key, input.Heard); break;
                case InputKind.Target: Target(input.X, input.Y, input.Heard); break;
                case InputKind.Control: Control(input.Control, input.Heard); break;
                default: throw new ArgumentException($"an input of unknown kind {input.Kind}");
            }
        }

        /// <summary>Judges a press heard at song time <paramref name="heard"/> on the beat or not, from what is known now.</summary>
        public Judged Judge(double heard) => Judgement.Judge(heard);

        /// <summary>Where the hero was at song time <paramref name="t"/>, up to <see cref="HeroWalk.History"/> ticks back (the re-walked path).</summary>
        public void HeroWas(double t, out double x, out double y) => hero.PositionWas(t, out x, out y);

        // ---- Hooks for the systems ----

        /// <summary>Holds the world for <paramref name="reason"/> or lets it go (the card moment's).</summary>
        internal void Hold(HoldReason reason, bool on)
        {
            bool held = State.Held;
            run.Hold(this, reason, on, Now);
            if (State.Held != held) HeldChanged();
        }

        /// <summary>Starts a waiting run at <paramref name="t"/> (the progression's, after the first offer).</summary>
        internal void StartRun(double t)
        {
            bool held = State.Held;
            run.Start(this, t);
            if (State.Held != held) HeldChanged();
        }

        /// <summary>Ends the run (death, victory).</summary>
        internal void End(RunEnd why)
        {
            bool held = State.Held;
            run.End(this, why, Now);
            if (State.Held != held) HeldChanged();
        }

        double heldSince, heldTotal;
        public double CombatTime(double t) => (State.Held ? Math.Min(t, heldSince) : t) - heldTotal;
        /// <summary>Elapsed card/pause time; weapon geometry can shift its outstanding deadlines here.</summary>
        public event Action<double> Resumed;
        void HeldChanged()
        {
            if (!dashes.Rules.LegacyForms)
            {
                if (State.Held) heldSince = Now;
                else
                {
                    double elapsed = Now - heldSince;
                    heldTotal += elapsed;
                    hero.Resume(elapsed);
                    dashes.Resume(elapsed);
                    health.Resume(elapsed);
                    horde.Resume(elapsed);
                    Bosses.Resume(elapsed);
                    Resumed?.Invoke(elapsed);
                }
            }
            hero.HeldChanged(this);
            horde.HeldChanged();
            Bosses.HeldChanged();
        }

        /// <summary>The hero lets go of its held keys and target, so a key held through the cards never walks it after.</summary>
        internal void LetGoOfHeldKeys()
        {
            if (!dashes.Rules.LegacyForms) blockedKeys |= pressedKeys;
            hero.LetGo();
        }

        internal void Emit(in SimEvent e) => events.Add(e);

        /// <summary>Installs the world's structures: they block the hero and take its blows (the structures' bead).</summary>
        internal void UseStructures(IStructures structures)
        {
            Structures = structures;
            Strikes.Add(structures);
        }

        // ---- Output ----

        public ReadOnlySpan<SimEvent> EventsSince(int eventCursor) => events.Since(eventCursor);

        /// <summary>Frees the events before <paramref name="eventCursor"/> once the client has read them.</summary>
        public void DiscardEventsBefore(int eventCursor) => events.DiscardBefore(eventCursor);

        /// <summary>A copy of the state at the frontier, for the client's history.</summary>
        public SimSnapshot Snapshot()
        {
            var h = State.Hero;
            return new SimSnapshot
            {
                Now = State.Now,
                Tick = State.Tick,
                Phase = State.Phase,
                RunClock = State.RunClock,
                HordeClock = State.HordeClock,
                Holds = State.Holds,
                Beat = State.Beat,
                LastBeat = State.LastBeat,
                Bar = State.Bar,
                Downbeat = State.Downbeat,
                CadenceBeats = State.CadenceBeats,
                Quiet = State.Quiet,
                HeroFromX = h.FromX,
                HeroFromY = h.FromY,
                HeroX = h.X,
                HeroY = h.Y,
                HeroMoveStart = h.MoveStart,
                HeroMoveEnd = h.MoveEnd,
                HeroFacingX = h.FacingX,
                HeroFacingY = h.FacingY,
                HeroHealth = h.Health,
                HeroMaxHealth = h.MaxHealth,
                HeroHurtAt = h.HurtAt,
                Dash = h.Dash,
                DashRank = h.DashRank,
                DashStart = h.DashStart,
                DashEnd = h.DashEnd,
                DashOnBeat = h.DashOnBeat,
                DashReadyAt = h.DashReadyAt,
                DashCooldownS = h.DashCooldownS,
                DashWaiting = h.DashWaiting,
                Ready = Arsenal.Ready,
                Phantoms = Arsenal.PhantomCount,
                OrbitSwords = Arsenal.OrbitSwords,
                OrbitAngle = Arsenal.OrbitAngle,
                OrbitRadius = Arsenal.OrbitRadius,
                EnemiesAlive = Enemies.Alive,
                Zone = State.Zone,
                Boss = Bosses.InDuel ? Bosses.Id : -1,
                BossCast = Bosses.InDuel ? Bosses.Cast : -1,
                BossHealth = Bosses.InDuel ? Bosses.Health : 0,
                BossMaxHealth = Bosses.InDuel ? Bosses.MaxHealth : 0,
                BossBreak = Bosses.InDuel ? Bosses.Break : 0,
                BossPhase = Bosses.Phase,
                BossElapsed = Bosses.DuelElapsed,
                BossStaggerFrom = Bosses.StaggerFrom,
                BossStaggerUntil = Bosses.StaggerUntil,
                Level = State.Level,
                Experience = State.Experience,
                ExperienceNeeded = State.ExperienceNeeded,
                HeldLevels = State.HeldLevels,
                Offer = State.Offer,
            };
        }

        // ---- Stepping ----

        /// <summary>Simulates whole ticks up to song time <paramref name="songTime"/>.</summary>
        public void StepTo(double songTime)
        {
            long target = (long)Math.Floor(songTime * TicksPerSecond + 1e-9);
            Stream.SettleArrivals(knownAtBudget, Math.Max(Now, target * TickSeconds), Now);
            while (State.Tick < target) RunTick();
        }

        void RunTick()
        {
            Ranged?.BindOwned();
            double end = (State.Tick + 1) * TickSeconds;
            while (true)
            {
                double reveal = Stream.NextKnownAt;
                double beat = Beats.NextTime(cursor);
                double pulse = Cadence.NextPulse;
                double weaponPulse = Weapons.NextPulse;
                double next = Math.Min(Math.Min(beat, pulse), weaponPulse);
                if (reveal <= next && reveal <= end)
                {
                    cursor = Math.Max(cursor, reveal);
                    Stream.Reveal(cursor);
                    Beats.TakeTicks();
                    continue;
                }
                if (next > end) break;
                cursor = Math.Max(cursor, next);
                UpdateBeatState(cursor);
                if (beat == next) FireBeat(beat);
                else if (pulse == next) FirePulse(cursor);
                else Weapons.Pulse(cursor);
            }
            cursor = end;
            State.Tick++;
            State.Now = end;
            UpdateBeatState(end);
            if (Quiet.Tick(end, State.Beat))
            {
                Emit(SimEvent.Quiet(end, Quiet.Reason, State.Quiet, Quiet.Since));
                State.Quiet = Quiet.Reason;
                State.QuietSince = Quiet.Since;
            }
            run.Tick(this);
            hero.Tick(this, end);
            dashes.Tick(this, end);
            health.ApplySupports(this);
            health.Tick(this, end);
            Arsenal.Tick(this, end);
            Weapons.Plan(end);
            Ranged?.Tick(end);
            horde.Tick(end);
            Bosses.Tick(end);
            StructureField?.Tick(this, end);
            Zones.Tick(this, end);
            Pickups.Tick(this);
            Progression.Tick(this, end);
        }

        void FireBeat(double t)
        {
            int i = Beats.Fire();
            int bar = Beats.BarOf(i, out bool downbeat);
            var beats = Beats.Revealed;
            Emit(SimEvent.Beat(t, i, bar, downbeat, Beats.StrongBeat(i), State.Beat, Beats.PeriodOf(i), beats[i].V1, Beats.TempoAt(i)));
            State.LastBeat = i;
            State.Bar = bar;
            State.Downbeat = downbeat;
            Progression.Beat(this, t, downbeat);
            Arsenal.Beat(this, t, i, downbeat, bar);
            Weapons.Beat(t, i);
            horde.Beat(t, i);
            Bosses.Beat(t, i, downbeat);
            Zones.Beat(this, t, Beats.PeriodOf(i));
            Cadenced(t, i, Cadence.OnBeat(i, t, State.Beat == Reliability.Reliable));
        }

        void FirePulse(double t)
        {
            Cadence.FirePulse(t);
            Cadenced(t, -1, CadenceStep.Strike);
        }

        void Cadenced(double t, int beat, CadenceStep step)
        {
            if ((step & CadenceStep.Changed) != 0)
            {
                Emit(SimEvent.Cadence(t, Cadence.Beats, State.CadenceBeats, Cadence.LockPeriod));
                State.CadenceBeats = Cadence.Beats;
            }
            if ((step & CadenceStep.Strike) == 0) return;
            Emit(SimEvent.Pulse(t, beat, Cadence.Beats, Cadence.PeriodS));
            State.LastPulse = t;
            Arsenal.Cadenced(t, beat);
        }

        void UpdateBeatState(double t)
        {
            var now = Beats.StateAt(t);
            if (now == State.Beat) return;
            Emit(SimEvent.BeatState(t, now, State.Beat));
            State.Beat = now;
            Arsenal.BeatState(t, now);
            Weapons.ReliabilityChanged(t);
            if (now != Reliability.Reliable) Cadenced(t, -1, Cadence.Beatless(t));
        }
    }
}
