// Additive soft dots for the treatments' particles (embers, glints, shards): vertex colour
// over a round falloff.
Shader "StyleLab/Particle"
{
    Properties { _MainTex ("Unused", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" }
        Pass
        {
            Name "Particle"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float d = length(i.uv * 2.0 - 1.0);
                float a = saturate(1.0 - d);
                a = a * a * (0.4 + 0.6 * smoothstep(0.5, 0.0, d));
                return half4(i.color.rgb * i.color.a * a * 4.0, 0);
            }
            ENDHLSL
        }
    }
}
