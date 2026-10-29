using UnityEngine;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// Publishes the hero to the canopy fade (CanopyFade.hlsl; environment.md, Canopy fade): tree
    /// pixels nearer the camera than the hero and within <see cref="RadiusM"/> of it on screen
    /// dither out. The demo calls <see cref="SetHero"/> every frame; nothing fades until it does.
    /// </summary>
    public static class CanopyFade
    {
        /// <summary>The cleared disc's radius, in metres at the hero's depth: a hero is about 2 m tall.</summary>
        public const float RadiusM = 3.2f;
        /// <summary>The disc's centre above the hero's feet.</summary>
        public const float ChestM = 1.1f;
        static readonly int HeroId = Shader.PropertyToID("_Demo3DHero");

        public static void SetHero(Vector3 feet, float radiusM = RadiusM) =>
            Shader.SetGlobalVector(HeroId, new Vector4(feet.x, feet.y + ChestM, feet.z, radiusM));

        public static void Clear() => Shader.SetGlobalVector(HeroId, Vector4.zero);
    }
}
