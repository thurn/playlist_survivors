using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The structure breaks as the client feels them (environment.md, Breaks on screen; epic ps-suy, Decision 8): the
    /// timeline's <see cref="BreakEvent"/>s grouped into moments, the pieces one bolt takes out at once, each sized by
    /// the wall it opens. From the sizes come the moment's post flash (PostFeel) and camera shake (CameraFeel), its
    /// effect bursts (EffectDriver) and its sound, all known at load, so the live player and fixed-time captures agree.
    /// A run without `--structure-breaks` has no moments and feels exactly as before. A trailer shot's crystal formation
    /// (trailer.md, "Crystal formations") is a moment of its own, the hero's blow smashing it: the heaviest of all, in the
    /// formations' own gem and crystal's sound whatever the structure style.
    /// </summary>
    public sealed class BreakMoments
    {
        /// <summary>The bolt's height above the ground (EffectDriver's projectile, DemoMain's stand-in): where it strikes.</summary>
        public const float BoltHeightM = 1.2f;

        public struct Piece
        {
            public int Placement;
            public Vector3 Base;     // the placement's position on the ground (with its lift)
            public float WidthM, HeightM;
        }

        public struct Moment
        {
            public double Time;
            public string Target;
            public Piece[] Pieces;
            public Vector3 Hit;      // where the bolt strikes, at its height
            public Vector3 Blow;     // its direction on the ground (unit)
            /// <summary>The square root of the wall's area the moment opens (m): a 4 × 3 m wall is about 3.5.</summary>
            public float SizeM;
            /// <summary>A crystal formation's (the hero's blow smashing it), not a wall's.</summary>
            public bool Crystal;
        }

        public readonly Moment[] Moments;
        public int Count => Moments.Length;

        BreakMoments(Moment[] moments) { Moments = moments; }

        public static readonly BreakMoments None = new BreakMoments(Array.Empty<Moment>());

        /// <summary>
        /// The moments of <paramref name="tl"/>'s breaks, on the placements of <paramref name="ground"/> (the layout the
        /// timeline ran on) and its crystal formations: the events that share a time, target and bolt are one moment.
        /// </summary>
        public static BreakMoments Of(Timeline tl, GroundLayout ground)
        {
            if (tl.Breaks.Length == 0) return None;
            var list = ground.Placements ?? throw new InvalidOperationException("BreakMoments: breaks on a layout without placements");
            var moments = new List<Moment>();
            var pieces = new List<Piece>();
            for (int i = 0, j; i < tl.Breaks.Length; i = j)
            {
                var first = tl.Breaks[i];
                pieces.Clear();
                float area = 0;
                int crystal = Array.FindIndex(tl.Crystals, c => c.Placement == first.Placement);
                for (j = i; j < tl.Breaks.Length && tl.Breaks[j].Time == first.Time && tl.Breaks[j].Projectile == first.Projectile && tl.Breaks[j].Target == first.Target; j++)
                {
                    if (crystal >= 0)
                    {
                        var c = tl.Crystals[crystal];
                        pieces.Add(new Piece { Placement = c.Placement, Base = new Vector3(c.Position.X, 0f, c.Position.Y), WidthM = 2f * c.RadiusM, HeightM = c.HeightM });
                        area += 2f * c.RadiusM * c.HeightM;
                        continue;
                    }
                    var p = list.Items[tl.Breaks[j].Placement];
                    var f = list.Props[p.Prop];
                    f.AlongX(out float minX, out float maxX, out _);
                    var piece = new Piece
                    {
                        Placement = tl.Breaks[j].Placement, Base = new Vector3(p.Position.X, p.Lift, p.Position.Y),
                        WidthM = Mathf.Max(0.5f, (maxX - minX) * p.Scale), HeightM = Mathf.Max(0.5f, f.HeightM * p.Scale),
                    };
                    pieces.Add(piece);
                    area += piece.WidthM * piece.HeightM;
                }
                var dir = first.Direction;
                moments.Add(new Moment
                {
                    Time = first.Time, Target = first.Target, Pieces = pieces.ToArray(),
                    Hit = new Vector3(first.Point.X, BoltHeightM, first.Point.Y), Blow = new Vector3(dir.X, 0, dir.Y),
                    SizeM = Mathf.Sqrt(area), Crystal = crystal >= 0,
                });
            }
            return new BreakMoments(moments.ToArray());
        }

        /// <summary>A moment's post flash (PostFeel's strength, as an accent's): a large wall flashes as a strong accent does.</summary>
        public static float FlashOf(float sizeM) => Mathf.Clamp(0.3f + 0.12f * sizeM, 0.5f, MaxFlash);

        /// <summary>A moment's camera shake (m, decaying over its beat), above the beat's own cap: a wall coming down is heavier than any hit.</summary>
        public static float ShakeOf(float sizeM) => Mathf.Clamp(0.05f + 0.04f * sizeM, 0.12f, MaxShake);

        /// <summary>The largest wall's flash and shake, which a crystal formation always takes: the cut's heaviest moments.</summary>
        public const float MaxFlash = 1.1f, MaxShake = 0.4f;

        public IEnumerable<(double time, float strength)> Flashes()
        {
            foreach (var m in Moments) yield return (m.Time, m.Crystal ? MaxFlash : FlashOf(m.SizeM));
        }

        public IEnumerable<(double time, float amplitude)> Shakes()
        {
            foreach (var m in Moments) yield return (m.Time, m.Crystal ? MaxShake : ShakeOf(m.SizeM));
        }

        /// <summary>A client effect at a moment (EffectDriver.SetBursts): a library effect by name, sized and tinted.</summary>
        public struct Burst
        {
            public double Time;
            public string Effect;
            public Vector3 Position, Blow;
            public float Scale;
            public uint Seed;
            public Color Tint;
        }

        /// <summary>
        /// Each moment's effect bursts in the structure style's manner: at the bolt's strike and at each piece. Crystal
        /// sprays gem chips (tinted <paramref name="gem"/>), fire bursts and leaves embers rising from the rubble,
        /// hologram crackles with a ring of lightning, and the others shock the ground and raise a dust cloud. A crystal
        /// formation's blow sprays its chips (tinted <paramref name="formationGem"/>) where it lands, up its height and
        /// out from its foot in a shock wave.
        /// </summary>
        public List<Burst> Bursts(string style, Color gem, Color formationGem = default)
        {
            var bursts = new List<Burst>();
            for (int i = 0; i < Moments.Length; i++)
            {
                var m = Moments[i];
                uint seed = (uint)(i + 1) * 2654435761u;
                void Add(string fx, Vector3 at, float scale, uint salt, Color tint = default) =>
                    bursts.Add(new Burst { Time = m.Time, Effect = fx, Position = at, Blow = m.Blow, Scale = scale, Seed = seed ^ salt, Tint = tint });
                if (m.Crystal)
                {
                    var p = m.Pieces[0];
                    Add("crystal-burst", m.Hit, 4f, 1, formationGem);
                    Add("crystal-burst", p.Base + Vector3.up * (p.HeightM * 0.35f), 5f, 0x9E37u, formationGem);
                    Add("crystal-burst", p.Base + Vector3.up * (p.HeightM * 0.75f), 4f, 0x7F4Au, formationGem);
                    Add("slam-shock", p.Base + Vector3.up * 0.1f, 1.6f, 0x51A3u);
                    continue;
                }
                float big = Mathf.Clamp(m.SizeM / 3f, 0.8f, 2f);
                switch (style)
                {
                    case "crystal": Add("crystal-burst", m.Hit, 2.6f * big, 1, gem); break;
                    case "fire": Add("death-burst-large", m.Hit, 1.1f * big, 1); break;
                    case "hologram": Add("hit-heavy", m.Hit, 1.4f * big, 1); break;
                    default: Add("slam-shock", m.Hit, 1.3f * big, 1); break;
                }
                for (int k = 0; k < m.Pieces.Length; k++)
                {
                    var p = m.Pieces[k];
                    var mid = p.Base + Vector3.up * (p.HeightM * 0.4f);
                    float size = Mathf.Clamp(Mathf.Sqrt(p.WidthM * p.HeightM) / 2f, 0.8f, 2.5f);
                    uint salt = 0x9E37u + (uint)k * 7919u;
                    switch (style)
                    {
                        case "crystal": Add("crystal-burst", mid, 2f * size, salt, gem); break;
                        case "fire": Add("ambient-embers", p.Base, size, salt); break;
                        case "hologram": Add("lightning-circle", p.Base + Vector3.up * 0.05f, size, salt); break;
                        default: Add("dust-puff", p.Base + Vector3.up * 0.3f, 3f * size, salt); break;
                    }
                }
            }
            return bursts;
        }

        /// <summary>The sound layers a style's break plays (sound bank categories, already baked), each with a level, a pitch and a delay (s).</summary>
        public static (string category, float level, float pitch, float delay)[] Layers(string style)
        {
            switch (style)
            {
                case "crystal": return new[] { ("shatter-body", 0.8f, 0.75f, 0f), ("shatter-glass", 0.7f, 0.8f, 0f), ("shatter-tinkle", 0.35f, 0.9f, 0.45f) };
                case "fire": return new[] { ("ring", 0.85f, 0.8f, 0f), ("shatter-body", 0.5f, 0.6f, 0f) };
                case "hologram": return new[] { ("lightning", 0.6f, 1.1f, 0f), ("shatter-glass", 0.4f, 1.25f, 0f) };
                default: return new[] { ("ring", 0.75f, 0.75f, 0f), ("shatter-body", 0.7f, 0.55f, 0f) };
            }
        }

        /// <summary>
        /// The moments' sounds, aggregated as the choreography's are (SoundAggregator): the moments on one beat share one
        /// voice per layer, whose gain grows with log2 of the pieces breaking on that beat, and whose variant follows the beat.
        /// A beat with a crystal formation's break plays crystal's layers at full gain.
        /// </summary>
        public List<SoundScheduler.Cue> Sounds(string style, SongMap map)
        {
            var cues = new List<SoundScheduler.Cue>();
            var walls = Layers(style);
            var crystal = Layers("crystal");
            for (int i = 0, j; i < Moments.Length; i = j)
            {
                long beat = Beat(map, Moments[i].Time);
                int pieces = 0;
                float size = 0;
                bool formation = false;
                for (j = i; j < Moments.Length && Beat(map, Moments[j].Time) == beat; j++)
                {
                    pieces += Moments[j].Pieces.Length;
                    size = Mathf.Max(size, Moments[j].SizeM);
                    formation |= Moments[j].Crystal;
                }
                float gain = formation ? 1f : (float)Math.Min(1.0, 0.6 + 0.2 * Math.Log(Math.Max(1, pieces), 2));
                float pitch = Mathf.Clamp(Mathf.Pow(3.5f / Mathf.Max(0.5f, size), 0.25f), 0.8f, 1.2f); // a larger wall sounds lower
                var layers = formation ? crystal : walls;
                for (int l = 0; l < layers.Length; l++)
                {
                    var (category, level, p, delay) = layers[l];
                    cues.Add(new SoundScheduler.Cue
                    {
                        Time = Moments[i].Time + delay, Name = category + "/" + (int)(Math.Abs(beat * 31 + l) % 3),
                        Gain = gain * level, Pitch = p * pitch,
                    });
                }
            }
            cues.Sort((a, b) => a.Time.CompareTo(b.Time));
            return cues;
        }

        static long Beat(SongMap map, double t) => map != null ? (long)Math.Floor(map.BeatIndexAt(t) + 1e-6) : (long)Math.Floor(t * 2);
    }
}
