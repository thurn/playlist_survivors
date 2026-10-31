using System;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>A chest lying on the field (<see cref="SimState.FieldChests"/>): its drop's item, where, its tier and its schedule minute.</summary>
    public struct FieldChest
    {
        public int Id;
        public double X, Y;
        public int Tier, Minute;
    }

    /// <summary>
    /// The latest chest's roulette as the HUD reads it (<see cref="SimState.Chest"/>): open from the
    /// moment the hero takes it until it lands, then kept, landed, until the next chest is taken.
    /// </summary>
    public struct ChestState
    {
        /// <summary>The roulette is spinning: the world is frozen for it.</summary>
        public bool Open;
        public int Chest, Tier, Minute;
        public double X, Y;
        /// <summary>When the hero took it, and its latest tick (NaN before the first).</summary>
        public double Opened, LastTick;
        /// <summary>Ticks so far, up to <see cref="Chests.Eighths"/>.</summary>
        public int Ticks;
        /// <summary>When it landed, NaN before.</summary>
        public double Landed;
        /// <summary>How many rewards it holds (the tier's), and each once it has landed.</summary>
        public int Rewards;
        public OfferCard Reward0, Reward1, Reward2, Reward3, Reward4;

        public OfferCard Reward(int k) =>
            k == 0 ? Reward0 : k == 1 ? Reward1 : k == 2 ? Reward2 : k == 3 ? Reward3 : k == 4 ? Reward4 : throw new ArgumentOutOfRangeException(nameof(k));

        internal void SetReward(int k, OfferCard card)
        {
            switch (k)
            {
                case 0: Reward0 = card; break;
                case 1: Reward1 = card; break;
                case 2: Reward2 = card; break;
                case 3: Reward3 = card; break;
                case 4: Reward4 = card; break;
                default: throw new ArgumentOutOfRangeException(nameof(k));
            }
        }
    }

    /// <summary>
    /// The bosses' chests (unity-game.md, Pickups, Level-ups and Chests; native's survivors/chests.rs and
    /// engine/chests.rs).
    ///
    /// - <b>Drops</b>: a scheduled boss carrying treasure drops its chest where it dies, on the centre of
    ///   the nearest open tile, a <see cref="SimEventKind.Drop"/> of <see cref="ItemKind.Chest"/>.
    /// - <b>Taking</b>: at a tick's end, while the world plays and the hero stands, the hero takes a
    ///   chest within <see cref="ReachM"/> (native's 22 px) when it is clear of the crowd: no live
    ///   enemy's body within <see cref="ClearM"/>. The roulette opens (<see cref="ChestPhase.Open"/>) and
    ///   the world freezes (<see cref="FreezeReason.Chest"/>).
    /// - <b>The roulette</b> ticks on <see cref="Eighths"/> eighth notes from the next downbeat (two of the
    ///   beat clock's two-beat bars), then lands on the downbeat after them. Its times are points of
    ///   the revealed beats' grid, carried on through the latest beat's continuation
    ///   (<see cref="BeatClock.GridPoint"/>); with no beat known it ticks half a free period apart. It
    ///   holds through a pause or the music stopping, and goes on from the next point after.
    /// - <b>The landing</b> gives the tier's rewards in turn (<see cref="Item"/>), each a
    ///   <see cref="ChestPhase.Reward"/>; then the chest's freeze lets go, so play resumes on the
    ///   landing's own beat, and levels reached meanwhile open their cards.
    /// - A chest of a minute whose treasure evolves (the schedule's minutes 10, 15, 20 and 25) may
    ///   evolve a weapon at its top rank whose partner passive is owned.
    /// </summary>
    internal sealed class Chests
    {
        /// <summary>The hero takes a chest this near (native's 22 px at 27 px a metre).</summary>
        public const double ReachM = 22.0 / 27;
        /// <summary>The hero is clear of the crowd with no live enemy's body this near.</summary>
        public const double ClearM = 2;
        /// <summary>The roulette's ticks before it lands: eight eighths, two two-beat bars (native's).</summary>
        public const int Eighths = 8;
        const int FieldCapacity = 32;

        readonly GameContent content;
        readonly SimState state;
        readonly BeliefLog log;
        readonly BeatClock clock;
        readonly RunRules run;
        readonly HeroHealth health;
        readonly Arsenal arsenal;
        readonly Horde horde;
        readonly ArenaGrid arena;
        readonly int[] reached;
        int dropped;
        // The roulette's next point: its fractional beat on the grid (NaN off it), and the song time it
        // looks for one from, or falls back to with no beat known.
        double nextBeat = double.NaN, nextSong = double.NaN;
        // A pause held the roulette: plan again from the tick it lets go.
        bool replan;

        public Chests(GameContent content, SimState state, BeliefLog log, BeatClock clock, RunRules run, HeroHealth health,
            Arsenal arsenal, Horde horde, ArenaGrid arena)
        {
            this.content = content;
            this.state = state;
            this.log = log;
            this.clock = clock;
            this.run = run;
            this.health = health;
            this.arsenal = arsenal;
            this.horde = horde;
            this.arena = arena;
            reached = new int[state.EnemyStore.Length];
            state.FieldChestStore = new FieldChest[FieldCapacity];
            state.Chest = new ChestState { Chest = -1, Opened = double.NaN, LastTick = double.NaN, Landed = double.NaN };
        }

        /// <summary>A boss dies at (x, y) at <paramref name="t"/> carrying a chest of <paramref name="tier"/> from <paramref name="minute"/>'s treasure.</summary>
        public void Drop(Simulation sim, double t, double x, double y, int tier, int minute)
        {
            if (state.FieldChestCount == FieldCapacity) return;
            OpenTile(ref x, ref y);
            state.FieldChestStore[state.FieldChestCount++] = new FieldChest { Id = dropped, X = x, Y = y, Tier = tier, Minute = minute };
            sim.Emit(SimEvent.Chest(t, dropped++, x, y, tier, minute));
        }

        /// <summary>The debug chest lies this many tiles ahead of the hero.</summary>
        public const int DebugTilesAhead = 2;

        /// <summary>
        /// The debug surfaces' chest (<see cref="DebugCommand.Chest"/>): tier 3, of the last minute whose
        /// treasure evolves, <see cref="DebugTilesAhead"/> tiles ahead of the hero now, for it to walk onto.
        /// </summary>
        public void DropAhead(Simulation sim, double t)
        {
            var minutes = content.Schedule.Minutes;
            int minute = minutes.Length - 1;
            while (minute >= 0 && !(minutes[minute].Treasure?.Evolves ?? false)) minute--;
            state.Hero.PositionAt(t, out double x, out double y);
            Sword.Facing(state.Hero.Facing, out double dx, out double dy);
            Drop(sim, t, x + dx * DebugTilesAhead * arena.TileM, y + dy * DebugTilesAhead * arena.TileM, 3, minute);
        }

        /// <summary>(x, y) moves to the centre of the open tile nearest it (left as it is with none near).</summary>
        void OpenTile(ref double x, ref double y)
        {
            int tx = (int)Math.Floor((x - arena.OriginX) / arena.TileM), ty = (int)Math.Floor((y - arena.OriginY) / arena.TileM);
            double best = double.PositiveInfinity;
            for (int r = 0; r <= 4 && double.IsPositiveInfinity(best); r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !arena.IsOpen(tx + dx, ty + dy)) continue;
                        double cx = arena.CentreX(tx + dx), cy = arena.CentreY(ty + dy);
                        double d = (cx - x) * (cx - x) + (cy - y) * (cy - y);
                        if (d >= best) continue;
                        best = d;
                        x = cx;
                        y = cy;
                    }
        }

        bool Paused => state.FrozenFor(FreezeReason.Pause) || state.FrozenFor(FreezeReason.MusicStopped);

        /// <summary>The per-tick system, at the tick's end: a pause's hold on the roulette, or the hero taking a chest.</summary>
        internal void Tick(Simulation sim, double end)
        {
            if (state.Chest.Open)
            {
                if (Paused) replan = true;
                else if (replan)
                {
                    replan = false;
                    nextBeat = double.NaN;
                    nextSong = end;
                }
                return;
            }
            if (state.FieldChestCount == 0 || Weapon.Rests(state)) return;
            state.Hero.PositionAt(end, out double hx, out double hy);
            int near = -1;
            double best = ReachM * ReachM + 1e-9;
            var field = state.FieldChestStore;
            for (int k = 0; k < state.FieldChestCount; k++)
            {
                double d = (field[k].X - hx) * (field[k].X - hx) + (field[k].Y - hy) * (field[k].Y - hy);
                if (d > best) continue;
                best = d;
                near = k;
            }
            if (near < 0 || (horde != null && horde.InReach(end, hx, hy, ClearM, reached) > 0)) return;
            Open(sim, end, near);
        }

        void Open(Simulation sim, double t, int k)
        {
            var c = state.FieldChestStore[k];
            Array.Copy(state.FieldChestStore, k + 1, state.FieldChestStore, k, state.FieldChestCount - k - 1);
            state.FieldChestCount--;
            state.Chest = new ChestState
            {
                Open = true,
                Chest = c.Id,
                Tier = c.Tier,
                Minute = c.Minute,
                X = c.X,
                Y = c.Y,
                Opened = t,
                LastTick = double.NaN,
                Landed = double.NaN,
                Rewards = content.Progression.Chests.ItemsByTier[c.Tier - 1],
            };
            sim.Emit(SimEvent.Chest(t, c.Id, ChestPhase.Open, c.Tier, c.Minute, c.X, c.Y));
            run.Freeze(sim, t, FreezeReason.Chest);
            nextBeat = double.NaN;
            nextSong = t;
            replan = false;
        }

        /// <summary>The roulette's next tick or landing, and its fractional beat (NaN off the grid); infinity when none is due.</summary>
        double Due(out double beat)
        {
            beat = double.NaN;
            if (!state.Chest.Open || replan || Paused || state.Phase != RunPhase.Playing) return double.PositiveInfinity;
            if (!double.IsNaN(nextBeat))
            {
                double t = clock.GridPoint(nextBeat);
                if (!double.IsNaN(t))
                {
                    beat = nextBeat;
                    return t;
                }
            }
            // Off the grid, or a continuation a tracking gap ended: the first point known from where it looks.
            bool downbeat = state.Chest.Ticks == 0 || state.Chest.Ticks == Eighths;
            double g = downbeat ? DownbeatAtOrAfter(nextSong, out beat) : clock.GridAtOrAfter(nextSong, 0.5, out beat);
            if (!double.IsNaN(g)) return g;
            beat = double.NaN;
            return nextSong;
        }

        /// <summary>The roulette's next time at or after <paramref name="floor"/>.</summary>
        internal double NextTime(double floor) => Math.Max(Due(out _), floor);

        /// <summary>The roulette's time (<see cref="NextTime"/>'s): a tick, or the landing.</summary>
        internal void Fire(Simulation sim, double t)
        {
            Due(out double beat);
            var c = state.Chest;
            if (c.Ticks == Eighths)
            {
                Land(sim, t);
                return;
            }
            sim.Emit(SimEvent.Chest(t, c.Chest, ChestPhase.Tick, c.Tier, c.Ticks, c.X, c.Y));
            c.Ticks++;
            c.LastTick = t;
            state.Chest = c;
            if (double.IsNaN(beat))
            {
                nextBeat = double.NaN;
                nextSong = t + StrideGrid.FreePeriod / 2;
                return;
            }
            nextBeat = c.Ticks == Eighths ? DownbeatFrom(beat + 0.5) : beat + 0.5;
            int b = Math.Min((int)Math.Floor(beat + 1e-9), log.Beats.Length - 1);
            nextSong = t + clock.PeriodOf(b) * (nextBeat - beat);
        }

        void Land(Simulation sim, double t)
        {
            var c = state.Chest;
            var minutes = content.Schedule.Minutes;
            bool evolves = c.Minute >= 0 && c.Minute < minutes.Length && minutes[c.Minute].Treasure != null && minutes[c.Minute].Treasure.Evolves;
            sim.Emit(SimEvent.Chest(t, c.Chest, ChestPhase.Land, c.Tier, c.Rewards, c.X, c.Y));
            for (int k = 0; k < c.Rewards; k++)
            {
                var card = Item(evolves);
                Give(t, card);
                c.SetReward(k, card);
                sim.Emit(SimEvent.Chest(t, c.Chest, ChestPhase.Reward, c.Tier, card.Packed | (k << 10), c.X, c.Y));
            }
            c.Open = false;
            c.Landed = t;
            state.Chest = c;
            run.Thaw(FreezeReason.Chest);
            arsenal.LevelUps.OpenPending(sim, t);
        }

        /// <summary>
        /// A chest's next reward (native's chest_item), which never brings an item not owned: when it may
        /// evolve, the first weapon in progression.json's evolution order that can (<see cref="Arsenal.EvolutionOf"/>);
        /// else a rank of the owned weapon lowest in rank below its top (content order on ties), then
        /// likewise of an owned passive; else the heal.
        /// </summary>
        public OfferCard Item(bool evolves)
        {
            if (evolves)
                foreach (string id in content.Progression.Chests.EvolutionOrder)
                {
                    int e = arsenal.EvolutionOf(content.WeaponIndex(id));
                    if (e >= 0) return new OfferCard { Item = CardItem.Weapon, Index = e, Before = 0, After = 1 };
                }
            var pick = new OfferCard { Item = CardItem.Heal, Index = -1 };
            foreach (var w in arsenal.Weapons)
                if (w.Rank < content.Weapons[w.Index].MaxRank && Lower(w, pick, CardItem.Weapon))
                    pick = new OfferCard { Item = CardItem.Weapon, Index = w.Index, Before = w.Rank, After = w.Rank + 1 };
            if (pick.Item != CardItem.Heal) return pick;
            foreach (var p in arsenal.Passives)
                if (p.Rank < content.Passives[p.Index].MaxRank && Lower(p, pick, CardItem.Passive))
                    pick = new OfferCard { Item = CardItem.Passive, Index = p.Index, Before = p.Rank, After = p.Rank + 1 };
            return pick;
        }

        static bool Lower(OwnedItem item, OfferCard pick, CardItem kind) =>
            pick.Item != kind || item.Rank < pick.Before || (item.Rank == pick.Before && item.Index < pick.Index);

        void Give(double t, OfferCard card)
        {
            switch (card.Item)
            {
                case CardItem.Weapon:
                    if (content.Weapons[card.Index].IsEvolution) arsenal.Evolve(card.Index);
                    else arsenal.Learn(card.Index);
                    break;
                case CardItem.Passive:
                    arsenal.LearnPassive(t, card.Index);
                    break;
                default:
                    health.Heal(t, content.Progression.MaxedHeal, 0);
                    break;
            }
        }

        /// <summary>The first downbeat point at or after <paramref name="s"/> known now, with its beat; NaN with none.</summary>
        double DownbeatAtOrAfter(double s, out double beat)
        {
            double t = clock.GridAtOrAfter(s, 1, out beat);
            if (double.IsNaN(t) || IsDownbeat((int)Math.Round(beat))) return t;
            beat += 1;
            t = clock.GridPoint(beat);
            if (double.IsNaN(t)) beat = double.NaN;
            return t;
        }

        /// <summary>The first downbeat's index at or after fractional beat <paramref name="beat"/>.</summary>
        int DownbeatFrom(double beat)
        {
            int i = (int)Math.Ceiling(beat - 1e-9);
            return IsDownbeat(i) ? i : i + 1;
        }

        /// <summary>
        /// Is beat <paramref name="i"/> a downbeat: by its bar when revealed (by its index's parity
        /// before any lock), and past the latest beat, every other beat on from it.
        /// </summary>
        bool IsDownbeat(int i)
        {
            int last = log.Beats.Length - 1;
            if (last < 0) return (i & 1) == 0;
            if (i > last) return IsDownbeat(last) == (((i - last) & 1) == 0);
            return clock.BarOf(i, out bool downbeat) >= 0 ? downbeat : (i & 1) == 0;
        }
    }
}
