using System;

namespace Champions.Sim
{
    /// <summary>
    /// The hero's progression (design.md, "Level-ups and Treasure"; Decisions 10-13 and 15): experience from
    /// kills, levels held for a quiet moment, the offers and the card moment, and a boss's treasure.
    ///
    /// - <b>Experience</b>: every kill adds its type's (<see cref="EnemyType.Experience"/>) on its impact frame
    ///   (<see cref="Killed"/>: the hero's blows' kills and a boss entrance's alike). Each level needs
    ///   <see cref="ProgressionRules.Needed"/>, the rest carrying on; a level reached is held
    ///   (<see cref="SimEventKind.LevelUp"/>, the indicator's dot) until it shows.
    /// - <b>Held for a quiet moment</b>: held levels show at the first tick end in a quiet moment
    ///   (<see cref="QuietMoments"/>: a gap of silence or a stretch without a reliable beat), or once the oldest has
    ///   waited <see cref="ProgressionRules.HeldMaxS"/> of run clock, on the next downbeat (at once with no reliable
    ///   beat). Never during a boss duel (<see cref="Bosses.InDuel"/>): they show after the boss's treasure, which opens
    ///   at the end of the tick the Ogre dies in (<see cref="Bosses.Died"/>, <see cref="Treasure"/>).
    ///   Several show one after another, and each level-up's pick heals <see cref="HeroRules.LevelUpHeal"/>.
    /// - <b>Offers</b> (<see cref="Offer"/>): three different cards, no reroll or skip. The first offer holds one
    ///   new dash, one new attack and one new mod; a level-up's draws from what the build can take (a new attack or
    ///   mod while a slot is free, a damaging dash the hero doesn't have, a rank of anything owned below its top),
    ///   at least one card a rank of something owned whenever one can be; a boss's treasure holds the evolutions of
    ///   the owned rank-3 attacks and dash, then raises to rank 3 of the highest-ranked owned below it, then mod
    ///   ranks and new mods. The offers draw from a generator of their own, seeded from the run's, so a scripted
    ///   run's picks get the same cards whatever the horde draws.
    /// - <b>The card moment</b>: the world holds (<see cref="HoldReason.Cards"/>: nothing moves or attacks and the
    ///   run clock stops; the client keeps the monsters bobbing) and the hero lets go of its keys and target, so a
    ///   key held through the cards never walks it after. While cards are up every key press is theirs
    ///   (<see cref="Press"/>): 1-3 pick a card (a click on one is its number), Left and Right focus one (the first
    ///   press the middle card) and Enter or Space pick the focused one. Nothing is picked before a card is focused,
    ///   so a dash pressed as the cards open picks nothing. Play resumes on the pick.
    /// - <b>The run's start</b> (<see cref="RunStart.FirstOffer"/>): the first offer opens at the sim's start, the
    ///   world empty and held; the run starts on the first downbeat after its pick, or at once with no reliable beat.
    /// </summary>
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
            if (config.Start == RunStart.FirstOffer) Open(sim, OfferKind.First);
        }

        /// <summary>The cards picked this run.</summary>
        public int Picks { get; private set; }

        // ---- Hooks for the systems ----

        /// <summary>An enemy of <paramref name="type"/> (<see cref="Enemy.Type"/>) died at <paramref name="t"/>: its experience, on its impact frame.</summary>
        internal void Killed(Simulation sim, int type, double t)
        {
            var cast = enemies.Cast;
            if (type < 1 || type > cast.Length) return;
            double gain = cast[type - 1].Experience;
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
            if (state.Phase != RunPhase.Playing || offer.Up) return;
            if (!Open(sim, OfferKind.Treasure) && state.HeldLevels > 0) Open(sim, OfferKind.LevelUp);
        }

        /// <summary>A boss died: the Ogre's treasure opens at the tick's end, once its blow has landed.</summary>
        void BossDied(Simulation sim, BossType boss)
        {
            if (boss.Death == BossDeath.Treasure) treasureDue = true;
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
            if (treasureDue)
            {
                treasureDue = false;
                Treasure(sim);
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
            if (!offer.Up) return false;
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
            if (card.Kind == CardKind.Dash) sim.Dashes.Equip(sim, card.Id, card.Rank);
            else if (card.Kind == CardKind.Attack) sim.Arsenal.Set(sim, Upgrade.Of(card.Attack, card.Rank));
            else sim.Arsenal.Set(sim, Upgrade.Of(card.Mod, card.Rank));
            if (kind == OfferKind.LevelUp) sim.Health.HealLevelUp(sim);
            if (kind != OfferKind.First && state.HeldLevels > 0 && state.Phase == RunPhase.Playing && Open(sim, OfferKind.LevelUp)) return;
            sim.Hold(HoldReason.Cards, false);
            if (kind != OfferKind.First || state.Phase != RunPhase.Waiting) return;
            awaitingStart = true;
            if (state.Beat != Reliability.Reliable) StartRun(sim, sim.Now);
        }

        void StartRun(Simulation sim, double t)
        {
            awaitingStart = false;
            sim.StartRun(t);
        }
    }
}
