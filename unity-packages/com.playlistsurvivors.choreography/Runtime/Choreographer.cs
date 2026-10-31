using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PS.Choreography
{
    /// <summary>The clip annotations, roster and layout a run needs, loaded from JSON strings.</summary>
    public sealed class ChoreographyData
    {
        public ClipLibrary Clips;
        public Roster Roster;
        public GroundLayout Layout;

        public FootprintTable Footprints;   // null when only open ground was loaded

        /// <summary>
        /// The run's inputs from JSON. An arena layout (one with <c>areas</c>) needs the footprint
        /// annotations (Data/footprints.json) to generate its placement list; open ground does not.
        /// </summary>
        /// <param name="density">The arena's decoration density to navigate (0: the layout's authored one).
        /// The client passes the density of the scene it loads, so monsters walk round what is drawn.</param>
        public static ChoreographyData FromJson(string annotationsJson, string rosterJson, string layoutJson, string footprintsJson = null, float density = 0)
        {
            ClipLibrary clips = ClipLibrary.Parse(annotationsJson);
            FootprintTable fp = footprintsJson == null ? null : FootprintTable.Parse(footprintsJson);
            var data = new ChoreographyData
            {
                Clips = clips,
                Roster = Roster.Parse(rosterJson, clips),
                Footprints = fp,
                Layout = GroundLayout.Parse(layoutJson, fp, density),
            };
            data.Layout.Arena?.Validate(data.Roster);
            return data;
        }

        /// <summary>The same clips, roster and footprints on another layout (a stress script's ground).</summary>
        public ChoreographyData WithLayout(GroundLayout layout)
        {
            layout.Arena?.Validate(Roster);
            return new ChoreographyData { Clips = Clips, Roster = Roster, Footprints = Footprints, Layout = layout };
        }
    }

    /// <summary>
    /// The choreographer (choreography.md, "The choreographer"): a deterministic forward
    /// simulation at 60 ticks per second over the whole song (or a stress script's synthetic
    /// grid), run at load. Its result is the <see cref="Timeline"/>.
    /// </summary>
    public static class Choreographer
    {
        public const double TickRate = 60;

        /// <summary>The song's timeline from its map and level script.</summary>
        public static Timeline Run(SongMap map, LevelScript script, ChoreographyData data, uint? seedOverride = null)
        {
            long t0 = Stopwatch.GetTimestamp();
            var plans = new SectionPlan[map.Sections.Length];
            for (int i = 0; i < plans.Length; i++)
            {
                SongSection sec = map.Sections[i];
                LevelSection ls = script.For(sec.Label);
                if (ls == null) throw new FormatException("level script: no entry for song-map section '" + sec.Label + "'");
                GoalArea goal = null;
                if (ls.Goal != null)
                {
                    goal = data.Layout.Goal(ls.Goal);
                    if (goal == null) throw new FormatException("level script: section '" + sec.Label + "' names unknown goal '" + ls.Goal + "'");
                }
                int target = (int)Math.Round(ls.OnScreen * script.EnvelopeOnScreen);
                plans[i] = new SectionPlan
                {
                    Label = sec.Label,
                    TargetOnScreen = target,
                    Reserve = (int)Math.Round(script.ReserveFactor * target),
                    ViewWidth = ls.ViewWidthM,
                    Preset = script.Preset(ls.Load),
                    Goal = goal,
                    Mix = data.Roster.ResolveMix(ls.Mix),
                    Combo = ls.Combo,
                    Spells = ls.Spells,
                    Boss = ls.Boss == null ? -1 : data.Roster.MonsterIndex(ls.Boss),
                    BossFrom = ls.BossFrom == null ? null : data.Layout.Goal(ls.BossFrom),
                    HeroAttacks = true,
                    MonsterAttacks = true,
                    Clear = ls.Clear,
                    Breaks = ls.Breaks,
                    BreakPieces = Array.ConvertAll(ls.Breaks, e => StructureBreaks.Resolve(data.Layout, e.Name)),   // fails on a target the layout lacks
                };
                if (ls.Boss != null && plans[i].Boss < 0) throw new FormatException("level script: unknown boss type '" + ls.Boss + "'");
                if (ls.BossFrom != null && plans[i].BossFrom == null) throw new FormatException("level script: section '" + sec.Label + "' names unknown bossFrom goal '" + ls.BossFrom + "'");
            }
            ShotRules shot = ShotRules.Resolve(script, map, data, plans);
            var sim = new Sim(map, plans, data, seedOverride ?? script.Seed, initialInnerRadius: 6f, worstBeats: false, heroStart: shot.HeroStart, viewPitchDeg: script.ViewPitchDeg,
                clearRadius: script.ClearRadiusM, clumpSize: script.ClumpSize, strandedS: script.StrandedS, bossFallsLast: script.BossFallsLast,
                tuning: script.Tuning, shot: shot);
            Timeline tl = sim.Run("song:" + map.Song);
            tl.BuildSeconds = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
            return tl;
        }

        /// <summary>A stress timeline (choreography.md, "Stress timelines").</summary>
        public static Timeline RunStress(StressScript script, ChoreographyData data, uint? seedOverride = null)
        {
            long t0 = Stopwatch.GetTimestamp();
            SongMap map = SongMap.Synthetic(script.Bpm, script.Seconds);
            var view = new ViewGeometry(script.ViewWidthM);
            int n = Math.Min(script.N, view.PackingCapacity());
            if (script.Layout != null && data.Layout.Arena != null && script.Layout != data.Layout.Name)
                throw new FormatException("stress script " + script.Scene + " expects layout '" + script.Layout + "', but the data is on '" + data.Layout.Name + "'");
            System.Numerics.Vector2 hold = System.Numerics.Vector2.Zero;
            if (script.Hold != null)
            {
                GoalArea area = data.Layout.Goal(script.Hold);
                if (area != null) hold = area.Center;
                else if (data.Layout.Arena != null) throw new FormatException("stress script " + script.Scene + ": no area '" + script.Hold + "' in layout '" + data.Layout.Name + "'");
            }
            if (script.HoldAt.HasValue) hold = script.HoldAt.Value;
            var plan = new SectionPlan
            {
                Label = map.Sections[0].Label,
                TargetOnScreen = n,
                Reserve = (int)Math.Round(script.ReserveFactor * n),
                ViewWidth = script.ViewWidthM,
                Preset = LoadPreset.Get(script.Load),
                Goal = new GoalArea { Name = script.Hold ?? "centre", Center = hold, Radius = script.HoldRadiusM },
                Mix = data.Roster.ResolveMix(script.Mix),
                Combo = script.HeroAttacks ? script.Combo : new GridEntry[0],
                Spells = script.HeroAttacks ? script.EffectiveSpells() : new GridEntry[0],
                Boss = -1,
                HeroAttacks = script.HeroAttacks,
                MonsterAttacks = script.MonsterAttacks,
            };
            var sim = new Sim(map, new[] { plan }, data, seedOverride ?? script.Seed, initialInnerRadius: script.OpeningInnerM > 0 ? script.OpeningInnerM : data.Roster.HeroRadius + 1f,
                worstBeats: script.WorstBeats, heroStart: hold);
            Timeline tl = sim.Run("stress:" + script.Scene + ":n" + n + ":w" + JsonOut.Format(script.ViewWidthM, 1) + ":" + script.Load);
            tl.BuildSeconds = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
            return tl;
        }
    }

    sealed class SectionPlan
    {
        public string Label;
        public int TargetOnScreen, Reserve;
        public float ViewWidth;
        public LoadPreset Preset;
        public GoalArea Goal;           // null: hold ground where the hero stands
        public int[] Mix;               // cumulative weights ×1000 per roster type
        public GridEntry[] Combo, Spells;
        public int Boss;
        public GoalArea BossFrom;       // see LevelSection.BossFrom
        public bool HeroAttacks, MonsterAttacks;
        public bool Clear;              // see LevelSection.Clear
        public GridEntry[] Breaks = new GridEntry[0];   // see LevelSection.Breaks
        public int[][] BreakPieces = new int[0][];      // each break's placements
    }

    /// <summary>
    /// A level script's trailer-shot fields (trailer.md, "Shot simulations"), resolved on the song map and the
    /// layout for one run. <see cref="Default"/> holds every field's default, under which the sim runs as before.
    /// </summary>
    sealed class ShotRules
    {
        public double SpanFrom = double.NaN, SpanTo = double.NaN;   // NaN: the whole song
        public double Cut = double.NaN, Final = double.NaN;         // song seconds, NaN: none
        public bool HasFixed;                                       // some combo entry fires at a fixed time
        public System.Numerics.Vector2? HeroStart;
        public System.Numerics.Vector2[] Path;                      // null: goals as before
        public float PathStepM;
        public bool PathLoop;
        public bool HoldGround;
        public float HeroStepS = -1;                                // negative: the roster's
        public bool KillBudget = true;
        public float ShapeScale;                                    // 0: the kill-rate feedback
        public float YawDeg;
        public float ViewWidthTo;                                   // 0: no zoom across the shot
        public string BossFrom;                                     // null: no boss rule
        public System.Numerics.Vector2? BossArea;                   // a named area the boss enters from
        public int BossType = -1;
        public int[] Duel;                                          // roster types, one per cast member; null: none

        public static readonly ShotRules Default = new ShotRules();

        public bool HasSpan => !double.IsNaN(SpanFrom);
        /// <summary>The hero acts on every beat (a kite path or the duel): it strides on a beat whose blow would start later.</summary>
        public bool ActsEveryBeat => Path != null || HoldGround;

        public static ShotRules Resolve(LevelScript script, SongMap map, ChoreographyData data, SectionPlan[] plans)
        {
            var r = new ShotRules
            {
                SpanFrom = script.SpanFromS, SpanTo = script.SpanToS,
                HoldGround = script.HoldGround, HeroStepS = script.HeroStepS, KillBudget = script.KillBudget,
                ShapeScale = script.ShapeScale, YawDeg = script.YawDeg, ViewWidthTo = script.ViewWidthToM, BossFrom = script.Boss,
            };
            var grid = new BeatGrid(map);
            if (script.Cut != null) r.Cut = map.TimeOfBarBeat(script.Cut, grid);
            if (script.Final != null) r.Final = map.TimeOfBarBeat(script.Final, grid);
            if (script.HeroPath != null)
            {
                r.Path = new System.Numerics.Vector2[script.HeroPath.Via.Length];
                for (int i = 0; i < r.Path.Length; i++) r.Path[i] = script.HeroPath.Via[i].Resolve(data.Layout, "path waypoint " + (i + 1));
                r.PathStepM = script.HeroPath.StepM;
                r.PathLoop = script.HeroPath.Loop;
            }
            if (script.HeroAt != null) r.HeroStart = script.HeroAt.Resolve(data.Layout, "heroAt");
            else if (r.Path != null) r.HeroStart = r.Path[0];
            if (r.BossFrom != null)
            {
                for (int t = 0; t < data.Roster.Monsters.Length; t++) if (data.Roster.Monsters[t].Rank == MonsterRank.Boss) { r.BossType = t; break; }
                if (r.BossType < 0) throw new FormatException("level script: boss rule, but the roster has no boss");
                if (double.IsNaN(r.Final)) throw new FormatException("level script: the boss rule needs a final slam (final)");
                if (r.BossFrom != "ahead" && r.BossFrom != "behind" && r.BossFrom != "left" && r.BossFrom != "right")
                    r.BossArea = new LayoutPoint { Area = r.BossFrom }.Resolve(data.Layout, "boss");
            }
            if (script.Duel != null)
            {
                var cast = new System.Collections.Generic.List<int>();
                foreach (MixEntry e in script.Duel)
                {
                    int t = data.Roster.MonsterIndex(e.Type);
                    if (t < 0) throw new FormatException("duel: unknown type '" + e.Type + "'");
                    for (int k = 0; k < (int)Math.Round(e.Weight); k++) cast.Add(t);
                }
                r.Duel = cast.ToArray();
            }
            // Fixed positions resolve per run: anchors are consumed from the cut (or the span's start) on.
            double anchorsFrom = !double.IsNaN(r.Cut) ? r.Cut : r.HasSpan ? r.SpanFrom : double.NegativeInfinity;
            foreach (SectionPlan p in plans)
            {
                p.Combo = GridEntry.Resolve(p.Combo, map, grid, r.Cut, r.Final, anchorsFrom, "combo entry");
                p.Spells = GridEntry.Resolve(p.Spells, map, grid, r.Cut, r.Final, anchorsFrom, "spell entry");
                foreach (GridEntry e in p.Combo) if (e.Fixed != FixedPosition.None) r.HasFixed = true;
            }
            return r;
        }
    }

    static class RosterExtensions
    {
        public static int[] ResolveMix(this Roster roster, MixEntry[] mix)
        {
            var cum = new int[roster.Monsters.Length];
            int acc = 0;
            for (int t = 0; t < roster.Monsters.Length; t++)
            {
                foreach (MixEntry e in mix)
                {
                    if (e.Type != roster.Monsters[t].Name) continue;
                    acc += (int)Math.Round(e.Weight * 1000);
                }
                cum[t] = acc;
            }
            foreach (MixEntry e in mix)
                if (roster.MonsterIndex(e.Type) < 0) throw new FormatException("monster mix: unknown type '" + e.Type + "'");
            if (acc <= 0) throw new FormatException("monster mix: no weight");
            return cum;
        }
    }
}
