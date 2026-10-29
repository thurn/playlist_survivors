// The synthetic load's optional glow quads: an additive, camera-facing soft disc per instance, which
// gives the transparent overdraw counter something to count until TD8's effects exist.
Shader "Demo3D/BenchGlow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.55, 0.2, 1)
        _Intensity ("Intensity", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "Glow"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes a)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(a);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                o.uv = a.uv * 2 - 1;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float f = saturate(1 - dot(i.uv, i.uv));
                return float4(_Color.rgb * (_Intensity * f * f), 0);
            }
            ENDHLSL
        }
    }
}
