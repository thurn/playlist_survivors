using System;
using System.Collections.Generic;

namespace PS.Choreography
{
    public enum MonsterRank { Fodder, Elite, Boss }

    public sealed class MonsterType
    {
        public string Name;
        public MonsterRank Rank;
        public float Radius, StepM, ReachM;
        public int HitPoints;
        public int WalkClip, AttackClip, HitClip, DeathClip;   // indices into the clip library
        /// <summary>Casters: the projectile each attack fires at the hero from its reach (-1: a melee blow).</summary>
        public int Projectile = -1;
    }

    public enum HitShape { Arc, Ring, Line, Circle, Single }

    /// <summary>A hero combo or spell: how it hits, before the kill-rate feedback scales it.</summary>
    public sealed class Move
    {
        public string Name;
        public HitShape Shape;
        public float Radius, ArcDeg, Length, Width, Strength;
        public float RangeM;          // spells: how far from the hero they may target
        public float Bars;            // beams: how long they sweep
        public int Projectile = -1;   // spells that fly
        public int Clip = -1;         // hero combos
        public float AdvanceM;        // hero combos: the least the hero moves over the clip (its root motion when larger)
        public float WindupS;         // hero combos: the clip's start to its impact, overriding the hero's windupS (0: the hero's)
        public float EffectRadius;    // hero combos: the reach their effect is drawn at full size (0: their own size)
        public int Effect = -1, Sound = -1;
    }

    public sealed class ProjectileType
    {
        public string Name;
        public float SpeedMps;
    }

    /// <summary>
    /// The placeholder roster's design numbers: sizes, gait steps, reach and hit points per type,
    /// the hero's moves and the spells. Clip timings come from the <see cref="ClipLibrary"/>.
    /// </summary>
    public sealed class Roster
    {
        public float HeroRadius, HeroStrideM, HeroReachM, HeroSenseM;
        /// <summary>
        /// The hero's step (its <c>stepS</c>): a stride covers its ground in this long from the beat, then
        /// the hero stands until the next, as the native game's hero steps. 0, the default, glides the
        /// stride over the whole beat.
        /// </summary>
        public float HeroStepS;
        /// <summary>
        /// The hero's windup (its <c>windupS</c>): each combo's clip starts this long before its impact and
        /// plays at the speed that lands its impact frame there, as the native game's sword swings do, and
        /// the next combo's windup may cut into its follow-through. 0, the default, fits clips within
        /// <see cref="ClipLibrary.MinSpeed"/> and <see cref="ClipLibrary.MaxSpeed"/> and plays them out.
        /// </summary>
        public float HeroWindupS;
        public int HeroWalkClip;
        public MonsterType[] Monsters;
        public ProjectileType[] Projectiles;
        public Move[] Combos, Spells;

        public int MonsterIndex(string name)
        {
            for (int i = 0; i < Monsters.Length; i++) if (Monsters[i].Name == name) return i;
            return -1;
        }

        int ProjectileIndex(string name, string user)
        {
            for (int i = 0; i < Projectiles.Length; i++) if (Projectiles[i].Name == name) return i;
            throw new FormatException("roster: " + user + " names unknown projectile '" + name + "'");
        }

        public Move Combo(string name) => FindMove(Combos, name);

        /// <summary>A combo's windup: its own, else the hero's (0: fitted by the clip library's speed limits).</summary>
        public float WindupOf(Move combo) => combo.WindupS > 0 ? combo.WindupS : HeroWindupS;
        public Move Spell(string name) => FindMove(Spells, name);

        static Move FindMove(Move[] moves, string name)
        {
            foreach (Move m in moves) if (m.Name == name) return m;
            return null;
        }

        public float MaxMonsterRadius
        {
            get
            {
                float r = 0;
                foreach (MonsterType m in Monsters) r = Math.Max(r, m.Radius);
                return r;
            }
        }

