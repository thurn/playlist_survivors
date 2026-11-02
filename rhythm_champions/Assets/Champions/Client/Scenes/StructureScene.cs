using System;
using System.Collections.Generic;
using System.Globalization;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `structures`: a structure field and a break (`champions stills --scene structures`). The real sim plays level 1's
    /// structures (Content/structures.json) on seed <see cref="Seed"/>, whose start stands between a hamlet, a lone tower
    /// and a grove, to a scripted song at 120 BPM, with the world presenter and the structure presenter (stand-ins until
    /// the trailer's edge look is ported), the hero's feet marked by a white ring (<see cref="HeroMarker"/>):
    /// - the hero walks from the start to stand <see cref="StandOffM"/> off the nearest small structure (a fence, a rock, a
    ///   tree or a wall) it can walk straight to, and at <see cref="DashAt"/> dashes through it with Blade Rush (the
    ///   scene's start dash; the plain Dash deals no damage), which breaks it on the key-down frame;
    /// - stills: the field from the start, the field round the hero before the dash, the break's impact frame (the
    ///   `break` moment, on whichever frame first shows it), and the field after it.
    /// </summary>
    sealed class StructureScene : IStillScene
    {
        public const string Name = "structures";
        public const ulong Seed = 41;
        public const double Bpm = 120, SongEnd = 20, DashAt = 6.0, StandOffM = 1.2;

        readonly GameContent _content;
        readonly Palette _palette;
        StructureSceneDriver _driver;

        public StructureScene(GameContent content, Palette palette)
        {
            _content = content;
            _palette = palette;
        }

        public double[] Times => new[] { 0.5, 4.27, 5.9, 6.25, 6.6, 7.5 };

        public string Moment => _driver != null ? _driver.Moment : null;

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            _driver = root.gameObject.AddComponent<StructureSceneDriver>();
            _driver.Begin(camera, _content, _palette, view);
            view.Hero = Vector3.zero;
            view.Ids[IdPass.Effect] = ("hero stand-in", "effect", HeroMarker.WidthM);
        }
    }

    /// <summary>Plays <see cref="StructureScene"/>'s script each frame, from the frame it began; torn down with its root.</summary>
    [DefaultExecutionOrder(-100)]
    sealed class StructureSceneDriver : MonoBehaviour
    {
        const double HeroRadius = 0.4;

        SimDriver _driver;
        List<StreamEntry> _entries;
        WorldPresenter _world;
        StructurePresenter _structures;
        HeroMarker _marker;
        Camera _camera;
        Stills.View _view;
        double _start;
        int _next, _cursor;
        Vector2 _stand, _through;
        bool _walking, _aimed, _dashed, _released, _broken;

        /// <summary>The moment to keep on this frame (StructureScene.Moment), or null.</summary>
        public string Moment { get; private set; }

        public void Begin(Camera camera, GameContent content, Palette palette, Stills.View view)
        {
            _camera = camera;
            _view = view;
            _start = Time.timeAsDouble;
            _entries = new ScriptedSong().Beats(0, StructureScene.SongEnd, StructureScene.Bpm).Entries();
            var config = new RunInputs { Seed = StructureScene.Seed, Loadout = new Upgrade[0] }.Build(content);
            config.Structures = content.Structures.Clone();
            config.Dashes.Start = "bladeRush";
            _driver = new SimDriver(config, new FrameClock());
            _world = new WorldPresenter(camera, palette);
            _structures = new StructurePresenter(config.Structures, palette, _world.World, camera.gameObject.layer);
            view.Ids[IdPass.Structure] = ("structures", "structure", StructurePresenter.MaxHeightM);
            _marker = new HeroMarker(camera.gameObject.layer);
            if (!Pick(_driver.Sim, out _stand, out _through, out string target))
                throw new InvalidOperationException($"seed {StructureScene.Seed}: no small structure the hero can walk straight to");
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "Structures: seed {0}, {1} standing; the hero walks to ({2:F2}, {3:F2}) and dashes through the {4} at {5} s",
                StructureScene.Seed, _driver.Sim.StructureField.Standing, _stand.x, _stand.y, target, StructureScene.DashAt));
        }

        /// <summary>
        /// The nearest standing fence, rock, tree or wall the hero can walk straight to from the start: where it stands
        /// off its near face, a point past it to dash at, and its kind.
        /// </summary>
        static bool Pick(Simulation sim, out Vector2 stand, out Vector2 through, out string kind)
        {
            var field = sim.StructureField;
            var rules = field.Rules;
            int best = -1;
            double bestD = double.MaxValue;
            stand = through = Vector2.zero;
            kind = null;
            for (int id = 0; id < field.Count; id++)
            {
                var s = field[id];
                var k = rules.Kinds[s.Kind];
                if (!s.Standing || k.Hits != 1) continue;
                double d = Math.Sqrt(s.X * s.X + s.Y * s.Y);
                // Off its face toward the start: a box's thin side's normal, a disc's radius.
                double nx = -s.X / d, ny = -s.Y / d, reach = k.Radius;
                if (k.Shape == FootprintShape.Box)
                {
                    nx = -s.Sin;
                    ny = s.Cos;
                    if (nx * s.X + ny * s.Y > 0)
                    {
                        nx = -nx;
                        ny = -ny;
                    }
                    reach = k.HalfY;
                }
                double off = reach + HeroRadius + StructureScene.StandOffM;
                double sx = s.X + nx * off, sy = s.Y + ny * off;
                field.Move(0, 0, HeroRadius, sx, sy, out double ex, out double ey, out int blocker);
                if (blocker >= 0 || d >= bestD) continue;
                best = id;
                bestD = d;
                stand = new Vector2((float)sx, (float)sy);
                through = new Vector2((float)(s.X - nx * 3), (float)(s.Y - ny * 3));
                kind = k.Id;
            }
            return best >= 0;
        }

        void Update()
        {
            Moment = null;
            if (_driver == null) return;
            double frame = Time.timeAsDouble - _start;
            double needed = _driver.StampNeededFor(FrameClock.FrontierAt(frame));
            while (_next < _entries.Count && _entries[_next].Stamp <= needed) _driver.AddSettled(_entries[_next++]);
            var sim = _driver.Sim;
            double heard = _driver.Times.Heard;
            if (!_walking)
            {
                _walking = true;
                sim.Target(_stand.x, _stand.y, heard);
            }
            else if (!_aimed && frame >= StructureScene.DashAt - 0.05)
            {
                // Turn to face it a few frames before the dash, which goes the way the hero means to move.
                _aimed = true;
                sim.Target(_through.x, _through.y, heard);
            }
            else if (!_dashed && frame >= StructureScene.DashAt)
            {
                _dashed = true;
                sim.Press(Key.Space, heard);
            }
            else if (_dashed && !_released)
            {
                _released = true;
                sim.Release(Key.Space, heard);
            }
            if (!_driver.Step(new ClockReadings { Real = Time.realtimeSinceStartupAsDouble, Frame = frame })) return;
            foreach (var e in sim.EventsSince(_cursor))
            {
                _world.Take(e);
                _structures.Take(e);
                if (e.Kind == SimEventKind.StructureBreak)
                    Log("Structures: structure {0} broke at {1:F3} s, thrown ({2:F2}, {3:F2})", e.I0, e.T, e.D0, e.D1);
                else if (e.Kind == SimEventKind.HeroDash) Log("Structures: dash at {0:F3} s", e.D0);
            }
            _world.Flush();
            _structures.Flush();
            _cursor = sim.EventCount;
            sim.DiscardEventsBefore(_cursor);
            double now = _driver.Times.FrameSong;
            sim.Snapshot().HeroAt(now, out double x, out double y);
            var feet = new Vector2((float)x, (float)y);
            _marker.Place(feet);
            _world.Present(now, feet, Time.deltaTime);
            _structures.Present(now);
            _view.Hero = new Vector3(feet.x, 0, feet.y);
            _view.Impact = _world.World.Rings.Special > 0;
            if (_structures.BrokeThisFrame > 0 && !_broken)
            {
                _broken = true;
                Moment = "break";
                Log("Structures: the break shows on the frame at {0:F3} s (scene {1:F3} s)", now, frame);
            }
        }

        void OnDestroy()
        {
            _marker?.Dispose();
            _structures?.Dispose();
            _world?.Dispose();
        }

        static void Log(string format, params object[] args) => Debug.Log(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
