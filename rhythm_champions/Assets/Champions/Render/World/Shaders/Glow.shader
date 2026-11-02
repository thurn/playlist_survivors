// A flat HDR colour (HeroMarker.cs): the scripted scenes' stand-in at the hero's feet, white and bright enough to bloom,
// until the hero's own edge cut draws there. Under the look metrics' ID render (_RC_IdPass) it draws black.
Shader "Champions/Glow"
{
    Properties
    {
        [HDR] _Color ("Colour (linear, HDR)", Color) = (3, 3, 3, 1)
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
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            float _RC_IdPass;

            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }

            half4 frag() : SV_Target { return _RC_IdPass > 0.5 ? half4(0, 0, 0, 1) : half4(_Color.rgb, 1); }
            ENDHLSL
        }
    }
}
