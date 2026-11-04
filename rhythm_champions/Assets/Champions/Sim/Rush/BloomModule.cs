using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>
    /// Boss Rush boss 3, the Bloom (boss-rush.md, The roster: "Rotating spirals"; ps-8z4.10): a carnivorous flower rooted
    /// at the centre of a ring arena. Its signature is <b>the arms</b>: lines of spores from its root to the wall that
    /// turn round it, so the hero circles it in the gaps between them. Each bar's arms are new hazards that land on the
    /// downbeat where the last bar's arms have turned to, so they read as one set of arms changing direction and speed
    /// on the bar. Around them:
    ///
    /// - <b>The bite</b>: whoever stands within <see cref="BiteTriggerM"/> of its root is snapped at, a cone landing on
    ///   the half beat (it punishes hugging the slow inner gaps).
    /// - <b>The spore spiral</b>: rings of shots on consecutive beats, each turned a little further, a bullet-hell spiral.
    /// - <b>The pulse</b>: the dash-through wave, a shockwave ring from its root faster than any walk, on a downbeat.
    ///
    /// The arms are taught alone first (no bite over them); then the moves take turns, the bite joining in. Below half
    /// health it blossoms (a four-beat untouchable transition): four arms, speed changes and reversals every bar, denser
    /// spirals and a two-front pulse.
    ///
    /// It arrives <see cref="ArriveY"/> up the screen from where it was called (the arena's boss start when the hero
    /// stands on its hero start) and draws its roots to its root through its entrance: the arena's boss start, or the
    /// fight's centre without an arena. Everything is driven from <see cref="Tick"/> and <see cref="BossFight.Plan"/>,
    /// so without a reliable beat it plays on the half-second fallback beat.
    /// </summary>
    public sealed class BloomModule : BossModule
    {
        public const string ModuleId = "bloom";

        /// <summary>Its arrival, up the screen from the fight's centre (the bloom arena's hero start is this far below its boss start).</summary>
        public const double ArriveY = 8;

        /// <summary>The arms: from the root to past the wall, their half-width, damage.</summary>
        public const double ArmLengthM = 18, ArmReachM = 0.45, ArmDamage = 12;
        /// <summary>A speed-1 arm's turn in a bar, radians, and the fastest any arm turns (its tip at the wall under the walk).</summary>
        public const double TurnPerBar = 0.4, MaxSpinRadPerS = 0.34;

        /// <summary>The bite: who's this close is snapped at; its cone's reach and half-arc; its damage; bars between bites.</summary>
        public const double BiteTriggerM = 5.5, BiteRangeM = 6.5, BiteHalfArc = 0.85, BiteDamage = 18;
        public const int BiteEveryBars = 2;

        /// <summary>The spiral's shots.</summary>
        public const double ShotSpeedMps = 6.5, ShotRadiusM = 0.4, ShotRangeM = 17, ShotDamage = 8;

        /// <summary>The pulse: its reach, speed (faster than any walk), thickness and damage.</summary>
        public const double PulseRadiusM = 18, PulseSpeedMps = 13, PulseThickM = 0.5, PulseDamage = 15;

        enum Kind : byte { Arms, Spiral, Pulse }

        /// <summary>One move of a phase's round: its kind, the arms' count and each bar's speed (signed: the turn's direction), the spiral's volleys and shots, the pulse's fronts.</summary>
        readonly struct Step
        {
            public readonly Kind Kind;
            public readonly int Count;
            public readonly double[] Bars;
            public readonly int Volleys, Fronts;

            Step(Kind kind, int count = 0, double[] bars = null, int volleys = 0, int fronts = 1)
            {
                Kind = kind;
                Count = count;
                Bars = bars;
                Volleys = volleys;
                Fronts = fronts;
            }

            public static Step Arms(int count, params double[] bars) => new Step(Kind.Arms, count, bars);
            public static Step Spiral(int volleys, int shots) => new Step(Kind.Spiral, shots, volleys: volleys);
            public static Step Pulse(int fronts) => new Step(Kind.Pulse, fronts: fronts);
        }

        /// <summary>
        /// Each phase's round, played in order and then looped from <see cref="LoopFrom"/>. Phase 1's first move teaches the
        /// arms alone: three, one way, steady.
        /// </summary>
        static readonly Step[][] Rounds =
        {
            new[]
            {
                Step.Arms(3, 1, 1, 1),
                Step.Spiral(3, 8),
                Step.Arms(3, 1, -1, 1),
                Step.Pulse(1),
                Step.Spiral(4, 8),
                Step.Arms(3, -1, -1.5, 1),
                Step.Pulse(1),
            },
            new[]
            {
                Step.Arms(4, 1, 1.5, -1, -1.5),
                Step.Spiral(4, 10),
                Step.Pulse(2),
                Step.Arms(4, -1.5, 1, 1.5, -1),
                Step.Spiral(5, 10),
                Step.Pulse(2),
            },
        };

        static readonly int[] LoopFrom = { 1, 0 };
        /// <summary>The beats of rest after each move, by phase.</summary>
        static readonly int[] RestBeats = { 1, 0 };

        double rootX, rootY;
        int round, step = -1, bar, lastAttack = -1, bite = -1;
        double nextMoveAt, nextBiteAt, armsLand, armsAngle, armsSpin;
        bool moving;

        public override string Id => ModuleId;
        public override BodySpec[] Bodies => new[] { new BodySpec(ModuleId, 0, ArriveY) };
        public override double[] PhasesBelow => new[] { 0.5 };
        public override int TransitionBeats => 4;

        /// <summary>Its root (where it stands rooted).</summary>
        public double RootX => rootX;
        public double RootY => rootY;

        protected internal override void Begin(BossFight f, double t)
        {
            var arena = f.Sim.Arena;
            bool rooted = arena != null && arena.Solid;
            rootX = rooted ? arena.BossStartX : f.CentreX;
            rootY = rooted ? arena.BossStartY : f.CentreY;
            ref readonly var b = ref f.Bodies[0];
            // Through its entrance it draws its roots to its root (it stays put when it arrived there).
            double land = f.Plan(EntranceBeats).Land;
            if (Math.Abs(b.X - rootX) + Math.Abs(b.Y - rootY) > 1e-6) f.MoveBody(0, rootX, rootY, t, land, eased: true);
            nextMoveAt = t;
            nextBiteAt = t;
        }

        protected internal override void PhaseBegins(BossFight f, int phase, double t, double until)
        {
            round = Math.Min(phase - 1, Rounds.Length - 1);
            step = -1;
            moving = false;
            lastAttack = bite = -1;
            nextMoveAt = nextBiteAt = until;
        }

        double BarS(BossFight f) => f.Sim.Beats.Rules.BeatsPerBar * f.BeatS;

        protected internal override void Tick(BossFight f, double end)
        {
            var steps = Rounds[round];
            if (moving)
            {
                var s = steps[step];
                if (s.Kind == Kind.Arms && bar < s.Bars.Length && end >= armsLand - 1e-9) Arms(f, s);
                else if ((s.Kind != Kind.Arms || bar >= s.Bars.Length) && !f.Pending(lastAttack))
                {
                    moving = false;
                    nextMoveAt = end + RestBeats[round] * f.BeatS;
                }
            }
            else if (end >= nextMoveAt - 1e-9)
            {
                step = step + 1 < steps.Length ? step + 1 : LoopFrom[round];
                Play(f, steps[step]);
            }
            Bite(f, end);
        }

        void Play(BossFight f, in Step s)
        {
            moving = true;
            switch (s.Kind)
            {
                case Kind.Arms:
                    bar = 0;
                    armsSpin = 0;
                    armsLand = double.NaN;
                    armsAngle = f.Random.NextDouble() * 2 * Math.PI;
                    Arms(f, s);
                    break;
                case Kind.Spiral:
                {
                    // Volleys on consecutive beats, each turned a quarter of the gap further: the shots wind outward in a spiral.
                    double turn = f.Random.NextDouble() * 2 * Math.PI, gap = 2 * Math.PI / s.Count, dir = f.Random.NextDouble() < 0.5 ? 1 : -1;
                    int attack = f.NextAttack();
                    for (int v = 0; v < s.Volleys; v++)
                        BossKit.Volley(f, 0, s.Count, ShotSpeedMps, ShotRadiusM, ShotRangeM, ShotDamage, f.MinTelegraphBeats + v,
                            turn + dir * v * gap / 4, attack: attack, move: v == 0 ? BossMove.Fireballs : BossMove.Hazard);
                    lastAttack = attack;
                    break;
                }
                case Kind.Pulse:
                    lastAttack = BossKit.Wave(f, 0, rootX, rootY, PulseRadiusM, PulseSpeedMps, PulseThickM, PulseDamage, f.MinTelegraphBeats + 1,
                        fronts: s.Fronts, move: BossMove.Stomp);
                    break;
            }
        }

        /// <summary>The next bar's arms, landing on the downbeat where the last bar's have turned to.</summary>
        void Arms(BossFight f, in Step s)
        {
            var tel = f.Plan(f.MinTelegraphBeats, Landing.Downbeat);
            if (!double.IsNaN(armsLand)) armsAngle += armsSpin * (tel.Land - armsLand);
            double barS = BarS(f);
            armsSpin = Math.Sign(s.Bars[bar]) * Math.Min(Math.Abs(s.Bars[bar]) * TurnPerBar / barS, MaxSpinRadPerS);
            int attack = f.NextAttack();
            for (int k = 0; k < s.Count; k++)
            {
                double a = armsAngle + 2 * Math.PI * k / s.Count;
                f.Add(new Hazard
                {
                    Attack = attack, Body = 0, Part = k, Move = BossMove.Breath, Telegraph = tel,
                    Shape = HitShape.Path(rootX, rootY, rootX + SimMath.Cos(a) * ArmLengthM, rootY + SimMath.Sin(a) * ArmLengthM, ArmReachM),
                    Damage = ArmDamage, ActiveS = barS, SpinRadPerS = armsSpin, Flags = HazardFlags.Signature,
                });
            }
            armsLand = tel.Land;
            lastAttack = attack;
            bar++;
        }

        /// <summary>Snaps at a hero standing close: never over the arms' first lesson or the pulse, at most every <see cref="BiteEveryBars"/> bars.</summary>
        void Bite(BossFight f, double end)
        {
            if (end < nextBiteAt - 1e-9 || (bite >= 0 && f.Pending(bite))) return;
            if (step < 0 || (f.Phase == 1 && step == 0) || Rounds[round][step].Kind == Kind.Pulse) return;
            f.HeroAt(end, out double hx, out double hy);
            double dx = hx - rootX, dy = hy - rootY, d = Math.Sqrt(dx * dx + dy * dy);
            if (d > BiteTriggerM) return;
            double ax = d > 1e-9 ? dx / d : 0, ay = d > 1e-9 ? dy / d : 1;
            f.Face(0, hx, hy);
            var tel = f.Plan(f.MinTelegraphBeats, Landing.HalfBeat);
            bite = f.NextAttack();
            f.Add(new Hazard
            {
                Attack = bite, Body = 0, Move = BossMove.Smash, Telegraph = tel,
                Shape = HitShape.Cone(rootX, rootY, ax, ay, BiteRangeM, BiteHalfArc), Damage = BiteDamage,
            });
            nextBiteAt = tel.Land + BiteEveryBars * BarS(f);
        }

        /// <summary>Each burst flings its seeds along a spiral arm from near its root, turned further each time.</summary>
        protected internal override void ShedSpots(BossFight f, int burst, double t, List<GemSpot> into)
        {
            const int n = 6;
            double from = burst * 1.1;
            for (int k = 0; k < n; k++)
            {
                double r = 4.5 + 1.5 * k, a = from + 0.35 * k;
                into.Add(new GemSpot(rootX + r * SimMath.Cos(a), rootY + r * SimMath.Sin(a), 1.0 / n));
            }
        }
    }
}
