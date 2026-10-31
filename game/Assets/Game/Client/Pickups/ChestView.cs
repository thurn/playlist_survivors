using System;
using System.Collections.Generic;
using Game.Client.Effects;
using Game.Client.Loop;
using Game.Client.Sound;
using Game.Client.Weapons;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine;

namespace Game.Client.Pickups
{
    /// <summary>
    /// The bosses' chests and their roulette in 3D (unity-game.md, Pickups, Level-ups and Chests;
    /// native's slice/treasure.rs), from the sim's events at their song times. The HUD draws the
    /// roulette's cards.
    ///
    /// - <b>On the field</b> (a <see cref="SimEventKind.Drop"/> of <see cref="ItemKind.Chest"/>): a gold
    ///   crystal reliquary hovering over its tile, turning, throbbing on the beat, a shard for each of
    ///   its items circling it (one, three or five by tier), under a faint column of gold light with a
    ///   glow on the ground, so it reads from across the arena. It pops in with a gold burst.
    /// - <b>Taken</b> (<see cref="ChestPhase.Open"/>): it rises over the hero's head (coins sound).
    ///   On each of the roulette's eighths (<see cref="ChestPhase.Tick"/>) it flashes and kicks, its
    ///   shards take the next weapons' colours, as the cards cycle, and a glass ping climbs the major
    ///   pentatonic.
    /// - <b>Landed</b> (<see cref="ChestPhase.Land"/> and its rewards): it bursts in gold, and each shard
    ///   takes its reward's colour and flies into the hero's chest, a ring of light sweeping out
    ///   (coins again). An evolution's own arrival is <see cref="EvolutionLayers"/>'.
    /// </summary>
    public sealed class ChestView : IPresenter, IDisposable
    {
        public static readonly Color Gold = new Color(1f, 0.72f, 0.18f);
        /// <summary>The chest's hover over its tile, over the hero's head while it spins, and its height.</summary>
        const float HoverM = 1.15f, OverheadM = 3.1f, SizeM = 1.0f;
        const float ShardOrbitM = 0.85f, ShardM = 0.32f, BeamM = 9f;
        const double PopS = 0.25, RiseS = 0.3, FlashS = 0.16, GatherS = 0.32;
        const float TurnDegPerS = 40, SpinDegPerS = 260;
        /// <summary>The ticks' glass pings: the major pentatonic up from the chime's root.</summary>
        static readonly float[] TickNotes = { 0, 2, 4, 7, 9, 12, 14, 16 };
        const float TickRoot = 91;
        const float BoundsM = 120;

        sealed class Chest
        {
            public int Id, Tier;
            public Vector2 At;
            public double Dropped, Opened = double.NaN, Landed = double.NaN;
            public readonly List<double> Ticks = new List<double>(8);
            public readonly List<Color> Rewards = new List<Color>(5);
            public bool Burst, Popped;
        }

        readonly Simulation _sim;
        readonly GameContent _content;
        readonly WeaponLooks _looks;
        readonly EffectsPresenter _effects;
        readonly SoundPresenter _sound;
        readonly Camera _camera;
        readonly PrismGround _ground;
        readonly Mesh _chestMesh, _shardMesh, _beamMesh;
        readonly CrystalBatch _chest, _beam;
        readonly Dictionary<Color, CrystalBatch> _shards = new Dictionary<Color, CrystalBatch>();
        readonly Color[] _cycle;
        readonly Dictionary<int, Chest> _chests = new Dictionary<int, Chest>();
        readonly List<int> _done = new List<int>();
        readonly int _cast;

        /// <param name="effects">The effects, or null without compute.</param>
        /// <param name="sound">The sounds, or null without them.</param>
        public ChestView(Camera camera, Simulation sim, GameContent content, WeaponLooks looks, EffectsPresenter effects, SoundPresenter sound)
        {
            _camera = camera;
            _sim = sim;
            _content = content;
            _looks = looks;
            _effects = effects;
            _sound = sound;
            _ground = new PrismGround(camera);
            _chestMesh = CrystalBatch.Build("chest", 6, 0.75f, 0.75f, 0.55f, 0.45f);
            _shardMesh = CrystalBatch.Build("chest-shard", 4, 0.5f, 0.5f, 0.6f, 0.4f);
            _beamMesh = CrystalBatch.Build("chest-beam", 8, 1f, 1f, 0.5f, 0.5f);
            _chest = new CrystalBatch(_chestMesh, Gold * 1.4f, "chest", 0.5f);
            _beam = new CrystalBatch(_beamMesh, Gold, "chest-beam", 0.08f);
            // The roulette's shards cycle through the base weapons' colours, as the cards cycle through the items.
            var cycle = new List<Color>();
            for (int w = 0; w < content.Weapons.Length; w++)
                if (!content.Weapons[w].IsEvolution) cycle.Add(ItemColour(CardItem.Weapon, w));
            _cycle = cycle.ToArray();
            _cast = effects?.IndexOf("prism-cast") ?? -1;
        }

        /// <summary>Chests shown or still to come, for tests.</summary>
        public int Count => _chests.Count;

