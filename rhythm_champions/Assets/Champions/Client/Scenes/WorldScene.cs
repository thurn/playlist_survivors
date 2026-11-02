using System.Collections.Generic;
using System.Globalization;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `--scene world`: the world's scripted scene for stills (run it with `--fixed-time`). The real sim plays a
    /// scripted song (<see cref="ScriptedSong"/>) on the frames' clock, its events go to the world presenter, and the
    /// hero stands at the origin, a white ring at its feet standing in for it (<see cref="HeroMarker"/>):
    /// - 0-30 s: 120 BPM, reliable from the first beats: a beat ring every 0.5 s, stronger on downbeats (the log's
    ///   `World: beat` lines say which); a section change at 12 s moves the zone key on the beat at 12 s, swept out over
    ///   that beat;
    /// - 30-40 s: no beat, ticks under the gate: no beat rings (the beat turns beatless at 32 s); a dash at
    ///   <see cref="DashAt"/>, on the beat as every press is beatless, sends its event ring.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class WorldScene : MonoBehaviour
    {
        public const string Name = "world";
        public const double Bpm = 120, BeatsUntil = 30, SongEnd = 40, ChangeAt = 12, DashAt = 34;

        SimDriver _driver;
        List<StreamEntry> _entries;
        WorldPresenter _world;
        HeroMarker _marker;
        int _next, _cursor;
        bool _dashed, _released;

        public static WorldScene Begin(GameObject host, WorldPresenter world)
        {
            var scene = host.AddComponent<WorldScene>();
            scene._world = world;
            scene._entries = new ScriptedSong().Beats(0, BeatsUntil, Bpm).Gap(BeatsUntil, SongEnd).Change(ChangeAt).Entries();
            var config = new RunInputs().Build(null);
            // One change in the script, well inside the run's first 30 s.
            config.Zones.SectionMinGapS = 0;
            scene._driver = new SimDriver(config, new FrameClock());
            scene._marker = new HeroMarker(host.layer);
            Debug.Log($"World: the scripted scene, {Bpm} BPM to {BeatsUntil} s, a section change at {ChangeAt} s, beatless to {SongEnd} s, a dash at {DashAt} s");
            return scene;
        }

        void Update()
        {
            double frame = Time.timeSinceLevelLoadAsDouble;
            double needed = _driver.StampNeededFor(FrameClock.FrontierAt(frame));
            while (_next < _entries.Count && _entries[_next].Stamp <= needed) _driver.AddSettled(_entries[_next++]);
            var sim = _driver.Sim;
            if (!_dashed && frame >= DashAt)
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
        }

        void OnDestroy() => _marker?.Dispose();

        static void Log(string format, params object[] args) => Debug.Log(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
