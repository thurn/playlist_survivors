using UnityEngine;

namespace Champions.Render
{
    /// <summary>
    /// The stills' type-ID pass (docs/champions/style.md, The type-ID pass), which `champions look` measures black share,
    /// the floor and the hero by. <see cref="Stills"/> draws the camera a second time with the global
    /// `_ChampionsIdPass` at 1, post and MSAA off, into a linear target; every Champions shader then writes its draw's
    /// code instead of its colour: `_ChampionsId` / 255 in red (Shaders/IdPass.hlsl's CHAMPIONS_ID_PASS). A drawer sets
    /// `_ChampionsId` on its material or property block, once per type. 0 is nothing drawn; the view json's table names
    /// the codes.
    /// </summary>
    public static class IdPass
    {
        public static readonly int On = Shader.PropertyToID("_ChampionsIdPass");
        public static readonly int Id = Shader.PropertyToID("_ChampionsId");

        public const int Hero = 250, Structure = 251, Effect = 252;

        /// <summary>A cast type's code: its index in enemies.json's cast, plus one.</summary>
        public static int Monster(int castIndex) => castIndex + 1;
    }
}
