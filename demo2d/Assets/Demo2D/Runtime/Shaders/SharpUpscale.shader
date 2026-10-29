// The pixel pipeline's upscale (PX5; README, The pixel pipeline): draws a window of the pixel
// target over the screen with sharp-bilinear filtering. Each texel is a flat square of about
// _Scale screen pixels, blended with its neighbour only across its last screen pixel, so a
// non-integer zoom or a sub-pixel scroll stays crisp without shimmer.
Shader "Demo2D/SharpUpscale"
{
    Properties
    {
        _MainTex("Pixel target", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SrcRect;   // the window: uv origin (xy) and size (zw)
                float4 _TexSize;   // the target's texels: w, h, 1/w, 1/h
                float _Scale;      // screen pixels per texel
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = _SrcRect.xy + v.uv * _SrcRect.zw;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 texel = i.uv * _TexSize.xy;
                float2 base = floor(texel);
                float2 d = texel - base - 0.5;
                float region = 0.5 - 0.5 / max(_Scale, 1.0);
                float2 f = (d - clamp(d, -region, region)) * max(_Scale, 1.0) + 0.5;
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, (base + f) * _TexSize.zw);
            }
            ENDHLSL
        }
    }
}
