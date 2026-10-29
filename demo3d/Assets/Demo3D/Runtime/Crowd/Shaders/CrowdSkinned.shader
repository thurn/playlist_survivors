// The bone-texture crowd (crowd.md, Drawing the crowd): drawn with Graphics.RenderMeshIndirect,
// one command per depth bin, every vertex skinned from the type's bone texture. Passes:
// UniversalForward (Forward+: sun with cascaded shadows, ambient from the global probe, every
// additional light through URP's cluster loop), DepthOnly, DepthNormals (SSAO), ShadowCaster
// and MotionVectors (the previous frame's instance and pose).
Shader "Demo3D/CrowdSkinned"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _Metallic ("Metallic", Range(0, 1)) = 0
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        [HDR] _FlashColor ("Hit Flash", Color) = (2.5, 2.4, 2.2, 1)
        [HDR] _DissolveEdgeColor ("Dissolve Edge", Color) = (6, 2.2, 0.6, 1)
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _Smoothness;
        half _Metallic;
        half4 _EmissionColor;
        half4 _FlashColor;
        half4 _DissolveEdgeColor;
    CBUFFER_END

    #include "CrowdCommon.hlsl"

    struct CrowdAttributes
    {
        float3 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        float4 boneIndex : TEXCOORD1;
        float4 boneWeight : TEXCOORD2;
        uint instanceID : SV_InstanceID;
    };

    // Per-vertex effect values: x hit flash, y dissolve, zw LOD fade.
    struct CrowdVertex
    {
        MonsterInstance m;
        uint slot;
        float3 positionWS;
        float3 normalWS;
        float4 fx;
    };

    CrowdVertex CrowdVertexOf(CrowdAttributes v)
    {
        CrowdVertex o;
        o.slot = CrowdSlot(v.instanceID);
        o.m = _Instances[o.slot];
        SkinVertex(o.m, o.slot, false, v.positionOS, v.normalOS, v.boneIndex, v.boneWeight, o.positionWS, o.normalWS);
        o.fx = float4(o.m.hitFlash, o.m.dissolve, CrowdLodFade(o.slot));
        return o;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 restPosition : TEXCOORD3;
                nointerpolation float4 fx : TEXCOORD4;
                nointerpolation float4 tint : TEXCOORD5;
                float fogFactor : TEXCOORD6;
            };

            Varyings vert(CrowdAttributes v)
            {
                CrowdVertex c = CrowdVertexOf(v);
                Varyings o;
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.restPosition = v.positionOS;
                o.fx = c.fx;
                o.tint = TintOf(c.m.tint);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // The main light's shadow takes TD9's static shadow map of the arena's static set
            // (environment.md, Lighting the night); the lighting is shared with the hero's shader.
            #include "../../Arena/Shaders/StaticShadow.hlsl"
            #define CrowdLighting Demo3DLightingPBR

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                float edge = CrowdDissolve(i.fx.y, i.restPosition);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor * i.tint;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalize(i.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS); // the global ambient probe
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = 1;

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo.rgb;
                s.metallic = _Metallic;
                s.smoothness = _Smoothness;
                s.occlusion = 1;
                s.alpha = 1;
                s.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb
                    + _FlashColor.rgb * i.fx.x + _DissolveEdgeColor.rgb * edge;

                half3 color = CrowdLighting(inputData, s);
                color = MixFog(color, inputData.fogCoord);
                return half4(color, 1);
            }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 restPosition : TEXCOORD0;
                nointerpolation float4 fx : TEXCOORD1;
            };

            Varyings vert(CrowdAttributes v)
            {
                Varyings o = (Varyings)0;
                // The draw holds every shadow caster, and URP repeats it for each cascade: skip
                // the skinning of a monster outside this cascade's light-space box (the depth
                // axis is not tested, since casters before the near plane are pancaked).
                uint slot = CrowdSlot(v.instanceID);
                float height;
                float4 sphere = CrowdSphere(_Instances[slot], height);
                float4 cs = mul(UNITY_MATRIX_VP, float4(sphere.xyz, 1));
                float2 extent = sphere.w * float2(length(UNITY_MATRIX_VP[0].xyz), length(UNITY_MATRIX_VP[1].xyz));
                if (any(abs(cs.xy) - extent > cs.w))
                {
                    o.positionCS = float4(2, 2, 2, 1); // every vertex at one point outside the view
                    return o;
                }
                CrowdVertex c = CrowdVertexOf(v);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - c.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(c.positionWS, c.normalWS, lightDirectionWS)));
                o.restPosition = v.positionOS;
                o.fx = c.fx;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                CrowdDissolve(i.fx.y, i.restPosition);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 restPosition : TEXCOORD0;
                nointerpolation float4 fx : TEXCOORD1;
            };

            Varyings vert(CrowdAttributes v)
            {
                CrowdVertex c = CrowdVertexOf(v);
                Varyings o;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.restPosition = v.positionOS;
                o.fx = c.fx;
                return o;
            }

            half frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                CrowdDissolve(i.fx.y, i.restPosition);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 restPosition : TEXCOORD1;
                nointerpolation float4 fx : TEXCOORD2;
            };

            Varyings vert(CrowdAttributes v)
            {
                CrowdVertex c = CrowdVertexOf(v);
                Varyings o;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.restPosition = v.positionOS;
                o.fx = c.fx;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                CrowdDissolve(i.fx.y, i.restPosition);
                float3 normalWS = normalize(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
                #else
                return half4(normalWS, 0);
                #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ZWrite On
            ColorMask RG
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
            float2 _CrowdDebugMotion; // a global: --debug-motion offsets every crowd motion vector (proves the pass runs)

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionCSNoJitter : TEXCOORD0;
                float4 previousPositionCSNoJitter : TEXCOORD1;
                float3 restPosition : TEXCOORD2;
                nointerpolation float4 fx : TEXCOORD3;
            };

            Varyings vert(CrowdAttributes v)
            {
                CrowdVertex c = CrowdVertexOf(v);
                bool previousPalette;
                MonsterInstance prev = PreviousOf(c.slot, c.m, previousPalette);
                float3 prevWS, prevN;
                // The previous pose: the previous frame's palette when this slot had one, else
                // this frame's pose at the previous instance's place.
                SkinVertex(prev, c.slot, previousPalette, v.positionOS, v.normalOS, v.boneIndex, v.boneWeight, prevWS, prevN);
                Varyings o;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, float4(c.positionWS, 1));
                o.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, float4(prevWS, 1));
                o.restPosition = v.positionOS;
                o.fx = c.fx;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                CrowdDissolve(i.fx.y, i.restPosition);
                float2 mv = CalcNdcMotionVectorFromCsPositions(i.positionCSNoJitter, i.previousPositionCSNoJitter);
                return float4(mv + _CrowdDebugMotion, 0, 0); // the offset is 0 except in the pass's own check

            }
            ENDHLSL
        }
    }
}
