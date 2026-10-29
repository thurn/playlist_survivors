using System.Collections.Generic;
using Demo2D.Choreography;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Demo2D.Runtime.Playback
{
    /// <summary>The arena's ground for the player and the editor's stills (PX6).</summary>
    public static class ArenaGround
    {
        /// <summary>
        /// The arena's Tilemaps (PX6), composed from its JSON: base grass, a corner-autotiled overlay per
        /// ground material, the cliffs and the stream, then the bridge, all under the characters.
        /// </summary>
        public static GameObject Build(ForestArena arena, PlaybackData data, Transform parent, List<UnityEngine.Object> made)
        {
            var composed = ArenaTiles.Compose(arena, TileSheet.Parse(data.forestTiles.text));
            var bySheet = new Dictionary<Vector2Int, Sprite>();
            for (int i = 0; i < data.sheetCells.Length; i++) bySheet[data.sheetCells[i]] = data.sheetSprites[i];
            var tiles = new TileBase[composed.Cells.Count];
            for (int i = 0; i < tiles.Length; i++)
            {
                var (col, row) = composed.Cells[i];
                // An empty cell of the sheet (a bridge's post row between its posts) has no sprite.
                if (!bySheet.TryGetValue(new Vector2Int(col, row), out var sprite)) continue;
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tiles[i] = tile;
                made.Add(tile);
            }
            var root = new GameObject("Ground");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(arena.OriginX, arena.OriginY, 0);
            root.AddComponent<Grid>().cellSize = Vector3.one;
            for (int layer = 0; layer < composed.Layers.Length; layer++)
            {
                var go = new GameObject(composed.LayerNames[layer]);
                go.transform.SetParent(root.transform, false);
                var map = go.AddComponent<Tilemap>();
                var r = go.AddComponent<TilemapRenderer>();
                r.sharedMaterial = data.spriteLit;
                r.sortingOrder = GroundOrder + layer;
                r.mode = TilemapRenderer.Mode.Chunk;
                var cells = composed.Layers[layer];
                var block = new TileBase[cells.Length];
                for (int i = 0; i < cells.Length; i++) block[i] = cells[i] != ArenaTiles.None ? tiles[cells[i]] : null; // null where the cell is empty
                map.SetTilesBlock(new BoundsInt(0, 0, 0, composed.W, composed.H, 1), block);
            }
            return root;
        }

        /// <summary>The ground layers' sorting orders count up from here, under the shadows (-1) and every sprite.</summary>
        const int GroundOrder = -1100;
    }
}
