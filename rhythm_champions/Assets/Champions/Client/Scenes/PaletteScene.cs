using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `palette`: the palette as drawn (docs/champions/style.md, The palette scene). Minute 5's horde from level1.json
    /// (its density and mix), each monster drawn by the crowd in the edge look (`--look trailer` for the trailer's) in
    /// its type's colour, walking in place toward the hero (a stand-in in white, Champions/StandIn), on the void, under
    /// the real post; a build without baked monsters draws stand-ins at their types' sizes instead. Placed from a fixed
    /// seed, so every run keeps the same still.
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
        readonly MonsterLook _look;

        public PaletteScene(GameContent content, Palette palette, MonsterLook look)
        {
            _content = content;
            _palette = palette;
            _look = look;
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

            var lib = MonsterLibrary.Load(_look);
            if (lib == null) Debug.LogWarning("PaletteScene: no baked monsters (champions monster bake all): stand-ins");
            var crowd = new List<(int Type, int Code, Vector3 Feet)>();
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
                        int baked = lib?.IndexOf(type.Id) ?? -1;
                        if (baked >= 0) crowd.Add((baked, code, new Vector3((float)x, lib.Types[baked].HoverM, (float)z)));
                        else Spawn(root, round, new Vector3((float)x, flies ? FlyM : 0, (float)z), (float)type.SizeM, (float)type.RadiusM, material);
                        break;
                    }
                }
            }

            if (crowd.Count > 0) view.Requeue = root.gameObject.AddComponent<PaletteCrowd>().Begin(camera, lib, _palette, crowd);
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
    /// <summary>
    /// Draws <see cref="PaletteScene"/>'s horde with the crowd every frame, from the frame it began, before Boot keeps a
    /// still of it; torn down with its root.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    sealed class PaletteCrowd : MonoBehaviour
    {
        MonsterCrowd _crowd;
        Camera _camera;
        List<(int Type, int Code, Vector3 Feet)>[] _byType;
        double _start;

        /// <summary>Starts drawing the monsters (type in the library, ID code, feet); the stills' requeue.</summary>
        public Action Begin(Camera camera, MonsterLibrary lib, Palette palette, List<(int Type, int Code, Vector3 Feet)> monsters)
        {
            _camera = camera;
            _start = Time.timeAsDouble;
            _byType = new List<(int, int, Vector3)>[lib.Types.Length];
            for (int t = 0; t < _byType.Length; t++) _byType[t] = monsters.FindAll(m => m.Type == t);
            int most = 1;
            foreach (var list in _byType) most = Math.Max(most, list.Count);
            _crowd = new MonsterCrowd(lib, most, palette);
            foreach (var list in _byType)
                if (list.Count > 0) _crowd.SetIdCode(list[0].Type, list[0].Code);
            Present(0); // drawn from the first frame, and in an EditMode test, where nothing updates it
            return () => _crowd.Queue(_camera);
        }

        void Update() => Present(Time.timeAsDouble - _start);

        void Present(double t)
        {
            for (int type = 0; type < _byType.Length; type++)
            {
                var list = _byType[type];
                if (list.Count == 0) continue;
                var clips = _crowd.Library.Types[type];
                int walk = Math.Max(0, clips.Clip("walk"));
                var slots = _crowd.Instances(type, list.Count);
                var motion = _crowd.Motion(type, list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var feet = list[i].Feet;
                    float time = (float)(t + 0.37 * i);
                    slots[i] = new MonsterInstance
                    {
                        Position = feet, Yaw = Mathf.Atan2(-feet.x, -feet.z), ClipA = (uint)walk, TimeA = time, ClipB = (uint)walk,
                        TimeB = time, Blend = 1, Scale = 1, Flags = (uint)CrowdFlags.Alive,
                    };
                    motion[i] = MonsterMotion.Still((uint)(type * 1000 + i + 1));
                }
            }
            _crowd.Draw(_camera, t);
        }

        void OnDestroy() => _crowd?.Dispose();
    }
}
