using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>
    /// Marks an image as our own render with the `ps-render` text tag, which the vendor guard
    /// requires of every JPEG under rhythm_champions/evidence/ (docs/archive/tech-demo-3d/vendor-assets.md):
    /// a JPEG comment segment starting `ps-render`.
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

        /// <summary>Encodes a texture as a tagged JPEG and writes it.</summary>
        public static void SaveJpeg(Texture2D tex, string path, string tool, int quality = 88)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, TagJpeg(tex.EncodeToJPG(quality), tool));
        }
    }
}
