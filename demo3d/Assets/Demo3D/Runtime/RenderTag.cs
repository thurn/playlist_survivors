using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Demo3D.Runtime
{
    /// <summary>
    /// Marks an image as our own render with the `ps-render` text tag, which the vendor guard
    /// requires of every PNG or JPEG under demo3d/evidence/ (vendor-assets.md, The vendor guard):
    /// a PNG tEXt chunk keyed `ps-render`, or a JPEG comment segment starting `ps-render`.
    /// </summary>
    public static class RenderTag
    {
        const string Key = "ps-render";

        public static byte[] TagJpeg(byte[] jpeg, string tool)
        {
            if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
                throw new ArgumentException("not a JPEG");
            var text = Encoding.ASCII.GetBytes(Key + " " + tool);
            int len = text.Length + 2;
            var output = new byte[jpeg.Length + 2 + len];
            output[0] = 0xFF;
            output[1] = 0xD8;
            output[2] = 0xFF;
            output[3] = 0xFE; // COM
            output[4] = (byte)(len >> 8);
            output[5] = (byte)(len & 0xFF);
            Buffer.BlockCopy(text, 0, output, 6, text.Length);
            Buffer.BlockCopy(jpeg, 2, output, 6 + text.Length, jpeg.Length - 2);
            return output;
        }

        public static byte[] TagPng(byte[] png, string tool)
        {
            const int signature = 8, ihdr = 8 + 13 + 4;
            if (png.Length < signature + ihdr || png[1] != (byte)'P' || png[2] != (byte)'N')
                throw new ArgumentException("not a PNG");
            var body = Encoding.ASCII.GetBytes(Key + "\0" + tool);
            var type = Encoding.ASCII.GetBytes("tEXt");
            var chunk = new byte[12 + body.Length];
            WriteBE(chunk, 0, (uint)body.Length);
            Buffer.BlockCopy(type, 0, chunk, 4, 4);
            Buffer.BlockCopy(body, 0, chunk, 8, body.Length);
            WriteBE(chunk, 8 + body.Length, Crc(chunk, 4, 4 + body.Length));
            int at = signature + ihdr; // right after IHDR
            var output = new byte[png.Length + chunk.Length];
            Buffer.BlockCopy(png, 0, output, 0, at);
            Buffer.BlockCopy(chunk, 0, output, at, chunk.Length);
            Buffer.BlockCopy(png, at, output, at + chunk.Length, png.Length - at);
            return output;
        }

        /// <summary>Encodes a texture as a tagged JPEG (or PNG for `.png`) and writes it.</summary>
        public static void Save(Texture2D tex, string path, string tool, int jpegQuality = 88)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var bytes = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? TagPng(tex.EncodeToPNG(), tool)
                : TagJpeg(tex.EncodeToJPG(jpegQuality), tool);
            File.WriteAllBytes(path, bytes);
        }

        static void WriteBE(byte[] b, int at, uint v)
        {
            b[at] = (byte)(v >> 24);
            b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8);
            b[at + 3] = (byte)v;
        }

        static uint Crc(byte[] b, int start, int count)
        {
            uint c = 0xFFFFFFFF;
            for (int i = start; i < start + count; i++)
            {
                c ^= b[i];
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            return c ^ 0xFFFFFFFF;
        }
    }
}
