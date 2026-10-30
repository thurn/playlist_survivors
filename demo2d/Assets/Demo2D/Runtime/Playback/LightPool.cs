using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// A fixed pool of point Light2Ds (PX7). Each frame its owner offers every light it would like
    /// (<see cref="Offer"/>) and the pool lights the brightest of them, ties broken by key, so the
    /// lit set is a function of the song time and a seek lights the same. Capping the pool bounds
    /// the 2D renderer's light passes at the climax.
    /// </summary>
    public sealed class LightPool : IDisposable
    {
        struct Want
        {
            public Vector2 At;
            public Color Color;
            public float Radius, Intensity;
            public long Key;
        }

        readonly Light2D[] _lights;
        readonly GameObject _root;
        readonly List<Want> _wants = new List<Want>();
        static readonly Comparison<Want> Brightest = (a, b) =>
        {
            int c = (b.Intensity * b.Radius).CompareTo(a.Intensity * a.Radius);
            return c != 0 ? c : a.Key.CompareTo(b.Key);
        };

        public int Capacity => _lights.Length;
        /// <summary>Lights lit by the last <see cref="Commit"/>.</summary>
        public int Lit { get; private set; }
        /// <summary>Lights offered since the last <see cref="Commit"/> (before the cap).</summary>
        public int Offered => _wants.Count;

        public LightPool(string name, int capacity, Transform parent)
        {
            _root = new GameObject(name);
            _root.transform.SetParent(parent, false);
            _lights = new Light2D[capacity];
            for (int i = 0; i < capacity; i++)
            {
                var l = new GameObject("Light").AddComponent<Light2D>();
                l.transform.SetParent(_root.transform, false);
                l.lightType = Light2D.LightType.Point;
                l.pointLightInnerRadius = 0;
                l.falloffIntensity = 0.6f;
                l.enabled = false;
                _lights[i] = l;
            }
        }

        public void Offer(Vector2 at, Color color, float radius, float intensity, long key)
        {
            if (intensity <= 0.001f || radius <= 0) return;
            _wants.Add(new Want { At = at, Color = color, Radius = radius, Intensity = intensity, Key = key });
        }

        /// <summary>Lights the brightest offers and clears them for the next frame.</summary>
        public void Commit()
        {
            _wants.Sort(Brightest);
            Lit = Math.Min(_wants.Count, _lights.Length);
            for (int i = 0; i < _lights.Length; i++)
            {
                var l = _lights[i];
                if (i >= Lit) { if (l.enabled) l.enabled = false; continue; }
                var w = _wants[i];
                l.transform.localPosition = new Vector3(w.At.x, w.At.y, 0);
                l.color = w.Color;
                l.intensity = w.Intensity;
                l.pointLightOuterRadius = w.Radius;
                if (!l.enabled) l.enabled = true;
            }
            _wants.Clear();
        }

        public void Dispose() => SongPlayback.Destroy(_root);
    }
}
