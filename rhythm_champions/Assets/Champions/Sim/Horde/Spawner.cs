using System;

namespace Champions.Sim
{
    /// <summary>
    /// Level 1's spawner (design.md, "Spawning" and "Toughness"; <see cref="LevelRules"/>), run each tick
    /// while the world plays:
    ///
    /// - <b>Ramp</b>: it keeps <see cref="Target"/> mobs alive, from <see cref="LevelRules.Ramp"/> over the run
    ///   clock (150 at the start, 600 by minute 12), spawning at most <see cref="LevelRules.PerS"/> a second from
    ///   an ellipse just off-screen round the hero, each pick from the mix of the types joined by then.
    /// - <b>Surges</b>: the analyzer's loudness level, smoothed over <see cref="LevelRules.SurgeSmoothS"/>, moves
    ///   the target toward the cap in loud stretches and down in quiet ones. With no music (no fresh tick, or
    ///   silence), it is the ramp alone. Above the target, mobs off-screen (outside the spawn ellipse) go,
    ///   at the spawning rate, so a quiet stretch thins the horde where nobody sees it.
    /// - <b>Formed waves</b>: a section change (strong enough, and long enough after the last wave) brings a
    ///   ring, a line or a swarm of bats on the next downbeat (at once with no beat).
    /// - <b>Never more than the cap</b> (1,000) alive, waves and groups included.
    /// - <b>Toughness</b>: a mob spawns with its type's toughness times <see cref="LevelRules.Toughness"/> at
    ///   the run clock.
    /// - <b>Bosses</b> stop it and resume it (<see cref="Stop"/>, <see cref="Resume"/>): after the Ogre with the
    ///   second-half mix, denser.
    /// </summary>
    public sealed class Spawner
    {
        readonly Simulation sim;
        readonly Horde horde;
        readonly EnemyRules cast;
        readonly LevelRules rules;
        double owed, thin;
        int thinCursor;
        int changesSeen;
        double lastWave = double.NegativeInfinity;
        bool waveDue;
        double waveAt;

        internal Spawner(Simulation sim, Horde horde, EnemyRules cast, LevelRules rules)
        {
            this.sim = sim;
            this.horde = horde;
            this.cast = cast;
            this.rules = rules;
        }

        public LevelRules Rules => rules;

        /// <summary>Stopped for a boss.</summary>
        public bool Stopped { get; private set; }

        /// <summary>The second half's mix and density (after the Ogre).</summary>
        public bool SecondHalf { get; private set; }

        /// <summary>The mobs alive it aims at now.</summary>
        public double Target { get; private set; }

        /// <summary>How loud, and how quiet, the music has been lately: 0 to 1 each.</summary>
        public double LoudShare { get; private set; }
        public double QuietShare { get; private set; }

        /// <summary>Formed waves this run.</summary>
        public int Waves { get; private set; }

        /// <summary>The bosses' hook: no more spawns (a planned wave is dropped) until <see cref="Resume"/>.</summary>
        internal void Stop()
        {
            Stopped = true;
            waveDue = false;
            owed = 0;
        }

        /// <summary>The bosses' hook: spawning again, with the second half's mix and density when <paramref name="secondHalf"/>.</summary>
        internal void Resume(bool secondHalf)
        {
            Stopped = false;
            SecondHalf |= secondHalf;
        }

        /// <summary>
        /// The target at run clock <paramref name="clock"/> with the music <paramref name="loud"/> and
        /// <paramref name="quiet"/> that share of late: the ramp (denser in the second half), moved toward the cap
        /// by loudness and down by quiet, never above the cap.
        /// </summary>
        public double TargetAt(double clock, double loud, double quiet)
        {
            double target = rules.Ramp.At(clock) * (SecondHalf ? rules.SecondHalfDensity : 1);
            target += (rules.Cap - target) * rules.SurgeStrength * loud;
            target *= 1 - (1 - rules.QuietScale) * quiet;
            return Math.Min(target, rules.Cap);
        }

        /// <summary>The health a mob of cast type <paramref name="kind"/> spawns with at run clock <paramref name="clock"/>.</summary>
        public double HealthAt(int kind, double clock) => cast.Cast[kind].Toughness * rules.Toughness.At(clock);

