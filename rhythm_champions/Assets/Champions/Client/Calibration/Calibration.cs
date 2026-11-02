using System;
using System.Collections.Generic;
using System.IO;
using Champions.Client.Music;
using UnityEngine;

namespace Champions.Client.Calibration
{
    /// <summary>
    /// One output device's calibration (ported from game/; design.md, Music > Sources), in seconds, as the
    /// clocks take it (<see cref="MusicClock"/>):
    /// <code>
    /// frameSong  = heardSong(now + displayLatency) + Video
    /// keySong(r) = heardSong(r) - Input
    /// </code>
    /// </summary>
    public struct Offsets
    {
        /// <summary>How much later the display shows a frame than the clock assumes: a larger value shows each moment earlier.</summary>
        public double Video;
        /// <summary>How late the player's presses land on what they hear.</summary>
        public double Input;

        public void ApplyTo(MusicClock clock)
        {
            clock.VideoCalibration = Video;
            clock.InputCalibration = Input;
        }

        public override string ToString() => $"video {Ms(Video):+0;-0;0} ms, input {Ms(Input):+0;-0;0} ms";

        /// <summary>Seconds as whole milliseconds, as the file keeps them.</summary>
        public static int Ms(double s) => (int)Math.Round(s * 1000);
    }

    /// <summary>
    /// The calibrations, one per output device by its Core Audio UID (<see cref="Plugin.Device"/>),
    /// kept in `calibration.json` in the player's data folder (~/Library/Application Support/
    /// Rhythm Champions/Rhythm Champions on the Mac), or the file `--calibration FILE` names.
    /// A device not calibrated yet plays with no offsets. Each value is whole milliseconds, as
    /// native's settings keep them.
    /// </summary>
    public sealed class CalibrationStore
    {
        [Serializable]
        sealed class Entry
        {
            public string uid;
            public string name; // the device's, for whoever reads the file
            public int videoMs;
            public int inputMs;
        }

        [Serializable]
        sealed class Saved
        {
            public List<Entry> devices = new List<Entry>();
        }

        readonly Saved _saved;

        public string Path { get; }

        CalibrationStore(string path, Saved saved)
        {
            Path = path;
            _saved = saved;
        }

        /// <summary>Where the player keeps its calibrations: `--calibration FILE`, else calibration.json in its data folder.</summary>
        public static string DefaultPath =>
            LaunchOptions.Current.CalibrationFile ?? System.IO.Path.Combine(Application.persistentDataPath, "calibration.json");

        /// <summary>The calibrations in `path` (<see cref="DefaultPath"/> if null); none if there is no file or it can't be read.</summary>
        public static CalibrationStore Load(string path = null)
        {
            path ??= DefaultPath;
            Saved saved = null;
            try
            {
                if (File.Exists(path)) saved = JsonUtility.FromJson<Saved>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Calibration: can't read {path}, starting from none: {e.Message}");
            }
            if (saved?.devices == null) saved = new Saved();
            saved.devices.RemoveAll(d => d == null || d.uid == null);
            return new CalibrationStore(path, saved);
        }

        /// <summary>The calibration of the device `uid`: its own, or none.</summary>
        public Offsets For(string uid)
        {
            var e = Find(uid);
            return e == null ? default : new Offsets { Video = e.videoMs / 1000.0, Input = e.inputMs / 1000.0 };
        }

        /// <summary>Whether the device `uid` has been calibrated.</summary>
        public bool Has(string uid) => Find(uid) != null;

        /// <summary>Keeps `offsets` for `device` (rounded to whole milliseconds) and writes the file; false if it can't be written.</summary>
        public bool Set(Plugin.Device device, Offsets offsets)
        {
            var e = Find(device.Uid);
            if (e == null)
            {
                e = new Entry { uid = device.Uid ?? "" };
                _saved.devices.Add(e);
            }
            e.name = device.Name;
            e.videoMs = Offsets.Ms(offsets.Video);
            e.inputMs = Offsets.Ms(offsets.Input);
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path, JsonUtility.ToJson(_saved, true) + "\n");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Calibration: can't write {Path}: {ex.Message}");
                return false;
            }
        }

        Entry Find(string uid)
        {
            uid ??= "";
            foreach (var e in _saved.devices)
                if (e.uid == uid) return e;
            return null;
        }
    }

    /// <summary>
    /// The input calibration's tap-along (native's and game/'s, docs/archive/design/native-app.md, Calibration (D2)):
    /// eight clicks at 120 BPM, and the player taps along. Its offset is the median of the taps'
    /// errors after the first two, counting only taps within a quarter beat of a click.
    /// </summary>
    public static class TapAlong
    {
        public const int Clicks = 8;
        public const double Period = 0.5; // 120 BPM
        public const double Window = Period / 4;
        /// <summary>Counted taps needed for an offset.</summary>
        public const int Needed = 3;

        /// <summary>A tap's error against the nearest click (s, late is positive); null when no click is within a quarter beat.</summary>
        public static double? Error(double tap, IReadOnlyList<double> clicks)
        {
            double best = double.PositiveInfinity;
            foreach (var c in clicks)
                if (Math.Abs(tap - c) < Math.Abs(best)) best = tap - c;
            return Math.Abs(best) <= Window ? best : (double?)null;
        }

        /// <summary>The offset (s) from tap and heard click times: null with fewer than <see cref="Needed"/> counted taps.</summary>
        public static double? Offset(IReadOnlyList<double> taps, IReadOnlyList<double> clicks)
        {
            var errors = new List<double>();
            for (int i = 2; i < taps.Count; i++)
                if (Error(taps[i], clicks) is double e) errors.Add(e);
            if (errors.Count < Needed) return null;
            errors.Sort();
            int m = errors.Count / 2;
            return errors.Count % 2 == 1 ? errors[m] : (errors[m - 1] + errors[m]) / 2;
        }
    }
}