        /// <summary>A reward's colour: a weapon's own, a passive's pale gold, the heal's rose.</summary>
        public Color ItemColour(CardItem item, int index)
        {
            if (item == CardItem.Passive) return new Color(1f, 0.9f, 0.6f);
            if (item != CardItem.Weapon) return new Color(1f, 0.35f, 0.45f);
            var def = _content.Weapons[index];
            switch (def.Shape.Kind)
            {
                case ShapeKind.Aura: return def.IsEvolution ? GarlicAura.SoulEater : GarlicAura.Garlic;
                case ShapeKind.Trail: return new Color(1f, 0.45f, 0.1f);
                default: return _looks.Colour(index);
            }
        }

        CrystalBatch Shards(Color colour)
        {
            if (!_shards.TryGetValue(colour, out var batch))
                _shards[colour] = batch = new CrystalBatch(_shardMesh, colour * 1.3f, "chest-shard", 0.6f);
            return batch;
        }

        public void OnEvent(in SimEvent e)
        {
            if (e.Kind == SimEventKind.Drop)
            {
                var d = e.AsDrop;
                if (d.ItemKind != ItemKind.Chest) return;
                _chests[d.Item] = new Chest { Id = d.Item, Tier = (int)d.Value, At = new Vector2((float)d.X, (float)d.Y), Dropped = e.T };
                return;
            }
            if (e.Kind != SimEventKind.Chest) return;
            var c = e.AsChest;
            if (!_chests.TryGetValue(c.Chest, out var chest))
                _chests[c.Chest] = chest = new Chest { Id = c.Chest, Tier = c.Tier, At = new Vector2((float)c.X, (float)c.Y), Dropped = e.T, Popped = true };
            switch (c.Phase)
            {
                case ChestPhase.Open:
                    chest.Opened = e.T;
                    _sound?.Request(SoundKind.ChestOpen, e.T, 1f, 1f, 1f);
                    break;
                case ChestPhase.Tick:
                    chest.Ticks.Add(e.T);
                    _sound?.Request(SoundKind.ChestTick, e.T, 1f, 1f, 1f, TickRoot + TickNotes[Math.Min(c.Tick, TickNotes.Length - 1)]);
                    break;
                case ChestPhase.Land:
                    chest.Landed = e.T;
                    _sound?.Request(SoundKind.ChestLand, e.T, 1f, 1f, 1f);
                    break;
                case ChestPhase.Reward:
                    var card = c.Reward;
                    chest.Rewards.Add(ItemColour(card.Item, card.Index));
                    break;
            }
        }

        public void Present(in PresentFrame frame)
        {
            double t = frame.Song;
            var hero = frame.State.Hero;
            hero.PositionAt(t, out double hx, out double hy);
            var heroAt = new Vector2((float)hx, (float)hy);
            float age = (float)(t - Math.Floor(t / 100) * 100);
            double beat = MusicalGrid.BeatIndexAt(_sim.Stream.Beats, t);
            float kick = double.IsNaN(beat) ? 0 : Mathf.Exp(-6f * (float)(beat - Math.Floor(beat)));
            _done.Clear();
            foreach (var pair in _chests)
            {
                var c = pair.Value;
                if (t < c.Dropped) continue;
                if (!c.Popped)
                {
                    c.Popped = true;
                    _effects?.Cues.Burst(c.Dropped, new Vector3(c.At.x, HoverM, c.At.y), Vector2.zero, 1.2f, Gold);
                }
                if (!double.IsNaN(c.Landed) && t >= c.Landed + GatherS)
                {
                    _done.Add(pair.Key);
                    continue;
                }
                if (double.IsNaN(c.Opened) || t < c.Opened) Field(c, t, kick, age);
                else Spin(c, t, heroAt, age);
            }
            foreach (int id in _done) _chests.Remove(id);
            var bounds = new Bounds(new Vector3(heroAt.x, 0, heroAt.y), Vector3.one * BoundsM);
            _beam.Draw(_camera, bounds);
            _chest.Draw(_camera, bounds);
            foreach (var batch in _shards.Values) batch.Draw(_camera, bounds);
        }

        /// <summary>A chest lying on the field: hovering, turning, throbbing on the beat, under its column of light.</summary>
        void Field(Chest c, double t, float kick, float age)
        {
            float pop = Mathf.Clamp01((float)((t - c.Dropped) / PopS));
            pop = 1 - Mathf.Pow(1 - pop, 3);
            float bob = 0.12f * Mathf.Sin((float)t * 2.2f + c.Id);
            var at = new Vector3(c.At.x, HoverM + bob, c.At.y);
            float size = SizeM * pop * (1 + 0.1f * kick);
            float turn = (float)t * TurnDegPerS + c.Id * 37;
            _chest.Crystal(Upright(at, turn, size));
            _beam.Afterimage(Matrix4x4.TRS(new Vector3(c.At.x, BeamM / 2, c.At.y), Quaternion.Euler(-90, 0, 0),
                new Vector3(0.35f * pop, 0.35f * pop, BeamM)));
            _ground.Draw(c.At, 1.4f, 1.4f, new PrismGround.Look { Tint = Gold, Fill = 1, Core = 0.8f, Intensity = pop * (0.35f + 0.25f * kick) }, age);
            int shards = ShardsOf(c);
            for (int k = 0; k < shards; k++)
            {
                float a = (float)t * 1.6f + 2 * Mathf.PI * k / shards;
                var s = at + new Vector3(Mathf.Cos(a), 0.15f * Mathf.Sin(2 * a), Mathf.Sin(a)) * ShardOrbitM * pop;
                Shards(Gold).Crystal(Upright(s, turn * 2, ShardM * pop));
            }
        }

