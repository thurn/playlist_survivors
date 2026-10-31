// An experience gem (GemView): a small faceted crystal lit from inside, its colour the gem's
// value tier, its facets catching a light from above, a rim that brightens toward the silhouette,
// and a throb on each beat (_Pulse, set by GemView) past the bloom's threshold. Unlit: the world
// is dark and the gems are among the lights in it. Drawn instanced.
Shader "Game/Gem"
{
    Properties
    {
        [HDR] _Color ("Gem", Color) = (0.35, 0.6, 1, 1)
        _Glow ("Glow", Float) = 0.75
        _Pulse ("Beat pulse (0..1)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Gem"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
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
                half _Pulse;
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
                half rim = pow(1 - saturate(dot(n, v)), 3);
                half facet = 0.45 + 0.55 * saturate(dot(n, normalize(float3(0.35, 1, 0.25))));
                half glow = _Glow * (1 + 1.2 * _Pulse);
                half3 c = _Color.rgb * (facet + rim) * glow + rim * 0.15;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
