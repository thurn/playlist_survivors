// A weapon's crystal (Client/Weapons: Magic Wand's bolts, King Bible's shards): faceted and lit
// from inside, white-hot where it faces the eye and its ability's colour toward the silhouette
// (abilities carry their own colours, white at the core), past the bloom's threshold. Unlit, like
// the gems. The opaque material draws the crystal; an additive copy (_SrcBlend One, _DstBlend One,
// _ZWrite 0, a lower _Glow) draws its afterimages. Drawn instanced.
Shader "Game/WeaponCrystal"
{
    Properties
    {
        [HDR] _Color ("Ability", Color) = (0.55, 0.2, 1, 1)
        _Glow ("Glow", Float) = 1.3
        _Core ("White core", Range(0, 1)) = 0.6
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "WeaponCrystal"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Glow;
                half _Core;
                half _SrcBlend;
                half _DstBlend;
                half _ZWrite;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
            };

            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS);
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                half facing = saturate(dot(n, v));
                half rim = pow(1 - facing, 2);
                half facet = 0.4 + 0.6 * saturate(dot(n, normalize(float3(0.35, 1, 0.25))));
                // Colour through the body and brighter at the rim; white only at the very core, so the bloom keeps the hue.
                half core = _Core * pow(facing, 6);
                half3 c = _Color.rgb * _Glow * (facet + 0.8 * rim) + core;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
