using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client.Bench
{
    /// <summary>Optional full-encounter timing, never run by CI. Synchronized 1080p render time includes CPU submission
    /// and readback: an upper bound, not a Metal GPU timestamp. The pickup overlay stresses the render cap; pickup
    /// ownership/collection stays in the simulation and is tested independently.</summary>
    sealed class FullBuildProbe : MonoBehaviour
    {
        Camera camera;
        Simulation sim;
        RunPresentation presentation;
        PickupPresenter pickups;
        readonly Pickup[] drops = new Pickup[PickupPresenter.Limit];
        RenderTexture target;
        string nameOfRun;
        readonly Stopwatch watch = new Stopwatch();
        readonly List<double> simulation = new List<double>(), presenting = new List<double>(), submit = new List<double>(), synced = new List<double>();
        int maxFlights, minAlive = int.MaxValue, maxEvents;
        public void Begin(Camera camera, Simulation sim, RunPresentation presentation, string name)
        {
            this.camera = camera; this.sim = sim; this.presentation = presentation; nameOfRun = name;
            pickups = new PickupPresenter(camera.gameObject.layer);
            for (int i = 0; i < drops.Length; i++)
                drops[i] = new Pickup { Id = i, Kind = i % 48 == 0 ? PickupKind.Healing : PickupKind.Experience,
                    X = (i % 24 - 12) * .8 + .3, Y = (i / 24 - 8) * .8 + .3, Value = 1 };
            if (LaunchOptions.Current.BenchOut != null)
            {
                target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { name = "Full build probe" };
                target.Create();
            }
        }
        public void SimulationSample(double ms)
        {
            if (sim.Now < 6) return;
            simulation.Add(ms);
            minAlive = Math.Min(minAlive, sim.Enemies.Alive);
            maxFlights = Math.Max(maxFlights, sim.Ranged.Flights.Count);
            maxEvents = Math.Max(maxEvents, sim.EventCount);
        }
        public void PresentationSample(double ms) { if (sim.Now >= 6) presenting.Add(ms); }
        public void Queue() => pickups.Draw(drops, drops.Length, sim.Now, camera);
        public void Frame()
        {
            if (sim == null) return;
            pickups.Draw(drops, drops.Length, sim.Now, camera);
            if (target == null) return;
            var previous = camera.targetTexture;
            watch.Restart();
            camera.targetTexture = target;
            try { camera.Render(); }
            finally { camera.targetTexture = previous; }
            double submitted = watch.Elapsed.TotalMilliseconds;
            AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32).WaitForCompletion();
            if (sim.Now >= 6) { submit.Add(submitted); synced.Add(watch.Elapsed.TotalMilliseconds); }
            presentation.Queue(camera);
            pickups.Draw(drops, drops.Length, sim.Now, camera);
        }
        void OnDestroy()
        {
            if (target != null)
            {
                string Stats(List<double> values)
                {
                    var v = values.OrderBy(x => x).ToArray();
                    string P(double q) => v.Length == 0 ? "null" : v[Math.Min(v.Length - 1, (int)(q * v.Length))].ToString("F3", CultureInfo.InvariantCulture);
                    return "{\"samples\":" + v.Length + ",\"p50\":" + P(.5) + ",\"p99\":" + P(.99) + "}";
                }
                string json = "{\"scene\":\"" + nameOfRun + "\",\"device\":\"" + SystemInfo.graphicsDeviceName +
                    "\",\"tier\":\"" + QualityTiers.Current + "\",\"width\":1920,\"height\":1080,\"minAlive\":" + minAlive +
                    ",\"pickupOverlay\":" + drops.Length + ",\"maxFlights\":" + maxFlights + " ,\"eventCursor\":" + maxEvents +
                    ",\"simMs\":" + Stats(simulation) + ",\"presentMs\":" + Stats(presenting) + ",\"presenterFaults\":" + presentation.Dispatch.Faults + ",\"renderSubmitMs\":" + Stats(submit) + ",\"renderSynchronizedMs\":" + Stats(synced) + "}\n";
                File.WriteAllText(LaunchOptions.Current.BenchOut + "-" + nameOfRun + ".json", json);
                UnityEngine.Debug.Log("FullBuildProbe: " + json);
                target.Release(); Destroy(target);
            }
            pickups?.Dispose();
        }
    }
}
