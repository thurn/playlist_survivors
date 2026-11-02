using System.Collections.Generic;
using System.Globalization;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `world`: the world's scripted scene (`champions stills world`). The real sim plays a scripted song
    /// (<see cref="ScriptedSong"/>) on the scene's own clock, its events go to a world presenter in the palette's zones,
    /// and the hero stands at the origin, a white ring at its feet standing in for it (<see cref="HeroMarker"/>):
    /// - 0-30 s: 120 BPM, reliable from the first beats: a beat ring every 0.5 s, stronger on downbeats (9 s is one; the
    ///   log's `World: beat` lines say which); a section change at 12 s moves the zone on the beat at 12 s, swept out over
    ///   that beat;
    /// - 30-40 s: no beat, ticks under the gate: no beat rings (the beat turns beatless at 32 s); a dash at
    ///   <see cref="DashAt"/>, on the beat as every press is beatless, sends its event ring, a special moment.
    /// Its stills: the beat wave at three phases after the downbeat at 9 s and a beat on, the zone's sweep, beatless
    /// play, and the dash's impact frame.
    /// </summary>
    sealed class WorldScene : IStillScene
    {
        public const string Name = "world";
        public const double Bpm = 120, BeatsUntil = 30, SongEnd = 40, ChangeAt = 12, DashAt = 34;

        readonly Palette _palette;

        public WorldScene(Palette palette)
        {
            _palette = palette;
        }

        public double[] Times => new[] { 9.03, 9.12, 9.25, 9.5, 12.15, 12.3, 12.7, 31.5, 33, 34.05 };

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            root.gameObject.AddComponent<WorldSceneDriver>().Begin(camera, _palette, view);
            view.Hero = Vector3.zero;
            view.Ids[IdPass.Effect] = ("hero stand-in", "effect", HeroMarker.WidthM);
        }
    }

    /// <summary>Plays <see cref="WorldScene"/>'s script each frame, from the frame it began; torn down with its root.</summary>
    [DefaultExecutionOrder(-100)]
    sealed class WorldSceneDriver : MonoBehaviour
    {
        SimDriver _driver;
        List<StreamEntry> _entries;
        WorldPresenter _world;
        HeroMarker _marker;
        Stills.View _view;
        double _start;
        int _next, _cursor;
        bool _dashed, _released;

        public void Begin(Camera camera, Palette palette, Stills.View view)
        {
            _view = view;
            _start = Time.timeAsDouble;
            _entries = new ScriptedSong().Beats(0, WorldScene.BeatsUntil, WorldScene.Bpm).Gap(WorldScene.BeatsUntil, WorldScene.SongEnd)
                .Change(WorldScene.ChangeAt).Entries();
            var config = new RunInputs().Build(null);
            // One change in the script, well inside the run's first 30 s.
            config.Zones.SectionMinGapS = 0;
            _driver = new SimDriver(config, new FrameClock());
            _world = new WorldPresenter(camera, palette);
            _marker = new HeroMarker(camera.gameObject.layer);
            Debug.Log($"World: the scripted scene, {WorldScene.Bpm} BPM to {WorldScene.BeatsUntil} s, a section change at " +
                      $"{WorldScene.ChangeAt} s, beatless to {WorldScene.SongEnd} s, a dash at {WorldScene.DashAt} s");
        }

        void Update()
        {
            if (_driver == null) return;
            double frame = Time.timeAsDouble - _start;
            double needed = _driver.StampNeededFor(FrameClock.FrontierAt(frame));
            while (_next < _entries.Count && _entries[_next].Stamp <= needed) _driver.AddSettled(_entries[_next++]);
            var sim = _driver.Sim;
            if (!_dashed && frame >= WorldScene.DashAt)
            {
                _dashed = true;
                sim.Press(Key.Space, _driver.Times.Heard);
            }
            else if (_dashed && !_released)
            {
                _released = true;
                sim.Release(Key.Space, _driver.Times.Heard);
            }
            if (!_driver.Step(new ClockReadings { Real = Time.realtimeSinceStartupAsDouble, Frame = frame })) return;
            foreach (var e in sim.EventsSince(_cursor))
            {
                _world.Take(e);
                if (e.Kind == SimEventKind.Beat)
                    Log("World: beat {0} at {1:F3} s{2}, {3}", e.I0, e.T, e.Downbeat ? ", a downbeat" : "", e.BeatReliability);
                else if (e.Kind == SimEventKind.Zone) Log("World: zone {0} from {1} at {2:F3} s over {3:F3} s", e.I0, e.I1, e.T, e.D0);
                else if (e.Kind == SimEventKind.HeroDash) Log("World: dash at {0:F3} s, on the beat {1}", e.D0, (e.I2 & 1) != 0);
                else if (e.Kind == SimEventKind.BeatState) Log("World: the beat is {0} at {1:F3} s", (Reliability)e.I0, e.T);
            }
            _world.Flush();
            _cursor = sim.EventCount;
            sim.DiscardEventsBefore(_cursor);
            double now = _driver.Times.FrameSong;
            sim.Snapshot().HeroAt(now, out double x, out double y);
            var feet = new Vector2((float)x, (float)y);
            _marker.Place(feet);
            _world.Present(now, feet, Time.deltaTime);
            _view.Hero = new Vector3(feet.x, 0, feet.y);
            _view.Impact = _world.World.Rings.Special > 0;
        }

        void OnDestroy()
        {
            _marker?.Dispose();
            _world?.Dispose();
        }

        static void Log(string format, params object[] args) => Debug.Log(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
