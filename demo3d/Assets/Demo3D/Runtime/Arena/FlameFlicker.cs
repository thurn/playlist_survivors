using UnityEngine;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// A flame light's flicker (environment.md: at most 8 flickering flame lights): two octaves of
    /// smooth noise on intensity and a little on range, phase-shifted per light by its position,
    /// so neighbouring braziers never pulse together. Driven by Time.time, which fixed-time
    /// captures step at 1/60 s.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class FlameFlicker : MonoBehaviour
    {
        public float baseIntensity = 3f;
        public float baseRange = 8f;
        public float amount = 0.18f;
        Light _light;
        float _seed;

        void Awake()
        {
            _light = GetComponent<Light>();
            var p = transform.position;
            _seed = Mathf.Abs(p.x * 12.9898f + p.z * 78.233f) % 97f;
        }

        void Update()
        {
            float t = Time.time + _seed;
            float n = 0.65f * (Mathf.PerlinNoise(t * 2.1f, _seed) - 0.5f) + 0.35f * (Mathf.PerlinNoise(t * 7.3f, _seed + 13f) - 0.5f);
            _light.intensity = baseIntensity * (1f + 2f * amount * n);
            _light.range = baseRange * (1f + 0.5f * amount * n);
        }
    }
}
