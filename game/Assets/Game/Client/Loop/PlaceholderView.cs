using System.Collections.Generic;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Loop
{
    /// <summary>
    /// A stand-in view until the presenters land: the hero as a white capsule that pulses on each
    /// beat, and enemies as red capsules from Spawn, EnemyStep, EnemyKnock and Death events (the sim's
    /// or a scripted feed's), on a plane under a sun when there is no arena. The look's camera rig
    /// frames them (LookPresenter). Sim ground (x, y) is Unity's (x, 0, y). A primitive's default material doesn't draw in URP players, so they
    /// all use a URP Lit material from Resources.
    /// </summary>
    public sealed class PlaceholderView : IPresenter
    {
        const double Pulse = 0.08; // the beat pulse's decay (s)
        const int Beats = 16;
        const string MaterialPath = "Placeholder/PlaceholderLit";

        sealed class Enemy
        {
            public Transform Body;
            public double Spawn = double.PositiveInfinity, Death = double.PositiveInfinity;
            public float X, Y;
            // The latest move and the one before it: a step arrives ahead of frameSong, so the
            // previous one may still be showing.
            public Move Latest, Previous;
        }

        struct Move
        {
            public float FromX, FromY, ToX, ToY;
            public double Start, End;
        }

        readonly Transform _hero;
        readonly Material _lit, _enemyMaterial;
        readonly List<Enemy> _enemies = new List<Enemy>();
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
            _enemyMaterial = Tinted(new Color(0.85f, 0.2f, 0.25f));
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
            if (e.Kind != SimEventKind.Beat && e.I0 < 0) return; // -1: the hero, or none
            switch (e.Kind)
            {
                case SimEventKind.Beat:
                    _beats[_beatCount++ % Beats] = e.T;
                    break;
                case SimEventKind.Spawn:
                {
                    var s = e.AsSpawn;
                    var enemy = EnemyAt(s.Enemy);
                    enemy.Spawn = e.T;
                    enemy.Death = double.PositiveInfinity;
                    enemy.X = (float)s.X;
                    enemy.Y = (float)s.Y;
                    enemy.Latest = enemy.Previous = default;
                    break;
                }
                case SimEventKind.EnemyStep:
                case SimEventKind.EnemyKnock:
                {
                    // Both carry from (D0, D1), to (D2, D3) and the move's start and end (D4, D5).
                    var enemy = EnemyAt(e.I0);
                    enemy.Previous = enemy.Latest;
                    enemy.Latest = new Move
                    {
                        FromX = (float)e.D0, FromY = (float)e.D1, ToX = (float)e.D2, ToY = (float)e.D3,
                        Start = e.D4, End = e.D5,
                    };
                    break;
                }
                case SimEventKind.Death:
                    EnemyAt(e.I0).Death = e.T;
                    break;
            }
        }

        Enemy EnemyAt(int id)
        {
            while (_enemies.Count <= id)
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Enemy " + _enemies.Count;
                body.GetComponent<Renderer>().sharedMaterial = _enemyMaterial;
                body.SetActive(false);
                _enemies.Add(new Enemy { Body = body.transform });
            }
            return _enemies[id];
        }

        public void Present(in PresentFrame frame)
        {
            double t = frame.Song;
            frame.State.Hero.PositionAt(t, out double hx, out double hy);
            float pulse = 1 + 0.25f * (float)System.Math.Exp(-SinceBeat(t) / Pulse);
            _hero.localPosition = new Vector3((float)hx, 1, (float)hy);
            _hero.localScale = new Vector3(pulse, 1, pulse);
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                bool alive = t >= enemy.Spawn && t < enemy.Death;
                if (enemy.Body.gameObject.activeSelf != alive) enemy.Body.gameObject.SetActive(alive);
                if (!alive) continue;
                var move = t >= enemy.Latest.Start ? enemy.Latest : enemy.Previous;
                float x = enemy.X, y = enemy.Y;
                if (move.End > move.Start)
                {
                    float a = Mathf.Clamp01((float)((t - move.Start) / (move.End - move.Start)));
                    x = Mathf.Lerp(move.FromX, move.ToX, a);
                    y = Mathf.Lerp(move.FromY, move.ToY, a);
                }
                enemy.Body.localPosition = new Vector3(x, 1, y);
            }
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
