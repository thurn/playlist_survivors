// Light pools on the ground under glowing pieces (demo3d's TD15; docs/tech-demo-3d/environment.md, Mood): a soft radial
// glow drawn additively a few centimetres above the ground, standing in for the bounce of a
// glowing tree's or lamp's light that the dark ground's lightmap barely shows. One quad per pool,
// all in one mesh (ArenaDressing; vertex colour: linear tint, intensity in alpha). Monsters and
// props stand in front of it (depth-tested, no depth write). Nothing vendor-derived.
Shader "Game/GroundGlow"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-70" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : TEXCOORD1; float fogFactor : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = v.uv * 2 - 1;
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                if (r2 >= 1) discard;
                float f = (1 - r2);
                f = f * f * (0.35 + 0.65 * f);          // a bright core with a long soft skirt
                float3 c = i.color.rgb * i.color.a * f;
                c = MixFogColor(c, 0, i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
