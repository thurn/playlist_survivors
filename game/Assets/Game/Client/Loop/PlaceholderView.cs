using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Loop
{
    /// <summary>
    /// A stand-in view until the presenters land: the hero as a white capsule that pulses on each
    /// beat (the crowd draws the enemies), on a plane under a sun when there is no arena. The look's
    /// camera rig frames it (LookPresenter). Sim ground (x, y) is Unity's (x, 0, y). A primitive's
    /// default material doesn't draw in URP players, so they all use a URP Lit material from
    /// Resources.
    /// </summary>
    public sealed class PlaceholderView : IPresenter
    {
        const double Pulse = 0.08; // the beat pulse's decay (s)
        const int Beats = 16;
        const string MaterialPath = "Placeholder/PlaceholderLit";

        readonly Transform _hero;
        readonly Material _lit;
        readonly double[] _beats = new double[Beats];
        int _beatCount;

        public PlaceholderView(Camera camera, bool ground = true)
        {
            _lit = Resources.Load<Material>(MaterialPath);
            var root = new GameObject("Placeholder").transform;
            var hero = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            hero.name = "Hero";
            hero.transform.SetParent(root);
            hero.GetComponent<Renderer>().sharedMaterial = Tinted(new Color(0.95f, 0.95f, 1f));
            _hero = hero.transform;
            if (!ground) return;
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.SetParent(root);
            plane.transform.localScale = new Vector3(30, 1, 30); // 300 m
            plane.GetComponent<Renderer>().sharedMaterial = Tinted(new Color(0.12f, 0.13f, 0.17f));
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.SetParent(root);
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.02f, 0.04f);
            }
        }

        Material Tinted(Color color) => new Material(_lit) { color = color };

        public void OnEvent(in SimEvent e)
        {
            if (e.Kind == SimEventKind.Beat) _beats[_beatCount++ % Beats] = e.T;
        }

        public void Present(in PresentFrame frame)
        {
            double t = frame.Song;
            frame.State.Hero.PositionAt(t, out double hx, out double hy);
            float pulse = 1 + 0.25f * (float)System.Math.Exp(-SinceBeat(t) / Pulse);
            _hero.localPosition = new Vector3((float)hx, 1, (float)hy);
            _hero.localScale = new Vector3(pulse, 1, pulse);
        }

        double SinceBeat(double t)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < Beats && i < _beatCount; i++)
                if (_beats[i] <= t && t - _beats[i] < best) best = t - _beats[i];
            return best;
        }
    }
}
