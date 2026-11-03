using System;

namespace Champions.Sim
{
    /// <summary>Collected XP earns immediate weapon/support choices, including during bosses.
    /// Kills leave persistent pickups; level-up picks never heal. Historical fixtures explicitly
    /// opt into archived offers through DashRules.LegacyForms.</summary>
    public sealed partial class Progression
    {
        /// <summary>The offers' generator's seed is the run's, mixed with this, so its draws are its own.</summary>
        const ulong OfferStream = 0x0FFE45C4D5UL;

        readonly SimState state;
        readonly ProgressionRules rules;
        readonly EnemyRules enemies;
        readonly DashRules dashes;
        readonly SimRandom random;
        Offer offer = new Offer { Focus = -1 };
        // The run clock when the oldest held level was reached (NaN with none held).
        double heldAt = double.NaN;
        bool awaitingStart, downbeatSeen, treasureDue;

        internal Progression(Simulation sim, SimConfig config)
        {
            state = sim.State;
            rules = config.Progression;
            enemies = config.Enemies;
            dashes = config.Dashes;
            random = new SimRandom(config.Seed ^ OfferStream);
            state.Level = 1;
            state.ExperienceNeeded = rules.Needed(1);
            state.Offer = offer;
            sim.Bosses.Died += BossDied;
            if (config.Start == RunStart.FirstOffer)
            {
                if (dashes.LegacyForms) Open(sim, OfferKind.First);
                else sim.StartRun(sim.Now);
            }
        }

        /// <summary>The cards picked this run.</summary>
        public int Picks { get; private set; }
        public int Fallbacks { get; private set; }

        // ---- Hooks for the systems ----

        /// <summary>An enemy of <paramref name="type"/> (<see cref="Enemy.Type"/>) died at <paramref name="t"/>: leave its collectible value at the death position.</summary>
        internal void Killed(Simulation sim, int type, double t, double x, double y)
        {
            var cast = enemies.Cast;
            if (type < 1 || type > cast.Length) return;
            double gain = cast[type - 1].Experience;
            if (!(gain > 0)) return;
            sim.Pickups.Drop(sim, PickupKind.Experience, x, y, gain);
            // Deterministic occasional recovery, without consuming the combat RNG stream.
            killsForHealing++;
            if (killsForHealing % rules.HealingEveryKills == 0) sim.Pickups.Drop(sim, PickupKind.Healing, x, y, rules.HealingShare);
        }

        int killsForHealing;

        internal void CollectExperience(Simulation sim, double gain, double t)
        {
            if (!(gain > 0)) return;
            double experience = state.Experience + gain;
            while (experience >= state.ExperienceNeeded - 1e-9)
            {
                experience = Math.Max(0, experience - state.ExperienceNeeded);
                state.Level++;
                state.ExperienceNeeded = rules.Needed(state.Level);
                if (state.HeldLevels++ == 0) heldAt = state.RunClock;
                sim.Emit(SimEvent.LevelUp(t, state.Level, state.HeldLevels, experience, state.ExperienceNeeded));
            }
            state.Experience = experience;
        }

        /// <summary>A boss's treasure opens now, and the held levels show after it.</summary>
        internal void Treasure(Simulation sim)
        {
            if (state.Phase != RunPhase.Playing) return;
            if (offer.Up) { treasureDue = true; return; }
            if (!Open(sim, OfferKind.Treasure))
            {
                if (!dashes.LegacyForms) DrainLevels(sim);
                else if (state.HeldLevels > 0) Open(sim, OfferKind.LevelUp);
            }
        }

        /// <summary>A nonfinal boss died: hold for its reward immediately. Archived fixtures defer to the tick end.</summary>
        void BossDied(Simulation sim, BossType boss)
        {
            if (boss.Death != BossDeath.Treasure) return;
            if (dashes.LegacyForms) treasureDue = true;
            else Treasure(sim);
        }

        // ---- Driven by the sim ----

        /// <summary>
        /// A key pressed while cards are up is theirs: true when it was (and the hero and the dash never see it).
        /// While the player's pause holds the world too, it does nothing.
        /// </summary>
        internal bool Press(Simulation sim, Key key)
        {
            if (!offer.Up) return false;
            if (state.HeldFor(HoldReason.Pause)) return true;
            switch (key)
            {
                case Key.One:
                case Key.Two:
                case Key.Three:
                    int slot = key - Key.One;
                    if (slot < offer.Count) Pick(sim, slot);
                    break;
                case Key.Left:
                case Key.Right:
                    int focus = offer.Focus < 0 ? (offer.Count - 1) / 2 : Math.Max(0, Math.Min(offer.Count - 1, offer.Focus + (key == Key.Right ? 1 : -1)));
                    if (focus == offer.Focus) break;
                    offer.Focus = focus;
                    state.Offer = offer;
                    sim.Emit(SimEvent.CardFocus(sim.Now, focus));
                    break;
                case Key.Enter:
                case Key.Space:
                    if (offer.Focus >= 0) Pick(sim, offer.Focus);
                    break;
            }
            return true;
        }

