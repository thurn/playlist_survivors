using UnityEngine;

namespace PS.Look.Hero
{
    /// <summary>
    /// A hero renderer's faceted mesh (neon-look.md, The Axes &gt; Hero), which the hero bake leaves on each of the
    /// knight's renderers (the package's HeroFacetBaker, called by game's HeroBaker and demo3d's RosterBaker): its
    /// mesh unwelded, each triangle with its own barycentrics and random value (UV3) and feature edges (UV5), as the
    /// crowd's style mesh, its bone weights kept. Derived from a vendor mesh at bake time, so never committed. A hero
    /// look draws it in place of the renderer's own (<see cref="HeroLook.Dress"/>); the classic look leaves it unused.
    /// </summary>
    public sealed class HeroFacets : MonoBehaviour
    {
        public Mesh faceted;
    }
}
