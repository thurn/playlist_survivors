using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>
    /// A frame's rigid pieces in one opaque material, each a mesh placed with its colour, drawn as one instanced draw per
    /// mesh and submesh from a buffer of their placements rather than a draw each: the weapon pieces in MeleeEdge (the
    /// melee, magic and construct visuals'), the flights in RangedFacet, the experience pickups in Pickup, which read
    /// their piece by its instance (Shaders/Pieces.hlsl). A full build's frame holds hundreds of them: drawn one by one
    /// they were most of the frame's CPU submission and a third of the weapon effects' GPU time (ps-6y2.22). Opaque and
    /// depth-tested, so the order they draw in changes nothing but exact depth ties.
    /// </summary>
    public sealed class PieceBatch : IDisposable
    {
        /// <summary>One piece as Pieces.hlsl reads it: its object-to-world matrix and its colour (linear rgb).</summary>
        [StructLayout(LayoutKind.Sequential)]
        struct Piece
        {
            public Matrix4x4 Matrix;
            public Vector4 Light;
            public const int Stride = 80;
        }

        static readonly int IdPieces = Shader.PropertyToID("_Pieces");
        static readonly int IdBase = Shader.PropertyToID("_PieceBase");

        readonly List<(Mesh Mesh, List<Piece> Pieces)> _groups = new List<(Mesh, List<Piece>)>();
        readonly List<(Mesh Mesh, MaterialPropertyBlock Block, int Count)> _draws = new List<(Mesh, MaterialPropertyBlock, int)>();
        readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
        Piece[] _packed = new Piece[64];
        GraphicsBuffer _buffer;

        /// <summary>Pieces added since the last <see cref="Clear"/>.</summary>
        public int Count { get; private set; }

        public void Clear()
        {
            foreach (var g in _groups) g.Pieces.Clear();
            Count = 0;
        }

        /// <summary>A piece of <paramref name="mesh"/> placed by <paramref name="matrix"/>, coloured <paramref name="light"/>
        /// (a gamma colour, as a material's is given: it goes to the shader linear).</summary>
        public void Add(Mesh mesh, Matrix4x4 matrix, Color light)
        {
            List<Piece> pieces = null;
            foreach (var g in _groups)
                if (g.Mesh == mesh)
                {
                    pieces = g.Pieces;
                    break;
                }
            if (pieces == null) _groups.Add((mesh, pieces = new List<Piece>()));
            var linear = light.linear;
            pieces.Add(new Piece { Matrix = matrix, Light = new Vector4(linear.r, linear.g, linear.b, 1) });
            Count++;
        }

        /// <summary>Uploads the pieces and queues their draws for the camera in <paramref name="material"/>.</summary>
        public void Queue(Material material, Camera camera)
        {
            Upload();
            foreach (var (mesh, block, count) in _draws)
            {
                var rp = Effects.Params(material, block, camera);
                for (int sub = 0; sub < mesh.subMeshCount; sub++) Graphics.RenderMeshPrimitives(rp, mesh, sub, count);
            }
        }

        void Upload()
        {
            _draws.Clear();
            if (Count == 0) return;
            if (_packed.Length < Count) Array.Resize(ref _packed, Mathf.NextPowerOfTwo(Count));
            if (_buffer == null || _buffer.count < Count)
            {
                _buffer?.Dispose();
                _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _packed.Length, Piece.Stride) { name = "Pieces" };
            }
            int at = 0;
            foreach (var (mesh, pieces) in _groups)
            {
                if (pieces.Count == 0) continue;
                if (_blocks.Count <= _draws.Count)
                {
                    var b = new MaterialPropertyBlock();
                    b.SetFloat(IdPass.Id, IdPass.Effect);
                    _blocks.Add(b);
                }
                var block = _blocks[_draws.Count];
                block.SetBuffer(IdPieces, _buffer);
                block.SetInt(IdBase, at);
                _draws.Add((mesh, block, pieces.Count));
                pieces.CopyTo(_packed, at);
                at += pieces.Count;
            }
            _buffer.SetData(_packed, 0, 0, at);
        }

        public void Dispose()
        {
            _buffer?.Dispose();
            _buffer = null;
        }
    }
}
