// The refractive-gem monster look's bodies (neon-look.md, The Axes > Monsters; GemLook.cs): the
// crowd's style mesh skinned as every look skins it (MonsterCommon.hlsl), drawn as clear gem showing
// the void and the grid behind it, split into the type's palette family, with reflected bands, a rim
// and fine lines in the slot's hue and a core of trapped light that flares on the beat
// (MonsterGem.hlsl, MonsterEdge.hlsl's EdgeCore). As an impulse ring passes (Impulse.hlsl's
// ImpulseCrowd) a living monster hops and its bands, rim and lines take the ring's light. A dark
// outline hull (MonsterOutline.hlsl) in the unlit pass URP draws beside it parts a clump's touching
// rims. Drawn in the transparent queue, after the camera's opaque texture is copied, with ZWrite:
// opaque on screen.
// Under the global _GameIdMask (the stills' ID render) it draws flat white.
Shader "Look/MonsterGem"
{
    Properties
    {
        [HDR] _Tint ("Slot hue (linear)", Color) = (0.3, 0.6, 1.0, 1)
        [HDR] _FringeA ("Fringe toward one neighbour (linear)", Color) = (0.2, 0.7, 0.8, 1)
        [HDR] _FringeB ("Fringe toward the other (linear)", Color) = (0.5, 0.4, 1.0, 1)
        _Core ("Gem's own dark (void.floor, linear)", Color) = (0.002, 0.002, 0.004, 1)
        [HDR] _Flash ("Flash (shatter.core, linear)", Color) = (1, 1, 1, 1)
        _Refraction ("Refraction", Float) = 0.05
        _Behind ("Scene behind's gain", Float) = 1.6
        _Shine ("Reflected bands' gain", Float) = 1.65
        _ShineWidth ("Share of facets in a band", Range(0.05, 0.9)) = 0.2
        _Sparkle ("Trapped light on the beat", Float) = 0.5
        _Far ("A far body's glow", Float) = 1.0
        _Rim ("Rim gain", Float) = 2.1
        _RimPower ("Rim falloff", Float) = 3.0
        _Edge ("Line gain", Float) = 1.0
        _EdgePx ("Line width (px)", Float) = 1.0
        _Beat ("Line lift on the beat", Float) = 0.4
        _Ring ("Ring light on bands, rim and lines", Float) = 1.1
        _CoreGlow ("Core at rest", Float) = 0.15
        _CoreFlare ("Core on the beat", Float) = 0.4
        _CoreSize ("Core radius (share of the body's)", Float) = 0.2
        _OutlinePx ("Outline width (px at 1080 rows)", Float) = 4
        _OutlineBack ("Outline hull behind the body (share of its radius)", Float) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; half4 _FringeA; half4 _FringeB; half4 _Core; half4 _Flash;
            float _Refraction; half _Behind; half _Shine; half _ShineWidth; half _Sparkle; half _Far;
            half _Rim; half _RimPower; half _Edge; half _EdgePx; half _Beat; half _Ring;
            half _CoreGlow; half _CoreFlare; half _CoreSize; half _OutlinePx; half _OutlineBack;
        CBUFFER_END
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterEdge.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterGem.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Impulse/Shaders/Impulse.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterOutline.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation float4 fx : TEXCOORD2; // x hit, y the beat's pulse (living only), zw the LOD fade
                float fogFactor : TEXCOORD4;
                float4 bary : TEXCOORD5;
                nointerpolation float3 edges : TEXCOORD6;
                nointerpolation float4 core : TEXCOORD7; // the body's bounding sphere (CrowdSphere): centre (world), radius
                nointerpolation float3 ring : TEXCOORD8; // the impulse rings' light where it stands (linear)
                nointerpolation float far : TEXCOORD9;   // GemFar
            };

            Varyings vert(MonsterAttributes v)
            {
                MonsterVertex c = MonsterSkin(v);
                MonsterInstance m = _Instances[CrowdSlot(v.instanceID)];
                float height;
                Varyings o;
                float alive = (m.flags & CROWD_ALIVE) != 0 ? 1 : 0;
                float hop;
                ImpulseCrowd(m.position.xz, hop, o.ring);
                o.ring *= alive;
                c.positionWS.y += hop * alive;
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.fx = float4(c.fx.x, alive * max(_SL_Pulse, c.fx.y), c.fx.zw);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.bary = v.bary;
                o.edges = v.edges;
                o.core = CrowdSphere(m, height);
                o.far = GemFar(o.core, height);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                if (_GameIdMask > 0.5) return half4(1, 1, 1, 1);
                // The slot's hue, not the role's gem on the instance.
                float lit, shine;
                float3 color = GemSurface(i.positionWS, i.normalWS, i.positionCS, i.bary, i.edges, _Tint.rgb, i.fx.y, 0, i.far, lit, shine);
                float3 core = EdgeCore(i.positionWS, i.core, _Tint.rgb, i.fx.y);
                color += core;
                color += i.ring * _Ring * (lit + shine);
                // A hit lights what is lit already, half toward white and half the slot's hue, so a crowd under
                // constant hits keeps its colour: the lines, the bands, the rim and the core.
                color += GemFlash(_Tint.rgb) * i.fx.x * LookFlash(LOOK_HIT_CALM) * (lit + 0.7 * shine + 0.4 * Luminance(core) + 0.05);
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            Cull Front
            HLSLPROGRAM
            #pragma vertex outlineVert
            #pragma fragment outlineFrag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            OutlineVaryings outlineVert(MonsterAttributes v) { return LineOutlineVert(v, _OutlinePx, _OutlineBack); }

            half4 outlineFrag(OutlineVaryings i) : SV_Target { return LineOutlineFrag(i, _Core.rgb); }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings vert(MonsterAttributes v)
            {
                ShadowVaryings o;
                // URP repeats the shadow draw for each cascade: skip a monster outside this cascade's
                // light-space box (the depth axis is not tested).
                uint slot = CrowdSlot(v.instanceID);
                float height;
                float4 sphere = CrowdSphere(_Instances[slot], height);
                float4 cs = mul(UNITY_MATRIX_VP, float4(sphere.xyz, 1));
                float2 extent = sphere.w * float2(length(UNITY_MATRIX_VP[0].xyz), length(UNITY_MATRIX_VP[1].xyz));
                if (any(abs(cs.xy) - extent > cs.w))
                {
                    o.positionCS = float4(2, 2, 2, 1);
                    return o;
                }
                MonsterVertex c = MonsterSkin(v);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - c.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(c.positionWS, c.normalWS, lightDirectionWS)));
                return o;
            }

            half4 frag(ShadowVaryings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
