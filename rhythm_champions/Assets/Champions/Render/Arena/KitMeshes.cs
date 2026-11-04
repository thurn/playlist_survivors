using System;
using System.Collections.Generic;
using UnityEngine;

namespace Champions.Render.Arena
{
    /// <summary>
    /// One piece of a Boss Rush act kit (boss-rush.md, Environments): a designed low-poly mesh in its own metres (y up from
    /// the ground, its middle at the origin, lying along x), each triangle's three drawn-edge flags (the edge opposite
    /// each corner), as `champions kit bake` writes it from tools/recipes/kit/KIT.py.
    /// </summary>
    public sealed class KitPiece
    {
        public Vector3[] Points;
        public int[] Triangles;
        public byte[] Edges;
        /// <summary>How far it reaches from its middle over the ground (m).</summary>
        public float Reach;

        /// <summary>Reads a piece's bytes: vertex and triangle counts, positions, triangles, flags (little-endian).</summary>
        public static KitPiece Read(byte[] bytes, string name)
        {
            if (bytes == null || bytes.Length < 8) throw new FormatException($"kit piece {name}: no header");
            int n = BitConverter.ToInt32(bytes, 0), m = BitConverter.ToInt32(bytes, 4);
            long want = 8L + n * 12L + m * 12L + m * 3L;
            if (n <= 0 || m <= 0 || bytes.Length != want) throw new FormatException($"kit piece {name}: {bytes.Length} bytes, expected {want}");
            var piece = new KitPiece { Points = new Vector3[n], Triangles = new int[m * 3], Edges = new byte[m * 3] };
            int at = 8;
            for (int i = 0; i < n; i++, at += 12)
            {
                var p = new Vector3(BitConverter.ToSingle(bytes, at), BitConverter.ToSingle(bytes, at + 4), BitConverter.ToSingle(bytes, at + 8));
                piece.Points[i] = p;
                piece.Reach = Mathf.Max(piece.Reach, new Vector2(p.x, p.z).magnitude);
            }
            for (int i = 0; i < m * 3; i++, at += 4)
            {
                int v = BitConverter.ToInt32(bytes, at);
                if (v < 0 || v >= n) throw new FormatException($"kit piece {name}: a triangle indexes past its corners");
                piece.Triangles[i] = v;
            }
            Array.Copy(bytes, at, piece.Edges, 0, m * 3);
            return piece;
        }
    }

    /// <summary>
    /// The baked act kits' pieces, from Resources/Kits/KIT/ (the ignored Assets/Baked/Kits/, which `champions kit bake`
    /// and `champions build` write), each kit read once. A kit that isn't baked has no pieces, and the arena draws its
    /// placeholder solids.
    /// </summary>
    public static class KitMeshes
    {
        public const string Root = "Kits/";
        static readonly Dictionary<string, Dictionary<string, KitPiece>> Loaded = new Dictionary<string, Dictionary<string, KitPiece>>();

        public static IReadOnlyDictionary<string, KitPiece> Load(string kit)
        {
            if (Loaded.TryGetValue(kit, out var pieces)) return pieces;
            pieces = new Dictionary<string, KitPiece>();
            foreach (var asset in Resources.LoadAll<TextAsset>(Root + kit)) pieces[asset.name] = KitPiece.Read(asset.bytes, $"{kit}/{asset.name}");
            Loaded[kit] = pieces;
            return pieces;
        }
    }
}
