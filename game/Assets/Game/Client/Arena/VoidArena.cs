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
    ///   floor takes the ground's place (GridFloor); the tiles the level data blocks with no structure on them, whose
    ///   trees, rocks and forest edge went with the set, are marked (<see cref="Blocked"/>): low crystal hedges on every
    ///   one beside walkable ground, in the structure look's terrain, and no grid on the floor over them (<see cref="Lay"/>);
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
        /// <summary>The level's impassable ground, marked under a world option; null otherwise (the arena's set shows it).</summary>
        public readonly BlockedGround Blocked;
        /// <summary>The terrain's look when the look names no structure look of its own.</summary>
        readonly StructureLook _terrain;
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
            var level = LevelBake.Load()?.Arena();
            if (World != null)
            {
                var set = new List<Renderer>();
                foreach (var r in stat.GetComponentsInChildren<Renderer>(true))
                    if (sites == null || !r.transform.IsChildOf(sites)) set.Add(r);
                if (arena.Mist != null) set.Add(arena.Mist);
                VoidWorld.Hide(set, arena.Lights);
                World.Apply(camera);
                if (level != null)
                {
                    var terrain = Structures ?? (_terrain = new StructureLook(StructureLook.EdgeGlass, look.Palette));
                    Blocked = Blocking(level, look, terrain.Terrain, arena.Root);
                    World.Terrain = terrain;
                    Debug.Log($"VoidArena: {level.Blocked.Length} blocked tiles, {Blocked.RimTiles} beside walkable ground under {Blocked.Pieces} crystal pieces");
                }
                else Debug.LogWarning("VoidArena: the build has no level data: its blocked ground stays unmarked");
            }
            if (Structures != null)
            {
                if (sites != null) VoidWorld.Hide(sites.GetComponentsInChildren<Renderer>(true));
                _props = new CrystalProps(arena.Root, Structures.Material);
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

        /// <summary>
        /// The impassable ground <paramref name="look"/> marks in <paramref name="terrain"/>: under a world option, the
        /// tiles <paramref name="level"/> blocks with no structure on them (structures stand as their own props); null
        /// under none, whose arena set shows them.
        /// </summary>
        public static BlockedGround Blocking(ArenaData level, ResolvedLook look, Material terrain, Transform parent = null)
        {
            if (level == null || look?.Option(LookAxes.World) == null) return null;
            var blocked = new bool[level.Width * level.Height];
            foreach (var t in level.Blocked) blocked[t.Y * level.Width + t.X] = true;
            return new BlockedGround(parent, terrain, blocked, level.Width, level.Height,
                new Vector2((float)level.OriginX, (float)level.OriginY), (float)level.TileM, 4099);
        }

        /// <summary>Lays <paramref name="floor"/> (the look's grid floor, or null) as the void world's ground: zones tint its lines and the blocked ground shows no grid.</summary>
        public void Lay(GridFloor floor)
        {
            if (World == null) return;
            World.Floor = floor;
            if (floor != null && Blocked != null) floor.SetBlocked(Blocked);
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
            Blocked?.Dispose();
            Structures?.Dispose();
            _terrain?.Dispose();
        }
    }
}
