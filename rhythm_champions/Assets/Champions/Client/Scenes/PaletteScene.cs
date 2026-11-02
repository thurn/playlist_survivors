using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `palette`: the palette as drawn (docs/champions/style.md, The palette scene). Minute 5's horde from level1.json
    /// (its density and mix), each monster a stand-in at its type's size in its type's colour (a near-black body whose
    /// silhouette glows, Champions/StandIn), round the hero in white, on the void, under the real post. It measures the
    /// palette's key, families, conformance and the hero's margin until the trailer's edge look replaces the stand-ins.
    /// Placed from a fixed seed, so every run keeps the same still.
    /// </summary>
    sealed class PaletteScene : IStillScene
    {
        public const string Name = "palette";
        const double MinuteS = 300;
        const int Seed = 5;
        const double ClearM = 2.5; // the hero's own ground: a living horde doesn't stand on it
        const float FlyM = 1.4f; // a swarmer's height above the ground
        const double GroupM = 1.2; // how far a group's members stand from its spot
        const float HeroM = 1.8f, HeroRadiusM = 0.35f;
        // The design's camera (design.md, Camera): about 60 degrees down, the hero framed at the centre, until the
        // world's camera rig lands.
        const float PitchDeg = 60f, DistanceM = 34f, FovDeg = 30f;

        readonly GameContent _content;
        readonly Palette _palette;

        public PaletteScene(GameContent content, Palette palette)
        {
            _content = content;
            _palette = palette;
        }

        public double[] Times => new[] { 0.25 };

        public string Moment => null;

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            var shader = Resources.Load<Shader>("Champions/StandIn") ?? throw new InvalidOperationException("no Champions/StandIn shader");
            var cast = _content.Enemies.Cast;
            var level = _content.Level;
            var materials = new Dictionary<int, Material>();
            Material MaterialFor(int code, Color line)
            {
                if (materials.TryGetValue(code, out var m)) return m;
                m = new Material(shader) { name = "Stand-in " + code };
                m.SetColor("_Color", line);
                m.SetColor("_Body", _palette.VoidBody);
                m.SetFloat(IdPass.Id, code);
                return materials[code] = m;
            }

            Spawn(root, false, Vector3.zero, HeroM, HeroRadiusM, MaterialFor(IdPass.Hero, _palette.HeroBody));
            view.Hero = Vector3.zero;
            view.Ids[IdPass.Hero] = ("hero", "hero", HeroM);

            var rng = new System.Random(Seed);
            var placed = new List<(double X, double Z, double R)>();
            var mix = Array.FindAll(level.Mix, e => e.FromS <= MinuteS);
            double total = 0;
            foreach (var e in mix) total += e.Weight;
            int alive = (int)Math.Round(level.Ramp.At(MinuteS));
            for (int n = 0; n < alive;)
            {
                double pick = rng.NextDouble() * total;
                var entry = mix[mix.Length - 1];
                foreach (var e in mix)
                    if ((pick -= e.Weight) < 0) { entry = e; break; }
                var type = cast[entry.Type];
                int code = IdPass.Monster(entry.Type);
                var material = MaterialFor(code, _palette.ForType(type.Id));
                view.Ids[code] = (type.Id, type.Role.ToString().ToLowerInvariant(), (float)type.SizeM);
                // A group (the bats' six) gathers round one spot; a lone monster takes any free spot.
                var (gx, gz) = InEllipse(rng, level.RingXM, level.RingYM);
                for (int g = 0; g < type.Group && n < alive; g++, n++)
                {
                    for (int tries = 0; tries < 30; tries++)
                    {
                        var (x, z) = type.Group > 1
                            ? (gx + (rng.NextDouble() * 2 - 1) * GroupM, gz + (rng.NextDouble() * 2 - 1) * GroupM)
                            : InEllipse(rng, level.RingXM, level.RingYM);
                        if (Math.Sqrt(x * x + z * z) < ClearM || Crowded(placed, x, z, type.RadiusM, _content.Enemies.GapShare)) continue;
                        placed.Add((x, z, type.RadiusM));
                        bool flies = type.Role == EnemyRole.Swarmer;
                        bool round = type.SizeM <= 2 * type.RadiusM;
                        Spawn(root, round, new Vector3((float)x, flies ? FlyM : 0, (float)z), (float)type.SizeM, (float)type.RadiusM, material);
                        break;
                    }
                }
            }

            camera.fieldOfView = FovDeg;
            camera.transform.rotation = Quaternion.Euler(PitchDeg, 0, 0);
            camera.transform.position = new Vector3(0, HeroM / 2, 0) - camera.transform.forward * DistanceM;
        }

        /// <summary>A uniform point in the spawn ellipse (level1.json's ring: x across, z up the screen).</summary>
        static (double X, double Z) InEllipse(System.Random rng, double rx, double rz)
        {
            double a = rng.NextDouble() * 2 * Math.PI, r = Math.Sqrt(rng.NextDouble());
            return (Math.Cos(a) * r * rx, Math.Sin(a) * r * rz);
        }

        /// <summary>Would a body of radius <paramref name="r"/> at x, z break the horde's dark gaps (centres apart by
        /// the radii's sum and its gap share)?</summary>
        static bool Crowded(List<(double X, double Z, double R)> placed, double x, double z, double r, double gapShare)
        {
            foreach (var p in placed)
            {
                double dx = p.X - x, dz = p.Z - z, keep = (p.R + r) * (1 + gapShare);
                if (dx * dx + dz * dz < keep * keep) return true;
            }
            return false;
        }

        /// <summary>A stand-in standing at <paramref name="feet"/>, <paramref name="height"/> tall: a sphere (round) or a
        /// capsule of <paramref name="radius"/>.</summary>
        static void Spawn(Transform root, bool round, Vector3 feet, float height, float radius, Material material)
        {
            var go = GameObject.CreatePrimitive(round ? PrimitiveType.Sphere : PrimitiveType.Capsule);
            go.transform.SetParent(root, false);
            // Unity's capsule is 2 m tall and 1 m across; its sphere 1 m across.
            go.transform.localScale = round ? Vector3.one * height : new Vector3(2 * radius, height / 2, 2 * radius);
            go.transform.localPosition = feet + Vector3.up * height / 2;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