        public static Roster Parse(string json, ClipLibrary clips)
        {
            JsonValue root = JsonValue.Parse(json);
            JsonValue hero = root["hero"];
            var r = new Roster
            {
                HeroRadius = hero["radius"].AsFloat(0.5f),
                HeroStrideM = hero["strideM"].AsFloat(0.9f),
                HeroReachM = hero["reachM"].AsFloat(2.4f),
                HeroSenseM = hero["senseM"].AsFloat(8f),
                HeroStepS = hero["stepS"].AsFloat(0f),
                HeroWindupS = hero["windupS"].AsFloat(0f),
                HeroWalkClip = clips.Find("hero walk"),
            };
            JsonValue mons = root["monsters"];
            var list = new List<MonsterType>();
            for (int i = 0; i < mons.Count; i++)
            {
                JsonValue m = mons[i];
                string name = m["name"].AsString();
                string rank = m["rank"].AsString("fodder");
                list.Add(new MonsterType
                {
                    Name = name,
                    Rank = rank == "boss" ? MonsterRank.Boss : rank == "elite" ? MonsterRank.Elite : MonsterRank.Fodder,
                    Radius = m["radius"].AsFloat(0.4f),
                    StepM = m["stepM"].AsFloat(0.55f),
                    ReachM = m["reachM"].AsFloat(0.5f),
                    HitPoints = m["hitPoints"].AsInt(2),
                    WalkClip = clips.Require(name + " walk"),
                    AttackClip = clips.Require(name + " attack"),
                    HitClip = clips.Require(name + " hit"),
                    DeathClip = clips.Require(name + " death"),
                });
            }
            r.Monsters = list.ToArray();

            JsonValue projs = root["projectiles"];
            r.Projectiles = new ProjectileType[projs.Count];
            for (int i = 0; i < projs.Count; i++)
                r.Projectiles[i] = new ProjectileType { Name = projs[i]["name"].AsString(), SpeedMps = projs[i]["speedMps"].AsFloat(30f) };
            for (int i = 0; i < mons.Count; i++)
            {
                string proj = mons[i]["projectile"].AsString();
                if (proj != null) r.Monsters[i].Projectile = r.ProjectileIndex(proj, "monster '" + r.Monsters[i].Name + "'");
            }

            r.Combos = ReadMoves(hero["combos"], clips, r, true);
            r.Spells = ReadMoves(root["spells"], clips, r, false);
            return r;
        }

        static Move[] ReadMoves(JsonValue obj, ClipLibrary clips, Roster r, bool combos)
        {
            var list = new List<Move>();
            if (obj.Type != JsonType.Object) return list.ToArray();
            foreach (KeyValuePair<string, JsonValue> kv in obj.Members)
            {
                JsonValue m = kv.Value;
                string shape = m["shape"].AsString("arc");
                var move = new Move
                {
                    Name = kv.Key,
                    Shape = shape == "ring" ? HitShape.Ring : shape == "line" ? HitShape.Line : shape == "circle" ? HitShape.Circle
                        : shape == "single" ? HitShape.Single : HitShape.Arc,
                    Radius = m["radius"].AsFloat(2f),
                    ArcDeg = m["arcDeg"].AsFloat(360f),
                    Length = m["length"].AsFloat(0f),
                    Width = m["width"].AsFloat(1f),
                    Strength = m["strength"].AsFloat(0.5f),
                    RangeM = m["rangeM"].AsFloat(10f),
                    Bars = m["bars"].AsFloat(1f),
                    AdvanceM = m["advanceM"].AsFloat(0f),
                    WindupS = m["windupS"].AsFloat(0f),
                    EffectRadius = m["effectRadius"].AsFloat(0f),
                    Effect = Catalog.Effect(m["effect"].AsString()),
                    Sound = Catalog.Sound(m["sound"].AsString()),
                };
                string proj = m["projectile"].AsString();
                if (proj != null) move.Projectile = r.ProjectileIndex(proj, "move '" + kv.Key + "'");
                if (combos) move.Clip = clips.Require("hero " + m["clip"].AsString(kv.Key));   // another combo's clip, by its name
                list.Add(move);
            }
            return list.ToArray();
        }
    }

    public sealed class GoalArea
    {
        public string Name;
        public System.Numerics.Vector2 Center;
        public float Radius;
    }

    /// <summary>
    /// The ground a run plays on: the hero's start, the named goal areas the level script's sections
    /// (and a stress script's hero) refer to, and, for an arena layout (TD6), the layout parameters,
    /// their placement list and its obstacle map. An open-ground layout (TD3's test layout: a
    /// <c>goals</c> list and no <c>areas</c>) has no placements and nothing blocks.
    /// </summary>
    public sealed class GroundLayout
    {
        public string Name;
        public System.Numerics.Vector2 HeroStart;
        public GoalArea[] Goals = new GoalArea[0];
        public ArenaLayout Arena;          // null on open ground
        public PlacementList Placements;   // null on open ground
        /// <summary>The crystal formations <see cref="WithCrystals"/> added after the layout's own placements; empty otherwise.</summary>
        public CrystalPiece[] Crystals = new CrystalPiece[0];
        /// <summary>Seconds spent generating the placement list and building the obstacle map (at load, once per layout).</summary>
        public double PlacementSeconds, NavGridSeconds;
        NavGrid nav;

        public GoalArea Goal(string name)
        {
            foreach (GoalArea g in Goals) if (g.Name == name) return g;
            return null;
        }

