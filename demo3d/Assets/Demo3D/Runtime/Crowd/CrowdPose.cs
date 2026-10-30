using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The bone-texture crowd's skinning on the CPU, for the crystal shatter (CrystalShatter), which
    /// poses a type's baked chunks to the pose a monster is in when it breaks. It mirrors the GPU's
    /// arithmetic step for step, so the chunks land where the crowd drew the monster:
    /// - <see cref="Bones"/> is CrowdSkinning.hlsl's SampleOf and BlendBone (CrowdPalette.compute):
    ///   each clip's two texture rows around its time, lerped, then the two clips crossfaded;
    /// - <see cref="Skin"/> is CrowdCommon.hlsl's SkinVertex: the weighted bone matrices, the point
    ///   scaled by the instance, turned by its yaw and moved to its position.
    /// It reads the texture the crowd draws with: 30 Hz under the default cuts' clips30, else 60 Hz
    /// (BoneTextureCrowd). The baked bone textures stay readable (CrowdBaker).
    /// </summary>
    public sealed class CrowdPose
    {
        readonly Vector4[] _texels;  // bone b's row r of frame f is texel (3b + r, f)
        readonly int _width;
        readonly Vector4[] _clips;   // per clip: first row, frames, length, loop
        public readonly float Rate;
        public readonly int BoneCount;

        public CrowdPose(CrowdType t, bool clips30)
        {
            clips30 &= t.Bones30 != null;
            var tex = clips30 ? t.Bones30 : t.Bones;
            _texels = tex.GetPixelData<Vector4>(0).ToArray();
            _width = tex.width;
            Rate = clips30 ? CrowdLibrary.CutClipRate : CrowdLibrary.ClipRate;
            BoneCount = t.BoneCount;
            _clips = new Vector4[t.Clips.Length];
            for (int i = 0; i < _clips.Length; i++)
            {
                var c = t.Clips[i];
                _clips[i] = clips30 ? new Vector4(c.FirstRow30, c.Frames30, c.Length, c.Loop ? 1 : 0) : new Vector4(c.FirstRow, c.Frames, c.Length, c.Loop ? 1 : 0);
            }
        }

        /// <summary>A type's pose as the crowd draws it under the active cuts (BoneTextureCrowd).</summary>
        public static CrowdPose Of(CrowdType t) => new CrowdPose(t, Cuts.Active.On(Cuts.Clips30));

        /// <summary>CrowdSkinning.hlsl's ClipRows: the two rows around a clip time and the fraction between them.</summary>
        void ClipRows(uint clip, float t, out int row0, out float frac)
        {
            var c = _clips[clip];
            float len = c.z;
            if (c.w > 0.5f) t = len > 0 ? t - Mathf.Floor(t / len) * len : 0;
            else t = Mathf.Clamp(t, 0, len);
            uint frames = (uint)c.y;
            uint f0 = System.Math.Min((uint)(t * Rate), frames - 2);
            float t0 = f0 / Rate;
            float t1 = Mathf.Min((f0 + 1) / Rate, len);
            frac = Mathf.Clamp01((t - t0) / Mathf.Max(t1 - t0, 1e-5f));
            row0 = (int)c.x + (int)f0;
        }

        /// <summary>Every bone's skinning matrix for an instance (rows 0..2; row 3 is 0 0 0 1).</summary>
        public void Bones(in MonsterInstance m, Matrix4x4[] into)
        {
            ClipRows(m.ClipA, m.TimeA, out int a0, out float fa);
            ClipRows(m.ClipB, m.TimeB, out int b0, out float fb);
            float blend = Mathf.Clamp01(m.Blend);
            for (int bone = 0; bone < BoneCount; bone++)
            {
                var r = new Matrix4x4();
                for (int row = 0; row < 3; row++)
                {
                    var x = _texels[a0 * _width + bone * 3 + row];
                    var y = _texels[(a0 + 1) * _width + bone * 3 + row];
                    var a = x + (y - x) * fa;
                    if (blend < 0.9999f)
                    {
                        var u = _texels[b0 * _width + bone * 3 + row];
                        var v = _texels[(b0 + 1) * _width + bone * 3 + row];
                        a = a * blend + (u + (v - u) * fb) * (1 - blend);
                    }
                    r.SetRow(row, a);
                }
                r.m33 = 1;
                into[bone] = r;
            }
        }

        /// <summary>A rest-pose corner's skinning matrix: its bones' matrices by its weights (SkinVertex).</summary>
        public static Matrix4x4 Blend(Matrix4x4[] bones, Vector4 index, Vector4 weight)
        {
            var s = Scaled(in bones[(int)index.x], weight.x);
            if (weight.y > 0) Add(ref s, in bones[(int)index.y], weight.y);
            if (weight.z > 0) Add(ref s, in bones[(int)index.z], weight.z);
            if (weight.w > 0) Add(ref s, in bones[(int)index.w], weight.w);
            s.m33 = 1;
            return s;
        }

        static Matrix4x4 Scaled(in Matrix4x4 m, float w) => new Matrix4x4
        {
            m00 = m.m00 * w, m01 = m.m01 * w, m02 = m.m02 * w, m03 = m.m03 * w,
            m10 = m.m10 * w, m11 = m.m11 * w, m12 = m.m12 * w, m13 = m.m13 * w,
            m20 = m.m20 * w, m21 = m.m21 * w, m22 = m.m22 * w, m23 = m.m23 * w,
        };

        static void Add(ref Matrix4x4 s, in Matrix4x4 m, float w)
        {
            s.m00 += m.m00 * w; s.m01 += m.m01 * w; s.m02 += m.m02 * w; s.m03 += m.m03 * w;
            s.m10 += m.m10 * w; s.m11 += m.m11 * w; s.m12 += m.m12 * w; s.m13 += m.m13 * w;
            s.m20 += m.m20 * w; s.m21 += m.m21 * w; s.m22 += m.m22 * w; s.m23 += m.m23 * w;
        }

        /// <summary>A root-space point skinned by <paramref name="skin"/> and placed by the instance (SkinVertex).</summary>
        public static Vector3 Place(in Matrix4x4 skin, Vector3 rest, in MonsterInstance m)
        {
            var p = skin.MultiplyPoint3x4(rest) * m.Scale;
            float sy = Mathf.Sin(m.Yaw), cy = Mathf.Cos(m.Yaw);
            return m.Position + new Vector3(p.x * cy + p.z * sy, p.y, -p.x * sy + p.z * cy);
        }

        /// <summary>A rest-pose normal skinned and turned by the instance's yaw (SkinVertex).</summary>
        public static Vector3 Turn(in Matrix4x4 skin, Vector3 rest, in MonsterInstance m)
        {
            var n = skin.MultiplyVector(rest);
            float sy = Mathf.Sin(m.Yaw), cy = Mathf.Cos(m.Yaw);
            return new Vector3(n.x * cy + n.z * sy, n.y, -n.x * sy + n.z * cy).normalized;
        }
    }
}
