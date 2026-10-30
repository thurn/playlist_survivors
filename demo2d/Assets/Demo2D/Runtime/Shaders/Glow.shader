// PX7's glowing sprites (the fireflies): unlit and additive, the sprite's colour times its vertex
// colour times _Intensity, so a dot can reach past 1 in the HDR pixel target and bloom.
Shader "Demo2D/Glow"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite", 2D) = "white" {}
        _Intensity("Intensity", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float _Intensity;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

        Varyings Vert(Attributes v)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            o.color = v.color;
            o.uv = v.uv;
            return o;
        }

        float4 Frag(Varyings i) : SV_Target
        {
            float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
            return float4(c.rgb * c.a * _Intensity, 0);
        }
        ENDHLSL

        // One pass for the 2D renderer (a second, SRPDefaultUnlit, would draw the glow twice).
        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
