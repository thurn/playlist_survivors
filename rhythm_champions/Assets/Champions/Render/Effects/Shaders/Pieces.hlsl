// The pieces PieceBatch draws at once, one instance each (MeleeEdge, RangedFacet, Pickup): its placement and colour.
#ifndef CHAMPIONS_PIECES_INCLUDED
#define CHAMPIONS_PIECES_INCLUDED

// Matches Champions.Render.PieceBatch.Piece (80 bytes): object to world, and the piece's colour (linear rgb).
struct Piece { float4x4 objectToWorld; float4 colour; };
StructuredBuffer<Piece> _Pieces;
float _PieceBase; // the draw's first piece

Piece PieceOf(uint instance) { return _Pieces[(uint)_PieceBase + instance]; }

#endif
