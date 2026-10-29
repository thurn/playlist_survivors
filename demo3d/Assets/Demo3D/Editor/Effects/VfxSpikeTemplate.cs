using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Demo3D.EditorTools.Effects
{
    /// <summary>
    /// TD8's runtime spike, VFX Graph arm (effects.md, Choosing the GPU runtime): writes the
    /// spike's template by editing a copy of VFX Graph 17.6's Simple Burst template as text, the
    /// route the design allows an agent. The edits: the burst's count, lifetime, speeds and sphere
    /// match the spike burst; gravity and drag leave the update; and one block, Set Position from
    /// the spawn event's source attribute (added to the sphere), joins Initialize, so each
    /// SendEvent places its burst. Output: Assets/Baked/Spike/Resources/SpikeBurst.vfx (the package
    /// template stays unmodified; nothing here is tracked but this script).
    /// </summary>
    public static class VfxSpikeTemplate
    {
        public const string OutPath = "Assets/Baked/Spike/Resources/SpikeBurst.vfx";

        public static void Write()
        {
            var src = Path.Combine(EditorApplication.applicationContentsPath,
                "Resources/PackageManager/BuiltInPackages/com.unity.visualeffectgraph/Editor/Templates/Simple_Burst.vfx");
            var s = File.ReadAllText(src).Replace("\r\n", "\n");
            // Burst count 128 -> 20.
            s = SlotValue(s, "8926484042661614703", "Count", "128", "20");
            // Lifetime 0.5..1 -> 0.35..0.8; speeds 0.5..4.5 -> 2..5.
            s = SlotValue(s, "8926484042661614722", "A", "0.5", "0.35");
            s = SlotValue(s, "8926484042661614722", "B", "1", "0.8");
            s = SlotValue(s, "8926484042661614912", "MinSpeed", "0.5", "2");
            s = SlotValue(s, "8926484042661614912", "MaxSpeed", "4.5", "5");
            // Room for a heavy beat (12k alive, as our runtime's run), bounds over the whole bench
            // ground and no culling, since spawns land anywhere in the view.
            s = Once(s, "  capacity: 128\n", "  capacity: 65536\n");
            s = Once(s, "\"size\":{\"x\":7.0,\"y\":8.0,\"z\":7.0}", "\"size\":{\"x\":600.0,\"y\":60.0,\"z\":600.0}");
            s = Once(s, "    m_CullingFlags: 3\n", "    m_CullingFlags: 0\n");
            // Gravity and drag out of Update (our burst has neither).
            s = Once(s, "  - {fileID: 8926484042661614922}\n  - {fileID: 8926484042661614929}\n", "");
            // Set Position (Add, from the event's source attribute) after the shape block.
            const string NewBlock = "8926484042661699001", NewActivation = "8926484042661699002";
            s = Once(s, "  - {fileID: 8926484042661614912}\n", "  - {fileID: 8926484042661614912}\n  - {fileID: " + NewBlock + "}\n");
            var block = Doc(s, "8926484042661614722");
            block = block.Replace("&8926484042661614722", "&" + NewBlock);
            block = Regex.Replace(block, @"  m_InputSlots:\n(  - \{fileID: -?\d+\}\n)+", "  m_InputSlots: []\n");
            block = Regex.Replace(block, @"m_ActivationSlot: \{fileID: -?\d+\}", "m_ActivationSlot: {fileID: " + NewActivation + "}");
            block = Regex.Replace(block, @"attribute: lifetime", "attribute: position");
            block = Regex.Replace(block, @"  Composition: 0", "  Composition: 1");
            block = Regex.Replace(block, @"  Source: 0", "  Source: 1");
            block = Regex.Replace(block, @"  Random: \d+", "  Random: 0");
            var act = Doc(s, Regex.Match(Doc(s, "8926484042661614722"), @"m_ActivationSlot: \{fileID: (-?\d+)\}").Groups[1].Value);
            var actId = Regex.Match(act, @"^--- !u!114 &(-?\d+)", RegexOptions.Multiline).Groups[1].Value;
            act = act.Replace("&" + actId, "&" + NewActivation).Replace("m_Owner: {fileID: 8926484042661614722}", "m_Owner: {fileID: " + NewBlock + "}");
            act = Regex.Replace(act, @"m_MasterSlot: \{fileID: -?\d+\}", "m_MasterSlot: {fileID: " + NewActivation + "}");
            s = s.TrimEnd('\n') + "\n" + block.TrimEnd('\n') + "\n" + act.TrimEnd('\n') + "\n";
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, s);
            AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("VfxSpikeTemplate: wrote " + OutPath);
        }

        static string Doc(string s, string id)
        {
            var m = Regex.Match(s, @"^--- !u!114 &" + id + @"\n(.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline);
            if (!m.Success) throw new Exception("VfxSpikeTemplate: no object " + id + " in the template");
            return m.Value;
        }

        static string Once(string s, string a, string b)
        {
            int i = s.IndexOf(a, StringComparison.Ordinal);
            if (i < 0) throw new Exception("VfxSpikeTemplate: the template changed; missing: " + a.Trim());
            return s.Substring(0, i) + b + s.Substring(i + a.Length);
        }

        /// <summary>Sets a block's input slot value (a slot named <paramref name="name"/> owned by the block).</summary>
        static string SlotValue(string s, string owner, string name, string from, string to)
        {
            var block = Doc(s, owner);
            foreach (Match m in Regex.Matches(block.Split(new[] { "m_OutputSlots" }, StringSplitOptions.None)[0], @"- \{fileID: (-?\d+)\}"))
            {
                var slot = Doc(s, m.Groups[1].Value);
                if (!Regex.IsMatch(slot, @"name: " + name + @"\n")) continue;
                var edited = Regex.Replace(slot, @"m_SerializableObject: " + Regex.Escape(from) + @"\n", "m_SerializableObject: " + to + "\n");
                if (edited == slot) throw new Exception($"VfxSpikeTemplate: slot {name} of {owner} isn't {from}");
                return s.Replace(slot, edited);
            }
            throw new Exception($"VfxSpikeTemplate: no slot {name} on {owner}");
        }
    }
}
