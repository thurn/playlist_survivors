using System.Collections.Generic;
using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// A Boss Rush boss's variation on the edge look (boss-rush.md, Look, effects, shaders and sound; BossEdge.shader):
    /// each gain 0 is off. A boss with a look draws its body with BossEdge instead of MonsterEdge, everything else equal.
    ///
    /// <code>
    /// { "mirror", new BossLook { Scan = 0.8f } },                         // a hologram
    /// { "magmaHeart", new BossLook { Flow = 0.6f, Ember = 1, EmberTint = new Color(1f, 0.3f, 0.05f) } },
    /// </code>
    ///
    /// Fight beads add their boss's line to <see cref="Looks"/>, keyed by its cast id (enemies.json). The variations are
    /// floats, so a new look costs the player's variant budget nothing.
    /// </summary>
    public struct BossLook
    {
        public const string Shader = "Crowd/BossEdge";

        /// <summary>Hologram scanlines; their density (per metre) and climb (m/s): 0 keeps the shader's defaults.</summary>
        public float Scan, ScanDensity, ScanSpeed;
        /// <summary>Crystal facet glints.</summary>
        public float Crystal;
        /// <summary>Light flowing along the edges, and its speed (bands/s) and scale (bands per metre): 0 keeps the defaults.</summary>
        public float Flow, FlowSpeed, FlowScale;
        /// <summary>The glitch's sideways jump, metres.</summary>
        public float Glitch;
        /// <summary>The ghostly fade, 0 solid to 1 gone.</summary>
        public float Ghost;
        /// <summary>The ember core, and its hot end (the boss's flavour hue run hot; never orange/red danger, yellow or white).</summary>
        public float Ember;
        public Color EmberTint;

        /// <summary>The bosses' looks by cast id.</summary>
        static readonly Dictionary<string, BossLook> Looks = new Dictionary<string, BossLook>
        {
            // Boss 1, the Treasure Goblin (ps-8z4.6): coin-gold glints on its facets and light running along its edges.
            { "treasureGoblin", new BossLook { Crystal = 0.7f, Flow = 0.35f, FlowSpeed = 1.6f } },
            // The Hive Queen (ps-8z4.8): light streaming along her edges like her drones' flight, a faint hologram's scan.
            { "hiveQueen", new BossLook { Flow = 0.7f, FlowSpeed = 1.6f, Scan = 0.25f } },
            // The Bloom (ps-8z4.10): sap flowing up its edges in its violet, and a hot ultraviolet heart.
            { "bloom", new BossLook { Flow = 0.65f, FlowSpeed = 0.7f, Ember = 0.5f, EmberTint = new Color(0.62f, 0.28f, 1f) } },
            // The Rootwarden (ps-8z4.12): sap light creeping slowly along its bark's edges, an ember of old wood in its core.
            { "rootwarden", new BossLook { Flow = 0.55f, FlowSpeed = 0.6f, Ember = 0.35f } },
            // Moonhowl (ps-8z4.14): a cold moonlight running its edges and a faint breathing fade, a beast half spirit.
            { "moonhowl", new BossLook { Flow = 0.45f, FlowSpeed = 0.9f, Ghost = 0.25f } },
        };

        public static bool TryGet(string castId, out BossLook look) => Looks.TryGetValue(castId, out look);

        /// <summary>Sets the look's gains on a BossEdge material.</summary>
        public void Apply(Material m)
        {
            m.SetFloat("_Scan", Scan);
            if (ScanDensity > 0) m.SetFloat("_ScanDensity", ScanDensity);
            if (ScanSpeed > 0) m.SetFloat("_ScanSpeed", ScanSpeed);
            m.SetFloat("_Crystal", Crystal);
            m.SetFloat("_Flow", Flow);
            if (FlowSpeed > 0) m.SetFloat("_FlowSpeed", FlowSpeed);
            if (FlowScale > 0) m.SetFloat("_FlowScale", FlowScale);
            m.SetFloat("_Glitch", Glitch);
            m.SetFloat("_Ghost", Ghost);
            m.SetFloat("_Ember", Ember);
            if (Ember > 0) m.SetColor("_EmberTint", EmberTint.linear);
        }
    }
}
