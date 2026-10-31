using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace PS.Choreography
{
    enum ImpactKind { HeroCombo, Ring, Bolt, Circle, LaserTick, MonsterAttack, WorstRing, MonsterBolt, Break }

    struct Impact
    {
        public double Time;
        public long Seq;
        public ImpactKind Kind;
        public int Entity, Target, Clip;
        public Move Move;
        public Vector2 Point;
        public float Yaw, Strength;
        public double ClipStart;     // the fitted clip's start and speed (attacks), NaN otherwise
        public float ClipSpeed;

        public bool Kills => Kind != ImpactKind.LaserTick && Kind != ImpactKind.MonsterAttack && Kind != ImpactKind.MonsterBolt && Kind != ImpactKind.Break;
    }

    /// <summary>A structure break the level script schedules (choreography.md, "Structure breaks"), and its bolt once sent.</summary>
    sealed class ScheduledBreak
    {
        public double Time;
        public float Strength;
        public string Target;
        public int[] Pieces;
        public int Projectile = -1;
        public Vector2 Origin, Point;
    }

    struct SoundRequest
    {
        public int Category;
        public double Time;
        public float Strength;
        public int Seq;
    }

    /// <summary>
    /// One run of the choreographer. Everything is single-threaded and ordered by entity id or by
    /// (time, sequence), so the same inputs give the same timeline on one runtime.
    /// </summary>
    sealed class Sim
    {
        const double TickDt = 1.0 / Choreographer.TickRate;
        const float SmallRadius = 0.5f;          // monsters above this are checked from a list, not the hash
        const float SimOverlap = 0.1f;           // the sim keeps overlaps within 10% of the smaller radius (the test allows 25%; transients add up to ~0.1)
        const float KnockbackSeconds = 0.12f;
        const float CorpseLinger = 1.0f;
        const float HitShapeInflate = 0.5f;      // a monster is struck when its centre is within the shape plus half its radius
        const float FollowTimeConstant = 0.4f;   // camera-and-sound.md: critically damped, 0.4 s
        const int ZoomBars = 2;
        const float SpawnMargin = 2f, SpawnBand = 4f;
        const float FarDespawn = 30f;
        const float MaxStepReach = 1.4f;         // the longest step a neighbour takes in a beat (0.95 m × 1.3 off screen), plus margin
        const double SectionLeadSeconds = 4;   // releases look ahead to a fuller next section this far (TD13)
        const float AheadCos = 0.2588f;          // cos 75°
        const float OffscreenStride = 1.3f;     // culled monsters walking in take longer steps
        const float ClumpDriftMps = 0.5f;       // the clumps' arcs slide along the view's edge this fast, so masses arrive from every side

        readonly SongMap map;
        readonly BeatGrid grid;
        readonly SectionPlan[] plans;
        readonly Roster roster;
        readonly ClipLibrary clips;
        readonly uint seed;
        readonly float initialInnerRadius;
        readonly bool worstBeats;
        readonly Vector2 heroStart;
        readonly double viewPitchDeg;
        readonly float clearRadius;   // the level script's clear ring: monsters' bodies keep this far from the hero (0: contact)
        readonly SimTuning tuning;    // the level script's tuning knobs (SimTuning)
        readonly ShotRules shot;      // the level script's trailer-shot fields (ShotRules.Default: none)
        readonly float heroStepS;     // the hero's step time: the shot's, else the roster's
        readonly int[] barOfBeat;
        readonly int[][] spellCount;

        Rng spawnRng, spellRng, cooldownRng;

        // Clumps (the level script's): each type refills from its own arcs of the view's edge, so it arrives as a mass.
        // Types are dealt in the mix's proportions rather than drawn, and a refill that finds no room in its type's
        // arcs is retried as that type, so crowded arcs do not thin their type out of the section's count.
        readonly float strandedS;
        readonly bool bossFallsLast;     // a clearing section's blows spare the boss until its last        // a monster off screen and unreleased this long leaves the song (0: never)
        readonly int clumpSize;          // the heaviest type's clump; 0: every type anywhere on the edge, drawn at random
        readonly float[] clumpArc, clumpStart;   // per roster type, its arc's length and start in the current section's pattern, in m
        readonly double[] clumpOwed;     // per roster type, the share of refills dealt to it so far minus those it got
        readonly List<int> clumpRetry = new List<int>();   // types whose refills found no room, owed first
        int clumpRetryNow;               // retries the current refill may take: those owed when it began, so one that fails again waits a beat
        float clumpPattern;              // the pattern's length: the arcs of the section's types end to end

        // Entities, indexed by id.
        int entityCount;
        EntityKind[] kind = new EntityKind[0];
        int[] etype = new int[0];
        double[] spawnT = new double[0], despawnT = new double[0];
        uint[] eseed = new uint[0];
        // Monster state, indexed by id.
        Vector2[] pos = new Vector2[0], rest = new Vector2[0], planFrom = new Vector2[0];
        MotionSegment[] cur = new MotionSegment[0];   // each monster's latest segment
        int[] hp = new int[0], planSerial = new int[0];
        float[] facing = new float[0], radius = new float[0];
        byte[] state = new byte[0];   // 0 none, 1 alive, 2 dying, 3 gone
        double[] busyUntil = new double[0], lastActionEnd = new double[0], bossDeathAfter = new double[0], lastWanted = new double[0];
        bool bossWalksIn;   // the boss entered from a level script's bossFrom side
        bool bossEntered;   // the boss rule's boss is in
        float bossKeep;     // the boss rule's keep-out from the hero, centre to centre
        bool[] onScreen = new bool[0], released = new bool[0];
        sbyte[] sideMem = new sbyte[0];   // with walls: the side of each monster's last sidestep (0 none yet)

        const byte Alive = 1, Dying = 2, Gone = 3;

        int[] live = new int[256];
        int liveCount;
        int[] big = new int[16];
        int bigCount;
        readonly SpatialHash hash = new SpatialHash();
        int[] buf = new int[1024];
        int[] strike = new int[256];
        int[] cand = new int[256];
        long[] keys = new long[256];
        int[] order = new int[256];
        int[] offIds = new int[256];
        long[] offKeys = new long[256];
        readonly List<int> beatSpawns = new List<int>();   // monsters spawned since the hash was last rebuilt

        readonly List<MotionSegment> segs = new List<MotionSegment>(1 << 16);
        readonly List<ActionEvent> actions = new List<ActionEvent>(1 << 14);
        readonly List<EffectEvent> effects = new List<EffectEvent>(1 << 14);
        readonly List<SoundRequest> sounds = new List<SoundRequest>(1 << 14);
        FramingSample[] framing;

        // Hero.
        readonly List<int> heroSegs = new List<int>();
        double heroPendingImpact = -1, heroLastEnd;
        bool heroFlurry;   // the pending impact falls between beats (an eighth's combo entry)
        int comboCycle;
        GoalArea goal;
        bool goalReached;
        // The kite path (the shot's): the waypoint the hero heads for, and each waypoint's field.
        int pathNext;
        bool pathDone;     // past a path's last waypoint: it runs on along its last leg
        Vector2 runOn;
        FlowField[] pathFields;

        // Section and feedback.
        int curSection = -1;
        SectionPlan plan;
        double secStart, secEnd;
        int secKills;
        float shapeScale = 1f;
        int barStruck;
        double barStart;
        SectionStats[] stats;
        int[] statTicks;

        // Camera.
        float wFrom, wTo, viewWidth;
        double wT0, wT1;
        Vector2 focus, focusVel;

        // Impacts.
        Impact[] heap = new Impact[256];
        int heapCount;
        long impactSeq;
        int beatSerial;
        float beatMaxStep;   // the longest step planned so far this beat
        int totalKills, killsAtClassify;
        double deathRate, lastClassify;
        int onScreenCount, offscreenAlive;
        bool populated;

        // Navigation (TD6): null on open ground, where the run is exactly TD3's.
        const float TypicalRadius = 0.4f;     // the field toward the hero crosses cells with more clearance than this
        const float RebuildMoveM = 1f;        // the field toward the hero is rebuilt once the hero stands this far from its source
        const float FieldReachM = 70f;        // ... and reaches this far by the way: past the view, the reserve band and the far despawn
        const float WallSlack = 0.02f;        // a step's intermediate points may come this much closer to a wall than its radius
        readonly NavGrid nav;
        FlowField heroField, heroFieldNext;   // toward the hero: the field in use, and the one being rebuilt
        readonly Dictionary<string, FlowField> goalFields = new Dictionary<string, FlowField>();
        FlowField goalField;                  // the current section's goal field
        int flowRebuilds, flowSlices, sliceBudget;

        // Structure breaks (a structure-breaks overlay): null without one, and the run is exactly as before.
        readonly ScheduledBreak[] breaks;
        readonly Move breakSpell;             // the roster's bolt, which the hero sends at each piece
        int nextBreak;                        // the first break whose bolt is not yet sent
        readonly List<BreakEvent> breakEvents = new List<BreakEvent>();
        bool navChanged;                      // a break changed the obstacle map since the last beat's rebuild check
        readonly List<string> goalRebuilds = new List<string>();   // goal fields to rebuild over the new map, in turn
        FlowField goalNext;                   // the goal field being rebuilt (goalRebuilds[0])
        int goalBudget, openLines;            // a rebuild's slices: cells per tick, and the open field's lines per tick

        public Sim(SongMap map, SectionPlan[] plans, ChoreographyData data, uint seed, float initialInnerRadius, bool worstBeats,
            Vector2? heroStart = null, double viewPitchDeg = ViewGeometry.PitchDeg, float clearRadius = 0, int clumpSize = 0, float strandedS = 0, bool bossFallsLast = false,
            SimTuning tuning = null, ShotRules shot = null)
        {
            this.tuning = tuning ?? SimTuning.Default;
            this.shot = shot ?? ShotRules.Default;
            heroStepS = this.shot.HeroStepS >= 0 ? this.shot.HeroStepS : data.Roster.HeroStepS;
            if (this.shot.ShapeScale > 0) shapeScale = this.shot.ShapeScale;
            this.strandedS = strandedS;
            this.bossFallsLast = bossFallsLast;
            this.viewPitchDeg = viewPitchDeg;
            this.clearRadius = clearRadius;
            this.map = map;
            grid = new BeatGrid(map);
            this.plans = plans;
            roster = data.Roster;
            clips = data.Clips;
            this.seed = seed;
            this.initialInnerRadius = initialInnerRadius;
            this.worstBeats = worstBeats;
            this.heroStart = heroStart ?? data.Layout.HeroStart;
            spawnRng = new Rng(seed, 1);
            spellRng = new Rng(seed, 2);
            cooldownRng = new Rng(seed, 3);
            this.clumpSize = clumpSize;
            if (clumpSize > 0)
            {
                clumpArc = new float[roster.Monsters.Length];
                clumpStart = new float[roster.Monsters.Length];
                clumpOwed = new double[roster.Monsters.Length];
            }
            barOfBeat = new int[map.Beats.Length];
            for (int bar = 1; ; bar++)
            {
                int b0 = map.DownbeatIndexOfBar(bar), b1 = map.DownbeatIndexOfBar(bar + 1);
                if (b0 >= map.Beats.Length) break;
                for (int b = Math.Max(0, b0); b < b1 && b < map.Beats.Length; b++) barOfBeat[b] = bar;
            }
            spellCount = new int[plans.Length][];
            for (int i = 0; i < plans.Length; i++)
            {
                spellCount[i] = new int[plans[i].Spells.Length];
                foreach (GridEntry e in plans[i].Combo)
                    if (roster.Combo(e.Name) == null) throw new FormatException("combo '" + e.Name + "' is not a hero combo in the roster");
                foreach (GridEntry e in plans[i].Spells)
                    if (roster.Spell(e.Name) == null) throw new FormatException("spell '" + e.Name + "' is not in the roster");
            }
            nav = data.Layout.Nav;
            foreach (SectionPlan p in plans)
                if (p.Breaks.Length > 0)
                {
                    // Breaks change the obstacle map, so this run navigates its own copy.
                    nav = nav.Clone();
                    breaks = ScheduleBreaks();
                    foreach (Move mv in roster.Spells) if (mv.Shape == HitShape.Single && breakSpell == null) breakSpell = mv;
                    if (breakSpell == null) throw new FormatException("structure breaks: the roster has no bolt (a spell of shape single) for the hero to send");
                    break;
                }
            if (nav != null)
            {
                heroField = new FlowField(nav, TypicalRadius);
                heroFieldNext = new FlowField(nav, TypicalRadius);
                heroField.MaxCost = heroFieldNext.MaxCost = (int)(FieldReachM / FlowField.MetresPerCost);
                // The hero's goal fields, one per named goal, from the goal's centre outward, built at load.
                foreach (SectionPlan p in plans)
                {
                    if (p.Goal == null || goalFields.ContainsKey(p.Goal.Name)) continue;
                    var f = new FlowField(nav, roster.HeroRadius + 0.05f);
                    f.BeginArea(p.Goal.Center, Math.Min(p.Goal.Radius, 1f));
                    f.BuildAll();
                    goalFields[p.Goal.Name] = f;
                }
                // The kite path's fields, one per waypoint, so each leg routes round the environment's structures.
                if (this.shot.Path != null)
                {
                    pathFields = new FlowField[this.shot.Path.Length];
                    for (int i = 0; i < pathFields.Length; i++)
                    {
                        pathFields[i] = new FlowField(nav, roster.HeroRadius + 0.05f);
                        pathFields[i].BeginArea(this.shot.Path[i], 1f);
                        pathFields[i].BuildAll();
                    }
                }
            }
            if (this.shot.Path != null) pathNext = this.shot.HeroStart == this.shot.Path[0] && this.shot.Path.Length > 1 ? 1 : 0;
            stats = new SectionStats[map.Sections.Length];
            statTicks = new int[map.Sections.Length];
            for (int i = 0; i < stats.Length; i++)
            {
                SongSection s = map.Sections[i];
                SectionPlan p = plans[i];
                stats[i] = new SectionStats
                {
                    Label = s.Label, Feel = s.Feel, Start = s.Start, End = s.End, Load = p.Preset.Name,
                    Goal = p.Goal == null ? null : p.Goal.Name, TargetOnScreen = p.TargetOnScreen, ViewWidthM = p.ViewWidth,
                    TargetDeathsPerSecond = p.Preset.DeathsPerSecond(p.TargetOnScreen),
                    TargetHitsPerSecond = p.Preset.HitsPerSecond(p.TargetOnScreen),
                };
            }
        }

        // ------------------------------------------------------------------ run

        public Timeline Run(string name)
        {
            // A shot simulates only its span (trailer.md, "Shot simulations"): the timeline still runs in song
            // seconds from 0, so its framing before the span holds the span's first sample.
            double from = shot.HasSpan ? shot.SpanFrom : 0;
            double duration = shot.HasSpan ? shot.SpanTo : map.Duration;
            int ticks = (int)Math.Ceiling(duration * Choreographer.TickRate) + 1;
            firstTick = shot.HasSpan ? (int)Math.Ceiling(from * Choreographer.TickRate - 1e-9) : 0;
            var tickMs = new float[ticks - firstTick];
            framing = new FramingSample[ticks];

            // Decision times: a pre-roll at the start when the first beat is later, then every beat.
            var decisions = new List<double>();
            var decisionBeat = new List<int>();
            if (!shot.HasSpan)
            {
                if (map.Beats[0] > 0) { decisions.Add(0); decisionBeat.Add(-1); }
                for (int b = 0; b < map.Beats.Length; b++) { decisions.Add(map.Beats[b]); decisionBeat.Add(b); }
            }
            else
            {
                int b0 = 0;
                while (b0 < map.Beats.Length && map.Beats[b0] < from - 1e-9) b0++;
                if (b0 >= map.Beats.Length || map.Beats[b0] > from + 1e-9) { decisions.Add(from); decisionBeat.Add(-1); }
                for (int b = b0; b < map.Beats.Length && map.Beats[b] <= duration + 1e-9; b++) { decisions.Add(map.Beats[b]); decisionBeat.Add(b); }
            }

            int hero = NewEntity(EntityKind.Hero, -1, from);
            despawnT[hero] = duration;
            heroSegs.Add(AddSeg(new MotionSegment(hero, from, from, heroStart, heroStart, 0, 0, Ease.Linear)));
            focus = heroStart;

            double toMs = 1000.0 / Stopwatch.Frequency;
            int di = 0;
            for (int k = firstTick; k < ticks; k++)
            {
                long start = Stopwatch.GetTimestamp();
                double t = shot.HasSpan ? Math.Max(from, k * TickDt) : k * TickDt;
                while (true)
                {
                    double nextImpact = heapCount > 0 ? heap[0].Time : double.PositiveInfinity;
                    double nextDecision = di < decisions.Count ? decisions[di] : double.PositiveInfinity;
                    double next = Math.Min(nextImpact, nextDecision);
                    if (next > t + 1e-9) break;
                    if (nextImpact <= nextDecision) ProcessImpact(Pop());
                    else
                    {
                        int b = decisionBeat[di];
                        double tnext = di + 1 < decisions.Count ? decisions[di + 1]
                            : b >= 0 && b + 1 < map.Beats.Length ? map.Beats[b + 1]
                            : decisions[di] + (map.Beats[map.Beats.Length - 1] - map.Beats[map.Beats.Length - 2]);
                        Decide(decisions[di], tnext, b);
                        di++;
                    }
                }
                UpdateTick(t, k);
                tickMs[k - firstTick] = (float)((Stopwatch.GetTimestamp() - start) * toMs);
            }
            for (int k = 0; k < firstTick; k++) framing[k] = framing[firstTick];
            return Build(name, duration, new SimTickTiming(tickMs, Choreographer.TickRate, flowSlices, flowRebuilds));
        }

        // ------------------------------------------------------------------ entities

        int NewEntity(EntityKind k, int type, double spawn)
        {
            int id = entityCount++;
            if (id >= kind.Length) Grow(Math.Max(256, kind.Length * 2));
            kind[id] = k; etype[id] = type; spawnT[id] = spawn; despawnT[id] = double.PositiveInfinity;
            eseed[id] = Rng.Hash(seed, (uint)id, 0x5EED);
            return id;
        }

        void Grow(int n)
        {
            Array.Resize(ref kind, n); Array.Resize(ref etype, n); Array.Resize(ref spawnT, n); Array.Resize(ref despawnT, n);
            Array.Resize(ref eseed, n); Array.Resize(ref pos, n); Array.Resize(ref rest, n); Array.Resize(ref planFrom, n);
            Array.Resize(ref cur, n); Array.Resize(ref hp, n); Array.Resize(ref planSerial, n); Array.Resize(ref facing, n);
            Array.Resize(ref radius, n); Array.Resize(ref state, n); Array.Resize(ref busyUntil, n); Array.Resize(ref lastActionEnd, n);
            Array.Resize(ref bossDeathAfter, n); Array.Resize(ref onScreen, n); Array.Resize(ref released, n); Array.Resize(ref lastWanted, n);
            Array.Resize(ref sideMem, n);
        }

        int AddSeg(MotionSegment s)
        {
            segs.Add(s);
            return segs.Count - 1;
        }

        void SetMonsterSeg(int m, MotionSegment s)
        {
            AddSeg(s);
            cur[m] = s;
            rest[m] = s.To;
            facing[m] = s.FacingTo;
        }

        Vector2 MonsterPosAt(int m, double t) => cur[m].PositionAt(t);

        ViewGeometry View(float width) => new ViewGeometry(width, viewPitchDeg, shot.YawDeg);

        /// <summary>
        /// Where the monster stands at <paramref name="t"/>. With <paramref name="hold"/>, it stops
        /// there: a zero-length segment supersedes the rest of its current one. Segments are never
        /// cut short in place, so the motion before <paramref name="t"/> stays exactly as planned.
        /// </summary>
        Vector2 StopAt(int m, double t, bool hold)
        {
            MotionSegment s = cur[m];
            if (s.T1 <= t) return s.To;
            Vector2 p = s.PositionAt(t);
            if (hold)
            {
                float f = s.FacingAt(t);
                SetMonsterSeg(m, new MotionSegment(m, t, t, p, p, f, f, Ease.Linear));
            }
            return p;
        }

        Vector2 HeroPosAt(double t)
        {
            for (int i = heroSegs.Count - 1; i >= 0; i--)
            {
                MotionSegment s = segs[heroSegs[i]];
                if (s.T0 <= t) return s.PositionAt(t);
            }
            return heroStart;
        }

        Vector2 HeroRest => segs[heroSegs[heroSegs.Count - 1]].To;
        float HeroFacing => segs[heroSegs[heroSegs.Count - 1]].FacingTo;

        void AddHeroSeg(MotionSegment s) => heroSegs.Add(AddSeg(s));

        int Spawn(int type, Vector2 p, double t, bool initial)
        {
            MonsterType mt = roster.Monsters[type];
            int m = NewEntity(EntityKind.Monster, type, t);
            state[m] = Alive;
            hp[m] = Math.Max(1, (int)Math.Round(mt.HitPoints * tuning.HitPointScale(mt.Rank)));
            radius[m] = mt.Radius;
            pos[m] = p;
            busyUntil[m] = 0;
            lastActionEnd[m] = 0;
            bossDeathAfter[m] = double.PositiveInfinity;
            lastWanted[m] = t;
            planSerial[m] = -1;
            float yaw = Easing.Yaw(HeroRest - p);
            SetMonsterSeg(m, new MotionSegment(m, t, t, p, p, yaw, yaw, Ease.Linear));
            actions.Add(new ActionEvent(m, ActionKind.Spawn, t, -1, -1, 0));
            if (liveCount == live.Length) Array.Resize(ref live, live.Length * 2);
            live[liveCount++] = m;
            if (!initial) stats[curSection].Spawns++;
            return m;
        }

        int PickType(int[] cumulative, bool fodderOnly)
        {
            int total = cumulative[cumulative.Length - 1];
            int r = spawnRng.Next(total);
            int type = 0;
            while (type < cumulative.Length - 1 && r >= cumulative[type]) type++;
            MonsterType mt = roster.Monsters[type];
            if (mt.Rank == MonsterRank.Boss || (fodderOnly && mt.Rank != MonsterRank.Fodder))
            {
                for (int i = 0; i < cumulative.Length; i++)
                {
                    int w = cumulative[i] - (i > 0 ? cumulative[i - 1] : 0);
                    if (w > 0 && roster.Monsters[i].Rank == MonsterRank.Fodder) return i;
                }
                for (int i = 0; i < roster.Monsters.Length; i++) if (roster.Monsters[i].Rank == MonsterRank.Fodder) return i;
            }
            return type;
        }

        // ------------------------------------------------------------------ the tick

        void UpdateTick(double t, int k)
        {
            // Advance everything a real sim moves each tick, then the camera. The spatial hash is rebuilt
            // lazily, by the first query on a tick that has decisions or impacts.
            bool sampleStats = (k & 3) == 0;
            int w = 0;
            bigCount = 0;
            int on = 0;
            var view = View(viewWidth > 0 ? viewWidth : plans[0].ViewWidth);
            for (int i = 0; i < liveCount; i++)
            {
                int m = live[i];
                if (state[m] == Dying && t >= despawnT[m]) state[m] = Gone;
                if (state[m] == Gone) continue;
                live[w++] = m;
                MotionSegment s = cur[m];
                pos[m] = s.From == s.To ? s.To : s.PositionAt(t);
                if (radius[m] > SmallRadius)
                {
                    if (bigCount == big.Length) Array.Resize(ref big, big.Length * 2);
                    big[bigCount++] = m;
                }
                if (sampleStats && view.Contains(focus, pos[m])) on++;
            }
            liveCount = w;
            hashDirty = true;   // rebuilt by the first query of the next tick that needs it

            // One slice of a flow-field rebuild toward the hero, spread over the beat's ticks.
            if (nav != null && heroFieldNext.Building)
            {
                flowSlices++;
                if (heroFieldNext.Step(sliceBudget))
                {
                    FlowField done = heroFieldNext;
                    heroFieldNext = heroField;
                    heroField = done;
                }
            }
            if (breaks != null) NavRebuildSlice();

            Vector2 target = HeroPosAt(t);
            float omega = 1f / FollowTimeConstant;
            float dt = (float)TickDt;
            if (k == firstTick) { focus = target; focusVel = Vector2.Zero; }
            else
            {
                Vector2 x = focus - target;
                float e = (float)Math.Exp(-omega * dt);
                Vector2 c = focusVel + omega * x;
                focus = target + (x + c * dt) * e;
                focusVel = (focusVel - omega * c * dt) * e;
            }
            viewWidth = WidthAt(t);
            framing[k] = new FramingSample(focus, viewWidth);

            if (sampleStats && curSection >= 0 && t < map.Sections[curSection].End + 1e-9)
            {
                SectionStats st = stats[curSection];
                st.MeanOnScreen += on;
                statTicks[curSection]++;
                if (on > st.MaxOnScreen) st.MaxOnScreen = on;
            }
        }

        bool hashDirty = true;
        int firstTick;   // the span's first tick (0 for the whole song)

        SpatialHash Near()
        {
            if (hashDirty) { hash.Build(live, liveCount, pos, focus); hashDirty = false; }
            return hash;
        }

        float WidthAt(double t)
        {
            if (shot.ViewWidthTo > 0)
            {
                // A shot's zoom eases across it, from the cut (or its start) to its end.
                double z0 = double.IsNaN(shot.Cut) ? shot.SpanFrom : shot.Cut, z1 = shot.SpanTo;
                float z = (float)Math.Max(0, Math.Min(1, (t - z0) / (z1 - z0)));
                z = z * z * (3 - 2 * z);
                return plans[0].ViewWidth + (shot.ViewWidthTo - plans[0].ViewWidth) * z;
            }
            if (curSection < 0) return plans[0].ViewWidth;
            if (t >= wT1 || wT1 <= wT0) return wTo;
            if (t <= wT0) return wFrom;
            float u = (float)((t - wT0) / (wT1 - wT0));
            u = u * u * (3 - 2 * u);
            return wFrom + (wTo - wFrom) * u;
        }

        // ------------------------------------------------------------------ decisions on each beat

        void Decide(double tb, double tnext, int bi)
        {
            int s = bi < 0 ? 0 : map.SectionIndexAt(tb);
            if (s != curSection) EnterSection(s, tb);
            if (bi >= 0 && grid.IsDownbeat[bi] && tb > barStart + 1e-6) BarFeedback(tb);
            if (nav != null) FlowBeat(tb, tnext);
            if (!populated) { InitialPopulation(tb); populated = true; }
            if (shot.BossType >= 0 && !bossEntered && (double.IsNaN(shot.Cut) || tb >= shot.Cut - 1e-9)) BossRuleEnter(tb);
            beatSerial++;
            bool free = map.Sections[s].Feel == SectionFeel.Free;
            var view = View(viewWidth > 0 ? viewWidth : plan.ViewWidth);
            Classify(view, tb);
            if (worstBeats && bi >= 0 && bi + 1 < map.Beats.Length && grid.IsDownbeat[bi + 1] && barOfBeat[bi + 1] % 4 == 0)
                WorstBeat(tb, map.Beats[bi + 1], view);
            if (bi >= 0)   // the pre-roll before the first beat only places the opening horde
            {
                HeroDecide(tb, tnext);
                MonsterDecide(tb, tnext, HeroPosAt(tnext), free);
            }
            Spells(tb, tnext);
            if (breaks != null) SendBreaks(tb, tnext);
            Refill(tb, view);
            beatSpawns.Clear();   // spawns are in the hash from the next tick on
        }

        void EnterSection(int s, double t)
        {
            bool first = curSection < 0;
            curSection = s;
            // A lighter section starts its hit shapes small again (TD6): shapes the feedback grew for a heavier one
            // would hold the horde at arm's length all through it, knocking back everything they strike past the budget.
            if (!first && nav != null && shot.ShapeScale <= 0 && plans[s].Preset.DeathsPerSecond(plans[s].TargetOnScreen) < plan.Preset.DeathsPerSecond(plan.TargetOnScreen))
                shapeScale = Math.Min(shapeScale, 1.5f);
            plan = plans[s];
            SongSection sec = map.Sections[s];
            secStart = sec.Start;
            secEnd = sec.End;
            secKills = 0;
            wFrom = first ? plan.ViewWidth : viewWidth;
            wTo = plan.ViewWidth;
            wT0 = t;
            wT1 = map.TimeOfBeatIndex(sec.FirstBeat + ZoomBars * map.BeatsPerBarAt(sec.FirstBar));
            if (first) viewWidth = wTo;
            Vector2 h = HeroRest;
            if (plan.Goal != null) goal = plan.Goal;
            else goal = new GoalArea { Name = "hold", Center = h, Radius = 3f };
            goalField = plan.Goal != null && goalFields.TryGetValue(plan.Goal.Name, out FlowField gf) ? gf : null;
            goalReached = Vector2.Distance(h, goal.Center) <= goal.Radius;
            if (goalReached && plan.Goal != null) stats[s].GoalReachedAt = t;
            barStart = t;
            barStruck = 0;
            if (clumpSize > 0) ClumpPattern();
            if (plan.Boss >= 0 && !first)
            {
                var view = View(Math.Max(viewWidth, plan.ViewWidth));
                Vector2 dir = plan.BossFrom != null ? Vector2.Normalize(plan.BossFrom.Center - h)
                    : goalReached ? new Vector2(0, 1) : nav != null ? GoalHeading(h, out _) : Vector2.Normalize(goal.Center - h);
                float br = roster.Monsters[plan.Boss].Radius;
                Vector2 p;
                if (nav == null)
                {
                    float dist = view.Near;
                    while (view.OutsideDistance(focus, focus + dir * dist) < SpawnMargin + br || SpawnBlocked(focus + dir * dist, br)) dist += 0.5f;
                    p = focus + dir * dist;
                }
                else p = BossEntry(view, dir, br);
                int m = Spawn(plan.Boss, p, t, false);
                beatSpawns.Add(m);
                bossWalksIn = plan.BossFrom != null;
                double bar = map.TimeOfBeatIndex(sec.EndBeat) - map.TimeOfBeatIndex(sec.EndBeat - map.BeatsPerBarAt(sec.LastBar));
                bossDeathAfter[m] = secEnd - bar;
            }
        }

        /// <summary>
        /// Each bar, scale the hero's and the spells' hit shapes so struck monsters (hits plus
        /// deaths) per second follow the load preset; who dies is held by the kill budget.
        /// </summary>
        void BarFeedback(double tb)
        {
            double barDur = tb - barStart;
            // With one-hit kills every monster struck dies, so the preset's hits never come.
            double hits = tuning.OneHitKills ? 0 : plan.Preset.HitsPerSecond(plan.TargetOnScreen);
            double target = (plan.Preset.DeathsPerSecond(plan.TargetOnScreen) + hits) * barDur;
            double rate = plan.Preset.DeathsPerSecond(plan.TargetOnScreen);
            if (target > 0 && barDur > 0 && shot.ShapeScale <= 0)   // a shot's fixed scale replaces the feedback
            {
                // Deaths come first: while the kill budget runs behind by more than half a second's worth,
                // the shapes grow to reach more monsters. Otherwise they steer struck monsters toward the
                // preset's deaths plus hits, but stay at most 2.5x, since a wide kill zone empties the
                // middle of the screen.
                bool behind = rate * (tb - secStart) - secKills > 0.5 * rate;
                double ratio = target / Math.Max(1, barStruck);
                float k = (float)Math.Max(0.85, Math.Min(1.2, Math.Sqrt(ratio)));
                if (behind) k = Math.Max(k, 1.1f);
                float cap = behind ? tuning.MaxShapeScale : tuning.HitsShapeScale;
                shapeScale = Math.Max(0.5f, Math.Min(Math.Max(cap, Math.Min(shapeScale, tuning.MaxShapeScale) * 0.95f), shapeScale * k));
                if (!behind && shapeScale > tuning.HitsShapeScale) shapeScale = Math.Max(tuning.HitsShapeScale, shapeScale * 0.95f);
            }
            barStart = tb;
            barStruck = 0;
        }

        void InitialPopulation(double t)
        {
            var view = View(plan.ViewWidth);
            Vector2 h = HeroRest;
            if (shot.Duel != null) { DuelCast(t, h); return; }
            float spacing = 0;   // the widest fodder's diameter, so the opening horde starts apart
            foreach (MonsterType mt in roster.Monsters) if (mt.Rank == MonsterRank.Fodder) spacing = Math.Max(spacing, 2 * mt.Radius);
            float half = view.Radius + SpawnMargin + SpawnBand + 4;
            int cols = (int)(2 * half / spacing) + 1;
            float rowH = spacing * 0.8660254f;
            int rows = (int)(2 * half / rowH) + 1;
            var pts = new List<Vector2>(cols * rows);
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                    pts.Add(new Vector2(h.X - half + i * spacing + ((j & 1) == 1 ? spacing / 2 : 0), h.Y - half + j * rowH));
            var dkeys = new long[pts.Count];
            var idx = new int[pts.Count];
            for (int i = 0; i < pts.Count; i++) { dkeys[i] = ((long)(Vector2.Distance(pts[i], h) * 1000) << 24) | (long)i; idx[i] = i; }
            Array.Sort(dkeys, idx);
            int onLeft = plan.TargetOnScreen, offLeft = plan.Reserve;
            foreach (int i in idx)
            {
                if (onLeft <= 0 && offLeft <= 0) break;
                Vector2 p = pts[i];
                float d = Vector2.Distance(p, h);
                if (d < initialInnerRadius) continue;
                if (nav != null && (nav.Distance(p) < spacing / 2 + 0.05f || !heroField.Reached(p))) continue;
                if (view.Contains(h, p, -0.5f))
                {
                    if (onLeft <= 0) continue;
                    onLeft--;
                }
                else if (!view.Contains(h, p, SpawnMargin) && view.Contains(h, p, SpawnMargin + SpawnBand))
                {
                    if (offLeft <= 0) continue;
                    offLeft--;
                }
                else continue;
                Spawn(PickType(plan.Mix, true), p, t, true);
            }
            for (int i = 0; i < liveCount; i++) pos[live[i]] = rest[live[i]];
            hash.Build(live, liveCount, pos, focus);
        }

        /// <summary>
        /// The duel's fixed cast (trailer.md, shot 1): the k-th member walks in to meet the k-th fixed combo entry,
        /// spread round the hero a third of a turn apart. Each starts as far out as it walks by its blow, so it
        /// arrives just inside that blow's reach then, and a nearer blow before it can't reach it.
        /// </summary>
        void DuelCast(double t, Vector2 h)
        {
            var blows = new List<GridEntry>();
            foreach (GridEntry e in plan.Combo) if (e.Fixed != FixedPosition.None) blows.Add(e);
            blows.Sort((a, b) => a.At.CompareTo(b.At));
            double beat = map.Beats[1] - map.Beats[0];
            int b0 = 0;
            while (b0 < map.Beats.Length - 1 && map.Beats[b0] < t - 1e-9) b0++;
            double walkFrom = map.Beats[b0];   // monsters first step on the first beat
            for (int k = 0; k < shot.Duel.Length; k++)
            {
                MonsterType mt = roster.Monsters[shot.Duel[k]];
                float contact = roster.HeroRadius + mt.Radius + 0.15f;
                double at = k < blows.Count ? blows[k].At : t + 4 * beat;
                // Just inside the blow's reach (a body is struck within half its radius past the shape's edge).
                float reach = k < blows.Count ? Scaled(HitSize(roster.Combo(blows[k].Name))) + HitShapeInflate * mt.Radius - 0.35f : contact;
                float arrive = Math.Max(contact, reach);
                float walk = (float)(mt.StepM * Math.Max(0, at - walkFrom) / beat);
                Vector2 dir = Easing.Dir((float)(k * 2 * Math.PI / 3) + (float)(shot.YawDeg * Math.PI / 180));
                Vector2 p = h + dir * (arrive + walk);
                if (nav != null) p = OpenNear(p, mt.Radius);
                Spawn(shot.Duel[k], p, t, true);
            }
            for (int i = 0; i < liveCount; i++) pos[live[i]] = rest[live[i]];
            hash.Build(live, liveCount, pos, focus);
        }

        /// <summary>The nearest open ground to <paramref name="p"/> for a body of radius <paramref name="r"/> that the field toward the hero reaches.</summary>
        Vector2 OpenNear(Vector2 p, float r)
        {
            for (float d = 0; d < 20f; d += 0.5f)
                for (int k = 0; k < (d == 0 ? 1 : 12); k++)
                {
                    Vector2 q = p + Easing.Dir((float)(k * Math.PI / 6)) * d;
                    if (nav.Distance(q) >= r + 0.05f && heroField.Reached(q)) return q;
                }
            return p;
        }

        /// <summary>A move's hit shape's size before scaling: its radius, or a line's length.</summary>
        static float HitSize(Move mv) => mv.Shape == HitShape.Line ? mv.Length : mv.Radius;

        /// <summary>
        /// The boss rule's entrance (trailer.md, "The boss"): on the cut, outside the view from the shot's side
        /// (relative to the hero's travel), walking in. It dies only to the final slam.
        /// </summary>
        void BossRuleEnter(double tb)
        {
            bossEntered = true;
            Vector2 h = HeroRest;
            Vector2 travel = shot.Path != null ? PathHeading(h) : Easing.Dir(HeroFacing);
            Vector2 dir;
            switch (shot.BossFrom)
            {
                case "ahead": dir = travel; break;
                case "behind": dir = -travel; break;
                case "left": dir = new Vector2(-travel.Y, travel.X); break;
                case "right": dir = new Vector2(travel.Y, -travel.X); break;
                default:
                    Vector2 d = shot.BossArea.Value - h;
                    dir = d.LengthSquared() > 1e-6f ? Vector2.Normalize(d) : travel;
                    break;
            }
            var view = View(Math.Max(viewWidth, plan.ViewWidth));
            float br = roster.Monsters[shot.BossType].Radius;
            Vector2 p;
            if (nav == null)
            {
                float dist = view.Near;
                while (view.OutsideDistance(focus, focus + dir * dist) < SpawnMargin + br || SpawnBlocked(focus + dir * dist, br)) dist += 0.5f;
                p = focus + dir * dist;
            }
            else p = BossEntry(view, dir, br);
            int m = Spawn(shot.BossType, p, tb, false);
            beatSpawns.Add(m);
            bossWalksIn = true;
            bossDeathAfter[m] = shot.Final;
            // Its keep-out: the shot's largest scaled hit shape, plus 1 m.
            foreach (GridEntry e in plan.Combo) bossKeep = Math.Max(bossKeep, Scaled(HitSize(roster.Combo(e.Name))) + 1f);
        }

        void Classify(ViewGeometry view, double tb)
        {
            // Deaths per second, smoothed over about a second.
            if (tb > lastClassify)
            {
                double dt = tb - lastClassify;
                double k = 1 - Math.Exp(-dt / 1.0);
                deathRate += k * ((totalKills - killsAtClassify) / dt - deathRate);
                lastClassify = tb;
                killsAtClassify = totalKills;
            }
            onScreenCount = 0;
            int offN = 0;
            float far = view.Radius + FarDespawn;
            // A kiting hero outruns the horde: off screen behind it, past the spawn band, a monster never catches up.
            Vector2 travel = shot.Path != null ? PathHeading(HeroRest) : Vector2.Zero;
            for (int i = 0; i < liveCount; i++)
            {
                int m = live[i];
                if (state[m] == Gone) continue;
                released[m] = false;
                bool on = view.Contains(focus, pos[m]);
                onScreen[m] = on;
                if (on) { onScreenCount++; lastWanted[m] = tb; continue; }
                if (state[m] != Alive) continue;
                float d = Vector2.Distance(pos[m], focus);
                float outside = view.OutsideDistance(focus, pos[m]);
                bool stranded = strandedS > 0 && tb - lastWanted[m] > strandedS && roster.Monsters[etype[m]].Rank != MonsterRank.Boss;
                if (travel != Vector2.Zero && outside > SpawnMargin + SpawnBand && Vector2.Dot(pos[m] - focus, travel) < 0 && roster.Monsters[etype[m]].Rank != MonsterRank.Boss)
                    stranded = true;
                if ((d > far || stranded) && busyUntil[m] <= tb)
                {
                    state[m] = Gone;
                    despawnT[m] = tb;
                    continue;
                }
                if (offN == offIds.Length) { Array.Resize(ref offIds, offN * 2); Array.Resize(ref offKeys, offN * 2); }
                offIds[offN] = m;
                offKeys[offN] = ((long)(outside * 1000) << 24) | (long)m;
                offN++;
            }
            offscreenAlive = offN;
            // Release the deficit plus the deaths expected while a released monster walks in from the
            // spawn band (about 3 s), nearest the view's edge first.
            int deficit = TargetAhead(tb, out _) - onScreenCount + (int)Math.Round(deathRate * tuning.ReleaseLeadS);
            if (deficit <= 0 || offN == 0 || plan.Clear) return;   // a clearing section lets no more in
            Array.Sort(offKeys, offIds, 0, offN);
            for (int i = 0; i < offN && i < deficit; i++) { released[offIds[i]] = true; lastWanted[offIds[i]] = tb; }
        }

        /// <summary>
        /// The on-screen target releases and refills work toward: the section's own, or within
        /// <see cref="SectionLeadSeconds"/> of a section with a larger one, that section's (TD13: a
        /// drop then lands with its horde instead of filling over the next 20 s).
        /// </summary>
        int TargetAhead(double tb, out int reserve)
        {
            reserve = plan.Reserve;
            int target = plan.TargetOnScreen;
            int next = curSection + 1;
            if (next < plans.Length && !plans[next].Clear && map.Sections[next].Start - tb <= SectionLeadSeconds && plans[next].TargetOnScreen > target)
            {
                target = plans[next].TargetOnScreen;
                reserve = plans[next].Reserve;
            }
            return target;
        }

        // ------------------------------------------------------------------ the hero

        void HeroDecide(double tb, double tnext)
        {
            // A kiting hero strides along its path on every beat, whether it strikes or not: its blows are moving strikes.
            if (shot.Path != null && !(tb >= shot.Final - 1e-9)) ShotStride(tb, tnext, HeroRest);
            Vector2 h = HeroPosAt(tb);
            if (!goalReached && Vector2.Distance(h, goal.Center) <= goal.Radius)
            {
                goalReached = true;
                if (plan.Goal != null) stats[curSection].GoalReachedAt = tb;
            }
            if (heroPendingImpact > tb + 1e-9) return;
            bool canAct = plan.HeroAttacks && plan.Combo.Length > 0;
            // While heading for an unreached goal, only monsters ahead (within 75° of the way to the goal)
            // stop the hero's stride; those beside and behind are left behind.
            Vector2 ahead = goalReached ? Vector2.Zero : nav != null ? GoalHeading(h, out _) : Vector2.Normalize(goal.Center - h);
            // Holding ground (a reached goal), the hero attacks whatever its hit shapes reach as the feedback
            // scales them (TD6: walls slow the horde, and a hero idling inside its own kill zone starves the
            // kill budget); travelling, only what is within its unscaled reach, unless the level script's
            // travelScaledReach lets it carve its way with its scaled shapes too.
            bool scaled = (goalReached || tuning.TravelScaledReach) && nav != null;
            float reach = scaled ? Math.Max(roster.HeroReachM, Scaled(roster.HeroReachM)) : roster.HeroReachM;
            // A shot's hero (a kite path or the duel) acts on every beat: it swings whatever is in reach, and strides
            // on a beat whose next blow would start after the next decision.
            bool everyBeat = shot.ActsEveryBeat;
            if (canAct && (plan.Clear || everyBeat || AnyMonsterWithin(h, reach, ahead)))   // a clearing section slams on every accent
            {
                // With a fixed windup the next combo's windup cuts into the last one's follow-through.
                ScheduleHeroAction(tb, roster.HeroWindupS > 0 ? tb : Math.Max(tb, heroLastEnd), startBefore: everyBeat ? tnext : double.PositiveInfinity);
                // A flurry (an eighth's combo entry) strikes between beats: the hero goes straight on to
                // the following beat's action, which no later decision could still schedule, so the
                // flurry adds a blow instead of shifting one.
                while (heroFlurry)
                {
                    double after = heroPendingImpact;
                    ScheduleHeroAction(after, roster.HeroWindupS > 0 ? after : heroLastEnd);
                    if (heroPendingImpact <= after + 1e-9) break;
                }
                if (!everyBeat || heroPendingImpact > tb + 1e-9) return;
            }
            if (heroLastEnd > tb + 1e-9 || shot.Path != null) return;
            if (tb >= shot.Final - 1e-9) return;   // after the final slam the hero plays out its recovery
            // With nothing in reach just before a clearing section (an outro the hero has emptied), the hero winds
            // up for its first slam rather than striding past the beat its windup has to start on. It commits only
            // at the last decision before that windup, so until then it still strides and strikes what walks in.
            int next = curSection + 1;
            if (canAct && next < plans.Length && plans[next].Clear && plans[next].HeroAttacks)
            {
                ScheduleHeroAction(tb, roster.HeroWindupS > 0 ? tb : Math.Max(tb, heroLastEnd), map.Sections[next].Start, tnext);
                if (heroPendingImpact > tb + 1e-9) return;
            }
            Stride(tb, tnext, h);
        }

        bool AnyMonsterWithin(Vector2 p, float r, Vector2 ahead)
        {
            int n = Near().Gather(p, r + SmallRadius, ref buf);
            for (int i = 0; i < n + bigCount; i++)
            {
                int m = i < n ? buf[i] : big[i - n];
                if (state[m] != Alive) continue;
                Vector2 d = pos[m] - p;
                float l = d.Length();
                if (l > r + radius[m]) continue;
                if (ahead == Vector2.Zero || l < 1e-4f || Vector2.Dot(d / l, ahead) >= AheadCos) return true;
            }
            return false;
        }

        /// <summary>
        /// Schedule the hero's next combo on the first grid point within 4 s (from <paramref name="firstPoint"/> on)
        /// whose clip fits from <paramref name="availFrom"/>, unless its clip would start after <paramref name="startBefore"/>.
        /// </summary>
        void ScheduleHeroAction(double tb, double availFrom, double firstPoint = double.NegativeInfinity, double startBefore = double.PositiveInfinity)
        {
            double last = Math.Min(shot.HasSpan ? shot.SpanTo + 1e-6 : double.PositiveInfinity, double.IsNaN(shot.Final) ? double.PositiveInfinity : shot.Final + 1e-6);
            for (int gi = grid.FirstAfter(Math.Max(tb, firstPoint - 1e-6)); gi < grid.Count && grid.Points[gi].Time <= tb + 4.0 && grid.Points[gi].Time <= last; gi++)
            {
                GridPoint p = grid.Points[gi];
                GridEntry[] combo = plans[p.Section].Combo;
                GridEntry e = PickCombo(combo, p, out bool plain);
                if (e == null) continue;
                Move mv = roster.Combo(e.Name);
                ClipAnnotation clip = clips[mv.Clip];
                float windup = roster.WindupOf(mv);
                // A combo with its own windup (demo3d's cleaves) may cut into the last one's follow-through, as every
                // combo does for a hero with a windup.
                bool cutsIn = mv.WindupS > 0 && roster.HeroWindupS <= 0;
                if (windup > 0 ? !ClipLibrary.FitWindup(clip, cutsIn ? tb : availFrom, p.Time, windup, out double start, out double end, out float speed)
                    : !ClipLibrary.Fit(clip, availFrom, p.Time, out start, out end, out speed, tuning.ComboMaxSpeed, tuning.ComboMinSpeed)) continue;
                if (start < tb - 1e-9) continue;
                if (start >= startBefore) return;
                // Across a section change a positioned combo owns its slot: skip a plain one whose
                // recovery would leave no room for the windup of the next section's positioned one
                // (the ending's accent slams after the outro, TD10). Within a section a positioned
                // combo that can't fit moves later, as before.
                if (plain && BlocksPositioned(gi, windup > 0 ? p.Time : end)) continue;
                // A flurry's blow between beats never costs the hero the next beat's, and with the level
                // script's yieldToAccents no beat's or flurry's blow costs the section's next accent or
                // phrase its spin or slam (both for a hero that retimes its clips by speed, as demo3d's does).
                if (windup <= 0 && (p.Flags & GridFlags.Beat) == 0 && FlurryTakesTheBeat(gi, availFrom, end)) continue;
                if (windup <= 0 && tuning.YieldToAccents && e.Priority <= GridEntry.BeatPriority && TakesTheAccent(gi, availFrom, end)) continue;
                // A shot's fixed entries (its cut, final slam, anchors and bar.beats) always land: no other blow costs one its fit.
                if (windup <= 0 && shot.HasFixed && e.Fixed == FixedPosition.None && TakesAFixedSlot(gi, availFrom, end)) continue;

                // One that cuts in starts where the hero then stands, mid-move, so it never pops.
                Vector2 from = cutsIn ? HeroPosAt(start) : HeroRest;
                float fFrom = Easing.Wrap(HeroFacing);
                Vector2 dir, to;
                if (shot.Path != null)
                {
                    // A moving strike: the blow faces along the path, and the beat's stride (HeroDecide's) carries the hero on.
                    dir = PathHeading(from);
                    to = from;   // unused: a kiting hero's blows lay no motion
                }
                else if (shot.HoldGround)
                {
                    dir = NearestDirection(from, out bool near);
                    if (!near) dir = Easing.Dir(fFrom);
                    to = from;
                }
                else
                {
                    dir = DenseDirection(from, roster.HeroReachM * 1.5f, out bool any);
                    if (!goalReached) { dir = nav != null ? GoalHeading(from, out _) : Vector2.Normalize(goal.Center - from); any = true; }   // cut a way toward the goal
                    if (!any) dir = Easing.Dir(fFrom);
                    to = from + dir * Math.Max(clip.RootMotionM, mv.AdvanceM);
                    if (nav != null) to = WallStep(from, to, roster.HeroRadius);
                    to = HeroPath(from, to);
                    if (goalReached)
                    {
                        // Pulled back into the goal, a step that slid along a wall could cut into it: it then stays where it slid.
                        Vector2 c = ClampToGoal(to);
                        if (nav == null || SegmentClear(from, c, roster.HeroRadius)) to = c;
                    }
                }
                float fTo = Easing.Unwrap(fFrom, Easing.Yaw(dir));
                // A stepping hero without root motion steps with the blow, on its beat; otherwise it moves over the clip.
                // A kiting hero's motion is its beats' strides alone (already facing along the path): a blow scheduled a
                // decision ahead would otherwise lay a segment from before the next stride over it.
                if (shot.Path == null)
                {
                    if (heroStepS > 0 && clip.RootMotionM <= 0)
                    {
                        AddHeroSeg(new MotionSegment(0, start, p.Time, from, from, fFrom, fTo, Ease.Linear));
                        AddHeroSeg(new MotionSegment(0, p.Time, p.Time + heroStepS, from, to, fTo, fTo, Ease.Pulse));
                    }
                    else AddHeroSeg(new MotionSegment(0, start, end, from, to, fFrom, fTo, Ease.Linear));
                }
                Push(new Impact
                {
                    Time = p.Time, Kind = ImpactKind.HeroCombo, Entity = 0, Target = -1, Clip = mv.Clip, Move = mv,
                    Yaw = Easing.Yaw(dir), Strength = Math.Max(mv.Strength, p.AccentStrength), ClipStart = start, ClipSpeed = speed,
                });
                heroPendingImpact = p.Time;
                heroFlurry = (p.Flags & GridFlags.Beat) == 0;
                heroLastEnd = end;
                if (plain) comboCycle++;
                stats[curSection].HeroActions++;
                return;
            }
        }

        /// <summary>
        /// Whether an action ending at <paramref name="end"/> would make the next positioned combo
        /// after grid point <paramref name="gi"/> (within 4 s), if it lies in a later section whose
        /// combos are all positioned (its only actions), unable to fit at full speed (or, with a
        /// windup, an action whose impact is <paramref name="end"/> leaving no room for its windup).
        /// </summary>
        bool BlocksPositioned(int gi, double end)
        {
            for (int k = gi + 1; k < grid.Count && grid.Points[k].Time <= grid.Points[gi].Time + 4.0; k++)
            {
                GridPoint q = grid.Points[k];
                GridEntry e = PickCombo(plans[q.Section].Combo, q, out bool plainNext);
                if (e == null || plainNext) continue;
                if (q.Section == grid.Points[gi].Section || !AllPositioned(plans[q.Section].Combo)) return false;
                Move next = roster.Combo(e.Name);
                ClipAnnotation c = clips[next.Clip];
                float windup = roster.WindupOf(next);
                return end > q.Time - (windup > 0 ? Math.Min(windup, c.Impact) : c.Impact / tuning.ComboMaxSpeed);
            }
            return false;
        }

        /// <summary>
        /// Whether a blow between beats at grid point <paramref name="gi"/>, ending at <paramref name="end"/>,
        /// would leave the next beat's combo unable to fit when it could have from <paramref name="availFrom"/>.
        /// </summary>
        bool FlurryTakesTheBeat(int gi, double availFrom, double end)
        {
            int k = gi + 1;
            while (k < grid.Count && (grid.Points[k].Flags & GridFlags.Beat) == 0) k++;
            if (k == grid.Count) return false;
            GridPoint q = grid.Points[k];
            GridEntry e = PickCombo(plans[q.Section].Combo, q, out _);
            if (e == null) return false;
            ClipAnnotation c = clips[roster.Combo(e.Name).Clip];
            return ClipLibrary.Fit(c, availFrom, q.Time, out _, out _, out _, tuning.ComboMaxSpeed, tuning.ComboMinSpeed)
                && !ClipLibrary.Fit(c, end, q.Time, out _, out _, out _, tuning.ComboMaxSpeed, tuning.ComboMinSpeed);
        }

        /// <summary>
        /// Whether a blow at grid point <paramref name="gi"/>, ending at <paramref name="end"/>, would leave
        /// the next combo positioned above the beat in the same section (within 4 s) unable to fit when it
        /// could have from <paramref name="availFrom"/>.
        /// </summary>
        bool TakesTheAccent(int gi, double availFrom, double end)
        {
            GridPoint p = grid.Points[gi];
            for (int k = gi + 1; k < grid.Count && grid.Points[k].Time <= p.Time + 4.0 && grid.Points[k].Section == p.Section; k++)
            {
                GridPoint q = grid.Points[k];
                GridEntry e = PickCombo(plans[q.Section].Combo, q, out _);
                if (e == null || e.Priority <= GridEntry.BeatPriority) continue;
                ClipAnnotation c = clips[roster.Combo(e.Name).Clip];
                return ClipLibrary.Fit(c, availFrom, q.Time, out _, out _, out _, tuning.ComboMaxSpeed, tuning.ComboMinSpeed)
                    && !ClipLibrary.Fit(c, end, q.Time, out _, out _, out _, tuning.ComboMaxSpeed, tuning.ComboMinSpeed);
            }
            return false;
        }

        /// <summary>
        /// Whether a blow at grid point <paramref name="gi"/>, ending at <paramref name="end"/>, would leave the next fixed
        /// combo entry (within 4 s) unable to fit when it could have from <paramref name="availFrom"/>.
        /// </summary>
        bool TakesAFixedSlot(int gi, double availFrom, double end)
        {
            GridPoint p = grid.Points[gi];
            for (int k = gi + 1; k < grid.Count && grid.Points[k].Time <= p.Time + 4.0; k++)
            {
                GridPoint q = grid.Points[k];
                GridEntry e = PickCombo(plans[q.Section].Combo, q, out _);
                if (e == null || e.Fixed == FixedPosition.None) continue;
                ClipAnnotation c = clips[roster.Combo(e.Name).Clip];
                return ClipLibrary.Fit(c, availFrom, q.Time, out _, out _, out _, tuning.ComboMaxSpeed, tuning.ComboMinSpeed)
                    && !ClipLibrary.Fit(c, end, q.Time, out _, out _, out _, tuning.ComboMaxSpeed, tuning.ComboMinSpeed);
            }
            return false;
        }

        static bool AllPositioned(GridEntry[] combo)
        {
            foreach (GridEntry e in combo) if (!e.Positioned) return false;
            return combo.Length > 0;
        }

        GridEntry PickCombo(GridEntry[] combo, in GridPoint p, out bool plain)
        {
            plain = false;
            GridEntry best = null;
            int plainCount = 0;
            GridFlags flags = p.Flags;
            foreach (GridEntry e in combo)
            {
                if (!e.Positioned) { plainCount++; continue; }
                if (!e.Matches(p)) continue;
                if (best == null || e.Priority > best.Priority) best = e;
            }
            // A plain entry plays on the beat, so on a beat it outranks an eighth's or a sixteenth's.
            if (best != null && (best.Priority >= GridEntry.BeatPriority || plainCount == 0 || (flags & GridFlags.Beat) == 0)) return best;
            if (plainCount == 0 || (flags & GridFlags.Beat) == 0) return null;
            int want = comboCycle % plainCount;
            foreach (GridEntry e in combo)
            {
                if (e.Positioned) continue;
                if (want-- == 0) { plain = true; return e; }
            }
            return null;
        }

        void Stride(double tb, double tnext, Vector2 h)
        {
            if (shot.Path != null || shot.HoldGround) { ShotStride(tb, tnext, h); return; }
            Vector2 dir;
            float len;
            if (!goalReached)
            {
                Vector2 d = goal.Center - h;
                float dist = d.Length();
                if (dist < 1e-4f) return;
                dir = d / dist;
                if (nav != null) dir = GoalHeading(h, out dist);
                len = Math.Min(roster.HeroStrideM, dist);
            }
            else
            {
                dir = DenseDirection(h, roster.HeroSenseM, out bool any);
                if (!any) return;
                len = roster.HeroStrideM;
            }
            Vector2 to = h + dir * len;
            if (goalReached) to = ClampToGoal(to);
            if (nav != null) to = WallStep(h, to, roster.HeroRadius);
            to = HeroPath(h, to);
            if (Vector2.Distance(h, to) < 0.05f) return;
            float fFrom = Easing.Wrap(HeroFacing);
            double t1 = heroStepS > 0 ? Math.Min(tnext, tb + heroStepS) : tnext;
            AddHeroSeg(new MotionSegment(0, tb, t1, h, to, fFrom, Easing.Unwrap(fFrom, Easing.Yaw(to - h)), Ease.Pulse));
            stats[curSection].HeroStrides++;
        }

        /// <summary>
        /// A shot's stride: along the kite path, a beat's stride; holding ground (the duel), a short step sideways
        /// to the nearest monster, to and fro across the hero's start.
        /// </summary>
        void ShotStride(double tb, double tnext, Vector2 h)
        {
            Vector2 to;
            if (shot.Path != null) to = PathStep(h);
            else
            {
                // Holding ground: to and fro across its spot, sideways to the nearest monster, so the brutes' walk-ins
                // (staged against the spot) stay true.
                const float SideStepM = 0.2f;
                Vector2 n = NearestDirection(h, out bool any);
                if (!any) n = Easing.Dir(HeroFacing);
                Vector2 side = new Vector2(-n.Y, n.X);
                Vector2 spot = shot.HeroStart ?? heroStart;
                Vector2 a = spot + side * SideStepM, b = spot - side * SideStepM;
                to = Vector2.Distance(h, a) >= Vector2.Distance(h, b) ? a : b;
                if (nav != null) to = WallStep(h, to, roster.HeroRadius);
            }
            if (Vector2.Distance(h, to) < 0.05f) return;
            float fFrom = Easing.Wrap(HeroFacing);
            Vector2 face = to - h;
            if (shot.HoldGround)   // stepping aside, the duel's hero keeps facing the nearest brute
            {
                Vector2 f = NearestDirection(to, out bool faces);
                if (faces) face = f;
            }
            float fTo = Easing.Unwrap(fFrom, Easing.Yaw(face));
            double t1 = heroStepS > 0 ? Math.Min(tnext, tb + heroStepS) : tnext;
            AddHeroSeg(new MotionSegment(0, tb, t1, h, to, fFrom, fTo, Ease.Pulse));
            stats[curSection].HeroStrides++;
        }

        /// <summary>Moves on to the next waypoint once the hero is within a stride of this one (past a path's last, it runs on).</summary>
        void AdvancePath(Vector2 h)
        {
            for (int k = 0; k < shot.Path.Length && !pathDone && Vector2.Distance(h, shot.Path[pathNext]) <= shot.PathStepM; k++)
            {
                if (pathNext + 1 < shot.Path.Length) pathNext++;
                else if (shot.PathLoop) pathNext = 0;
                else
                {
                    pathDone = true;
                    Vector2 leg = shot.Path.Length > 1 ? shot.Path[pathNext] - shot.Path[pathNext - 1] : Easing.Dir(HeroFacing);
                    runOn = leg.LengthSquared() > 1e-6f ? Vector2.Normalize(leg) : Easing.Dir(HeroFacing);
                }
            }
        }

        /// <summary>The way along the kite path from <paramref name="h"/>: toward the next waypoint, round structures by its field.</summary>
        Vector2 PathHeading(Vector2 h)
        {
            if (pathDone) return runOn;
            Vector2 target = shot.Path[pathNext];
            Vector2 d = target - h;
            float l = d.Length();
            Vector2 straight = l > 1e-4f ? d / l : Easing.Dir(HeroFacing);
            if (nav == null || nav.Clear(h, target, roster.HeroRadius)) return straight;
            return pathFields[pathNext].Direction(h, out Vector2 dir) ? dir : straight;
        }

        /// <summary>A beat's stride along the kite path from <paramref name="h"/>, sliding along walls.</summary>
        Vector2 PathStep(Vector2 h)
        {
            AdvancePath(h);
            Vector2 to = h + PathHeading(h) * shot.PathStepM;
            return nav != null ? WallStep(h, to, roster.HeroRadius) : to;
        }

        /// <summary>The direction from <paramref name="p"/> to the nearest live monster within the hero's sense.</summary>
        Vector2 NearestDirection(Vector2 p, out bool any)
        {
            int n = Near().Gather(p, roster.HeroSenseM, ref buf);
            float best = float.MaxValue;
            Vector2 dir = Vector2.Zero;
            for (int i = 0; i < n + bigCount; i++)
            {
                int m = i < n ? buf[i] : big[i - n];
                if (i < n && radius[m] > SmallRadius) continue;
                if (state[m] != Alive) continue;
                Vector2 d = pos[m] - p;
                float l = d.Length();
                if (l < best && l > 1e-4f) { best = l; dir = d / l; }
            }
            any = best < float.MaxValue;
            return dir;
        }

        Vector2 ClampToGoal(Vector2 p)
        {
            Vector2 d = p - goal.Center;
            float l = d.Length();
            return l <= goal.Radius ? p : goal.Center + d * (goal.Radius / l);
        }

        /// <summary>The hero's move from <paramref name="a"/> toward <paramref name="b"/>, shortened so it stops short of monsters.</summary>
        Vector2 HeroPath(Vector2 a, Vector2 b)
        {
            if (HeroClear(a, b)) return b;
            float lo = 0, hi = 1;
            for (int it = 0; it < 6; it++)
            {
                float mid = (lo + hi) / 2;
                if (HeroClear(a, Vector2.Lerp(a, b, mid))) lo = mid; else hi = mid;
            }
            return Vector2.Lerp(a, b, lo);
        }

        bool HeroClear(Vector2 a, Vector2 p)
        {
            float hr = roster.HeroRadius;
            if (nav != null && !SegmentClear(a, p, hr)) return false;
            int n = Near().Gather(p, hr + SmallRadius + MaxStepReach, ref buf);
            for (int i = 0; i < n; i++)
                if (!HeroClearOf(buf[i], a, p, hr)) return false;
            for (int i = 0; i < bigCount; i++)
                if (!HeroClearOf(big[i], a, p, hr)) return false;
            return true;
        }

        bool HeroClearOf(int m, Vector2 a, Vector2 p, float hr)
        {
            if (state[m] != Alive) return true;   // the dying fall away and never block
            // With a clear ring the hero stops short of the horde's edge too, leaving the ring to its blows:
            // well short holding ground, and closer while cutting a way toward a goal, where the horde backs off.
            float need = Math.Max(hr, (goalReached ? 0.75f : 0.25f) * clearRadius) + radius[m];
            float d = Vector2.Distance(rest[m], p);
            return d >= need || d >= Vector2.Distance(rest[m], a);
        }

        /// <summary>The centre of the 45° sector holding the most monsters within <paramref name="r"/>, weighted toward an unreached goal.</summary>
        Vector2 DenseDirection(Vector2 p, float r, out bool any)
        {
            Span8 counts = default;
            int n = Near().Gather(p, r, ref buf);
            any = false;
            for (int i = 0; i < n + bigCount; i++)
            {
                int m = i < n ? buf[i] : big[i - n];
                if (i < n && radius[m] > SmallRadius) continue;
                if (state[m] != Alive) continue;
                Vector2 d = pos[m] - p;
                float l = d.Length();
                if (l > r || l < 1e-4f) continue;
                float yaw = Easing.Yaw(d);
                int sector = ((int)Math.Floor((yaw + Math.PI) / (Math.PI / 4) + 0.5)) & 7;
                counts[sector] += 1f;
                any = true;
            }
            if (!any) return Vector2.Zero;
            if (!goalReached)
            {
                float gy = Easing.Yaw(goal.Center - p);
                for (int s = 0; s < 8; s++)
                {
                    float sy = (float)(-Math.PI + s * Math.PI / 4);
                    if (Math.Abs(Easing.Unwrap(gy, sy) - gy) <= 1.18f) counts[s] *= 1.5f;
                }
            }
            int best = 0;
            for (int s = 1; s < 8; s++) if (counts[s] > counts[best]) best = s;
            return Easing.Dir((float)(-Math.PI + best * Math.PI / 4));
        }

        struct Span8
        {
            float a0, a1, a2, a3, a4, a5, a6, a7;
            public float this[int i]
            {
                get
                {
                    switch (i) { case 0: return a0; case 1: return a1; case 2: return a2; case 3: return a3; case 4: return a4; case 5: return a5; case 6: return a6; default: return a7; }
                }
                set
                {
                    switch (i) { case 0: a0 = value; break; case 1: a1 = value; break; case 2: a2 = value; break; case 3: a3 = value; break; case 4: a4 = value; break; case 5: a5 = value; break; case 6: a6 = value; break; default: a7 = value; break; }
                }
            }
        }

        // ------------------------------------------------------------------ monsters

        void MonsterDecide(double tb, double tnext, Vector2 heroAt, bool free)
        {
            int n = 0;
            for (int i = 0; i < liveCount; i++)
            {
                int m = live[i];
                if (state[m] != Alive || busyUntil[m] > tb + 1e-9) continue;
                if (n == keys.Length) { Array.Resize(ref keys, n * 2); Array.Resize(ref order, n * 2); }
                // Nearest the hero first, by the way there: with walls, the front of a queue at a gap
                // plans before those pressed against the wall beside it, so the queue moves up.
                float way = Vector2.Distance(rest[m], heroAt);
                if (nav != null) way = Math.Min(Math.Max(way, heroField.CostAt(rest[m])), 1e6f);
                keys[n] = ((long)(way * 1000) << 24) | (long)m;
                order[n] = m;
                n++;
            }
            Array.Sort(keys, order, 0, n);
            beatMaxStep = 0;
            Ease ease = free ? Ease.Linear : Ease.Pulse;
            for (int oi = 0; oi < n; oi++)
            {
                int m = order[oi];
                if (state[m] != Alive) continue;   // trampled by a walking-in boss this beat
                MonsterType mt = roster.Monsters[etype[m]];
                Vector2 a = rest[m];
                float contact = roster.HeroRadius + mt.Radius + 0.15f;
                Vector2 toHero = heroAt - a;
                float d = toHero.Length();
                // With a clear ring the horde holds at its edge and never reaches the hero to swing: the hero's
                // blows meet the front rank there, and the hero stays in plain sight.
                float hold = Math.Max(contact, clearRadius + mt.Radius);
                // The boss rule's boss keeps beyond the shot's largest hit shape, backing off as the hero closes in.
                bool keepOut = shot.BossType >= 0 && mt.Rank == MonsterRank.Boss;
                if (keepOut) hold = Math.Max(hold, bossKeep);
                if ((clearRadius > 0 || keepOut) && d < hold - 0.1f)
                {
                    BackOff(m, mt, a, heroAt, toHero, d, hold, tb, tnext, ease, keepOut ? 2 * mt.StepM : mt.StepM);
                    continue;
                }
                // Casters still fire from their reach, which lies beyond the ring: their bolts never crowd the hero.
                // A walking-in boss casts only once in view, however long its reach: its entrance is the show.
                bool walkingBoss = mt.Rank == MonsterRank.Boss && bossWalksIn;
                if (plan.MonsterAttacks && (clearRadius <= 0 || mt.Projectile >= 0) && d <= contact + mt.ReachM && (nav == null || nav.LineClear(a, heroAt))
                    && (onScreen[m] || !walkingBoss))
                {
                    TryScheduleAttack(m, mt, tb, toHero);
                    continue;
                }
                // A boss with a side to enter from always walks in: it enters beyond the band the deficit
                // releases nearest first, so a full climax would otherwise hold it out of view (PX8).
                if (!onScreen[m] && !released[m] && !walkingBoss) continue;
                Vector2 dir = d > 1e-4f ? toHero / d : Easing.Dir(facing[m]);
                float left = d;
                if (nav != null) dir = MonsterHeading(a, heroAt, mt.Radius, dir, ref left);
                float stepM = onScreen[m] ? mt.StepM : mt.StepM * OffscreenStride;
                float step = Math.Min(stepM, Math.Max(0, left - hold));
                Vector2 best = a;
                float bestProg = 0;
                if (step > 1e-3f)
                {
                    Vector2 b = a + dir * step;
                    if (nav != null) b = WallStep(a, b, mt.Radius);
                    if (walkingBoss && !onScreen[m] && !free) Trample(m, a, b, tb);
                    GatherCandidates(m, a, Math.Max(step, 0.8f * mt.StepM));
                    float f = SepFraction(m, a, b);
                    best = Vector2.Lerp(a, b, f);
                    bestProg = nav == null ? step * f : Vector2.Dot(dir, best - a);
                    if (bestProg < 0.5f * mt.StepM && step >= 0.2f * mt.StepM)
                    {
                        // One sidestep a beat, alternating sides, lets a blocked monster slide around the one ahead.
                        // With walls (TD6) a monster that would still mark time also tries the other side, then
                        // wider sidesteps, starting from the side its last sidestep took: a queue through a gap fans
                        // out round the hero instead of filing in single file, two monsters touching side by side
                        // never lock each other, and a monster keeps sliding the same way instead of zig-zagging.
                        int side = ((m + beatSerial) & 1) == 0 ? 1 : -1;
                        bool sticky = nav != null && sideMem[m] != 0;
                        if (sticky) side = sideMem[m];
                        // Away from walls (8 m by the open-distance field) the horde comes from every side and TD3's
                        // one sidestep serves; hemmed in on every side, one try is all there is room for.
                        int tries = nav == null || nav.OpenAt(a) > 8f || Touching(m, a) >= 5 ? 1 : sticky ? 2 : 4;
                        for (int k = 0; k < tries; k++)
                        {
                            if (k > 0 && Vector2.Distance(best, a) >= 0.2f * mt.StepM) break;
                            int s = sticky || (k & 1) == 0 ? side : -side;
                            float turn = s * (sticky ? (k == 0 ? 0.8f : 1.4f) : (k < 2 ? 0.8f : 1.4f));
                            Vector2 b2 = a + Rotate(dir, turn) * (mt.StepM * 0.8f);
                            if (nav != null) b2 = WallStep(a, b2, mt.Radius);
                            if (Vector2.Distance(b2, heroAt) < hold) continue;
                            // A sidestep shorter than a quarter of its length would only mark time: skip the search
                            // when a neighbour already blocks that quarter.
                            if (nav != null && !AllClear(m, a, b2, 0.25f)) continue;
                            float f2 = SepFraction(m, a, b2);
                            Vector2 c2 = Vector2.Lerp(a, b2, f2);
                            float prog = Vector2.Dot(dir, c2 - a);
                            if (k == 0 ? prog > bestProg + 0.05f : prog > 0.02f && Vector2.Distance(c2, a) >= 0.2f * mt.StepM)
                            {
                                best = c2; bestProg = prog;
                                if (nav != null) sideMem[m] = (sbyte)s;
                            }
                        }
                    }
                }
                if (nav != null && best != a) best = WallKeep(m, a, best, mt.Radius);
                if (Vector2.Distance(best, a) < 0.2f * mt.StepM)
                {
                    best = a;   // marching in place
                    if (nav != null) sideMem[m] = 0;   // a monster marking time may try either side next beat
                }
                float fFrom = Easing.Wrap(facing[m]);
                float fTo = Easing.Unwrap(fFrom, Easing.Yaw(dir));
                SetMonsterSeg(m, new MotionSegment(m, tb, tnext, a, best, fFrom, fTo, ease));
                beatMaxStep = Math.Max(beatMaxStep, Vector2.Distance(a, best));
                planSerial[m] = beatSerial;
                planFrom[m] = a;
            }
        }

        /// <summary>
        /// A walking-in boss tramples the small fry in its step's way: a full climax's horde packs the whole
        /// view, and would otherwise hold the boss at its edge, out of sight. The dying never block.
        /// </summary>
        void Trample(int m, Vector2 a, Vector2 b, double tb)
        {
            float step = Vector2.Distance(a, b);
            int n = Near().Gather(a, radius[m] + SmallRadius + step + Math.Max(beatMaxStep, 0.5f) + 0.05f, ref buf);
            for (int i = 0; i < n; i++)
            {
                int j = buf[i];
                if (j == m || state[j] != Alive || radius[j] > SmallRadius) continue;
                float reach = radius[m] + radius[j];
                bool planned = planSerial[j] == beatSerial;
                if (ArenaLayout.SegmentDistance(rest[j], a, b) < reach || (planned && ArenaLayout.SegmentDistance(planFrom[j], a, b) < reach)) Kill(j, tb, 1f);
            }
        }

        /// <summary>
        /// A monster inside the clear ring (the hero closed in on it) steps back out toward the ring's
        /// edge, still facing the hero, as far as its neighbours and the walls let it.
        /// </summary>
        void BackOff(int m, MonsterType mt, Vector2 a, Vector2 heroAt, Vector2 toHero, float d, float hold, double tb, double tnext, Ease ease, float maxStep)
        {
            Vector2 away = d > 1e-4f ? -toHero / d : -Easing.Dir(facing[m]);
            float step = Math.Min(maxStep, hold - d);
            Vector2 b = a + away * step;
            if (nav != null) b = WallStep(a, b, mt.Radius);
            GatherCandidates(m, a, step);
            Vector2 best = Vector2.Lerp(a, b, SepFraction(m, a, b));
            if (nav != null && best != a) best = WallKeep(m, a, best, mt.Radius);
            float fFrom = Easing.Wrap(facing[m]);
            float fTo = Easing.Unwrap(fFrom, Easing.Yaw(toHero));
            SetMonsterSeg(m, new MotionSegment(m, tb, tnext, a, best, fFrom, fTo, ease));
            beatMaxStep = Math.Max(beatMaxStep, Vector2.Distance(a, best));
            planSerial[m] = beatSerial;
            planFrom[m] = a;
        }

        static Vector2 Rotate(Vector2 v, float yaw)
        {
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            return new Vector2(v.X * c + v.Y * s, -v.X * s + v.Y * c);
        }

        /// <summary>
        /// The largest fraction of the step from <paramref name="a"/> to <paramref name="b"/> that
        /// keeps every neighbour apart over the whole step. Neighbours already planned this beat
        /// move with the same timing and ease, so their relative path is a straight segment and its
        /// closest approach is exact; the others stand at their current segment's end.
        /// </summary>
        /// <summary>Collects the neighbours a step of up to <paramref name="maxStep"/> from <paramref name="a"/> could touch (in cand, nc).</summary>
        void GatherCandidates(int m, Vector2 a, float maxStep)
        {
            float ri = radius[m];
            int n = Near().Gather(a, ri + SmallRadius + maxStep + Math.Max(beatMaxStep, 0.5f) + 0.05f, ref buf);
            nc = 0;
            for (int i = 0; i < n + bigCount; i++)
            {
                int j = i < n ? buf[i] : big[i - n];
                if (j == m || (i < n && radius[j] > SmallRadius)) continue;
                if (state[j] != Alive) continue;   // the dying fall away and never block
                // Only neighbours whose own move this beat can come within reach of this step.
                bool planned = planSerial[j] == beatSerial;
                float reachJ = planned ? Vector2.Distance(planFrom[j], rest[j]) : 0f;
                Vector2 cj = planned ? planFrom[j] : rest[j];
                float lim = ri + radius[j] + maxStep + reachJ;
                if (Vector2.DistanceSquared(a, cj) > lim * lim) continue;
                if (nc == cand.Length) Array.Resize(ref cand, nc * 2);
                cand[nc++] = j;
            }
        }

        /// <summary>Does the step from <paramref name="a"/> to <paramref name="b"/>, cut to fraction <paramref name="f"/>, keep clear of every gathered neighbour?</summary>
        bool AllClear(int m, Vector2 a, Vector2 b, float f)
        {
            for (int k = 0; k < nc; k++)
                if (!PairOk(m, cand[k], radius[m], a, b, f)) return false;
            return true;
        }

        /// <summary>How many of the gathered neighbours (cand) touch a monster standing at <paramref name="a"/>, by where they stand or, if planned this beat, where they started.</summary>
        int Touching(int m, Vector2 a)
        {
            int n = 0;
            for (int k = 0; k < nc; k++)
            {
                int j = cand[k];
                Vector2 q = planSerial[j] == beatSerial ? planFrom[j] : rest[j];
                float reach = radius[m] + radius[j] + 0.3f;
                if (Vector2.DistanceSquared(a, q) < reach * reach) n++;
            }
            return n;
        }

        int nc;

        float SepFraction(int m, Vector2 a, Vector2 b)
        {
            float ri = radius[m];
            int nf = 0;
            for (int k = 0; k < nc; k++)
            {
                if (PairOk(m, cand[k], ri, a, b, 1f)) continue;
                if (nf == strike.Length) Array.Resize(ref strike, nf * 2);
                strike[nf++] = cand[k];
            }
            if (nf == 0) return 1f;
            float lo = 0, hi = 1;
            for (int it = 0; it < 6; it++)
            {
                float mid = (lo + hi) / 2;
                bool ok = true;
                for (int k = 0; k < nf && ok; k++) ok = PairOk(m, strike[k], ri, a, b, mid);
                if (ok) lo = mid; else hi = mid;
            }
            // A neighbour moving past can clear the full step but not a shorter one, so check every
            // candidate at the chosen fraction and halve it until all clear (standing still always does).
            float f = lo;
            while (f > 0)
            {
                bool ok = true;
                for (int k = 0; k < nc && ok; k++) ok = PairOk(m, cand[k], ri, a, b, f);
                if (ok) break;
                f = f < 0.1f ? 0 : f * 0.5f;
            }
            return f;
        }

        bool PairOk(int m, int j, float ri, Vector2 a, Vector2 b, float f)
        {
            float rj = radius[j];
            float dmin = ri + rj - SimOverlap * Math.Min(ri, rj);
            Vector2 bi = a + (b - a) * f;
            Vector2 r0, r1;
            if (planSerial[j] == beatSerial) { r0 = a - planFrom[j]; r1 = bi - rest[j]; }
            else { r0 = a - rest[j]; r1 = bi - rest[j]; }
            float d0 = r0.Length();
            float need = Math.Min(dmin, d0 - 1e-4f);
            return ClosestApproach(r0, r1) >= need;
        }

        static float ClosestApproach(Vector2 r0, Vector2 r1)
        {
            Vector2 d = r1 - r0;
            float dd = Vector2.Dot(d, d);
            if (dd < 1e-12f) return r0.Length();
            float u = -Vector2.Dot(r0, d) / dd;
            if (u < 0) u = 0; else if (u > 1) u = 1;
            return (r0 + d * u).Length();
        }

        bool TryScheduleAttack(int m, MonsterType mt, double tb, Vector2 toHero)
        {
            ClipAnnotation clip = clips[mt.AttackClip];
            double cooldown = cooldownRng.Next(3) * 0.25 * (map.Beats[1] - map.Beats[0]);
            double availFrom = Math.Max(tb, lastActionEnd[m] + cooldown);
            for (int gi = grid.FirstAfter(tb); gi < grid.Count && grid.Points[gi].Time <= tb + 3.0; gi++)
            {
                GridPoint p = grid.Points[gi];
                if ((p.Flags & GridFlags.Beat) == 0) continue;
                if (!ClipLibrary.Fit(clip, availFrom, p.Time, out double start, out double end, out float speed)) continue;
                if (start < tb - 1e-9) continue;
                Push(new Impact { Time = p.Time, Kind = ImpactKind.MonsterAttack, Entity = m, Target = 0, Clip = mt.AttackClip, ClipStart = start, ClipSpeed = speed });
                busyUntil[m] = end;
                lastActionEnd[m] = end;
                float fFrom = Easing.Wrap(facing[m]);
                float fTo = Easing.Unwrap(fFrom, Easing.Yaw(toHero));
                if (Math.Abs(fTo - fFrom) > 0.35f)
                    SetMonsterSeg(m, new MotionSegment(m, tb, Math.Min(start, tb + 0.15), rest[m], rest[m], fFrom, fTo, Ease.Linear));
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ structure breaks

        /// <summary>
        /// Each section's breaks on its grid: an entry breaks on the k-th occurrence of its position in its section
        /// (its "/k", the first by default). A position the section lacks, or a piece broken twice, fails.
        /// </summary>
        ScheduledBreak[] ScheduleBreaks()
        {
            var list = new List<ScheduledBreak>();
            var broken = new HashSet<int>();
            for (int s = 0; s < plans.Length; s++)
                for (int k = 0; k < plans[s].Breaks.Length; k++)
                {
                    GridEntry e = plans[s].Breaks[k];
                    int seen = 0;
                    GridPoint at = default;
                    for (int gi = grid.FirstAfter(map.Sections[s].Start - 1e-6); gi < grid.Count && seen < e.Every; gi++)
                    {
                        GridPoint p = grid.Points[gi];
                        if (p.Section != s || (p.Flags & e.Position) != e.Position) continue;
                        if (++seen == e.Every) at = p;
                    }
                    if (seen < e.Every)
                        throw new FormatException("structure breaks: section '" + plans[s].Label + "' has no " + e.Position.ToString().ToLowerInvariant() + " #" + e.Every + " for '" + e + "'");
                    foreach (int piece in plans[s].BreakPieces[k])
                        if (!broken.Add(piece)) throw new FormatException("structure breaks: '" + e.Name + "' breaks a piece that an earlier entry broke");
                    list.Add(new ScheduledBreak { Time = at.Time, Strength = Math.Max(0.8f, at.AccentStrength), Target = e.Name, Pieces = plans[s].BreakPieces[k] });
                }
            // By time; same-time breaks keep the script's order (List.Sort is not stable).
            var order = new int[list.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => { int c = list[a].Time.CompareTo(list[b].Time); return c != 0 ? c : a.CompareTo(b); });
            return Array.ConvertAll(order, i => list[i]);
        }

        /// <summary>
        /// The hero's bolt at each break whose flight starts before the next decision: it flies from where the hero
        /// stands at its launch to the target's nearest point, and strikes on the break's grid point (choreography.md,
        /// "Structure breaks": a spell sent at the piece, so a break is always the fight's).
        /// </summary>
        void SendBreaks(double tb, double tnext)
        {
            float speed = roster.Projectiles[breakSpell.Projectile].SpeedMps;
            for (; nextBreak < breaks.Length; nextBreak++)
            {
                ScheduledBreak b = breaks[nextBreak];
                double launch = b.Time - Vector2.Distance(HeroRest, NearestPiecePoint(b, HeroRest)) / speed;
                if (launch >= tnext) return;
                launch = Math.Max(tb, Math.Min(launch, b.Time));
                b.Origin = HeroPosAt(launch);
                b.Point = NearestPiecePoint(b, b.Origin);
                int proj = NewEntity(EntityKind.Projectile, breakSpell.Projectile, launch);
                despawnT[proj] = b.Time;
                b.Projectile = proj;
                float yaw = Easing.Yaw(b.Point - b.Origin);
                AddSeg(new MotionSegment(proj, launch, b.Time, b.Origin, b.Point, yaw, yaw, Ease.Linear));
                Vector2 muzzle = b.Origin + Easing.Dir(yaw) * (roster.HeroRadius + 0.4f);
                effects.Add(new EffectEvent(Catalog.BoltLaunch, -1, eseed[proj], launch, new Vector3(muzzle.X, 1.3f, muzzle.Y), yaw, 0.6f));
                Push(new Impact { Time = b.Time, Kind = ImpactKind.Break, Entity = proj, Target = nextBreak, Move = breakSpell, Point = b.Point, Strength = b.Strength });
            }
        }

        Vector2 NearestPiecePoint(ScheduledBreak b, Vector2 from)
        {
            Vector2 best = from;
            float bd = float.MaxValue;
            foreach (int piece in b.Pieces)
            {
                if (!nav.Blocks(piece)) continue;
                Vector2 q = nav.NearestPoint(piece, from);
                float d = Vector2.Distance(q, from);
                if (d < bd) { bd = d; best = q; }
            }
            return best;
        }

        /// <summary>
        /// The bolt strikes: its pieces leave the obstacle map (their footprints at once, the capped field and the
        /// passable cells round them), and the field toward the hero, the uncapped field and the goal fields of this
        /// and the later sections rebuild over the new map, sliced across the ticks like any rebuild.
        /// </summary>
        void Break(ScheduledBreak b, double t)
        {
            effects.Add(new EffectEvent(Catalog.BoltImpact, -1, eseed[b.Projectile], t, new Vector3(b.Point.X, 1f, b.Point.Y), 0, 1f));
            if (breakSpell.Sound >= 0) Sound(breakSpell.Sound, t, b.Strength);
            foreach (int piece in b.Pieces) breakEvents.Add(new BreakEvent(t, piece, b.Target, b.Projectile, b.Origin, b.Point));
            float halfWidth = nav.Remove(b.Pieces);
            // Room for every size (choreography.md): the opening lets the largest monster through with 0.5 m to spare
            // each side, as far as the capped field can tell.
            float need = Math.Min(roster.MaxMonsterRadius + 0.5f, NavGrid.MaxClearance - NavGrid.CellM);
            if (halfWidth < need)
                throw new FormatException("structure breaks: '" + b.Target + "' leaves an opening " + (2 * halfWidth).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    + " m wide; the largest monster needs " + (2 * roster.MaxMonsterRadius + 1).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " m");
            navChanged = true;
            double bi = Math.Floor(map.BeatIndexAt(t));
            int ticks = Math.Max(1, (int)Math.Floor((map.TimeOfBeatIndex(bi + 1) - map.TimeOfBeatIndex(bi)) * Choreographer.TickRate));   // a beat's
            nav.BeginOpenRebuild();
            openLines = (nav.W + nav.H + ticks - 1) / ticks;
            goalBudget = Math.Max(256, (nav.W * nav.H + ticks - 1) / ticks);
            goalRebuilds.Clear();
            goalNext = null;
            for (int s = map.SectionIndexAt(t); s < plans.Length; s++)
                if (plans[s].Goal != null && !goalRebuilds.Contains(plans[s].Goal.Name)) goalRebuilds.Add(plans[s].Goal.Name);
        }

        /// <summary>One tick's slice of the rebuilds a break started: the uncapped field's lines, then each goal field in turn, swapped in when done.</summary>
        void NavRebuildSlice()
        {
            if (nav.RebuildingOpen) nav.StepOpen(openLines);
            if (goalNext == null && goalRebuilds.Count > 0)
            {
                GoalArea g = Array.Find(plans, p => p.Goal != null && p.Goal.Name == goalRebuilds[0]).Goal;
                goalNext = new FlowField(nav, roster.HeroRadius + 0.05f);
                goalNext.BeginArea(g.Center, Math.Min(g.Radius, 1f));
                flowRebuilds++;
            }
            if (goalNext == null) return;
            flowSlices++;
            if (!goalNext.Step(goalBudget)) return;
            string name = goalRebuilds[0];
            if (goalField == goalFields[name]) goalField = goalNext;
            goalFields[name] = goalNext;
            goalNext = null;
            goalRebuilds.RemoveAt(0);
        }

        // ------------------------------------------------------------------ navigation (TD6)

        /// <summary>
        /// On each beat: when the hero has entered a new cell, start rebuilding the field toward the
        /// hero, to be advanced one slice a tick across this beat's ticks (choreography.md, "Rebuilds
        /// are rare and spread out"). Monsters keep using the previous field until the new one is done.
        /// The first field is built whole, at load.
        /// </summary>
        void FlowBeat(double tb, double tnext)
        {
            Vector2 h = HeroRest;
            if (heroField.SourceCell < 0)
            {
                heroField.Begin(h);
                heroField.BuildAll();
                flowRebuilds++;
                flowSlices++;
                return;
            }
            if (navChanged) navChanged = false;   // a break: rebuild over the new map, restarting one begun on the old
            else if (heroFieldNext.Building || Vector2.Distance(h, heroField.Source) < RebuildMoveM) return;
            heroFieldNext.Begin(h);
            flowRebuilds++;
            int ticks = Math.Max(1, (int)Math.Floor((tnext - tb) * Choreographer.TickRate));
            sliceBudget = Math.Max(256, (heroField.Settled + ticks - 1) / ticks);
        }

        /// <summary>
        /// A monster's heading at <paramref name="a"/>: straight at the hero where the line to it is
        /// clear of walls, else downhill along the field toward the hero. <paramref name="left"/> becomes the way's length by the field.
        /// </summary>
        Vector2 MonsterHeading(Vector2 a, Vector2 heroAt, float r, Vector2 straight, ref float left)
        {
            if (nav.Clear(a, heroAt, r)) return straight;
            FlowField f = heroField;
            // Off the field (squeezed against a wall, where the cell is too tight for it), Direction heads for
            // the cheapest reached neighbour cell; off the grid or walled in, head straight and let walls push out.
            if (!f.Direction(a, out Vector2 dir)) return straight;
            float cost = f.CostAt(a);
            if (!float.IsInfinity(cost)) left = Math.Max(left, cost);
            return dir;
        }

        /// <summary>The hero's way toward the section's goal at <paramref name="h"/>, by its goal field, and the way's length.</summary>
        Vector2 GoalHeading(Vector2 h, out float left)
        {
            Vector2 d = goal.Center - h;
            float dist = d.Length();
            left = dist;
            Vector2 straight = dist > 1e-4f ? d / dist : new Vector2(0, 1);
            if (goalField == null || nav.Clear(h, goal.Center, roster.HeroRadius)) return straight;
            if (!goalField.Direction(h, out Vector2 dir)) return straight;
            float cost = goalField.CostAt(h);
            if (!float.IsInfinity(cost)) left = cost;
            return dir;
        }

        /// <summary>
        /// A step from <paramref name="a"/> toward <paramref name="b"/> for a body of radius
        /// <paramref name="r"/>: far from every wall (one field lookup at each end) it stands; near
        /// one, it slides: four quarter steps, each pushed out of the walls along their normals, so a
        /// step into a wall runs along it and round its corners. The step ends at the farthest of
        /// those points whose straight line from <paramref name="a"/> keeps clear.
        /// </summary>
        Vector2 WallStep(Vector2 a, Vector2 b, float r)
        {
            float len = Vector2.Distance(a, b);
            if (Math.Min(nav.ClearanceLowerBound(a), nav.ClearanceLowerBound(b)) - len / 2 >= r) return b;
            Vector2 d = (b - a) / 4, p = a;
            Vector2 p1 = a, p2 = a, p3 = a, p4 = a;
            int n = 0;
            for (int k = 1; k <= 4; k++)
            {
                p += d;
                if (!nav.PushOut(ref p, r)) break;
                n = k;
                if (k == 1) p1 = p; else if (k == 2) p2 = p; else if (k == 3) p3 = p; else p4 = p;
            }
            if (n >= 4 && SegmentClear(a, p4, r)) return p4;
            if (n >= 3 && SegmentClear(a, p3, r)) return p3;
            if (n >= 2 && SegmentClear(a, p2, r)) return p2;
            if (n >= 1 && SegmentClear(a, p1, r)) return p1;
            return a;
        }

        /// <summary>
        /// A planned step's end pulled back toward <paramref name="a"/> until it keeps the full radius
        /// from every wall (or no less than <paramref name="a"/> had), so nobody ratchets into a wall
        /// a little each beat. A shortened step is checked against the neighbours again, and stays put
        /// if any would come too close.
        /// </summary>
        Vector2 WallKeep(int m, Vector2 a, Vector2 p, float r)
        {
            if (nav.ClearanceLowerBound(p) >= r) return p;
            float keep = Math.Min(r, nav.Distance(a));
            if (nav.Distance(p) >= keep) return p;
            float lo = 0, hi = 1;
            for (int it = 0; it < 5; it++)
            {
                float mid = (lo + hi) / 2;
                if (nav.Distance(Vector2.Lerp(a, p, mid)) >= keep) lo = mid; else hi = mid;
            }
            Vector2 q = Vector2.Lerp(a, p, lo);
            for (int k = 0; k < nc; k++)
                if (!PairOk(m, cand[k], radius[m], a, q, 1f)) return a;
            return q;
        }

        /// <summary>Does the straight move from <paramref name="a"/> to <paramref name="b"/> keep radius <paramref name="r"/> clear of walls (or no closer than it starts)?</summary>
        bool SegmentClear(Vector2 a, Vector2 b, float r)
        {
            float len = Vector2.Distance(a, b);
            if (Math.Min(nav.ClearanceLowerBound(a), nav.ClearanceLowerBound(b)) - len / 2 >= r) return true;
            // The end must keep the full radius (or no less than the start had), so nobody ratchets into a wall
            // a little more each beat; the points between may graze it by WallSlack.
            float keep = Math.Min(r, nav.Distance(a));
            int n = Math.Max(2, (int)Math.Ceiling(len / 0.15f));
            for (int i = 1; i <= n; i++)
                if (nav.Distance(Vector2.Lerp(a, b, (float)i / n)) < (i == n ? keep : keep - WallSlack)) return false;
            return true;
        }

        /// <summary>Where the boss enters: outside the view toward <paramref name="dir"/>, turning in 30° steps until the ground is open.</summary>
        Vector2 BossEntry(ViewGeometry view, Vector2 dir, float br)
        {
            for (int k = 0; k < 12; k++)
            {
                float turn = (k + 1) / 2 * (float)(Math.PI / 6) * ((k & 1) == 0 ? 1 : -1);
                Vector2 d = Rotate(dir, turn);
                for (float dist = view.Near; dist < view.Radius + 20f; dist += 0.5f)
                {
                    Vector2 p = focus + d * dist;
                    if (view.OutsideDistance(focus, p) >= SpawnMargin + br && !SpawnBlocked(p, br)) return p;
                }
            }
            return focus + dir * (view.Radius + SpawnMargin + br);
        }

        // ------------------------------------------------------------------ spells

        void Spells(double tb, double tnext)
        {
            for (int gi = grid.FirstAfter(tb); gi < grid.Count && grid.Points[gi].Time <= tnext + 1e-9; gi++)
            {
                GridPoint p = grid.Points[gi];
                SectionPlan sp = plans[p.Section];
                if (!sp.HeroAttacks) continue;
                if (p.Time >= shot.Final - 1e-6) continue;   // nothing but the final slam fires on its beat, and nothing after it
                if (shot.HasSpan && p.Time > shot.SpanTo + 1e-6) continue;
                for (int k = 0; k < sp.Spells.Length; k++)
                {
                    GridEntry e = sp.Spells[k];
                    if (!e.Matches(p)) continue;
                    int c = spellCount[p.Section][k]++;
                    if (c % e.Every != 0) continue;
                    // With one-hit kills the hero's blows reap whatever walks in, so spells only top up the
                    // section's deaths: one that would land while they keep up with the load's rate holds back
                    // (unless the kill budget is off, as in a trailer shot).
                    if (tuning.OneHitKills && shot.KillBudget && p.Section == curSection && OnRate(p.Time)) continue;
                    CastSpell(roster.Spell(e.Name), p, tb, tnext);
                }
            }
        }

        /// <summary>
        /// Whether this section's deaths at <paramref name="t"/> keep up with its load preset's rate, to within
        /// a quarter second's worth: a spell's kills come in a lump, so one cast a hair behind would overshoot.
        /// </summary>
        bool OnRate(double t)
        {
            double rate = plan.Preset.DeathsPerSecond(plan.TargetOnScreen);
            return rate > 0 && secKills >= rate * (t - secStart - 0.25);
        }

        void CastSpell(Move mv, GridPoint p, double tb, double tnext)
        {
            float strength = Math.Max(mv.Strength, p.AccentStrength);
            switch (mv.Shape)
            {
                case HitShape.Ring:
                    Push(new Impact { Time = p.Time, Kind = ImpactKind.Ring, Entity = 0, Target = -1, Move = mv, Strength = strength });
                    break;
                case HitShape.Single:
                {
                    Vector2 h = HeroPosAt(p.Time);
                    float speed = roster.Projectiles[mv.Projectile].SpeedMps;
                    float range = (float)Math.Min(mv.RangeM, speed * (p.Time - tb));
                    if (range < 2f) break;
                    if (!PickTarget(h, 1.5f, range, p.Time, true, out int target, out Vector2 landing)) break;
                    double launch = p.Time - Vector2.Distance(h, landing) / speed;
                    Vector2 from = HeroPosAt(launch);
                    int proj = NewEntity(EntityKind.Projectile, mv.Projectile, launch);
                    despawnT[proj] = p.Time;
                    float yaw = Easing.Yaw(landing - from);
                    AddSeg(new MotionSegment(proj, launch, p.Time, from, landing, yaw, yaw, Ease.Linear));
                    // The muzzle flash fires from just beyond the hero's body, toward the target, and small: one
                    // a beat at the hero's own centre would bury the hero in fire.
                    Vector2 muzzle = from + Easing.Dir(yaw) * (roster.HeroRadius + 0.4f);
                    effects.Add(new EffectEvent(Catalog.BoltLaunch, -1, eseed[proj], launch, new Vector3(muzzle.X, 1.3f, muzzle.Y), yaw, 0.6f));
                    Push(new Impact { Time = p.Time, Kind = ImpactKind.Bolt, Entity = proj, Target = target, Move = mv, Point = landing, Strength = strength });
                    break;
                }
                case HitShape.Circle:
                {
                    Vector2 h = HeroPosAt(p.Time);
                    if (!PickTarget(h, 3f, mv.RangeM, p.Time, false, out int target, out Vector2 point)) break;
                    Push(new Impact { Time = p.Time, Kind = ImpactKind.Circle, Entity = 0, Target = target, Move = mv, Point = point, Strength = strength });
                    break;
                }
                case HitShape.Line:
                {
                    double beat = tnext - tb;
                    double dur = mv.Bars * map.BeatsPerBarAt(1) * beat;
                    float yaw0 = Easing.Wrap(HeroFacing);
                    effects.Add(new EffectEvent(mv.Effect, 0, Rng.Hash(seed, (uint)effects.Count, 0x1A5E), p.Time,
                        new Vector3(0, 1.2f, 0), yaw0, Reaching(mv.Length)));
                    if (mv.Sound >= 0) Sound(mv.Sound, p.Time, strength);
                    for (int gi = grid.FirstAfter(p.Time); gi < grid.Count && grid.Points[gi].Time <= p.Time + dur + 1e-9; gi++)
                    {
                        GridPoint g = grid.Points[gi];
                        if ((g.Flags & GridFlags.Eighth) == 0) continue;
                        if (g.Time >= shot.Final - 1e-6 || (shot.HasSpan && g.Time > shot.SpanTo + 1e-6)) break;   // nothing else on the final slam's beat
                        float yaw = yaw0 + (float)(2 * Math.PI * (g.Time - p.Time) / dur);
                        Push(new Impact { Time = g.Time, Kind = ImpactKind.LaserTick, Entity = 0, Target = -1, Move = mv, Yaw = yaw, Strength = 0.2f });
                    }
                    break;
                }
                default:
                    Push(new Impact { Time = p.Time, Kind = ImpactKind.Ring, Entity = 0, Target = -1, Move = mv, Strength = strength });
                    break;
            }
        }

        bool PickTarget(Vector2 h, float minR, float maxR, double t, bool needLine, out int target, out Vector2 point)
        {
            int n = Near().Gather(h, maxR + SmallRadius, ref buf);
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                int m = buf[i];
                if (state[m] != Alive) continue;
                float d = Vector2.Distance(pos[m], h);
                if (d < minR || d > maxR) continue;
                if (!onScreen[m]) continue;
                if (shot.BossType >= 0 && roster.Monsters[etype[m]].Rank == MonsterRank.Boss) continue;   // the boss rule: spells skip it
                if (needLine && nav != null && !nav.LineClear(h, pos[m])) continue;   // bolts need a clear line; lightning ignores walls
                if (c == strike.Length) Array.Resize(ref strike, c * 2);
                strike[c++] = m;
            }
            if (c == 0) { target = -1; point = h; return false; }
            Array.Sort(strike, 0, c);
            target = strike[spellRng.Next(c)];
            point = MonsterPosAt(target, t);
            return true;
        }

        // ------------------------------------------------------------------ impacts

        void ProcessImpact(Impact im)
        {
            double t = im.Time;
            // An impact on a section's first downbeat belongs to that section's budget.
            int sec = map.SectionIndexAt(t);
            if (sec > curSection) EnterSection(sec, t);
            int killsBefore = totalKills;
            switch (im.Kind)
            {
                case ImpactKind.HeroCombo:
                {
                    Vector2 o = HeroPosAt(t);
                    Move mv = im.Move;
                    float size = mv.Shape == HitShape.Line ? mv.Length : mv.Radius;
                    float reach = Reaching(size);
                    int n = mv.Shape == HitShape.Line
                        ? QueryLine(o, im.Yaw, reach, Scaled(mv.Width), t)
                        : QueryArc(o, im.Yaw, reach, mv.Shape == HitShape.Arc ? mv.ArcDeg : 360f, t);
                    actions.Add(new ActionEvent(0, ActionKind.Attack, t, im.Clip, -1, im.Strength, im.ClipStart, im.ClipSpeed));
                    // The trail matches the blow's reach (the feedback's scale, or the ring's floor on it).
                    float fxScale = clearRadius > 0 && size > 0 ? reach / size : shapeScale;
                    // A combo drawn with another's effect (the cleave's giant slash) scales it to the reach it cuts.
                    if (mv.EffectRadius > 0) fxScale *= size / mv.EffectRadius;
                    if (mv.Effect >= 0)
                        effects.Add(new EffectEvent(mv.Effect, 0, Rng.Hash(seed, (uint)effects.Count, 0xC0B0), t, new Vector3(0, 1f, 0), im.Yaw, fxScale));
                    Sound(mv.Sound >= 0 ? mv.Sound : Catalog.SwingSound, t, im.Strength);
                    if (plan.Clear) ClearStrike(t, o, im.Strength);
                    else if (Math.Abs(t - shot.Final) <= 1e-6) FinalSlam(t, o, im.Strength);
                    else Strike(t, o, n, im.Strength, true);
                    int swingKills = totalKills - killsBefore;
                    if (tuning.HeavyBlowKills > 0 && swingKills >= tuning.HeavyBlowKills)
                        Sound(Catalog.Sound("ring"), t, Math.Min(1f, swingKills / (2f * tuning.HeavyBlowKills)));
                    stats[curSection].HeroSwings++;
                    stats[curSection].HeroKills += swingKills;
                    stats[curSection].MaxSwingKills = Math.Max(stats[curSection].MaxSwingKills, swingKills);
                    break;
                }
                case ImpactKind.Ring:
                {
                    Vector2 o = HeroPosAt(t);
                    float r = Reaching(im.Move.Radius);
                    int n = QueryArc(o, 0, r, 360f, t);
                    if (im.Move.Effect >= 0)
                        effects.Add(new EffectEvent(im.Move.Effect, -1, Rng.Hash(seed, (uint)effects.Count, 0x7106), t, new Vector3(o.X, 0.1f, o.Y), 0, r));
                    if (im.Move.Sound >= 0) Sound(im.Move.Sound, t, im.Strength);
                    Strike(t, o, n, im.Strength, false);
                    stats[curSection].SpellKills += totalKills - killsBefore;
                    break;
                }
                case ImpactKind.Bolt:
                {
                    int n = QueryArc(im.Point, 0, 0.8f, 360f, t);
                    if (n > 1)
                    {
                        int best = 0;
                        float bd = float.MaxValue;
                        for (int i = 0; i < n; i++)
                        {
                            float d = Vector2.Distance(MonsterPosAt(strike[i], t), im.Point);
                            if (d < bd) { bd = d; best = i; }
                        }
                        strike[0] = strike[best];
                        n = 1;
                    }
                    effects.Add(new EffectEvent(Catalog.BoltImpact, -1, eseed[im.Entity], t, new Vector3(im.Point.X, 1f, im.Point.Y), 0, 1f));
                    if (im.Move.Sound >= 0) Sound(im.Move.Sound, t, im.Strength);
                    Strike(t, im.Point, n, im.Strength, false);
                    stats[curSection].SpellKills += totalKills - killsBefore;
                    break;
                }
                case ImpactKind.Circle:
                {
                    float r = Scaled(im.Move.Radius);
                    int n = QueryArc(im.Point, 0, r, 360f, t);
                    if (im.Move.Effect >= 0)
                        effects.Add(new EffectEvent(im.Move.Effect, -1, Rng.Hash(seed, (uint)effects.Count, 0x119E), t, new Vector3(im.Point.X, 0, im.Point.Y), 0, r));
                    if (im.Move.Sound >= 0) Sound(im.Move.Sound, t, im.Strength);
                    Strike(t, im.Point, n, im.Strength, false);
                    stats[curSection].SpellKills += totalKills - killsBefore;
                    break;
                }
                case ImpactKind.LaserTick:
                {
                    Vector2 o = HeroPosAt(t);
                    int n = QueryLine(o, im.Yaw, Reaching(im.Move.Length), Scaled(im.Move.Width), t);
                    if (tuning.OneHitKills || !shot.KillBudget)
                    {
                        // The sweep kills all it strikes but the boss, which it only flashes: a knockback a tick
                        // would shove it back on every one of the sweep's many ticks.
                        int k = 0;
                        for (int i = 0; i < n; i++)
                        {
                            if (roster.Monsters[etype[strike[i]]].Rank != MonsterRank.Boss) { strike[k++] = strike[i]; continue; }
                            if (shot.BossType >= 0) continue;   // the boss rule: the sweep skips it entirely
                            actions.Add(new ActionEvent(strike[i], ActionKind.Tick, t, -1, -1, im.Strength));
                            stats[curSection].Ticks++;
                        }
                        Strike(t, o, k, im.Strength, false);
                        stats[curSection].SpellKills += totalKills - killsBefore;
                        break;
                    }
                    for (int i = 0; i < n; i++) actions.Add(new ActionEvent(strike[i], ActionKind.Tick, t, -1, -1, im.Strength));
                    stats[curSection].Ticks += n;
                    break;
                }
                case ImpactKind.MonsterAttack:
                {
                    int m = im.Entity;
                    if (state[m] != Alive) break;
                    MonsterType mt = roster.Monsters[etype[m]];
                    Vector2 h = HeroPosAt(t);
                    Vector2 p = MonsterPosAt(m, t);
                    bool inReach = Vector2.Distance(h, p) <= roster.HeroRadius + mt.Radius + 0.15f + mt.ReachM + 0.3f;
                    actions.Add(new ActionEvent(m, ActionKind.Attack, t, im.Clip, inReach ? 0 : -1, 0.3f, im.ClipStart, im.ClipSpeed));
                    stats[curSection].Attacks++;
                    if (inReach && mt.Projectile >= 0) Fire(m, mt, t, p, h);
                    else if (inReach) Parry(m, t, p, h);
                    break;
                }
                case ImpactKind.MonsterBolt:
                    Parry(im.Target, t, im.Point, HeroPosAt(t));
                    break;
                case ImpactKind.Break:
                    Break(breaks[im.Target], t);
                    break;
                case ImpactKind.WorstRing:
                {
                    Vector2 o = HeroPosAt(t);
                    var view = View(viewWidth);
                    int c = 0;
                    for (int i = 0; i < liveCount; i++)
                    {
                        int m = live[i];
                        if (state[m] != Alive || !view.Contains(focus, pos[m])) continue;
                        if (c == keys.Length) { Array.Resize(ref keys, c * 2); Array.Resize(ref order, c * 2); }
                        keys[c] = ((long)(Vector2.Distance(pos[m], o) * 1000) << 24) | (long)m;
                        order[c] = m;
                        c++;
                    }
                    Array.Sort(keys, order, 0, c);
                    int kills = (int)Math.Round(0.3 * c);
                    for (int i = 0; i < kills; i++) Kill(order[i], t, 1f);
                    effects.Add(new EffectEvent(Catalog.Effect("ring"), -1, Rng.Hash(seed, (uint)effects.Count, 0x3A57), t, new Vector3(o.X, 0.1f, o.Y), 0, viewWidth / 2));
                    Sound(Catalog.Sound("ring"), t, 1f);
                    break;
                }
            }
        }

        /// <summary>A hit shape's size after the kill-rate feedback, kept within a quarter of the view's width.</summary>
        float Scaled(float size) => Math.Min(size * shapeScale, tuning.MaxShapeViewFraction * viewWidth);

        /// <summary>
        /// The reach of a shape centred on the hero, after the feedback: with a clear ring, never short
        /// of the front rank at its edge, so the hero's blows always land on the horde, not the air.
        /// </summary>
        float Reaching(float size) => clearRadius > 0 ? Math.Max(Scaled(size), Math.Min(size, clearRadius + 0.6f)) : Scaled(size);

        int QueryArc(Vector2 o, float yaw, float r, float arcDeg, double t)
        {
            var view = View(viewWidth);
            int n = Near().Gather(o, r + SmallRadius + 0.6f, ref buf);
            float cosHalf = (float)Math.Cos(arcDeg * Math.PI / 360);
            Vector2 fwd = Easing.Dir(yaw);
            int c = 0;
            for (int i = 0; i < n + bigCount; i++)
            {
                int m = i < n ? buf[i] : big[i - n];
                if (i < n && radius[m] > SmallRadius) continue;
                if (state[m] != Alive) continue;
                Vector2 p = MonsterPosAt(m, t);
                if (!view.Contains(focus, p, radius[m])) continue;
                Vector2 d = p - o;
                float l = d.Length();
                if (l > r + HitShapeInflate * radius[m]) continue;
                if (arcDeg < 359f && l > 1e-4f && Vector2.Dot(d / l, fwd) < cosHalf && l > radius[m]) continue;
                if (c == strike.Length) Array.Resize(ref strike, c * 2);
                strike[c++] = m;
            }
            Array.Sort(strike, 0, c);
            return c;
        }

        int QueryLine(Vector2 o, float yaw, float length, float width, double t)
        {
            var view = View(viewWidth);
            Vector2 fwd = Easing.Dir(yaw);
            Vector2 mid = o + fwd * (length / 2);
            int n = Near().Gather(mid, length / 2 + width + SmallRadius + 0.6f, ref buf);
            int c = 0;
            for (int i = 0; i < n + bigCount; i++)
            {
                int m = i < n ? buf[i] : big[i - n];
                if (i < n && radius[m] > SmallRadius) continue;
                if (state[m] != Alive) continue;
                Vector2 pm = MonsterPosAt(m, t);
                if (!view.Contains(focus, pm, radius[m])) continue;
                Vector2 d = pm - o;
                float along = Vector2.Dot(d, fwd);
                float across = Math.Abs(d.X * fwd.Y - d.Y * fwd.X);
                float inflate = HitShapeInflate * radius[m];
                if (along < -inflate || along > length + inflate || across > width / 2 + inflate) continue;
                if (c == strike.Length) Array.Resize(ref strike, c * 2);
                strike[c++] = m;
            }
            Array.Sort(strike, 0, c);
            return c;
        }

        /// <summary>
        /// The kill budget: deaths the section's load preset asks for by the midpoint to the next
        /// killing impact (or the section's end), less those already dealt in this section.
        /// </summary>
        int Allot(double t)
        {
            double rate = plan.Preset.DeathsPerSecond(plan.TargetOnScreen);
            if (rate <= 0) return 0;
            double next = double.PositiveInfinity;
            for (int i = 0; i < heapCount; i++)
                if (heap[i].Kills && heap[i].Time < next && heap[i].Time >= t) next = heap[i].Time;
            // The next chance to kill: the next pending killing impact, or in a free section its next grid
            // point, or in a pulsed one at most two beats on; within the last two beats, the section's end.
            double nextOpp;
            if (map.Sections[curSection].Feel == SectionFeel.Free)
            {
                nextOpp = next;
                int gi = grid.FirstAfter(t);
                if (gi < grid.Count && grid.Points[gi].Section == curSection) nextOpp = Math.Min(nextOpp, grid.Points[gi].Time);
            }
            else
            {
                double beat = map.Beats[1] - map.Beats[0];
                nextOpp = t + 2 * beat >= secEnd ? double.PositiveInfinity : Math.Min(next, t + 2 * beat);
            }
            double horizon = nextOpp < secEnd ? t + 0.5 * (nextOpp - t) : secEnd;
            return Math.Max(0, (int)Math.Round(rate * (horizon - secStart) - secKills));
        }

        void Strike(double t, Vector2 origin, int n, float strength, bool heroCombo)
        {
            if (n == 0) return;
            int allot = Allot(t);
            if (keys.Length < n) { Array.Resize(ref keys, Math.Max(n, keys.Length * 2)); Array.Resize(ref order, keys.Length); }
            Vector2 hero = clearRadius > 0 ? HeroPosAt(t) : Vector2.Zero;
            for (int i = 0; i < n; i++)
            {
                int m = strike[i];
                Vector2 pm = MonsterPosAt(m, t);
                long dmm = Math.Min((1L << 20) - 1, (long)(Vector2.Distance(pm, origin) * 1000));
                // With a clear ring, monsters inside it die first, so the blows keep the ring open.
                long outside = clearRadius > 0 && Vector2.Distance(pm, hero) > clearRadius + radius[m] ? 1L << 50 : 0;
                keys[i] = outside | ((long)hp[m] << 40) | (dmm << 20) | (long)(m & 0xFFFFF);   // inside the ring, fewest hit points, then nearest, then id
                order[i] = m;
            }
            Array.Sort(keys, order, 0, n);
            int kills = 0;
            barStruck += n;
            for (int i = 0; i < n; i++)
            {
                int m = order[i];
                if (roster.Monsters[etype[m]].Rank == MonsterRank.Boss)
                {
                    if (shot.BossType >= 0) continue;   // the boss rule: only the final slam touches it
                    if (heroCombo && t >= bossDeathAfter[m]) Kill(m, t, 1f);
                    else Hit(m, t, origin, strength);
                }
                else if (tuning.OneHitKills || !shot.KillBudget || kills < allot) { Kill(m, t, strength); kills++; }
                else Hit(m, t, origin, strength);
            }
        }

        /// <summary>
        /// A clearing section's blow (the ending, TD13): it kills its share of the monsters alive on
        /// screen, nearest first, so that each accent left takes as many and the last takes the rest;
        /// its shockwave is a ring to the view's edge. The kill budget doesn't apply.
        /// </summary>
        void ClearStrike(double t, Vector2 o, float strength)
        {
            var view = View(viewWidth);
            int left = 0;   // this section's accents after this one
            for (int gi = grid.FirstAfter(t + 1e-6); gi < grid.Count && grid.Points[gi].Section == curSection; gi++)
                if ((grid.Points[gi].Flags & GridFlags.Accent) != 0) left++;
            // The last blow also takes the off-screen band, which the view's drift after it would reveal.
            float margin = left == 0 ? SpawnMargin + SpawnBand : 0;
            int c = 0;
            for (int i = 0; i < liveCount; i++)
            {
                int m = live[i];
                if (state[m] != Alive || !view.Contains(focus, MonsterPosAt(m, t), radius[m] + margin)) continue;
                if (c == keys.Length) { Array.Resize(ref keys, c * 2); Array.Resize(ref order, c * 2); }
                keys[c] = ((long)(Vector2.Distance(MonsterPosAt(m, t), o) * 1000) << 24) | (long)(m & 0xFFFFFF);
                order[c] = m;
                c++;
            }
            Array.Sort(keys, order, 0, c);
            int kills = (c + left) / (left + 1);
            // With bossFallsLast the boss falls to the last blow, however near: an earlier one takes the next nearest.
            int killed = 0;
            for (int i = 0; i < c && killed < kills; i++)
            {
                if (bossFallsLast && left > 0 && roster.Monsters[etype[order[i]]].Rank == MonsterRank.Boss) continue;
                Kill(order[i], t, strength);
                killed++;
            }
            barStruck += killed;
            effects.Add(new EffectEvent(Catalog.Effect("ring"), -1, Rng.Hash(seed, (uint)effects.Count, 0xC1EA), t, new Vector3(o.X, 0.1f, o.Y), 0, viewWidth / 2));
        }

        /// <summary>
        /// A shot's final slam (trailer.md, "The boss"): it kills every monster in view, the boss included, and the
        /// off-screen band the view's drift after it would reveal; its shockwave is a ring to the view's edge.
        /// </summary>
        void FinalSlam(double t, Vector2 o, float strength)
        {
            var view = View(viewWidth);
            int killed = 0;
            for (int i = 0; i < liveCount; i++)
            {
                int m = live[i];
                if (state[m] != Alive || !view.Contains(focus, MonsterPosAt(m, t), radius[m] + SpawnMargin + SpawnBand)) continue;
                Kill(m, t, strength);
                killed++;
            }
            barStruck += killed;
            effects.Add(new EffectEvent(Catalog.Effect("ring"), -1, Rng.Hash(seed, (uint)effects.Count, 0xF1A1), t, new Vector3(o.X, 0.1f, o.Y), 0, viewWidth / 2));
        }

        void Hit(int m, double t, Vector2 origin, float strength)
        {
            MonsterType mt = roster.Monsters[etype[m]];
            hp[m] = Math.Max(1, hp[m] - 1);
            actions.Add(new ActionEvent(m, ActionKind.Hit, t, mt.HitClip, -1, strength));
            effects.Add(new EffectEvent(Catalog.HitSpark, m, Rng.Hash(eseed[m], (uint)(t * 1000), 1), t, new Vector3(0, 1f, 0), 0, 1f));
            Sound(Catalog.HitSound, t, strength);
            stats[curSection].Hits++;

            Vector2 p = StopAt(m, t, false);
            Vector2 dir = p - origin;
            dir = dir.LengthSquared() > 1e-8f ? Vector2.Normalize(dir) : Easing.Dir(facing[m] + (float)Math.PI);
            float dist = (mt.Rank == MonsterRank.Fodder ? 0.25f : 0.1f) + 0.2f * strength;
            // The push starts from where the current step was going: neighbours behind planned around
            // that end, so stopping short of it would run them into this one.
            Vector2 baseEnd = rest[m];
            // ... and gets there no later than the step would have (at least 30 ms on).
            double tEnd = cur[m].T1 > t ? Math.Max(t + 0.03, Math.Min(t + KnockbackSeconds, cur[m].T1)) : t + KnockbackSeconds;
            Vector2 to = KnockbackPath(m, p, t, baseEnd, baseEnd + dir * dist, tEnd);
            float f = Easing.Wrap(facing[m]);
            SetMonsterSeg(m, new MotionSegment(m, t, tEnd, p, to, f, f, Ease.Linear));
            planSerial[m] = -1;
            busyUntil[m] = Math.Max(busyUntil[m], tEnd);
        }

        Vector2 KnockbackPath(int m, Vector2 s0, double t, Vector2 a, Vector2 b, double tEnd)
        {
            if (KnockbackOk(m, s0, t, a, b, tEnd)) return b;
            float lo = 0, hi = 1;
            for (int it = 0; it < 4; it++)
            {
                float mid = (lo + hi) / 2;
                if (KnockbackOk(m, s0, t, a, Vector2.Lerp(a, b, mid), tEnd)) lo = mid; else hi = mid;
            }
            return Vector2.Lerp(a, b, lo);
        }

        /// <summary>
        /// Does a knockback from <paramref name="s0"/> (at <paramref name="t"/>) to <paramref name="p"/>
        /// (at <paramref name="tEnd"/>) keep clear of every neighbour, at its end and halfway, or at
        /// least no closer than the unpushed move to <paramref name="a"/> would come?
        /// </summary>
        bool KnockbackOk(int m, Vector2 s0, double t, Vector2 a, Vector2 p, double tEnd)
        {
            float ri = radius[m];
            double tMid = (t + tEnd) / 2;
            Vector2 pMid = Vector2.Lerp(s0, p, 0.5f), aMid = Vector2.Lerp(s0, a, 0.5f);
            if (nav != null && (nav.Distance(p) < Math.Min(ri, nav.Distance(a)) || nav.Distance(pMid) < Math.Min(ri, nav.Distance(aMid)) - WallSlack)) return false;
            int n = Near().Gather(p, ri + SmallRadius + MaxStepReach, ref buf);
            for (int i = 0; i < n + bigCount; i++)
            {
                int j = i < n ? buf[i] : big[i - n];
                if (j == m || (i < n && radius[j] > SmallRadius)) continue;
                if (state[j] != Alive) continue;
                float dmin = ri + radius[j] - SimOverlap * Math.Min(ri, radius[j]);
                Vector2 q1 = rest[j], q2 = MonsterPosAt(j, tEnd);
                float now = Math.Min(Vector2.Distance(a, q1), Vector2.Distance(a, q2));
                float need = Math.Min(dmin, now - 1e-4f);
                if (Vector2.Distance(p, q1) < need || Vector2.Distance(p, q2) < need) return false;
                Vector2 qm = MonsterPosAt(j, tMid);
                if (Vector2.Distance(pMid, qm) < Math.Min(dmin, Vector2.Distance(aMid, qm) - 1e-4f)) return false;
            }
            return true;
        }

        void Kill(int m, double t, float strength)
        {
            MonsterType mt = roster.Monsters[etype[m]];
            StopAt(m, t, true);
            state[m] = Dying;
            ClipAnnotation death = clips[mt.DeathClip];
            despawnT[m] = t + Math.Max(0.2, death.End - death.Impact) + CorpseLinger;
            actions.Add(new ActionEvent(m, ActionKind.Death, t, mt.DeathClip, -1, strength));
            effects.Add(new EffectEvent(Catalog.DeathBurst, m, Rng.Hash(eseed[m], 0xDEAD), t, new Vector3(0, 0.6f, 0), 0, mt.Radius / 0.4f));
            Sound(Catalog.DeathSound, t, strength);
            secKills++;
            totalKills++;
            stats[curSection].Deaths++;
        }

        void Sound(int category, double t, float strength) =>
            sounds.Add(new SoundRequest { Category = category, Time = t, Strength = strength, Seq = sounds.Count });

        // ------------------------------------------------------------------ worst beats, refills

        void WorstBeat(double tb, double downbeat, ViewGeometry view)
        {
            for (int i = 0; i < liveCount; i++)
            {
                int m = live[i];
                if (state[m] != Alive || busyUntil[m] > tb + 1e-9 || !view.Contains(focus, pos[m])) continue;
                MonsterType mt = roster.Monsters[etype[m]];
                if (!ClipLibrary.Fit(clips[mt.AttackClip], tb, downbeat, out double start, out double end, out float speed) || start < tb - 1e-9) continue;
                Push(new Impact { Time = downbeat, Kind = ImpactKind.MonsterAttack, Entity = m, Target = 0, Clip = mt.AttackClip, ClipStart = start, ClipSpeed = speed });
                busyUntil[m] = end;
                lastActionEnd[m] = end;
            }
            Push(new Impact { Time = downbeat, Kind = ImpactKind.WorstRing, Entity = 0, Target = -1, Strength = 1f });
        }

        // The kiting refill's buffer: it starts at, and leaks back toward, a nominal fraction of the target (about the
        // ground ahead the view sweeps in a few beats), and integrates the shortfall on screen.
        const float KiteBufferNominal = 1.3f, KiteBufferLeak = 0.2f, KiteBufferGain = 0.3f, KiteSpawnBand = 6f;
        float kiteBuffer = float.NaN;   // a kiting shot's off-screen buffer (NaN until its first refill)

        void Refill(double tb, ViewGeometry current)
        {
            if (shot.Duel != null || tb >= shot.Final - 1e-6) return;   // a duel's cast is fixed, and refill stops at the final slam
            int total = onScreenCount + offscreenAlive;
            int want = TargetAhead(tb, out int reserve) + reserve;
            bool kite = shot.Path != null;
            if (kite)
            {
                // A kiting hero sweeps its view over whatever waits ahead, so how many wait there sets how many it sees:
                // the off-screen buffer integrates the on-screen shortfall each beat, holding the shot near its target
                // (a placeholder for the formations' refill streams).
                float nominal = KiteBufferNominal * plan.TargetOnScreen;
                if (float.IsNaN(kiteBuffer)) kiteBuffer = nominal;
                kiteBuffer = Math.Max(0, Math.Min(3f * plan.TargetOnScreen,
                    kiteBuffer + KiteBufferGain * (plan.TargetOnScreen - onScreenCount) + KiteBufferLeak * (nominal - kiteBuffer)));
                want = plan.TargetOnScreen + (int)kiteBuffer;
            }
            int need = want - total;
            if (need <= 0 || plan.Clear) return;
            if (!kite) need = Math.Min(need, Math.Max(tuning.RefillMinPerBeat, (int)(want / tuning.RefillDivisor)));   // the buffer paces a kiting shot's
            var view = View(Math.Max(current.Width, plan.ViewWidth));
            Vector2 travel = Vector2.Zero;
            if (kite) travel = PathHeading(HeroRest);
            else if (!goalReached)
            {
                Vector2 d = goal.Center - HeroRest;
                if (d.LengthSquared() > 1e-6f) travel = Vector2.Normalize(d);
                if (nav != null && travel != Vector2.Zero) travel = GoalHeading(HeroRest, out _);
            }
            clumpRetryNow = clumpRetry.Count;
            for (int i = 0; i < need; i++)
            {
                int type = clumpSize > 0 ? ClumpType() : PickType(plan.Mix, false);
                float r = roster.Monsters[type].Radius;
                bool placed = false;
                // A kiting hero leaves the back edge behind: its refills come from the edges it runs toward, from a deeper
                // band, so a wide view reaching past open ground still finds room.
                for (int attempt = 0, attempts = kite ? 24 : nav == null ? 6 : 12; attempt < attempts && !placed; attempt++)
                {
                    float u = clumpSize > 0 ? ClumpU(type, view, tb) : spawnRng.NextFloat();
                    float margin = SpawnMargin + r + spawnRng.Range(0, kite ? KiteSpawnBand : SpawnBand);
                    Vector2 p = view.EdgePoint(focus, u, margin, out Vector2 normal);
                    if (kite ? Vector2.Dot(normal, travel) < -0.2f : travel != Vector2.Zero && spawnRng.NextFloat() > (1f + 2f * Math.Max(0f, Vector2.Dot(normal, travel))) / 3f)
                        continue;
                    if (SpawnBlocked(p, r)) continue;
                    beatSpawns.Add(Spawn(type, p, tb, false));
                    placed = true;
                }
                if (!placed && clumpSize > 0) clumpRetry.Add(type);
            }
        }

        /// <summary>
        /// Lays the section's types' arcs end to end, in roster order: the pattern that repeats round the view's
        /// edge. Each arc is its type's share of the ground the section's refills cover (weight × diameter²), so
        /// every arc fills alike, and the pattern is as long as makes the heaviest type's arc hold
        /// <see cref="clumpSize"/> packed square across the spawn band. The section deals its types afresh.
        /// </summary>
        void ClumpPattern()
        {
            double ground = 0;
            int heaviest = -1;
            for (int t = 0; t < clumpArc.Length; t++)
            {
                int w = MixWeight(t);
                if (w <= 0) continue;
                float d = 2 * roster.Monsters[t].Radius;
                ground += w * d * d;
                if (heaviest < 0 || w > MixWeight(heaviest)) heaviest = t;
            }
            clumpPattern = 0;
            if (heaviest >= 0)
            {
                float dh = 2 * roster.Monsters[heaviest].Radius;
                clumpPattern = (float)(clumpSize * dh * dh / SpawnBand * ground / (MixWeight(heaviest) * dh * dh));
            }
            float start = 0;
            for (int t = 0; t < clumpArc.Length; t++)
            {
                int w = MixWeight(t);
                float d = 2 * roster.Monsters[t].Radius;
                clumpStart[t] = start;
                clumpArc[t] = w > 0 ? (float)(clumpPattern * w * d * d / ground) : 0;
                start += clumpArc[t];
            }
            Array.Clear(clumpOwed, 0, clumpOwed.Length);
            clumpRetry.Clear();
        }

        int MixWeight(int type) => roster.Monsters[type].Rank == MonsterRank.Boss ? 0 : plan.Mix[type] - (type > 0 ? plan.Mix[type - 1] : 0);

        /// <summary>The next refill's type: a retried one, or else the type the mix most owes (smooth weighted round-robin).</summary>
        int ClumpType()
        {
            if (clumpRetryNow > 0)
            {
                clumpRetryNow--;
                int retry = clumpRetry[0];
                clumpRetry.RemoveAt(0);
                return retry;
            }
            int total = 0;
            for (int t = 0; t < clumpOwed.Length; t++) total += MixWeight(t);
            int best = -1;
            for (int t = 0; t < clumpOwed.Length; t++)
            {
                int w = MixWeight(t);
                if (w <= 0) continue;
                clumpOwed[t] += w / (double)total;
                if (best < 0 || clumpOwed[t] > clumpOwed[best]) best = t;
            }
            if (best < 0) return PickType(plan.Mix, false);   // a mix of bosses only: PickType's fodder fallback
            clumpOwed[best] -= 1;
            return best;
        }

        /// <summary>
        /// A perimeter fraction for <see cref="ViewGeometry.EdgePoint"/> in one of <paramref name="type"/>'s arcs:
        /// the section's pattern repeats round the edge as many whole times as fit, stretched to close the loop,
        /// and drifts along it with time.
        /// </summary>
        float ClumpU(int type, ViewGeometry view, double t)
        {
            if (clumpPattern <= 0) return spawnRng.NextFloat();
            float perimeter = view.Perimeter;
            int reps = Math.Max(1, (int)(perimeter / clumpPattern));
            float scale = perimeter / (reps * clumpPattern);
            float along = (spawnRng.Next(reps) * clumpPattern + clumpStart[type] + spawnRng.NextFloat() * clumpArc[type]) * scale;
            return (along + (float)(t * ClumpDriftMps % perimeter)) / perimeter;
        }

        bool SpawnBlocked(Vector2 p, float r)
        {
            if (nav != null && (nav.Distance(p) < r + 0.05f || !heroField.Reached(p))) return true;   // open cells the field toward the hero reaches
            int n = Near().Gather(p, r + SmallRadius + MaxStepReach, ref buf);
            int nb = beatSpawns.Count;
            for (int i = 0; i < n + bigCount + nb; i++)
            {
                int j = i < n ? buf[i] : i < n + bigCount ? big[i - n] : beatSpawns[i - n - bigCount];
                if (i < n && radius[j] > SmallRadius) continue;
                if (state[j] != Alive && state[j] != Dying) continue;
                float need = r + radius[j];
                if (Vector2.Distance(p, rest[j]) < need || Vector2.Distance(p, pos[j]) < need) return true;
                // A neighbour stepping this beat must not pass through the spawn point either.
                if (planSerial[j] == beatSerial && ClosestApproach(p - planFrom[j], p - rest[j]) < need) return true;
            }
            return false;
        }

        /// <summary>The hero, standing at <paramref name="h"/>, blocks a blow or a bolt that came from <paramref name="from"/>.</summary>
        void Parry(int m, double t, Vector2 from, Vector2 h)
        {
            Vector2 dir = from - h;
            dir = dir.LengthSquared() > 1e-8f ? Vector2.Normalize(dir) : new Vector2(0, 1);
            Vector2 at = h + dir * roster.HeroRadius;
            actions.Add(new ActionEvent(0, ActionKind.Parry, t, -1, m, 0.3f));
            effects.Add(new EffectEvent(Catalog.ParrySpark, -1, eseed[m], t, new Vector3(at.X, 1.2f, at.Y), Easing.Yaw(dir), 1f));
            Sound(Catalog.ParrySound, t, 0.3f);
            stats[curSection].Parries++;
        }

        /// <summary>
        /// A caster's attack releases its projectile on the beat, aimed where the hero will stand; it
        /// lands, and the hero parries it, on the first eighth note it can reach at its speed.
        /// </summary>
        void Fire(int m, MonsterType mt, double t, Vector2 p, Vector2 h)
        {
            float speed = roster.Projectiles[mt.Projectile].SpeedMps;
            Vector2 dir = h - p;
            dir = dir.LengthSquared() > 1e-8f ? Vector2.Normalize(dir) : new Vector2(0, 1);
            Vector2 from = p + dir * mt.Radius;
            int gi = grid.FirstAfter(t + Math.Max(0, Vector2.Distance(from, h) - roster.HeroRadius) / speed);
            while (gi < grid.Count && (grid.Points[gi].Flags & GridFlags.Eighth) == 0) gi++;
            if (gi >= grid.Count) return;
            double land = grid.Points[gi].Time;
            Vector2 target = HeroPosAt(land), way = target - from;
            Vector2 to = way.LengthSquared() > 1e-8f ? target - Vector2.Normalize(way) * roster.HeroRadius : target;
            int proj = NewEntity(EntityKind.Projectile, mt.Projectile, t);
            despawnT[proj] = land;
            float yaw = Easing.Yaw(to - from);
            AddSeg(new MotionSegment(proj, t, land, from, to, yaw, yaw, Ease.Linear));
            effects.Add(new EffectEvent(Catalog.BoltLaunch, -1, eseed[proj], t, new Vector3(from.X, 1.3f, from.Y), yaw, 1f));
            Push(new Impact { Time = land, Kind = ImpactKind.MonsterBolt, Entity = proj, Target = m, Point = from });
        }

        // ------------------------------------------------------------------ heap

        void Push(Impact im)
        {
            im.Seq = impactSeq++;
            if (heapCount == heap.Length) Array.Resize(ref heap, heap.Length * 2);
            int i = heapCount++;
            heap[i] = im;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (!Less(heap[i], heap[parent])) break;
                Impact tmp = heap[i]; heap[i] = heap[parent]; heap[parent] = tmp;
                i = parent;
            }
        }

        Impact Pop()
        {
            Impact top = heap[0];
            heap[0] = heap[--heapCount];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, s = i;
                if (l < heapCount && Less(heap[l], heap[s])) s = l;
                if (r < heapCount && Less(heap[r], heap[s])) s = r;
                if (s == i) break;
                Impact tmp = heap[i]; heap[i] = heap[s]; heap[s] = tmp;
                i = s;
            }
            return top;
        }

        static bool Less(in Impact a, in Impact b)
        {
            if (a.Time != b.Time) return a.Time < b.Time;
            // At one instant monsters swing first, then the hero's blows land.
            int ka = a.Kind == ImpactKind.MonsterAttack || a.Kind == ImpactKind.MonsterBolt ? 0 : 1;
            int kb = b.Kind == ImpactKind.MonsterAttack || b.Kind == ImpactKind.MonsterBolt ? 0 : 1;
            if (ka != kb) return ka < kb;
            return a.Seq < b.Seq;
        }

        // ------------------------------------------------------------------ the timeline

        Timeline Build(string name, double duration, SimTickTiming timing)
        {
            var tl = new Timeline { Name = name, Seed = seed, Duration = duration, Map = map, Grid = grid, TickTiming = timing, ViewPitchDeg = viewPitchDeg, ViewYawDeg = shot.YawDeg };
            int e = entityCount;
            tl.Entities = new EntityInfo[e];
            for (int i = 0; i < e; i++)
            {
                double despawn = double.IsPositiveInfinity(despawnT[i]) ? duration : Math.Min(despawnT[i], duration);
                tl.Entities[i] = new EntityInfo(i, etype[i], kind[i], spawnT[i], despawn, eseed[i]);
            }

            // Segments and actions grouped by entity (counting sort keeps append order), then by time.
            tl.SegmentStart = new int[e + 1];
            foreach (MotionSegment s in segs) tl.SegmentStart[s.Entity + 1]++;
            for (int i = 0; i < e; i++) tl.SegmentStart[i + 1] += tl.SegmentStart[i];
            tl.Segments = new MotionSegment[segs.Count];
            var cursor = (int[])tl.SegmentStart.Clone();
            foreach (MotionSegment s in segs) tl.Segments[cursor[s.Entity]++] = s;
            for (int i = 0; i < e; i++) InsertionSortSegments(tl.Segments, tl.SegmentStart[i], tl.SegmentStart[i + 1]);

            tl.ActionStart = new int[e + 1];
            foreach (ActionEvent a in actions) tl.ActionStart[a.Entity + 1]++;
            for (int i = 0; i < e; i++) tl.ActionStart[i + 1] += tl.ActionStart[i];
            tl.Actions = new ActionEvent[actions.Count];
            cursor = (int[])tl.ActionStart.Clone();
            foreach (ActionEvent a in actions) tl.Actions[cursor[a.Entity]++] = a;
            for (int i = 0; i < e; i++) InsertionSortActions(tl.Actions, tl.ActionStart[i], tl.ActionStart[i + 1]);

            var effIdx = new int[effects.Count];
            for (int i = 0; i < effIdx.Length; i++) effIdx[i] = i;
            Array.Sort(effIdx, (x, y) => { int c = effects[x].Time.CompareTo(effects[y].Time); return c != 0 ? c : x.CompareTo(y); });
            tl.Effects = new EffectEvent[effects.Count];
            for (int i = 0; i < effIdx.Length; i++) tl.Effects[i] = effects[effIdx[i]];

            tl.Sounds = SoundAggregator.Aggregate(sounds, map, seed, tuning);
            tl.Framing = framing;
            tl.Breaks = breakEvents.ToArray();   // in time order, as they struck

            tl.TypeNames = new string[roster.Monsters.Length];
            tl.TypeRadii = new float[roster.Monsters.Length];
            for (int i = 0; i < roster.Monsters.Length; i++) { tl.TypeNames[i] = roster.Monsters[i].Name; tl.TypeRadii[i] = roster.Monsters[i].Radius; }
            tl.ProjectileNames = new string[roster.Projectiles.Length];
            for (int i = 0; i < roster.Projectiles.Length; i++) tl.ProjectileNames[i] = roster.Projectiles[i].Name;
            tl.ClipNames = new string[clips.Count];
            for (int i = 0; i < clips.Count; i++) tl.ClipNames[i] = clips[i].Use;
            tl.EffectNames = (string[])Catalog.Effects.Clone();
            tl.SoundNames = Catalog.SoundNames();

            for (int i = 0; i < stats.Length; i++)
            {
                if (statTicks[i] > 0) stats[i].MeanOnScreen /= statTicks[i];
                stats[i].FinalShapeScale = shapeScale;
            }
            tl.Sections = stats;
            return tl;
        }

        static void InsertionSortSegments(MotionSegment[] a, int lo, int hi)
        {
            for (int i = lo + 1; i < hi; i++)
            {
                MotionSegment x = a[i];
                int j = i - 1;
                while (j >= lo && a[j].T0 > x.T0) { a[j + 1] = a[j]; j--; }
                a[j + 1] = x;
            }
        }

        static void InsertionSortActions(ActionEvent[] a, int lo, int hi)
        {
            for (int i = lo + 1; i < hi; i++)
            {
                ActionEvent x = a[i];
                int j = i - 1;
                while (j >= lo && a[j].Impact > x.Impact) { a[j + 1] = a[j]; j--; }
                a[j + 1] = x;
            }
        }
    }
}
