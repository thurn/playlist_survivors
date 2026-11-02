// A stand-in for the monsters, the hero, the bosses and the structures until the trailer's edge look is ported
// (StandIn.cs): a near-black body whose silhouette glows in a role's colour. Unlit; honours the type-ID pass.
Shader "Champions/StandIn"
{
    Properties
    {
        _Color ("Line", Color) = (1, 1, 1, 1)
        _Body ("Body", Color) = (0, 0, 0, 1)
        _Rim ("Rim (1 - facing where the line starts)", Range(0.05, 1)) = 0.55
        _ChampionsId ("ID pass code", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "StandIn"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Body;
                half _Rim;
                float _ChampionsId;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings vert(Attributes a)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(a.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                CHAMPIONS_ID_PASS(_ChampionsId)
                float facing = saturate(dot(normalize(i.normalWS), normalize(i.viewWS)));
                half edge = smoothstep(_Rim, _Rim + 0.08, 1 - facing);
                return half4(lerp(_Body.rgb, _Color.rgb, edge), 1);
            }
            ENDHLSL
        }
    }
}