        internal void Tick(double end)
        {
            double dt = Simulation.TickSeconds;
            Listen(end, dt);
            Watch(end);
            if (Stopped) return;
            double clock = sim.State.RunClock;
            Target = TargetAt(clock, LoudShare, QuietShare);
            if (waveDue && waveAt <= end + 1e-9)
            {
                waveDue = false;
                Wave(end, clock);
            }
            double room = Math.Min(Target, rules.Cap) - horde.Alive;
            if (room < 1)
            {
                owed = 0;
                if (room <= -1) Thin(end, dt);
                return;
            }
            thin = 0;
            owed = Math.Min(owed + rules.PerS * dt, Math.Max(1, rules.PerS * 0.25));
            while (owed >= 1 && horde.Alive < Math.Min(Target, rules.Cap))
                owed -= Pick(end, clock);
        }

        /// <summary>The loud and quiet shares follow the latest tick's level, when it is fresh and not silent.</summary>
        void Listen(double end, double dt)
        {
            var ticks = sim.Stream.Ticks;
            int i = BeatServices.LastAtOrBefore(ticks, end);
            bool loud = false, quiet = false;
            if (i >= 0 && end - ticks[i].T <= BeatServices.ClearTickAge + 1e-9 && ticks[i].V1 >= sim.Beats.Rules.SilenceEnergy)
            {
                var level = (LoudnessLevel)(int)ticks[i].V2;
                loud = level == LoudnessLevel.Loud;
                quiet = level == LoudnessLevel.Quiet;
            }
            double a = Math.Min(1, dt / rules.SurgeSmoothS);
            LoudShare += ((loud ? 1 : 0) - LoudShare) * a;
            QuietShare += ((quiet ? 1 : 0) - QuietShare) * a;
        }

        /// <summary>New section changes: a strong one, long enough after the last wave, plans a wave on the next downbeat.</summary>
        void Watch(double end)
        {
            int count = sim.Stream.Count(EntryKind.Change);
            for (; changesSeen < count; changesSeen++)
            {
                if (Stopped || waveDue) continue;
                var change = sim.Stream.Get(EntryKind.Change, changesSeen);
                double clock = sim.State.RunClock;
                if (change.V0 < rules.WaveMinStrength || clock - lastWave < rules.WaveMinGapS) continue;
                double at = sim.State.Beat == Reliability.Reliable ? sim.Beats.NextDownbeat(end, out _) : double.NaN;
                waveAt = double.IsNaN(at) ? end : at;
                waveDue = true;
                lastWave = clock;
            }
        }

        /// <summary>Above the target: mobs outside the spawn ellipse go, at up to the spawning rate, from where the last pass stopped.</summary>
        void Thin(double end, double dt)
        {
            thin = Math.Min(thin + rules.PerS * dt, Math.Max(1, rules.PerS * 0.25));
            sim.HeroWas(end, out double hx, out double hy);
            var enemies = horde.Enemies;
            int n = enemies.Count;
            for (int seen = 0; seen < n && thin >= 1 && horde.Alive > Target; seen++)
            {
                int id = thinCursor = (thinCursor + 1) % n;
                if (!horde.Owns(id)) continue;
                ref readonly var e = ref enemies[id];
                double ex = (e.X - hx) / rules.RingXM, ey = (e.Y - hy) / rules.RingYM;
                if (ex * ex + ey * ey <= 1 || end < horde.Mobs[id].HeadUntil) continue;
                horde.Despawn(id, end);
                thin -= 1;
            }
        }

        // ---- Spawning ----

        /// <summary>Spawns one pick of the mix (a whole group) at a ring point; returns how many.</summary>
        int Pick(double t, double clock)
        {
            int kind = Draw(SecondHalf ? rules.SecondHalfMix : rules.Mix, clock, false);
            if (kind < 0) return 1;
            var type = cast.Cast[kind];
            RingPoint(t, type.RadiusM, out double x, out double y);
            int n = Math.Min(type.Group, rules.Cap - horde.Alive);
            double health = HealthAt(kind, clock);
            for (int k = 0; k < n; k++)
            {
                double ox = 0, oy = 0;
                if (k > 0) Disc(1.5, out ox, out oy);
                horde.Spawn(kind, x + ox, y + oy, health);
            }
            return Math.Max(1, n);
        }

        /// <summary>A type drawn by weight from the entries joined by <paramref name="clock"/> (fodder only: no elites, swarmers or ranged); -1 with none.</summary>
        int Draw(MixEntry[] mix, double clock, bool fodderOnly)
        {
            double total = 0;
            foreach (var m in mix)
                if (Joined(m, clock, fodderOnly)) total += m.Weight;
            if (!(total > 0)) return -1;
            double pick = sim.Random.NextDouble() * total;
            int last = -1;
            foreach (var m in mix)
            {
                if (!Joined(m, clock, fodderOnly)) continue;
                last = m.Type;
                pick -= m.Weight;
                if (pick < 0) return m.Type;
            }
            return last;
        }

