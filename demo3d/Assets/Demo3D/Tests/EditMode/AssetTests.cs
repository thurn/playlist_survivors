using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Demo3D.EditorTools.Assets;
using Demo3D.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Demo3D.Tests
{
    /// <summary>TD2's roster, clip annotations and render tag.</summary>
    public class AssetTests
    {
        static readonly string[] Monster = { "walk", "attack", "hit", "death", "idle" };
        static readonly string[] Hero = { "walk", "run", "idle", "slashL", "slashR", "slash3", "thrust", "spin", "slam", "parry" };

        static bool VendorSynced => Directory.Exists("Assets/Vendor") && Directory.EnumerateFileSystemEntries("Assets/Vendor").Any();

        [Test]
        public void EveryRosterAssetResolves()
        {
            if (!VendorSynced) Assert.Ignore("the vendor folder is empty: run demo3d setup");
            foreach (var t in Roster.Load())
            {
                Assert.That(Roster.LoadPrefab(t.prefab), Is.Not.Null, t.id);
                foreach (var kv in t.clips)
                    Assert.That(Roster.LoadClip(kv.Value), Is.Not.Null, $"{t.id}.{kv.Key}");
            }
        }

        [Test]
        public void TheRosterHasNineTypesAndAHero()
        {
            var kinds = Roster.Load().GroupBy(t => t.kind).ToDictionary(g => g.Key, g => g.Count());
            Assert.That(kinds["fodder"], Is.EqualTo(6));
            Assert.That(kinds["elite"], Is.EqualTo(2));
            Assert.That(kinds["boss"], Is.EqualTo(1));
            Assert.That(kinds["hero"], Is.EqualTo(1));
        }

        [Test]
        public void EveryClipTheDemoUsesIsAnnotated()
        {
            var json = JObject.Parse(File.ReadAllText(ClipAnnotator.OutPath));
            var clips = json["annotations"].ToDictionary(c => (string)c["type"] + "." + (string)c["role"]);
            foreach (var t in Roster.Load().Where(t => t.kind != "rejected-boss"))
            {
                var roles = t.kind == "hero" ? Hero : Monster;
                foreach (var role in roles)
                {
                    Assert.That(clips.ContainsKey(t.id + "." + role), $"{t.id}.{role} is not annotated");
                    var c = clips[t.id + "." + role];
                    float length = (float)c["length"];
                    Assert.That(length, Is.GreaterThan(0), $"{t.id}.{role}");
                    if (role == "walk" || role == "run")
                        Assert.That((float?)c["strideM"], Is.GreaterThan(0.2f), $"{t.id}.{role} stride");
                    else if (role != "idle")
                    {
                        float impact = (float)c["impact"], end = (float)c["end"];
                        Assert.That(impact, Is.InRange(0f, length), $"{t.id}.{role} impact");
                        Assert.That(end, Is.InRange(impact, length + 0.02f), $"{t.id}.{role} end");
                    }
                }
                if (t.kind == "hero")
                    foreach (var combo in new[] { "slashL", "slashR", "slash3", "thrust", "spin", "slam" })
                        Assert.That(clips[t.id + "." + combo]["rootMotionM"], Is.Not.Null, $"{combo} root motion");
            }
        }

        [Test]
        public void SyntyImpactsFallInsideTheVendorsHitWindow()
        {
            // Synty marks each attack's hit with its own _Hit_ clip; the annotator's peak-speed
            // impact should land within a few frames after that clip starts.
            var json = JObject.Parse(File.ReadAllText(ClipAnnotator.OutPath));
            int checkedClips = 0;
            foreach (var c in json["annotations"].Where(c => c["vendorHit"] != null && c["impact"] != null && (string)c["role"] != "hit"))
            {
                float impact = (float)c["impact"], vendor = (float)c["vendorHit"];
                Assert.That(impact - vendor, Is.InRange(-0.02f, 0.2f), $"{c["type"]}.{c["role"]}");
                checkedClips++;
            }
            Assert.That(checkedClips, Is.GreaterThanOrEqualTo(6));
        }

        [Test]
        public void JpegTagIsACommentSegmentAfterTheStart()
        {
            var tex = new Texture2D(4, 4);
            var tagged = RenderTag.TagJpeg(tex.EncodeToJPG(), "test");
            Assert.That(tagged[0], Is.EqualTo(0xFF));
            Assert.That(tagged[3], Is.EqualTo(0xFE));
            Assert.That(Encoding.ASCII.GetString(tagged, 6, 9), Is.EqualTo("ps-render"));
            Assert.That(tex.LoadImage(tagged), Is.True);
        }

        [Test]
        public void PngTagIsATextChunkAfterTheHeader()
        {
            var tex = new Texture2D(4, 4);
            var tagged = RenderTag.TagPng(tex.EncodeToPNG(), "test");
            Assert.That(Encoding.ASCII.GetString(tagged, 33 + 4, 4), Is.EqualTo("tEXt"));
            Assert.That(Encoding.ASCII.GetString(tagged, 33 + 8, 9), Is.EqualTo("ps-render"));
            Assert.That(tex.LoadImage(tagged), Is.True);
        }
    }
}
