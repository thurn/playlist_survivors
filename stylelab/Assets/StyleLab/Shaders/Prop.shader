// The stage's props (Stage.cs builds their meshes): flat-shaded rocks, and glowing crystal
// clusters whose glow rises toward their tips and breathes with the beat.
Shader "StyleLab/Prop"
{
    Properties
    {
        _Color ("Colour", Color) = (0.1, 0.08, 0.12, 1)
        [HDR] _Glow ("Glow", Color) = (0, 0, 0, 1)
        _GlowHeight ("Glow height (metres)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "StyleLabPragmas.hlsl"
            #include "StyleLabCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half4 _Glow; float _GlowHeight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fogFactor : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light L = SL_MainLight(i.positionWS);
                float ao = SL_Ao(i.positionCS);
                float3 color = _Color.rgb * (L.color * saturate(dot(N, L.direction)) * L.shadowAttenuation + SampleSH(N) * ao);
                float3 addDiffuse = 0, addSpec = 0;
                SL_AdditionalLights(i.positionWS, i.positionCS, N, V, 0.0, 60.0, addDiffuse, addSpec);
                color += _Color.rgb * addDiffuse + addSpec * 0.3;
                float3 H = normalize(L.direction + V);
                color += pow(saturate(dot(N, H)), 60.0) * L.color * L.shadowAttenuation * 0.4;
                float h = saturate(i.positionWS.y / max(_GlowHeight, 0.1));
                float fres = pow(1.0 - saturate(dot(N, V)), 2.0);
                color += _Glow.rgb * (0.15 + h * h * 1.2 + fres * 0.6) * (0.8 + 0.5 * _SL_Pulse);
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
        UsePass "Hidden/StyleLab/Passes/SHADOWCASTER"
        UsePass "Hidden/StyleLab/Passes/DEPTHONLY"
        UsePass "Hidden/StyleLab/Passes/DEPTHNORMALS"
    }
}
