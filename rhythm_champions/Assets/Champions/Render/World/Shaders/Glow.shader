// A flat HDR colour (HeroMarker.cs): the scripted scenes' stand-in at the hero's feet, white and bright enough to bloom,
// until the hero's own edge cut draws there. In the stills' type-ID pass it draws its _ChampionsId (an effect's code).
Shader "Champions/Glow"
{
    Properties
    {
        [HDR] _Color ("Colour (linear, HDR)", Color) = (3, 3, 3, 1)
        _ChampionsId ("Type-ID pass code", Float) = 252
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _ChampionsId;
            CBUFFER_END

            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }

            half4 frag() : SV_Target
            {
                CHAMPIONS_ID_PASS(_ChampionsId)
                return half4(_Color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
