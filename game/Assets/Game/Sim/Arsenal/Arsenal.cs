using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>An owned weapon or passive: its content index and rank.</summary>
    public struct OwnedItem
    {
        public int Index;
        public int Rank;
    }

    /// <summary>
    /// The hero's arsenal (unity-game.md, The Other Weapons; native's survivors/arsenal.rs): six
    /// weapon slots and six passive slots, the stats the passives make, the weapons' shots, and the
    /// gems and level-ups that grow it (<see cref="Gems"/>, <see cref="LevelUps"/>). It is one timed
    /// source in the sim's tick: its slots' times in slot order, then the gems'.
    ///
    /// - <b>Ranks</b>: a pick adds a weapon at rank 1 to a free slot or raises an owned one by a
    ///   rank, to its top (8 for a base weapon); passives likewise, to theirs (5, Duplicator 2).
    /// - <b>Stats</b>: progression.json's base stats plus every passive rank's change (Hollow
    ///   Heart's compounds max health by 20% a rank), clamped to the caps. Max health and Armor
    ///   reach the hero's health at once, and added max health is healed, as in Vampire Survivors.
    ///   Recovery heals that much health a second of run clock.
    /// - <b>Shots</b> (interval weapons, <see cref="Weapon"/>): each slot's cooldown counts on the
    ///   run clock from when the weapon was learned, its interval times Cooldown at a time, never
    ///   from when a shot fired, so snapping never loses throughput. A shot ready at song time s
    ///   fires on the first point of the weapon's grid (beats or eighths) at or after s among the
    ///   beats revealed then, the latest one's period carrying its grid on while the next is not
    ///   yet known (<see cref="BeatClock.GridPoint"/>); one that would share its slot's previous
    ///   point moves to the sixteenth after it. With no beat known ahead it fires at s: the raw
    ///   interval. A freeze unplans the
    ///   shots (the run clock stops, so no more come ready) and the resume plans them again.
    /// - Nothing shoots while the world is frozen or the hero has fallen.
    /// </summary>
    internal sealed class Arsenal
    {
        const double Eps = 1e-9;
        /// <summary>Shots ready but not fired, per slot: a few at most, unless a weapon's interval is far below its grid's.</summary>
        const int ShotCapacity = 32;

        /// <summary>A shot come ready: its song time and fractional beat on the grid, NaN while unplanned (the beat NaN off the grid).</summary>
        struct Shot
        {
            public double Fire, Beat;
        }

        sealed class Slot
        {
            public Weapon Weapon;
            /// <summary>The run clock time the next shot comes ready.</summary>
            public double NextReady;
            public readonly Shot[] Shots = new Shot[ShotCapacity];
            public int Count;
            public double LastFire = double.NegativeInfinity, LastBeat = double.NaN;
        }

        readonly GameContent content;
        readonly SimState state;
        readonly BeatClock clock;
        readonly RunRules run;
        readonly HeroHealth health;
        readonly Func<WeaponDef, int, Weapon> make;
        readonly Slot[] slots;
        readonly OwnedItem[] passives;
        Stats raw;
        double recoveredTo;

        public Arsenal(ArsenalConfig config, GameContent content, SimState state, BeliefLog log, BeatClock clock, RunRules run,
            HeroHealth health, Func<Arsenal, WeaponDef, int, Weapon> weapon)
        {
            if (content == null) throw new ArgumentException("the arsenal needs content");
            this.content = content;
            this.state = state;
            this.clock = clock;
            this.run = run;
            this.health = health;
            make = (def, index) => weapon(this, def, index);
            var progression = content.Progression;
            slots = new Slot[progression.WeaponSlots];
            passives = new OwnedItem[progression.PassiveSlots];
            state.WeaponStore = new OwnedItem[progression.WeaponSlots];
            state.PassiveStore = passives;
            raw = config.Stats ?? progression.Hero.Stats;
            ApplyStats();
            state.Hero.MaxHealth = state.Hero.Health = Stats.MaxHp;
            foreach (var (id, rank) in config.Weapons)
            {
                int w = content.WeaponIndex(id);
                if (w < 0) throw new ArgumentException("no weapon '" + id + "'");
                for (int r = 0; r < rank; r++) Learn(w);
            }
            foreach (var (id, rank) in config.Passives)
            {
                int p = content.PassiveIndex(id);
                if (p < 0) throw new ArgumentException("no passive '" + id + "'");
                for (int r = 0; r < rank; r++) LearnPassive(state.Now, p);
            }
            recoveredTo = state.RunClock;
            Gems = new Gems(content.Progression.Gems, state, clock, this);
            LevelUps = new LevelUps(config.Cards, content, state, log, clock, run, health, this);
        }

        public Gems Gems { get; }
        public LevelUps LevelUps { get; }

        /// <summary>The hero's final stats: base, passives, caps.</summary>
        public Stats Stats { get; private set; }

        public ReadOnlySpan<OwnedItem> Weapons => state.WeaponSlots;
        public ReadOnlySpan<OwnedItem> Passives => state.PassiveSlots;

        public bool WeaponSlotsFull => state.WeaponCount == slots.Length;
        public bool PassiveSlotsFull => state.PassiveCount == passives.Length;

        /// <summary>A weapon's rank, 0 when not owned.</summary>
        public int WeaponRank(int weapon)
        {
            foreach (var w in Weapons)
                if (w.Index == weapon) return w.Rank;
            return 0;
        }

        public int PassiveRank(int passive)
        {
            foreach (var p in Passives)
                if (p.Index == passive) return p.Rank;
            return 0;
        }

        /// <summary>Is an evolution of base weapon <paramref name="weapon"/> owned?</summary>
        public bool OwnsEvolutionOf(int weapon)
        {
            string id = content.Weapons[weapon].Id;
            foreach (var w in Weapons)
                if (content.Weapons[w.Index].EvolutionOf == id) return true;
            return false;
        }

        /// <summary>The first sword-shaped weapon owned, or null.</summary>
        public Sword Sword
        {
            get
            {
                for (int s = 0; s < state.WeaponCount; s++)
                    if (slots[s].Weapon is Sword sword) return sword;
                return null;
            }
        }

        /// <summary>A pick of weapon <paramref name="weapon"/>: rank 1 in a free slot, ready on the run clock now, or one rank more.</summary>
        public void Learn(int weapon)
        {
            for (int s = 0; s < state.WeaponCount; s++)
            {
                if (slots[s].Weapon.Index != weapon) continue;
                var w = slots[s].Weapon;
                if (w.Rank >= w.Def.MaxRank) throw new InvalidOperationException(w.Def.Id + " is at its top rank");
                state.WeaponStore[s].Rank = ++w.Rank;
                return;
            }
            if (WeaponSlotsFull) throw new InvalidOperationException("no free weapon slot");
            int slot = state.WeaponCount++;
            slots[slot] = new Slot { Weapon = make(content.Weapons[weapon], weapon), NextReady = state.RunClock };
            state.WeaponStore[slot] = new OwnedItem { Index = weapon, Rank = 1 };
        }

        /// <summary>A pick of passive <paramref name="passive"/> at <paramref name="t"/>: rank 1 in a free slot or one rank more, and its stats.</summary>
        public void LearnPassive(double t, int passive)
        {
            var def = content.Passives[passive];
            int slot = 0;
            while (slot < state.PassiveCount && passives[slot].Index != passive) slot++;
            if (slot == state.PassiveCount)
            {
                if (PassiveSlotsFull) throw new InvalidOperationException("no free passive slot");
                passives[state.PassiveCount++] = new OwnedItem { Index = passive };
            }
            if (passives[slot].Rank >= def.MaxRank) throw new InvalidOperationException(def.Id + " is at its top rank");
            passives[slot].Rank++;
            for (int i = 0; i < Stats.Names.Length; i++)
                if (i == 6) raw.MaxHp *= 1 + def.PerRank.MaxHp;
                else raw[i] += def.PerRank[i];
            double before = state.Hero.MaxHealth;
            ApplyStats();
            state.Hero.MaxHealth = Stats.MaxHp;
            if (Stats.MaxHp > before) health.Heal(t, Stats.MaxHp - before, 0);
        }

        void ApplyStats()
        {
            var caps = content.Progression.Hero;
            var s = raw;
            for (int i = 0; i < Stats.Names.Length; i++) s[i] = Math.Min(caps.CapMax[i], Math.Max(caps.CapMin[i], s[i]));
            Stats = s;
            state.Stats = s;
            health.Armor = s.Armor;
            state.PickupRadius = content.Progression.Gems.PickupRadiusM * s.Magnet;
        }

        bool Holding => Weapon.Rests(state);

        /// <summary>The next of the arsenal's exact times at or after <paramref name="floor"/>.</summary>
        internal double NextTime(double floor)
        {
            double t = double.PositiveInfinity;
            for (int s = 0; s < state.WeaponCount; s++)
            {
                var slot = slots[s];
                t = Math.Min(t, slot.Weapon.NextTime(floor));
                if (slot.Weapon.OnBeats || Holding) continue;
                t = Math.Min(t, Math.Max(run.SongAt(slot.NextReady), floor));
                for (int k = 0; k < slot.Count; k++)
                    if (!double.IsNaN(slot.Shots[k].Fire)) t = Math.Min(t, Math.Max(slot.Shots[k].Fire, floor));
            }
            return Math.Min(t, Gems.NextTime(floor));
        }

        /// <summary>
        /// One of the arsenal's exact times (<see cref="NextTime"/>'s), the first due in slot order:
        /// in a slot, its shots coming ready, then a shot, then the weapon's own; then a gem's arrival.
        /// </summary>
        internal void Fire(Simulation sim, double t)
        {
            for (int s = 0; s < state.WeaponCount; s++)
            {
                var slot = slots[s];
                if (!slot.Weapon.OnBeats && !Holding)
                {
                    if (run.SongAt(slot.NextReady) <= t)
                    {
                        while (run.SongAt(slot.NextReady) <= t)
                        {
                            if (slot.Count < ShotCapacity)
                                slot.Shots[slot.Count++] = new Shot { Fire = double.NaN, Beat = double.NaN };
                            slot.NextReady += slot.Weapon.Interval;
                        }
                        Plan(slot, t);
                        return;
                    }
                    for (int k = 0; k < slot.Count; k++)
                    {
                        if (!(slot.Shots[k].Fire <= t)) continue;
                        Array.Copy(slot.Shots, k + 1, slot.Shots, k, slot.Count - k - 1);
                        slot.Count--;
                        slot.Weapon.Shoot(sim, t);
                        return;
                    }
                }
                if (slot.Weapon.NextTime(t) <= t)
                {
                    slot.Weapon.Fire(sim, t);
                    return;
                }
            }
            Gems.Fire(sim, t);
        }

        /// <summary>Plans a slot's unplanned shots at song time <paramref name="now"/> on the weapon's grid.</summary>
        void Plan(Slot slot, double now)
        {
            double step = slot.Weapon.Def.Grid == FireGrid.Eighth ? 0.5 : 1;
            for (int k = 0; k < slot.Count; k++)
            {
                ref var shot = ref slot.Shots[k];
                if (!double.IsNaN(shot.Fire)) continue;
                double at = clock.GridAtOrAfter(now, step, out double beat);
                if (!double.IsNaN(at) && at <= slot.LastFire + Eps && !double.IsNaN(slot.LastBeat))
                {
                    // Its slot's previous shot holds this point: the next sixteenth after that one.
                    double next = Math.Floor(slot.LastBeat * 4 + Eps) / 4 + 0.25;
                    double t = clock.GridPoint(next);
                    if (double.IsNaN(t))
                    {
                        at = slot.LastFire;
                        beat = slot.LastBeat;
                    }
                    else
                    {
                        at = t;
                        beat = next;
                    }
                }
                if (double.IsNaN(at))
                {
                    at = now;
                    beat = double.NaN;
                }
                shot.Fire = at;
                shot.Beat = beat;
                slot.LastFire = at;
                slot.LastBeat = beat;
            }
        }

        /// <summary>The world froze: the slots' planned shots wait for the resume.</summary>
        internal void Hold()
        {
            for (int s = 0; s < state.WeaponCount; s++)
            {
                var slot = slots[s];
                for (int k = 0; k < slot.Count; k++) slot.Shots[k].Fire = slot.Shots[k].Beat = double.NaN;
                slot.LastFire = double.NegativeInfinity;
                slot.LastBeat = double.NaN;
            }
        }

        /// <summary>Play resumes at <paramref name="t"/>: the shots that came ready before the freeze are planned from here.</summary>
        internal void Resume(double t)
        {
            for (int s = 0; s < state.WeaponCount; s++) Plan(slots[s], t);
        }

        /// <summary>The per-tick system, at the tick's end: the weapons' own, the gems, and Recovery.</summary>
        internal void Tick(Simulation sim, double end)
        {
            for (int s = 0; s < state.WeaponCount; s++) slots[s].Weapon.Tick(sim, end);
            Gems.Tick(sim, end);
            double played = state.RunClock - recoveredTo;
            recoveredTo = state.RunClock;
            if (played > 0 && Stats.Recovery > 0 && !state.Hero.Fallen) health.Heal(end, Stats.Recovery * played, 0);
        }
    }
}
