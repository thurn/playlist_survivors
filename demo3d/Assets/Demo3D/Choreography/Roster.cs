using System;
using System.Collections.Generic;

namespace Demo3D.Choreography
{
    public enum MonsterRank { Fodder, Elite, Boss }

    public sealed class MonsterType
    {
        public string Name;
        public MonsterRank Rank;
        public float Radius, StepM, ReachM;
        public int HitPoints;
        public int WalkClip, AttackClip, HitClip, DeathClip;   // indices into the clip library
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
        public int HeroWalkClip;
        public MonsterType[] Monsters;
        public ProjectileType[] Projectiles;
        public Move[] Combos, Spells;

        public int MonsterIndex(string name)
        {
            for (int i = 0; i < Monsters.Length; i++) if (Monsters[i].Name == name) return i;
            return -1;
        }

        public Move Combo(string name) => FindMove(Combos, name);
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
                    Effect = Catalog.Effect(m["effect"].AsString()),
                    Sound = Catalog.Sound(m["sound"].AsString()),
                };
                string proj = m["projectile"].AsString();
                if (proj != null)
                {
                    for (int i = 0; i < r.Projectiles.Length; i++) if (r.Projectiles[i].Name == proj) move.Projectile = i;
                    if (move.Projectile < 0) throw new FormatException("roster: unknown projectile '" + proj + "'");
                }
                if (combos) move.Clip = clips.Require("hero " + kv.Key);
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
        public static GroundLayout Parse(string json, FootprintTable footprints = null)
        {
            JsonValue root = JsonValue.Parse(json);
            if (root.Has("areas"))
            {
                if (footprints == null) throw new FormatException("layout '" + root["name"].AsString() + "' has areas, which need the footprint annotations");
                return FromArena(ArenaLayout.Parse(json), footprints);
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

        static System.Numerics.Vector2 Vec(JsonValue v) =>
            v.Count >= 2 ? new System.Numerics.Vector2(v[0].AsFloat(), v[1].AsFloat()) : System.Numerics.Vector2.Zero;
    }
}