        bool Joined(in MixEntry m, double clock, bool fodderOnly) =>
            m.Weight > 0 && (SecondHalf || m.FromS <= clock + 1e-9) && (!fodderOnly || cast.Cast[m.Type].Role == EnemyRole.Fodder);

        /// <summary>A point on the spawn ellipse round the hero, clear of structures when it can find one.</summary>
        void RingPoint(double t, double radius, out double x, out double y)
        {
            sim.HeroWas(t, out double hx, out double hy);
            x = y = 0;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                double a = sim.Random.NextDouble() * 2 * Math.PI, j = sim.Random.NextDouble() * rules.RingJitterM;
                x = hx + Math.Cos(a) * (rules.RingXM + j);
                y = hy + Math.Sin(a) * (rules.RingYM + j);
                if (Clear(x, y, radius, t)) return;
            }
        }

        readonly StructList<int> blockers = new StructList<int>(8);

        bool Clear(double x, double y, double radius, double t)
        {
            var structures = sim.Structures;
            if (structures == null) return true;
            blockers.Clear();
            structures.Reach(HitShape.Circle(x, y, radius), t, blockers);
            return blockers.Count == 0;
        }

        /// <summary>A uniform random point in a disc of <paramref name="radius"/>.</summary>
        void Disc(double radius, out double x, out double y)
        {
            double a = sim.Random.NextDouble() * 2 * Math.PI, r = radius * Math.Sqrt(sim.Random.NextDouble());
            x = Math.Cos(a) * r;
            y = Math.Sin(a) * r;
        }

        // ---- Formed waves ----

        /// <summary>A formed wave at <paramref name="t"/>: a ring, a line or (once bats have joined) a swarm, up to the cap.</summary>
        void Wave(double t, double clock)
        {
            var mix = SecondHalf ? rules.SecondHalfMix : rules.Mix;
            int swarmer = -1;
            foreach (var m in mix)
                if (Joined(m, clock, false) && cast.Cast[m.Type].Role == EnemyRole.Swarmer) swarmer = m.Type;
            var kind = (WaveKind)sim.Random.Below(swarmer >= 0 ? 3 : 2);
            int type = kind == WaveKind.Swarm ? swarmer : Draw(mix, clock, true);
            if (type < 0) return;
            int count = kind == WaveKind.Ring ? rules.RingCount : kind == WaveKind.Line ? rules.LineCount : rules.SwarmCount;
            count = Math.Min(count, rules.Cap - horde.Alive);
            if (count <= 0) return;
            sim.HeroWas(t, out double hx, out double hy);
            double health = HealthAt(type, clock), speed = cast.Cast[type].SpeedMps;
            double a0 = sim.Random.NextDouble() * 2 * Math.PI, dx = Math.Cos(a0), dy = Math.Sin(a0);
            double cx = hx, cy = hy;
            switch (kind)
            {
                case WaveKind.Ring:
                    for (int k = 0; k < count; k++)
                    {
                        double a = a0 + 2 * Math.PI * k / count;
                        horde.Spawn(type, hx + Math.Cos(a) * rules.RingRadiusM, hy + Math.Sin(a) * rules.RingRadiusM, health);
                    }
                    break;
                case WaveKind.Line:
                {
                    // Broadside: a line across (dx, dy), starting behind the hero and marching through.
                    cx = hx - dx * rules.LineOffsetM;
                    cy = hy - dy * rules.LineOffsetM;
                    double until = t + 2 * rules.LineOffsetM / Math.Max(0.1, speed);
                    for (int k = 0; k < count; k++)
                    {
                        double along = ((k + 0.5) / count - 0.5) * rules.LineLengthM;
                        int id = horde.Spawn(type, cx - dy * along, cy + dx * along, health);
                        ref var m = ref horde.Edit(id);
                        m.HeadX = dx;
                        m.HeadY = dy;
                        m.HeadUntil = until;
                    }
                    break;
                }
                default:
                    cx = hx + dx * rules.RingXM;
                    cy = hy + dy * rules.RingYM;
                    for (int k = 0; k < count; k++)
                    {
                        Disc(rules.SwarmRadiusM, out double ox, out double oy);
                        horde.Spawn(type, cx + ox, cy + oy, health);
                    }
                    break;
            }
            Waves++;
            sim.Emit(SimEvent.HordeWave(t, kind, count, type, cx, cy, dx, dy));
        }
    }
}
