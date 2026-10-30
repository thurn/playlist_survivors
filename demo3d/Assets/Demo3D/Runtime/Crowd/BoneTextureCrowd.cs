using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: InternalsVisibleTo("Demo3D.EditMode.Tests")]

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The bone-texture crowd (crowd.md, Bone-texture path and Drawing the crowd). Each frame the
    /// client writes every type's instances straight into a GraphicsBuffer (LockBufferForWrite),
    /// in stable slots; Draw unlocks them, runs the culling pass (frustum plus the sun's shadow
    /// extension, 8 depth bins, LOD by projected height) and the palette pass (each visible
    /// monster's bones blended once; TD5 measured and deleted a per-vertex variant), then issues a flat set of RenderMeshIndirect calls: per type, one per
    /// submesh and LOD for the main draw (no shadows), the same again for a `--monster-style` shell, and one per submesh for the shadow proxy.
    /// The instance buffers are double-buffered, so the previous frame's copy feeds motion vectors.
    /// </summary>
    public sealed class BoneTextureCrowd : ICrowdRenderer
    {
        const int Bins = CrowdCulling.Bins;
        const int ArgsInts = 5;

        static readonly int IdInstances = Shader.PropertyToID("_Instances");
        static readonly int IdPrevInstances = Shader.PropertyToID("_PrevInstances");
        static readonly int IdVisibleList = Shader.PropertyToID("_VisibleList");
        static readonly int IdLodState = Shader.PropertyToID("_LodState");
        static readonly int IdClipTable = Shader.PropertyToID("_ClipTable");
        static readonly int IdBoneTex = Shader.PropertyToID("_BoneTex");
        static readonly int IdPrevCount = Shader.PropertyToID("_PrevCount");
        static readonly int IdBoneCount = Shader.PropertyToID("_BoneCount");
        static readonly int IdLodIndex = Shader.PropertyToID("_LodIndex");
        static readonly int IdLodDither = Shader.PropertyToID("_LodDither");
        static readonly int IdCrowdTime = Shader.PropertyToID("_CrowdTime");
        static readonly int IdCrowdFrame = Shader.PropertyToID("_CrowdFrame");
        static readonly int IdPalette = Shader.PropertyToID("_Palette");
        static readonly int IdPrevPalette = Shader.PropertyToID("_PrevPalette");
        static readonly int IdPrevStamp = Shader.PropertyToID("_PrevStamp");
        static readonly int IdArgs = Shader.PropertyToID("_Args");
        static readonly int IdShadowArgs = Shader.PropertyToID("_ShadowArgs");
        static readonly int IdVisibleListOut = Shader.PropertyToID("_VisibleListOut");
        static readonly int IdShadowListOut = Shader.PropertyToID("_ShadowListOut");
        static readonly int IdLodStateRW = Shader.PropertyToID("_LodStateRW");
        static readonly int IdVisibleFlags = Shader.PropertyToID("_VisibleFlags");
        static readonly int IdCounters = Shader.PropertyToID("_Counters");
        static readonly int IdFrustumPlanes = Shader.PropertyToID("_FrustumPlanes");
        static readonly int IdShadowDir = Shader.PropertyToID("_ShadowDir");
        static readonly int IdShadowSinElev = Shader.PropertyToID("_ShadowSinElev");
        static readonly int IdCamPos = Shader.PropertyToID("_CamPos");
        static readonly int IdCamForward = Shader.PropertyToID("_CamForward");
        static readonly int IdBinNear = Shader.PropertyToID("_BinNear");
        static readonly int IdBinFar = Shader.PropertyToID("_BinFar");
        static readonly int IdPixelScale = Shader.PropertyToID("_PixelScale");
        static readonly int IdLodThresholds = Shader.PropertyToID("_LodThresholds");
        static readonly int IdLodCount = Shader.PropertyToID("_LodCount");
        static readonly int IdShadowLod = Shader.PropertyToID("_ShadowLod");
        static readonly int IdTypeColor = Shader.PropertyToID("_Demo3DTypeColor");
        static readonly int IdTypeRotate = Shader.PropertyToID("_Demo3DTypeRotate");
        static readonly int IdCount = Shader.PropertyToID("_Count");
        static readonly int IdCapacity = Shader.PropertyToID("_Capacity");
        static readonly int IdCommands = Shader.PropertyToID("_Commands");
        static readonly int IdSubmeshes = Shader.PropertyToID("_Submeshes");
        static readonly int IdClipBounds = Shader.PropertyToID("_ClipBounds");
        static readonly int IdPaletteOut = Shader.PropertyToID("_PaletteOut");
        static readonly int IdStampOut = Shader.PropertyToID("_StampOut");
        static readonly int IdClipRate = Shader.PropertyToID("_ClipRate");

        /// <summary>
        /// LOD thresholds in projected pixels, coarsest last: LOD1 below x, LOD2 below y. LOD1 and
        /// LOD2 are TD11's <c>lod</c> cut (<see cref="Cuts"/>); without it every type draws LOD0 only,
        /// and the mechanism runs with one LOD.
        /// </summary>
        public float4 LodThresholds = new float4(60, 30, 0, 0);

        /// <summary>The LOD the shadow proxy draws (crowd.md, Shadows can use a proxy): 2 under TD11's <c>shadow-proxy</c> cut.</summary>
        public int ShadowLod;

        /// <summary>The cuts this crowd was initialized with (<see cref="Cuts.Active"/> at Initialize).</summary>
        public Cuts Cuts { get; private set; }

        /// <summary>Ablation switches for measurement (both on in every result row unless stated).</summary>
        public bool DrawMain = true, DrawShadows = true;

        /// <summary>The sun-ward extension of the culling volume (off only to show what it adds).</summary>
        public bool ShadowExtension = true;

        /// <summary>Skips the palette pass (measurement only: with `_CrowdNoSkin` set, nothing reads it).</summary>
        public bool SkipPalette;

        /// <summary>
        /// The clock of the LOD crossfades, in seconds: the timeline's time where there is one, so
        /// fixed-time captures come out the same; Time.time otherwise.
        /// </summary>
        public Func<float> Clock = () => Time.time;

        /// <summary>The horde's `--monster-style`, set before Initialize: it picks each type's materials (baseline: the baked ones).</summary>
        public MonsterStyle Style;

        public CrowdCounters Counters => _counters;

        /// <summary>The latest on-screen counts read back from the GPU (a frame or two late).</summary>
        public int OnScreenAlive { get; private set; }
        public int OnScreenDying { get; private set; }
        public int ShadowOnly { get; private set; }

        readonly CrowdLibrary _lib;
        readonly ComputeShader _cull, _palette;
        readonly int _kClear, _kCull, _kCopy, _kBlend;
        readonly int[] _kernels;
        readonly float4[] _planes = new float4[6];
        readonly Vector4[] _planeVectors = new Vector4[6];
        readonly Plane[] _planeScratch = new Plane[6];
        TypeState[] _types = Array.Empty<TypeState>();
        CrowdCounters _counters;
        int _capacity;
        uint _frame = 2; // stamps start at 0, so a fresh palette never passes for last frame's
        readonly int _ring = Mathf.Max(2, QualitySettings.maxQueuedFrames) + 2;

        sealed class TypeState
        {
            public CrowdType Type;
            public Mesh[] Lods;
            public Mesh ShadowMesh; // the shadow proxy's mesh: LOD0, or LOD2 under the shadow-proxy cut
            public Texture2D Bones;  // 60 Hz, or 30 Hz under the clips30 cut
            public float ClipRate;
            public Material[] Materials;
            public Material Shell; // the style's shell (fire's flames, painted's ink line), else null
            public GraphicsBuffer[] Instances; // a ring: the CPU never writes one the GPU may still read
            public GraphicsBuffer VisibleList, ShadowList, LodState, VisibleFlags, Counters, Args, ShadowArgs, ClipTable, ClipBounds;
            public readonly GraphicsBuffer[] Palette = new GraphicsBuffer[2];
            public readonly GraphicsBuffer[] Stamp = new GraphicsBuffer[2];
            public int PaletteSlots;
            public GraphicsBuffer LastPalette;
            public MaterialPropertyBlock[] Main; // per LOD (a queued draw keeps its block)
            public MaterialPropertyBlock Shadow;
            public int Count, PrevCount;
            public bool Locked;
            public int[] Readback = new int[4];
            public Action<AsyncGPUReadbackRequest> OnReadback;
        }

        public BoneTextureCrowd(CrowdLibrary lib)
        {
            _lib = lib;
            _cull = lib.CullShader ? lib.CullShader : throw new InvalidOperationException("CrowdLibrary has no cull shader");
            _kClear = _cull.FindKernel("ClearArgs");
            _kCull = _cull.FindKernel("Cull");
            _kCopy = _cull.FindKernel("CopyCounts");
            _kernels = new[] { _kClear, _kCull, _kCopy };
            _palette = lib.PaletteShader ? lib.PaletteShader : throw new InvalidOperationException("CrowdLibrary has no palette shader");
            _kBlend = _palette.FindKernel("Blend");
        }

        public void Initialize(string[] typeIds, int capacityPerType)
        {
            Dispose();
            _capacity = capacityPerType;
            Cuts = Cuts.Active;
            LodThresholds = new float4(Cuts.LodPx1, Cuts.LodPx2, 0, 0);
            ShadowLod = Cuts.On(Cuts.ShadowProxy) ? Mathf.Clamp(Cuts.ShadowProxyLod, 0, 2) : 0;
            _types = new TypeState[typeIds.Length];
            for (int i = 0; i < typeIds.Length; i++)
            {
                int at = _lib.IndexOf(typeIds[i]);
                if (at < 0) throw new ArgumentException($"BoneTextureCrowd: no baked type '{typeIds[i]}' (run BakeCrowd)");
                _types[i] = Create(_lib.Types[at]);
            }
        }

        TypeState Create(CrowdType t)
        {
            var cuts = Cuts ?? Cuts.Active;
            // LOD0 to LOD2: the welded meshes, or the unwelded style meshes for a style that needs them.
            bool styleMesh = Style != null && Style.UsesStyleMesh;
            var meshes = styleMesh ? new[] { t.StyleMesh, t.StyleLod1, t.StyleLod2 } : new[] { t.Mesh, t.Lod1, t.Lod2 };
            if (styleMesh && meshes[0] == null) throw new InvalidOperationException($"BoneTextureCrowd: {t.Id} has no style mesh for {MonsterStyle.Flag} {Style.Name} (re-run BakeCrowd)");
            bool lods3 = cuts.On(Cuts.Lod) && meshes[1] != null && meshes[2] != null;
            bool clips30 = cuts.On(Cuts.Clips30) && t.Bones30 != null;
            if (cuts.On(Cuts.Lod) && !lods3) Debug.LogWarning($"BoneTextureCrowd: {t.Id} has no LOD1/LOD2 (re-run BakeCrowd); LOD0 only");
            var s = new TypeState
            {
                Type = t,
                Lods = lods3 ? meshes : new[] { meshes[0] },
                ShadowMesh = ShadowLod == 2 && meshes[2] != null ? meshes[2] : ShadowLod == 1 && meshes[1] != null ? meshes[1] : meshes[0],
                Bones = clips30 ? t.Bones30 : t.Bones,
                ClipRate = clips30 ? CrowdLibrary.CutClipRate : CrowdLibrary.ClipRate,
            };
            s.OnReadback = r =>
            {
                if (r.hasError || r.layerDataSize < 16) return;
                var data = r.GetData<int>();
                for (int k = 0; k < 4; k++) s.Readback[k] = data[k];
            };
            int cap = _capacity;
            int lods = s.Lods.Length;
            int subs = t.Mesh.subMeshCount;
            s.Instances = new GraphicsBuffer[_ring];
            for (int i = 0; i < _ring; i++)
                s.Instances[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite,
                    cap, MonsterInstance.Stride) { name = t.Id + "_instances" + i };
            s.VisibleList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap * Bins * lods, 4) { name = t.Id + "_visible" };
            s.ShadowList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 4) { name = t.Id + "_shadowList" };
            s.LodState = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 16) { name = t.Id + "_lodState" };
            s.LodState.SetData(new uint[cap * 4]);
            s.VisibleFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 4) { name = t.Id + "_flags" };
            s.Counters = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 4, 4) { name = t.Id + "_counters" };

            // Commands: [submesh][lod][bin] for the main draw, [submesh] for the shadow proxy.
            var args = new uint[subs * lods * Bins * ArgsInts];
            var shadowArgs = new uint[subs * ArgsInts];
            for (int sm = 0; sm < subs; sm++)
            {
                for (int l = 0; l < lods; l++)
                {
                    var d = s.Lods[l].GetSubMesh(sm);
                    for (int b = 0; b < Bins; b++)
                    {
                        int at = ((sm * lods + l) * Bins + b) * ArgsInts;
                        args[at] = (uint)d.indexCount;
                        args[at + 2] = (uint)d.indexStart;
                        args[at + 3] = (uint)d.baseVertex;
                        args[at + 4] = (uint)((l * Bins + b) * cap);
                    }
                }
                var sd = s.ShadowMesh.GetSubMesh(sm);
                shadowArgs[sm * ArgsInts] = (uint)sd.indexCount;
                shadowArgs[sm * ArgsInts + 2] = (uint)sd.indexStart;
                shadowArgs[sm * ArgsInts + 3] = (uint)sd.baseVertex;
            }
            s.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, args.Length, 4) { name = t.Id + "_args" };
            s.Args.SetData(args);
            s.ShadowArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, shadowArgs.Length, 4) { name = t.Id + "_shadowArgs" };
            s.ShadowArgs.SetData(shadowArgs);

            var clips = new Vector4[t.Clips.Length];
            for (int i = 0; i < clips.Length; i++)
                clips[i] = clips30
                    ? new Vector4(t.Clips[i].FirstRow30, t.Clips[i].Frames30, t.Clips[i].Length, t.Clips[i].Loop ? 1 : 0)
                    : new Vector4(t.Clips[i].FirstRow, t.Clips[i].Frames, t.Clips[i].Length, t.Clips[i].Loop ? 1 : 0);
            s.ClipTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, clips.Length, 16) { name = t.Id + "_clips" };
            s.ClipTable.SetData(clips);
            var spheres = new Vector4[t.Clips.Length];
            for (int i = 0; i < spheres.Length; i++) spheres[i] = new Vector4(t.Clips[i].CenterY, t.Clips[i].Radius, t.Clips[i].HeightM, 0);
            s.ClipBounds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, spheres.Length, 16) { name = t.Id + "_clipBounds" };
            s.ClipBounds.SetData(spheres);

            s.Materials = Style != null ? Style.Materials(_lib, t) : t.Materials;
            s.Shell = Style?.Shell(_lib, t);
            s.Main = new MaterialPropertyBlock[lods];
            for (int l = 0; l < lods; l++) s.Main[l] = new MaterialPropertyBlock();
            s.Shadow = new MaterialPropertyBlock();
            foreach (var mpb in AllBlocks(s))
            {
                mpb.SetTexture(IdBoneTex, s.Bones);
                mpb.SetBuffer(IdClipTable, s.ClipTable);
                mpb.SetBuffer(IdClipBounds, s.ClipBounds);
                mpb.SetBuffer(IdLodState, s.LodState);
                mpb.SetInt(IdBoneCount, t.BoneCount);
            }
            for (int l = 0; l < lods; l++)
            {
                s.Main[l].SetBuffer(IdVisibleList, s.VisibleList);
                s.Main[l].SetFloat(IdLodDither, 1);
                s.Main[l].SetFloat(IdLodIndex, l);
                s.Main[l].SetVector(IdTypeColor, t.SignatureColor());
                s.Main[l].SetVector(IdTypeRotate, t.HueRotation());
            }
            s.Shadow.SetBuffer(IdVisibleList, s.ShadowList);
            s.Shadow.SetFloat(IdLodDither, 0);
            s.Shadow.SetFloat(IdLodIndex, ShadowLod);
            return s;
        }

        /// <summary>
        /// The type's instance slots for this frame, mapped GPU memory: write every field of every
        /// slot (never read it). Slots are stable per entity; a type not requested in a frame
        /// draws nothing that frame.
        /// </summary>
        public NativeArray<MonsterInstance> Instances(int type, int count)
        {
            var s = _types[type];
            if (count > _capacity) throw new ArgumentOutOfRangeException(nameof(count), $"capacity is {_capacity}");
            if (s.Locked) throw new InvalidOperationException("BoneTextureCrowd: Instances called twice for a type in one frame");
            s.Count = count;
            s.Locked = true;
            return s.Instances[_frame % _ring].LockBufferForWrite<MonsterInstance>(0, count);
        }

        void EnsurePalette(TypeState s)
        {
            if (s.PaletteSlots >= s.Count) return;
            int slots = Mathf.Min(_capacity, Mathf.Max(256, Mathf.NextPowerOfTwo(s.Count)));
            for (int i = 0; i < 2; i++)
            {
                s.Palette[i]?.Dispose();
                s.Stamp[i]?.Dispose();
                s.Palette[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, slots * s.Type.BoneCount * 3, 16) { name = s.Type.Id + "_palette" + i };
                s.Stamp[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, slots, 4) { name = s.Type.Id + "_stamp" + i };
                s.Stamp[i].SetData(new uint[slots]);
            }
            s.PaletteSlots = slots;
        }

        public void Draw(Camera camera)
        {
            // Palettes and stamps are GPU-only, so two suffice; the CPU-written instance buffers
            // form a ring longer than the frames in flight.
            int cur = (int)(_frame & 1), prev = 1 - cur;
            int icur = (int)(_frame % _ring), iprev = (int)((_frame - 1) % _ring);
            float time = Clock();

            CrowdCulling.Planes(camera, _planeScratch, _planes);
            for (int i = 0; i < 6; i++) _planeVectors[i] = _planes[i];
            var sun = RenderSettings.sun;
            float3 lightDir = sun != null ? (float3)sun.transform.forward : new float3(0, -1, 0);
            var bins = CrowdCulling.BinRange(camera, 5f, 2f);
            _cull.SetVectorArray(IdFrustumPlanes, _planeVectors);
            _cull.SetVector(IdShadowDir, new Vector4(lightDir.x, lightDir.y, lightDir.z, 0));
            _cull.SetFloat(IdShadowSinElev, ShadowExtension ? CrowdCulling.SinElevation(lightDir) : 1e9f);
            _cull.SetVector(IdCamPos, camera.transform.position);
            _cull.SetVector(IdCamForward, camera.transform.forward);
            _cull.SetFloat(IdBinNear, bins.x);
            _cull.SetFloat(IdBinFar, bins.y);
            _cull.SetFloat(IdPixelScale, CrowdCulling.PixelScale(camera));
            _cull.SetVector(IdLodThresholds, LodThresholds);
            _cull.SetFloat(IdCrowdTime, time);

            var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            int draws = 0;
            long skinned = 0;
            int instances = 0;
            for (int ti = 0; ti < _types.Length; ti++)
            {
                var s = _types[ti];
                if (s.Locked) s.Instances[icur].UnlockBufferAfterWrite<MonsterInstance>(s.Count);
                else s.Count = 0;
                s.Locked = false;
                instances += s.Count;
                if (s.Count > 0) Cull(s, icur, iprev);
                if (s.Count > 0)
                {
                    if (SkipPalette) EnsurePalette(s);
                    else Blend(s, icur, cur);
                }

                for (int bi = 0; bi <= s.Main.Length; bi++)
                {
                    var mpb = bi < s.Main.Length ? s.Main[bi] : s.Shadow;
                    mpb.SetBuffer(IdInstances, s.Instances[icur]);
                    mpb.SetBuffer(IdPrevInstances, s.Instances[iprev]);
                    mpb.SetInt(IdPrevCount, s.PrevCount);
                    mpb.SetFloat(IdCrowdTime, time);
                    if (s.PaletteSlots > 0)
                    {
                        mpb.SetBuffer(IdPalette, s.Palette[cur]);
                        mpb.SetBuffer(IdPrevPalette, s.Palette[prev]);
                        mpb.SetBuffer(IdPrevStamp, s.Stamp[prev]);
                        mpb.SetInt(IdCrowdFrame, (int)_frame);
                    }
                }
                if (s.Count > 0)
                {
                    int lods = s.Lods.Length;
                    for (int sm = 0; sm < s.Materials.Length; sm++)
                    {
                        for (int l = 0; l < lods && DrawMain; l++)
                        {
                            var rp = new RenderParams(s.Materials[sm])
                            {
                                worldBounds = bounds,
                                matProps = s.Main[l],
                                shadowCastingMode = ShadowCastingMode.Off,
                                receiveShadows = true,
                                motionVectorMode = MotionVectorGenerationMode.Object,
                                lightProbeUsage = LightProbeUsage.Off, // ambient from the global probe
                                reflectionProbeUsage = ReflectionProbeUsage.Off,
                                camera = camera,
                                layer = 0,
                            };
                            Graphics.RenderMeshIndirect(rp, s.Lods[l], s.Args, Bins, (sm * lods + l) * Bins);
                            draws++;
                            if (s.Shell == null) continue;
                            // The shell: the same mesh, instances, LOD block and args, its own material.
                            rp.material = s.Shell;
                            Graphics.RenderMeshIndirect(rp, s.Lods[l], s.Args, Bins, (sm * lods + l) * Bins);
                            draws++;
                        }
                        if (!DrawShadows) continue;
                        var srp = new RenderParams(s.Materials[sm])
                        {
                            worldBounds = bounds,
                            matProps = s.Shadow,
                            camera = camera,
                            shadowCastingMode = ShadowCastingMode.ShadowsOnly,
                            receiveShadows = false,
                            motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                            lightProbeUsage = LightProbeUsage.Off,
                            reflectionProbeUsage = ReflectionProbeUsage.Off,
                            layer = 0,
                        };
                        Graphics.RenderMeshIndirect(srp, s.ShadowMesh, s.ShadowArgs, 1, sm);
                        draws++;
                    }
                    AsyncGPUReadback.Request(s.Counters, s.OnReadback);
                }
                else Array.Clear(s.Readback, 0, 4);
                s.PrevCount = s.Count;
            }

            int alive = 0, dying = 0, shadowOnly = 0, casters = 0;
            foreach (var s in _types)
            {
                alive += s.Readback[0];
                dying += s.Readback[1];
                shadowOnly += s.Readback[2];
                casters += s.Readback[3];
                skinned += (long)(s.Readback[0] + s.Readback[1]) * s.Type.Vertices;
            }
            OnScreenAlive = alive;
            OnScreenDying = dying;
            ShadowOnly = shadowOnly;
            _counters = new CrowdCounters
            {
                Instances = instances, Visible = alive + dying, SkinnedVertices = skinned, Draws = draws, ShadowCasters = casters,
            };
            _frame++;
        }

        static IEnumerable<MaterialPropertyBlock> AllBlocks(TypeState s)
        {
            foreach (var b in s.Main) yield return b;
            yield return s.Shadow;
        }

        void Cull(TypeState s, int icur, int iprev)
        {
            int commands = s.Lods.Length * Bins;
            int subs = s.Materials.Length;
            _cull.SetInt(IdLodCount, s.Lods.Length);
            _cull.SetInt(IdShadowLod, ShadowLod);
            _cull.SetInt(IdCount, s.Count);
            _cull.SetInt(IdCapacity, _capacity);
            _cull.SetInt(IdCommands, commands);
            _cull.SetInt(IdSubmeshes, subs);
            foreach (var k in _kernels)
            {
                _cull.SetBuffer(k, IdArgs, s.Args);
                _cull.SetBuffer(k, IdShadowArgs, s.ShadowArgs);
                _cull.SetBuffer(k, IdCounters, s.Counters);
            }
            _cull.SetBuffer(_kCull, IdInstances, s.Instances[icur]);
            _cull.SetBuffer(_kCull, IdPrevInstances, s.Instances[iprev]);
            _cull.SetInt(IdPrevCount, s.PrevCount);
            _cull.SetBuffer(_kCull, IdClipBounds, s.ClipBounds);
            _cull.SetBuffer(_kCull, IdVisibleListOut, s.VisibleList);
            _cull.SetBuffer(_kCull, IdShadowListOut, s.ShadowList);
            _cull.SetBuffer(_kCull, IdLodStateRW, s.LodState);
            _cull.SetBuffer(_kCull, IdVisibleFlags, s.VisibleFlags);
            _cull.Dispatch(_kClear, (commands * subs + 63) / 64, 1, 1);
            _cull.Dispatch(_kCull, (s.Count + 63) / 64, 1, 1);
            if (subs > 1) _cull.Dispatch(_kCopy, (commands + 63) / 64, 1, 1);
        }

        void Blend(TypeState s, int icur, int cur)
        {
            EnsurePalette(s);
            _palette.SetInt(IdCount, s.Count);
            _palette.SetInt(IdBoneCount, s.Type.BoneCount);
            _palette.SetInt(IdCrowdFrame, (int)_frame);
            _palette.SetFloat(IdClipRate, s.ClipRate);
            _palette.SetBuffer(_kBlend, IdInstances, s.Instances[icur]);
            _palette.SetBuffer(_kBlend, IdVisibleFlags, s.VisibleFlags);
            _palette.SetBuffer(_kBlend, IdClipTable, s.ClipTable);
            _palette.SetTexture(_kBlend, IdBoneTex, s.Bones);
            _palette.SetBuffer(_kBlend, IdPaletteOut, s.Palette[cur]);
            _palette.SetBuffer(_kBlend, IdStampOut, s.Stamp[cur]);
            s.LastPalette = s.Palette[cur];
            _palette.Dispatch(_kBlend, (s.Count * s.Type.BoneCount + 63) / 64, 1, 1);
        }

        // The culling pass's outputs, for the tests.
        internal GraphicsBuffer DebugArgs(int type) => _types[type].Args;
        internal GraphicsBuffer DebugShadowArgs(int type) => _types[type].ShadowArgs;
        internal GraphicsBuffer DebugVisibleList(int type) => _types[type].VisibleList;
        internal GraphicsBuffer DebugShadowList(int type) => _types[type].ShadowList;
        internal int Capacity => _capacity;
        internal GraphicsBuffer DebugLastPalette(int type) => _types[type].LastPalette;

        public void Dispose()
        {
            foreach (var s in _types)
            {
                if (s.Locked) s.Instances[_frame % _ring].UnlockBufferAfterWrite<MonsterInstance>(0);
                foreach (var b in s.Instances) b?.Dispose();
                foreach (var b in new List<GraphicsBuffer>
                         {
                             s.VisibleList, s.ShadowList, s.LodState, s.VisibleFlags,
                             s.Counters, s.Args, s.ShadowArgs, s.ClipTable, s.ClipBounds, s.Palette[0], s.Palette[1], s.Stamp[0], s.Stamp[1],
                         })
                    b?.Dispose();
            }
            _types = Array.Empty<TypeState>();
        }
    }
}
