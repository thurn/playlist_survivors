using System;
using System.Collections.Generic;
using Game.Client.Loop;
using PS.Game.Sim.Level;
using PS.Look;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Client.Arena
{
    /// <summary>
    /// The baked arena drawn in the look's world kit (neon-look.md, The Axes &gt; World), once it is ready. Its layout,
    /// structures and footprints stay the bake's; only the drawing changes:
    /// - a world option built (<see cref="VoidWorld"/>): the Synty set under Static (everything but the structure sites),
    ///   the mist and the flames' lights go, the fog and the camera's clear colour are the palette's void, and the grid
    ///   floor takes the ground's place (GridFloor);
    /// - a structure look built (<see cref="StructureLook"/>): each structure site's kit pieces give way to crystal kit
    ///   pieces standing on the site's tiles in the level data (the build's own arena.json, as the arena drawn is the
    ///   build's), in the look's material, with their own shards (<see cref="CrystalProps"/>); without one, the kit
    ///   pieces turn crystal as before (<see cref="CrystalStructures"/>).
    /// The static shadow map renders again from what still stands. <see cref="Sites"/> and <see cref="Sets"/> are the
    /// structures for the shatter, by the level data's site index.
    /// </summary>
    public sealed class VoidArena : IDisposable
    {
        public readonly VoidWorld World;
        public readonly StructureLook Structures;
        readonly CrystalProps _props;
        readonly CrystalStructures _crystal;

        /// <summary>Each site's renderers for the shatter: the crystal props', or the arena's own kit pieces.</summary>
        public readonly IReadOnlyList<Renderer[]> Sites;
        /// <summary>Each site renderer's shards (the crystal props'), or null for the baked ones of the kit's meshes.</summary>
        public IReadOnlyList<StructureShards.Set[]> Sets => _props?.Sets;

        public VoidArena(ArenaRuntime arena, ResolvedLook look, Camera camera)
        {
            World = VoidWorld.For(look);
            Structures = StructureLook.For(look);
            var stat = arena.Root.Find("Static");
            var sites = stat.Find("Structures");
            if (World != null)
            {
                var set = new List<Renderer>();
                foreach (var r in stat.GetComponentsInChildren<Renderer>(true))
                    if (sites == null || !r.transform.IsChildOf(sites)) set.Add(r);
                if (arena.Mist != null) set.Add(arena.Mist);
                VoidWorld.Hide(set, arena.Lights);
                World.Apply(camera);
            }
            if (Structures != null)
            {
                if (sites != null) VoidWorld.Hide(sites.GetComponentsInChildren<Renderer>(true));
                _props = new CrystalProps(arena.Root, Structures.Material);
                var level = LevelBake.Load()?.Arena();
                bool tiled = level != null && level.Structures.Length == arena.Sites.Count;
                if (!tiled) Debug.LogWarning("VoidArena: the build's level data doesn't list the arena's sites: crystal props fill each site's bounds");
                for (int i = 0; i < arena.Sites.Count; i++)
                {
                    var site = tiled ? level.Structures[i] : null;
                    _props.Add(tiled ? Tiles(level, site) : Tiles(arena.Sites[i], (float)(level?.TileM ?? 1.5)), (float)(level?.TileM ?? 1.5),
                        site != null && site.Large, 7919 * i + 17);
                }
                Sites = _props.Sites;
                if (Structures.Refracts && camera != null && camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                    data.requiresColorOption = CameraOverrideOption.On;
                arena.Restage(_props.Renderers);
                Debug.Log($"VoidArena: {arena.Sites.Count} sites as {_props.Renderers.Count} crystal pieces in {Structures.Option}" +
                          (World != null ? $", world {World.Option}" : ""));
            }
            else
            {
                _crystal = new CrystalStructures();
                _crystal.Apply(arena, camera);
                Sites = arena.Sites;
                if (World != null) arena.Restage(new List<Renderer>(arena.Structures));
            }
        }

        /// <summary>A site's tiles' centres on the ground.</summary>
        static List<Vector2> Tiles(ArenaData level, StructureSite site)
        {
            var tiles = new List<Vector2>(site.Tiles.Length);
            foreach (var t in site.Tiles)
                tiles.Add(new Vector2((float)(level.OriginX + (t.X + 0.5) * level.TileM), (float)(level.OriginY + (t.Y + 0.5) * level.TileM)));
            return tiles;
        }

        /// <summary>The tiles a site's pieces cover, from their bounds: the centres of the <paramref name="tileM"/> grid inside them.</summary>
        static List<Vector2> Tiles(Renderer[] site, float tileM)
        {
            var tiles = new List<Vector2>();
            if (site.Length == 0) return tiles;
            var b = site[0].bounds;
            foreach (var r in site) b.Encapsulate(r.bounds);
            for (float x = Mathf.Floor(b.min.x / tileM) * tileM; x < b.max.x; x += tileM)
                for (float z = Mathf.Floor(b.min.z / tileM) * tileM; z < b.max.z; z += tileM)
                    tiles.Add(new Vector2(x + 0.5f * tileM, z + 0.5f * tileM));
            return tiles;
        }

        public void Dispose()
        {
            _props?.Dispose();
            _crystal?.Dispose();
            Structures?.Dispose();
        }
    }
}