        /// <summary>The roulette: over the hero's head, flashing on its eighths, then bursting as its rewards fly home.</summary>
        void Spin(Chest c, double t, Vector2 hero, float age)
        {
            float rise = Mathf.Clamp01((float)((t - c.Opened) / RiseS));
            rise = rise * rise * (3 - 2 * rise);
            var over = new Vector3(hero.x, OverheadM, hero.y);
            var field = new Vector3(c.At.x, HoverM, c.At.y);
            var at = Vector3.Lerp(field, over, rise);
            int ticks = 0;
            double last = double.NegativeInfinity;
            foreach (double tick in c.Ticks)
                if (tick <= t + 1e-9)
                {
                    ticks++;
                    last = tick;
                }
            float flash = Mathf.Exp(-(float)((t - last) / FlashS));
            int shards = ShardsOf(c);
            bool landed = !double.IsNaN(c.Landed) && t >= c.Landed;
            if (!landed)
            {
                float spin = (float)(t - c.Opened) * (SpinDegPerS + 60 * ticks);
                _chest.Crystal(Upright(at, spin, SizeM * (1 + 0.3f * flash)));
                _beam.Afterimage(Matrix4x4.TRS(new Vector3(at.x, at.y / 2, at.z), Quaternion.Euler(-90, 0, 0),
                    new Vector3(0.5f, 0.5f, at.y) * (0.6f + 0.6f * flash)));
                _ground.Draw(hero, 2.2f, 0.4f, new PrismGround.Look { Tint = Gold, Edge = 1, Fill = 0.5f, Intensity = 0.25f + 1.2f * flash }, age);
                for (int k = 0; k < shards; k++)
                {
                    float a = (float)(t - c.Opened) * (2.5f + 0.4f * ticks) + 2 * Mathf.PI * k / shards;
                    var s = at + new Vector3(Mathf.Cos(a), 0.2f * Mathf.Sin(3 * a), Mathf.Sin(a)) * ShardOrbitM * 1.3f;
                    var colour = _cycle.Length > 0 ? _cycle[(ticks + k) % _cycle.Length] : Gold;
                    Shards(colour).Crystal(Upright(s, spin * 1.5f, ShardM * 1.2f * (1 + 0.4f * flash)));
                }
                return;
            }
            if (!c.Burst)
            {
                c.Burst = true;
                _effects?.Cues.Burst(c.Landed, at, Vector2.zero, 1.8f, Gold);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3;
                    _effects?.Cues.Spark(c.Landed, _cast, at, new Vector2(Mathf.Cos(a), Mathf.Sin(a)), 1.2f, Gold);
                }
            }
            float u = Mathf.Clamp01((float)((t - c.Landed) / GatherS));
            var chest = new Vector3(hero.x, 1.1f, hero.y);
            _ground.Draw(hero, Mathf.Lerp(1f, 9f, 1 - Mathf.Pow(1 - u, 3)), 0.6f,
                new PrismGround.Look { Tint = Gold, Edge = 1, Fill = 0.4f, Intensity = 1.5f * (1 - u) }, age);
            for (int k = 0; k < c.Rewards.Count; k++)
            {
                float a = 2 * Mathf.PI * k / c.Rewards.Count;
                var start = at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * ShardOrbitM * 1.6f;
                var s = Vector3.Lerp(start, chest, u * u);
                Shards(c.Rewards[k]).Crystal(Upright(s, (float)t * SpinDegPerS, ShardM * 1.5f * (1 - 0.6f * u)));
            }
        }

        int ShardsOf(Chest c) => _content.Progression.Chests.ItemsByTier[Mathf.Clamp(c.Tier, 1, 3) - 1];

        /// <summary>A crystal standing upright at <paramref name="at"/>, turned <paramref name="turn"/> degrees about the vertical.</summary>
        static Matrix4x4 Upright(Vector3 at, float turn, float size) =>
            Matrix4x4.TRS(at, Quaternion.AngleAxis(turn, Vector3.up) * Quaternion.Euler(-90, 0, 0), new Vector3(size, size, size * 1.3f));

        public void Dispose()
        {
            _ground.Dispose();
            _chest.Dispose();
            _beam.Dispose();
            foreach (var batch in _shards.Values) batch.Dispose();
            CrystalBatch.Destroy(_chestMesh);
            CrystalBatch.Destroy(_shardMesh);
            CrystalBatch.Destroy(_beamMesh);
        }
    }
}
