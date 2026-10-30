// The hero's sword arcs and spell rings (Effects.cs): an additive ribbon whose uv.x runs along
// the swing and uv.y across it. The leading edge is white-hot; the tail cools and fades.
Shader "StyleLab/Slash"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (3, 1.6, 0.6, 1)
        _Age ("Age (0..1)", Range(0, 1)) = 0
        _Sweep ("Sweep", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" }
        Pass
        {
            Name "Slash"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Age; half _Sweep;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float head = _Sweep;
                float along = i.uv.x;
                clip(head - along);
                float tail = saturate(1.0 - (head - along) * 2.2);
                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                float core = pow(across, 3.0);
                float fade = 1.0 - _Age;
                float3 color = _Color.rgb * tail * (across * 0.6 + core * 1.6) * fade;
                color += float3(4, 3.6, 3.0) * pow(tail, 6.0) * core * fade;
                return half4(color, 0);
            }
            ENDHLSL
        }
    }
}
