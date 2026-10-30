using System;
using System.Collections.Generic;
using PS.Choreography;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// How heavy each of the hero's blows feels (camera-and-sound.md, Blow weight; epic ps-5pd, Decision 5):
    /// the kills a blow makes give it a weight from 0 (at most <see cref="Amounts.CrispKills"/>: a crisp
    /// slash) to 1 (<see cref="Amounts.HeavyKills"/> or more: a hammer), on a log scale, and the weight
    /// drives the camera's punch and shake (<see cref="CameraFeel"/>), the hit-stop (<see cref="WorldTime"/>),
    /// the trail's, shock's and sparks' size (EffectDriver) and the light's knee (EffectLights).
    /// The amounts are an intensity overlay's <c>feel</c> object; a normal run has none, and feels as before.
    /// Like the rest of the client's feel it is a function of song time, so fixed-time captures reproduce it.
    /// </summary>
    public sealed class BlowFeel
    {
        /// <summary>An intensity overlay's <c>feel</c> object (choreography.md, Intensity overlays).</summary>
        public sealed class Amounts
        {
            /// <summary>A blow's weight is 0 at or below <see cref="CrispKills"/> kills and 1 at <see cref="HeavyKills"/>, log-scaled between.</summary>
            public int CrispKills = 8, HeavyKills = 60;
            /// <summary>A blow's punch: the view's zoom-in at weight 1, on top of the accents' 2% (CameraFeel).</summary>
            public float Punch;
            /// <summary>The shake its beat adds at weight 1 (m), and the shake's cap (m), raised from 0.25 (inside the 2 m spawn margin).</summary>
            public float Shake, ShakeCap = CameraFeel.ShakeCap;
            /// <summary>The hit-stop: the horde and the hero hold this long after a weight-1 blow (s), then catch up over <see cref="HitStopCatchUp"/>.</summary>
            public float HitStop, HitStopCatchUp = 0.15f;
            /// <summary>Trails and shocks grow by 1 + <see cref="Trail"/> × weight; hit sparks by <see cref="Spark"/> (and the blow's weight on top); death bursts by <see cref="DeathBurst"/>.</summary>
            public float Trail, Spark = 1f, DeathBurst = 1f;
            /// <summary>Effect lights' knee (EffectLights.Knee): a cluster's intensity saturates toward it, so a mass kill is one wave of light, not a white-out. 0: no knee.</summary>
            public float LightKnee;
            /// <summary>The crystal shatter's cap on live shard sets (CrystalShatter.Cap); deaths past it break by the shader's own shatter. 0: no cap.</summary>
            public int ShardCap;
            /// <summary>The hero's drawn size, so it reads at an intensity's wider views; its gait stretches with it, so its feet don't slide.</summary>
            public float HeroScale = 1f;

            /// <summary>The amounts in <paramref name="feel"/>, or null if it isn't an object (a normal run).</summary>
            public static Amounts Parse(JsonValue feel)
            {
                if (feel == null || feel.Type != JsonType.Object) return null;
                var d = new Amounts();
                var a = new Amounts
                {
                    CrispKills = feel["crispKills"].AsInt(d.CrispKills),
                    HeavyKills = feel["heavyKills"].AsInt(d.HeavyKills),
                    Punch = feel["punch"].AsFloat(d.Punch),
                    Shake = feel["shake"].AsFloat(d.Shake),
                    ShakeCap = feel["shakeCap"].AsFloat(d.ShakeCap),
                    HitStop = feel["hitStop"].AsFloat(d.HitStop),
                    HitStopCatchUp = feel["hitStopCatchUp"].AsFloat(d.HitStopCatchUp),
                    Trail = feel["trail"].AsFloat(d.Trail),
                    Spark = feel["spark"].AsFloat(d.Spark),
                    DeathBurst = feel["deathBurst"].AsFloat(d.DeathBurst),
                    LightKnee = feel["lightKnee"].AsFloat(d.LightKnee),
                    ShardCap = feel["shardCap"].AsInt(d.ShardCap),
                    HeroScale = feel["heroScale"].AsFloat(d.HeroScale),
                };
                if (a.CrispKills < 0 || a.HeavyKills <= a.CrispKills) throw new FormatException("feel: heavyKills must be above crispKills (and crispKills not negative)");
                if (a.HitStop < 0 || !(a.HitStopCatchUp > 0) || a.HitStop > a.HitStopCatchUp)
                    throw new FormatException("feel: hitStop must be 0 to hitStopCatchUp, so the catch-up runs at most twice as fast");
                if (a.Punch < 0 || a.Shake < 0 || a.ShakeCap < 0 || a.Trail < 0 || !(a.Spark > 0) || !(a.DeathBurst > 0) || a.LightKnee < 0 || a.ShardCap < 0 || !(a.HeroScale > 0))
                    throw new FormatException("feel: amounts must not be negative");
                return a;
            }

            /// <summary>The amounts in an intensity overlay's JSON (null for normal, or an overlay without a <c>feel</c>).</summary>
            public static Amounts FromOverlay(string overlayJson) =>
                overlayJson == null ? null : Parse(JsonValue.Parse(overlayJson)["feel"]);
        }

        public readonly Amounts Feel;
        readonly double[] _at;     // the hero's blows' impacts, sorted
        readonly int[] _kills;     // per blow
        readonly float[] _weight;  // per blow

        public BlowFeel(Timeline tl, Amounts feel)
        {
            Feel = feel ?? throw new ArgumentNullException(nameof(feel));
            // The deaths a blow makes land on its impact (Sim.Strike), so they share its time.
            var deaths = new Dictionary<double, int>();
            var blows = new List<double>();
            foreach (var a in tl.Actions)
            {
                if (a.Kind == ActionKind.Death) deaths[a.Impact] = deaths.TryGetValue(a.Impact, out int n) ? n + 1 : 1;
                else if (a.Kind == ActionKind.Attack && a.Entity == 0) blows.Add(a.Impact);
            }
            blows.Sort();
            _at = blows.ToArray();
            _kills = new int[_at.Length];
            _weight = new float[_at.Length];
            for (int i = 0; i < _at.Length; i++)
            {
                _kills[i] = deaths.TryGetValue(_at[i], out int k) ? k : 0;
                _weight[i] = Weight(_kills[i], feel);
            }
        }

        /// <summary>A blow's weight for its kills: 0 to 1, log-scaled between the crisp and heavy counts.</summary>
        public static float Weight(int kills, Amounts feel)
        {
            double lo = Math.Log(1 + feel.CrispKills), hi = Math.Log(1 + feel.HeavyKills);
            return (float)Math.Max(0, Math.Min(1, (Math.Log(1 + kills) - lo) / (hi - lo)));
        }

        public int Count => _at.Length;
        public double Time(int i) => _at[i];
        public int Kills(int i) => _kills[i];
        public float WeightOf(int i) => _weight[i];

        /// <summary>The weight of the hero's blow landing at exactly <paramref name="impact"/>, or -1 if none does.</summary>
        public float WeightAt(double impact)
        {
            int i = Array.BinarySearch(_at, impact);
            return i >= 0 ? _weight[i] : -1f;
        }

        /// <summary>
        /// The world's time at song time t, for the horde's and the hero's poses: from each blow's impact
        /// it holds for <see cref="Amounts.HitStop"/> × the blow's weight, then catches up linearly by the
        /// end of <see cref="Amounts.HitStopCatchUp"/> after the hold. The impact itself lands on time, the
        /// world never runs backwards (the lag's slope is at most 1), and it is back on the beat within
        /// hold + catch-up, under the eighth's 0.19 s at 155 BPM. The music, the camera and the effects keep
        /// song time, so the punch, sparks and light burst play over the frozen blow.
        /// </summary>
        public double WorldTime(double t)
        {
            if (Feel.HitStop <= 0) return t;
            double window = Feel.HitStop + Feel.HitStopCatchUp;
            int i = Array.BinarySearch(_at, t);
            if (i < 0) i = ~i - 1;
            double lag = 0;
            for (; i >= 0 && t - _at[i] < window; i--)
            {
                double hold = Feel.HitStop * _weight[i];
                if (hold <= 0) continue;
                double u = t - _at[i];
                double l = u < hold ? u : hold * Math.Max(0, 1 - (u - hold) / Feel.HitStopCatchUp);
                lag = Math.Max(lag, l);
            }
            return t - lag;
        }
    }
}
