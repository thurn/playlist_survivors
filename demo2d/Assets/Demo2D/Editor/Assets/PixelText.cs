using System.Collections.Generic;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>A 3×5 pixel font for labelling evidence images (lower case, digits, a little punctuation).</summary>
    public static class PixelText
    {
        static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['a'] = "010101111101101", ['b'] = "110101110101110", ['c'] = "011100100100011", ['d'] = "110101101101110",
            ['e'] = "111100110100111", ['f'] = "111100110100100", ['g'] = "011100101101011", ['h'] = "101101111101101",
            ['i'] = "111010010010111", ['j'] = "001001001101010", ['k'] = "101101110101101", ['l'] = "100100100100111",
            ['m'] = "101111111101101", ['n'] = "110101101101101", ['o'] = "010101101101010", ['p'] = "110101110100100",
            ['q'] = "010101101110011", ['r'] = "110101110101101", ['s'] = "011100010001110", ['t'] = "111010010010010",
            ['u'] = "101101101101111", ['v'] = "101101101101010", ['w'] = "101101111111101", ['x'] = "101101010101101",
            ['y'] = "101101010010010", ['z'] = "111001010100111", ['0'] = "111101101101111", ['1'] = "010110010010111",
            ['2'] = "110001010100111", ['3'] = "110001010001110", ['4'] = "101101111001001", ['5'] = "111100110001110",
            ['6'] = "011100111101111", ['7'] = "111001010010010", ['8'] = "111101111101111", ['9'] = "111101111001110",
            ['_'] = "000000000000111", ['.'] = "000000000000010", ['-'] = "000000111000000", ['/'] = "001001010100100",
            [':'] = "000010000010000", ['*'] = "000101010101000", ['('] = "010100100100010", [')'] = "010001001001010",
            [','] = "000000000010100", ['+'] = "000010111010000", [' '] = "000000000000000",
        };

        public const int Advance = 4; // glyph width plus a pixel of space, before scaling

        /// <summary>Draws text into a bottom-up pixel buffer, its top-left corner at (x, yTop).</summary>
        public static void Draw(Color32[] px, int width, int height, int x, int yTop, string text, Color32 color, int scale = 2)
        {
            foreach (char raw in text.ToLowerInvariant())
            {
                if (!Glyphs.TryGetValue(raw, out var g)) g = Glyphs['.'];
                for (int r = 0; r < 5; r++)
                for (int c = 0; c < 3; c++)
                {
                    if (g[r * 3 + c] != '1') continue;
                    for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                    {
                        int px0 = x + c * scale + dx, py0 = yTop - r * scale - dy;
                        if (px0 >= 0 && px0 < width && py0 >= 0 && py0 < height) px[py0 * width + px0] = color;
                    }
                }
                x += Advance * scale;
            }
        }
    }
}
