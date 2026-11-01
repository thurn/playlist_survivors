using System;
using System.Collections.Generic;
using PS.Look;
using UnityEngine;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// A crystal-cut environment drawn in the look's world kit (neon-look.md, The Axes &gt; World and The Trailer), once
    /// its scene is ready. The layout, and so the horde's routes, stay the environment's; only the drawing changes:
    /// - a world option built: its whole Synty set under Static, its mist and its flames' lights go (the grid floor takes
    ///   the ground's place: GridFloor), and its static shadow map renders again from nothing left standing;
    /// - a structure look built: a crystal kit piece stands where each of its structures stood (its Static/Structures
    ///   pieces, the walls the horde routes round; not its landmarks, whose wide footprints buried the shots' formations
    ///   in crystal), on the tiles their bounds cover (<see cref="CrystalProps"/>), in the look's material, on the scene's
    ///   resident layer.
    /// The fog and clear colour are the world's (<see cref="VoidWorld.Apply"/>, on every switch to the environment).
    /// The trailer's formations are the shots' own (StructureShatter).
    /// </summary>
    public sealed class VoidArena : IDisposable
    {
        /// <summary>The tile a prop's footprint is cut into, as the game's (m).</summary>
        public const float TileM = 1.5f;

        readonly CrystalProps _props;

        public VoidArena(ArenaRuntime arena, VoidWorld world, StructureLook structures)
        {
            var stat = arena.Root.Find("Static");
            var pieces = new List<Transform>();
            var structuresGroup = stat.Find("Structures");
            if (structuresGroup != null) foreach (Transform t in structuresGroup) pieces.Add(t);
            if (structures != null)
            {
                _props = new CrystalProps(arena.Root, structures.Material, arena.Layer >= 0 ? arena.Layer : 0);
                foreach (var piece in pieces)
                {
                    var tiles = Tiles(piece.GetComponentsInChildren<MeshRenderer>(true));
                    if (tiles.Count > 0) _props.Add(tiles, TileM, tiles.Count >= 9, _props.Sites.Count * 7919 + 31);
                }
            }
            if (world != null)
            {
                var hidden = new List<Renderer>(stat.GetComponentsInChildren<Renderer>(true));
                var mist = arena.Root.Find("Mist");
                if (mist != null) hidden.AddRange(mist.GetComponentsInChildren<Renderer>(true));
                VoidWorld.Hide(hidden);
                arena.Unlight();
                arena.RefreshShadowMap();
            }
            else if (_props != null)
                foreach (var piece in pieces)
                    VoidWorld.Hide(piece.GetComponentsInChildren<Renderer>(true));
            Debug.Log($"VoidArena: {arena.SceneName}" + (world != null ? $" in {world.Option}" : "") +
                      (_props != null ? $", {pieces.Count} structures as {_props.Renderers.Count} crystal pieces in {structures.Option}" : ""));
        }

        /// <summary>The centres of the tiles a piece's renderers cover on the ground.</summary>
        static List<Vector2> Tiles(MeshRenderer[] renderers)
        {
            var tiles = new List<Vector2>();
            if (renderers.Length == 0) return tiles;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            for (float x = Mathf.Floor(b.min.x / TileM) * TileM; x < b.max.x; x += TileM)
                for (float z = Mathf.Floor(b.min.z / TileM) * TileM; z < b.max.z; z += TileM)
                    tiles.Add(new Vector2(x + 0.5f * TileM, z + 0.5f * TileM));
            return tiles;
        }

        public void Dispose() => _props?.Dispose();
    }
}
