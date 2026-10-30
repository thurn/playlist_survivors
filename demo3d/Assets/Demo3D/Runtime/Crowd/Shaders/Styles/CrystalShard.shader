// The crystal shatter's shards (crowd.md, Monster styles; CrystalShatter): Crystal.shader's surface
// (CrystalCommon.hlsl) on a chunk of a monster, posed as it was when the blow broke it and drawn by a
// MeshRenderer. The skin is the style mesh's own triangles, skinned as the crowd skins them, so the
// chunks match the crowd's crystal exactly; inside faces (UV1.x 1) are brighter and more saturated.
// _ShardFlash (per renderer) is the break's white flash, as the crowd's hit flash.
//
// The material draws in Crystal's queue, which gets no motion vectors from URP, so moving shards
// would smear under TAA. CrystalShatter gives every shard a second material on this shader in the
// last opaque queue (2499) with only its MotionVectors pass enabled: URP draws that pass for opaque
// queues, so each shard writes its own object motion (the renderer's previous matrix), and TAA
// reprojects it. The twin writes no colour and no depth, so the opaque texture behind the shard is
// untouched.
Shader "Demo3D/Styles/CrystalShard"
{
    Properties
    {
        [HDR] _Tint ("Gem", Color) = (0.55, 0.25, 1.0, 1)
        _Refraction ("Refraction", Float) = 0.06
        _Facets ("Faceting", Range(0, 1)) = 0.9
        _Gain ("Horde gain (MonsterStyle tuning)", Float) = 1
        _Peak ("Horde peak (MonsterStyle tuning)", Float) = 1000
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; float _Refraction; half _Facets;
            half _Gain; half _Peak;
            float _HeightM;
        CBUFFER_END

        // Per renderer (a MaterialPropertyBlock): the break's flash (0..1).
        float _ShardFlash;

        #include "StyleCommon.hlsl"

        struct ShardAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 inside : TEXCOORD1; // x 1 on an inside face
            float4 bary : TEXCOORD3;   // the style mesh's barycentrics and value per triangle
        };
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
            #include_with_pragmas "StylePragmas.hlsl"
            #include "CrystalCommon.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 bary : TEXCOORD2;
                float inside : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings vert(ShardAttributes v)
            {
                Varyings o = (Varyings)0;
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.bary = v.bary;
                o.inside = v.inside.x;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Demo3DIdMask > 0.5) return half4(1, 1, 1, 1); // TD15's ID render, as the crowd's
                return CrystalShade(i.positionWS, i.normalWS, i.positionCS, i.bary, _ShardFlash, 0, i.inside, i.fogFactor);
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
            float3 _LightDirection;
            float3 _LightPosition;

            float4 vert(ShardAttributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG
            Cull Back

            HLSLPROGRAM
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }
}