        /// <summary>The obstacle map and wall distance field, built on first use; null on open ground.</summary>
        public NavGrid Nav
        {
            get
            {
                if (nav == null && Placements != null)
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    nav = new NavGrid(Placements);
                    NavGridSeconds = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) / (double)System.Diagnostics.Stopwatch.Frequency;
                }
                return nav;
            }
        }

        /// <summary>
        /// An open-ground layout, or an arena layout's parameters (which need the footprint table to
        /// generate their placement list).
        /// </summary>
        /// <param name="density">An arena's decoration density for the placement list (0: the layout's own,
        /// its authored density), so the timeline navigates the scene the client shows.</param>
        public static GroundLayout Parse(string json, FootprintTable footprints = null, float density = 0)
        {
            JsonValue root = JsonValue.Parse(json);
            if (root.Has("areas"))
            {
                if (footprints == null) throw new FormatException("layout '" + root["name"].AsString() + "' has areas, which need the footprint annotations");
                ArenaLayout arena = ArenaLayout.Parse(json);
                return FromArena(density > 0 ? arena.WithDensity(density) : arena, footprints);
            }
            var layout = new GroundLayout
            {
                Name = root["name"].AsString("layout"),
                HeroStart = Vec(root["heroStart"]),
            };
            JsonValue goals = root["goals"];
            layout.Goals = new GoalArea[goals.Count];
            for (int i = 0; i < goals.Count; i++)
                layout.Goals[i] = new GoalArea { Name = goals[i]["name"].AsString(), Center = Vec(goals[i]["center"]), Radius = goals[i]["radius"].AsFloat(3f) };
            return layout;
        }

        public static GroundLayout FromArena(ArenaLayout arena, FootprintTable footprints)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            PlacementList list = arena.Generate(footprints);
            return new GroundLayout
            {
                Name = arena.Name,
                HeroStart = arena.HeroStart,
                Goals = list.Goals,
                Arena = arena,
                Placements = list,
                PlacementSeconds = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) / (double)System.Diagnostics.Stopwatch.Frequency,
            };
        }

        /// <summary>
        /// Where <paramref name="crystals"/> stand on this layout, as they would follow its own placements (trailer.md, "Crystal
        /// formations"). A formation on open ground, off the arena, or standing in one of the layout's blocking pieces fails,
        /// naming it (<paramref name="what"/>: "shot 2").
        /// </summary>
        public CrystalPiece[] ResolveCrystals(CrystalFormation[] crystals, string what)
        {
            if (crystals.Length == 0) return new CrystalPiece[0];
            if (Placements == null) throw new FormatException(what + ": crystals need an arena layout, not open ground '" + Name + "'");
            var pieces = new CrystalPiece[crystals.Length];
            for (int i = 0; i < crystals.Length; i++)
            {
                CrystalFormation c = crystals[i];
                string name = what + " crystal " + (i + 1);
                System.Numerics.Vector2 p = c.At.Resolve(this, name);
                System.Numerics.Vector2 half = Placements.Size / 2;
                if (Math.Abs(p.X) > half.X - c.RadiusM || Math.Abs(p.Y) > half.Y - c.RadiusM)
                    throw new FormatException(name + ": " + c.At + " is off layout '" + Name + "'");
                float clear = Nav.Distance(p);
                if (clear < c.RadiusM - 1e-3f)
                    throw new FormatException(name + ": at " + c.At + " it stands in a structure of layout '" + Name + "' (" + clear.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                        + " m clear, its radius is " + c.RadiusM.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ")");
                pieces[i] = new CrystalPiece(Placements.Items.Length + i, p, c.RadiusM, c.HeightM);
            }
            return pieces;
        }

        /// <summary>
        /// This layout with <paramref name="crystals"/> standing on it (trailer.md, "Crystal formations"): its placements and a
        /// blocking round footprint for each formation after them, so the run's obstacle map routes the hero and the horde round
        /// them and a break can take one out (<see cref="NavGrid.Remove"/>). The map is the new layout's own.
        /// </summary>
        public GroundLayout WithCrystals(CrystalFormation[] crystals, string what)
        {
            CrystalPiece[] pieces = ResolveCrystals(crystals, what);
            if (pieces.Length == 0) return this;
            PlacementList list = Placements;
            var props = new Footprint[list.Props.Length + pieces.Length];
            Array.Copy(list.Props, props, list.Props.Length);
            var items = new Placement[list.Items.Length + pieces.Length];
            Array.Copy(list.Items, items, list.Items.Length);
            for (int i = 0; i < pieces.Length; i++)
            {
                CrystalPiece c = pieces[i];
                props[list.Props.Length + i] = new Footprint
                {
                    Id = "trailer/" + CrystalPiece.Target(i), Blocks = true, Shape = FootprintShape.Circle, Radius = c.RadiusM, HeightM = c.HeightM,
                };
                items[c.Placement] = new Placement(list.Props.Length + i, c.Position, 0f, 1f, -1);
            }
            var withCrystals = new PlacementList
            {
                Layout = list.Layout, Seed = list.Seed, Size = list.Size, DecorationDensity = list.DecorationDensity,
                Props = props, Items = items, HeroStart = list.HeroStart, Goals = list.Goals,
            };
            return new GroundLayout { Name = Name, HeroStart = HeroStart, Goals = Goals, Arena = Arena, Placements = withCrystals, Crystals = pieces };
        }

        static System.Numerics.Vector2 Vec(JsonValue v) =>
            v.Count >= 2 ? new System.Numerics.Vector2(v[0].AsFloat(), v[1].AsFloat()) : System.Numerics.Vector2.Zero;
    }
}
