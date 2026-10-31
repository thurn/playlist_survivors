using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// Experience, levels and their cards (unity-game.md, Pickups, Level-ups and Chests; native's
    /// survivors/progression.rs and engine/offers.rs).
    ///
    /// - <b>Experience</b>: a gem adds its value times Growth (one more at levels 20 and 40), and
    ///   every threshold it passes (progression.json's curve, <c>factor x level²</c>) is a level.
    /// - <b>Cards</b>: each level reached opens an offer of the scripted table's three cards
    ///   (<see cref="Offers"/>) and freezes the world (<see cref="FreezeReason.LevelUp"/>); the
    ///   hero lets go of its held keys. The cards flip in on the next three committed beats in a
    ///   <see cref="SimEventKind.LevelUp"/>: past the beats revealed, a period of the latest on
    ///   (native waits <see cref="StrideGrid.FreePeriod"/>, off the beat), or the free period
    ///   apart with no beat at all.
    /// - <b>Choosing</b> takes the recorded keys: Left, Up and Right select the first, second and
    ///   third card (native's), and Space or Enter confirms the selected one once the third has
    ///   flipped in, each a <see cref="SimEventKind.Card"/>. A key held when the cards open does
    ///   nothing until it is released and pressed again. A confirmed card ranks up its weapon or
    ///   passive, or heals; the next level's offer opens at once, and after the last the world
    ///   resumes on the next beat.
    /// - An offer opens only while the run plays and the hero stands; levels reached otherwise wait.
    /// </summary>
    internal sealed class LevelUps
    {
        readonly bool cards;
        readonly GameContent content;
        readonly SimState state;
        readonly BeliefLog log;
        readonly BeatClock clock;
        readonly RunRules run;
        readonly HeroHealth health;
        readonly Arsenal arsenal;
        readonly ExperienceCurve curve;
        int serial;
        int down, blocked;

        public LevelUps(bool cards, GameContent content, SimState state, BeliefLog log, BeatClock clock, RunRules run, HeroHealth health,
            Arsenal arsenal)
        {
            this.clock = clock;
            this.cards = cards;
            this.content = content;
            this.state = state;
            this.log = log;
            this.run = run;
            this.health = health;
            this.arsenal = arsenal;
            curve = content.Progression.Experience;
            state.Level = 1;
            Publish();
        }

        /// <summary>Growth at the hero's level now.</summary>
        double Growth => state.Stats.Growth + (Array.IndexOf(curve.GrowthLevels, state.Level) >= 0 ? curve.GrowthBonus : 0);

        /// <summary>A gem worth <paramref name="value"/> arrives at <paramref name="t"/>.</summary>
        public void Gain(Simulation sim, double t, double value)
        {
            state.Experience += value * Growth;
            while (state.Experience >= curve.Total(state.Level))
            {
                state.Level++;
                state.PendingLevels++;
            }
            Publish();
            Open(sim, t);
        }

        void Publish()
        {
            state.ExperienceFloor = curve.Total(state.Level - 1);
            state.ExperienceNext = curve.Total(state.Level);
        }

        void Open(Simulation sim, double t)
        {
            if (!cards || state.PendingLevels == 0 || state.Offer.Open || state.Phase != RunPhase.Playing || state.Hero.Fallen) return;
            int level = state.Level - state.PendingLevels + 1;
            Span<OfferCard> c = stackalloc OfferCard[3];
            Offers.Cards(content, arsenal, level, c);
            Span<double> reveal = stackalloc double[3];
            double after = t;
            var beats = log.Beats;
            for (int k = 0; k < 3; k++)
            {
                int i = 0;
                while (i < beats.Length && beats[i].T <= after + 1e-9) i++;
                after = i < beats.Length ? beats[i].T
                    : after + (beats.Length > 0 ? clock.PeriodOf(beats.Length - 1) : StrideGrid.FreePeriod);
                reveal[k] = after;
            }
            state.Offer = new Offer
            {
                Open = true, Level = level, Serial = serial,
                Card0 = c[0], Card1 = c[1], Card2 = c[2],
                Reveal0 = reveal[0], Reveal1 = reveal[1], Reveal2 = reveal[2],
            };
            blocked = down;
            sim.Emit(SimEvent.LevelUp(t, level, serial++, c[0].Packed | (c[1].Packed << 10) | (c[2].Packed << 20),
                reveal[0], reveal[1], reveal[2]));
            run.Freeze(sim, t, FreezeReason.LevelUp);
            sim.LetGoOfHeldKeys();
        }

        /// <summary>A key goes down at <paramref name="t"/>: returns whether an open offer took it.</summary>
        public bool Press(Simulation sim, Key key, double t)
        {
            int bit = 1 << (int)key;
            down |= bit;
            if (!state.Offer.Open) return false;
            // Held keys wait for a fresh press, and the pause menu has the keys while it is up.
            if ((blocked & bit) != 0 || state.FrozenFor(FreezeReason.Pause)) return true;
            var offer = state.Offer;
            int select = key == Key.Left ? 0 : key == Key.Up ? 1 : key == Key.Right ? 2 : -1;
            if (select >= 0 && select != offer.Selected)
            {
                offer.Selected = select;
                state.Offer = offer;
                sim.Emit(SimEvent.Card(t, offer.Serial, select, false));
            }
            else if ((key == Key.Space || key == Key.Enter) && t >= offer.Reveal2 - 1e-9) Pick(sim, t);
            return true;
        }

        /// <summary>A key goes up: a key held when the cards opened counts again.</summary>
        public void Release(Key key)
        {
            int bit = 1 << (int)key;
            down &= ~bit;
            blocked &= ~bit;
        }

        void Pick(Simulation sim, double t)
        {
            var offer = state.Offer;
            var card = offer.Card(offer.Selected);
            switch (card.Item)
            {
                case CardItem.Weapon:
                    arsenal.Learn(card.Index);
                    break;
                case CardItem.Passive:
                    arsenal.LearnPassive(t, card.Index);
                    break;
                default:
                    health.Heal(t, content.Progression.MaxedHeal, 0);
                    break;
            }
            offer.Open = false;
            state.Offer = offer;
            state.PendingLevels--;
            sim.Emit(SimEvent.Card(t, offer.Serial, offer.Selected, true));
            if (state.PendingLevels > 0) Open(sim, t);
            else run.Thaw(FreezeReason.LevelUp);
        }
    }
}
