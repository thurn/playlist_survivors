using System.Text;
using Demo2D.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Demo2D.Tests
{
    /// <summary>Stills carry the `ps-render` tag the vendor guard requires under demo2d/evidence/.</summary>
    public class RenderTagTests
    {
        [Test]
        public void ATaggedPngHasThePsRenderTextChunkAfterItsHeader()
        {
            var tex = new Texture2D(2, 2);
            var png = RenderTag.TagPng(tex.EncodeToPNG(), "test");
            Object.DestroyImmediate(tex);
            Assert.That(Encoding.ASCII.GetString(png, 8 + 25 + 4, 4), Is.EqualTo("tEXt"));
            Assert.That(Encoding.ASCII.GetString(png, 8 + 25 + 8, 10), Is.EqualTo("ps-render\0"));
        }

        [Test]
        public void ATaggedJpegStartsWithThePsRenderComment()
        {
            var tex = new Texture2D(2, 2);
            var jpeg = RenderTag.TagJpeg(tex.EncodeToJPG(), "test");
            Object.DestroyImmediate(tex);
            Assert.That(jpeg[2], Is.EqualTo(0xFF));
            Assert.That(jpeg[3], Is.EqualTo(0xFE));
            Assert.That(Encoding.ASCII.GetString(jpeg, 6, 9), Is.EqualTo("ps-render"));
        }
    }
}