        /// <summary>Committed beat at <paramref name="t"/> fired: the run's start waits for a downbeat, and so does a level held too long.</summary>
        internal void Beat(Simulation sim, double t, bool downbeat)
        {
            if (!downbeat) return;
            if (awaitingStart) StartRun(sim, t);
            downbeatSeen = true;
        }

        /// <summary>The tick ending at <paramref name="end"/>: the run's start without a beat, a boss's treasure, and the held levels' quiet moment.</summary>
        internal void Tick(Simulation sim, double end)
        {
            if (awaitingStart && state.Beat != Reliability.Reliable) StartRun(sim, end);
            bool downbeat = downbeatSeen;
            downbeatSeen = false;
            if (treasureDue && !offer.Up)
            {
                treasureDue = false;
                Treasure(sim);
            }
            if (!dashes.LegacyForms)
            {
                DrainLevels(sim);
                return;
            }
            if (state.HeldLevels == 0 || sim.Bosses.InDuel || state.Phase != RunPhase.Playing || state.Holds != 0) return;
            bool overdue = state.RunClock - heldAt >= rules.HeldMaxS - 1e-9;
            if (state.Quiet != QuietReason.None || overdue && (downbeat || state.Beat != Reliability.Reliable)) Open(sim, OfferKind.LevelUp);
        }

        // ---- The card moment ----

        /// <summary>Opens an offer of <paramref name="kind"/> (a level-up's takes a held level); false when there is nothing to offer.</summary>
        bool Open(Simulation sim, OfferKind kind)
        {
            if (kind == OfferKind.LevelUp && --state.HeldLevels == 0) heldAt = double.NaN;
            Compose(sim, kind);
            state.Offer = offer;
            if (!offer.Up)
            {
                if (!dashes.LegacyForms) { Fallbacks++; sim.Health.GrowFallback(); }
                return false;
            }
            sim.Hold(HoldReason.Cards, true);
            sim.LetGoOfHeldKeys();
            sim.Emit(SimEvent.Offer(sim.Now, offer));
            return true;
        }

        /// <summary>Card <paramref name="slot"/> is picked: the build takes it, and the next held level shows, or play resumes.</summary>
        void Pick(Simulation sim, int slot)
        {
            var card = offer[slot];
            var kind = offer.Kind;
            offer = new Offer { Focus = -1 };
            state.Offer = offer;
            Picks++;
            sim.Emit(SimEvent.Pick(sim.Now, slot, card, kind));
            if (card.Kind == CardKind.Weapon) state.Build.Upgrade(card.Weapon);
            else if (card.Kind == CardKind.Support) state.Build.Upgrade(card.Support);
            else if (card.Kind == CardKind.Dash) sim.Dashes.Equip(sim, card.Id, card.Rank);
            else if (card.Kind == CardKind.Attack) sim.Arsenal.Set(sim, Upgrade.Of(card.Attack, card.Rank));
            else sim.Arsenal.Set(sim, Upgrade.Of(card.Mod, card.Rank));
            if (!dashes.LegacyForms)
            {
                if (treasureDue)
                {
                    treasureDue = false;
                    Treasure(sim);
                }
                else DrainLevels(sim);
                if (!offer.Up) sim.Hold(HoldReason.Cards, false);
                return;
            }
            if (kind != OfferKind.First && state.HeldLevels > 0 && state.Phase == RunPhase.Playing && Open(sim, OfferKind.LevelUp)) return;
            sim.Hold(HoldReason.Cards, false);
            if (kind != OfferKind.First || state.Phase != RunPhase.Waiting) return;
            awaitingStart = true;
            if (state.Beat != Reliability.Reliable) StartRun(sim, sim.Now);
        }

        void DrainLevels(Simulation sim)
        {
            if (offer.Up || state.Phase != RunPhase.Playing || state.HeldFor(HoldReason.Pause)) return;
            while (state.HeldLevels > 0)
                if (Open(sim, OfferKind.LevelUp)) return;
        }

        void StartRun(Simulation sim, double t)
        {
            awaitingStart = false;
            sim.StartRun(t);
        }
    }
}
