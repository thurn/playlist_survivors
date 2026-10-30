// Elven Realm's triplanar rock (SyntyStudios_RockTriplanar, a built-in pipeline surface shader)
// ported to URP for the kit (TD14; environment.md, Environment rendering: "only what the kit uses
// is ported"). Sides: the rock-wall albedo and normals projected on the object's three axes,
// blended by the object-space normal; top: grass albedo and ground normals projected on world XZ,
// laid over where the normal faces up (coverage amount and falloff, as the vendor shader's). Lit
// through Demo3DLightingPBR, so at E2 the rock takes the static shadow map like the rest of the kit.
//
// Hand-written HLSL rather than Shader Graph (environment.md's suggestion): a Shader Graph asset
// can't be authored reliably in batch mode, and what the port needs from it, GPU Resident Drawer
// support, comes from DOTS instancing here. The material properties reuse URP Lit's (LitInput's
// UnityPerMaterial, so the SRP Batcher and DOTS instancing take it, and URP Lit's own shadow,
// depth and normals passes work unchanged):
//   _BaseMap          sides' albedo (_BaseMap_ST.x: the sides' tiling per object-space metre)
//   _BumpMap          sides' normals (_BumpScale)
//   _DetailAlbedoMap  top albedo (_DetailAlbedoMap_ST.x: the top's tiling per world metre)
//   _DetailNormalMap  top normals (_DetailNormalMapScale)
//   _BaseColor        the sides' colour shift; _SpecColor the top's colour shift
//   _ClearCoatMask    coverage amount (-1..1); _ClearCoatSmoothness coverage falloff (exponent)
Shader "Demo3D/RockTriplanar"
{
    Properties
    {
        [MainTexture] _BaseMap("Sides Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Sides Colour Shift", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.2
        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.02
        _MetallicGlossMap("Metallic", 2D) = "white" {}
        _SpecColor("Top Colour Shift", Color) = (1, 1, 1)
        _SpecGlossMap("Specular", 2D) = "white" {}
        _BumpScale("Sides Normal Scale", Float) = 1.0
        _BumpMap("Sides Normals", 2D) = "bump" {}
        _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}
        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}
        [HDR] _EmissionColor("Color", Color) = (0, 0, 0)
        _EmissionMap("Emission", 2D) = "white" {}
        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailAlbedoMap("Top Albedo", 2D) = "white" {}
        _DetailNormalMapScale("Top Normal Scale", Range(0.0, 2.0)) = 1.0
        _DetailNormalMap("Top Normals", 2D) = "bump" {}
        _ClearCoatMask("Coverage Amount", Range(-1.0, 1.0)) = 0.0
        _ClearCoatSmoothness("Coverage Falloff", Range(0.01, 2.0)) = 0.5
        _Surface("__surface", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _SrcBlend("__src", Float) = 1.0
        _DstBlend("__dst", Float) = 0.0
        _ZWrite("__zw", Float) = 1.0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        _QueueOffset("Queue offset", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull[_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "StaticShadow.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // A tangent-space normal from a planar projection, whiteout-blended onto the surface
            // normal's two in-plane components (Golus's triplanar normals); returned in the
            // projection's own axes (xy in the plane, z along its axis).
            half3 Whiteout(half3 tn, half2 surfaceInPlane, half surfaceAlong)
            {
                return half3(tn.xy + surfaceInPlane, abs(tn.z) * surfaceAlong);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // Sides: the object's three axes, weighted by the object-space normal.
                half3 nOS = normalize(i.normalOS);
                half3 w = abs(nOS);
                w /= max(dot(w, 1), 1e-4);
                float t = _BaseMap_ST.x;
                float2 uvX = i.positionOS.zy * t, uvY = i.positionOS.xz * t, uvZ = i.positionOS.xy * t;
                half3 sideAlbedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * w.x
                                 + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * w.y
                                 + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * w.z;
                half3 tnX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _BumpScale);
                half3 tnY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), _BumpScale);
                half3 tnZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _BumpScale);
                half3 nX = Whiteout(tnX, nOS.zy, nOS.x).zyx;
                half3 nY = Whiteout(tnY, nOS.xz, nOS.y).xzy;
                half3 nZ = Whiteout(tnZ, nOS.xy, nOS.z);
                half3 sideWS = TransformObjectToWorldNormal(normalize(nX * w.x + nY * w.y + nZ * w.z));

                // Top: world XZ, laid over where the surface faces up.
                half3 nWS = normalize(i.normalWS);
                float2 uvTop = i.positionWS.xz * _DetailAlbedoMap_ST.x;
                half3 topAlbedo = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, uvTop).rgb;
                half3 tnTop = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uvTop), _DetailNormalMapScale);
                half3 topWS = normalize(Whiteout(tnTop, nWS.xz, nWS.y).xzy);
                half amount = _ClearCoatMask, falloff = max(_ClearCoatSmoothness, 0.01);
                half cover = pow(saturate(nWS.y + amount), falloff);
                half3 normalWS = normalize(lerp(sideWS, topWS, cover));
                half coverAlbedo = pow(saturate(normalWS.y + amount), falloff);

                SurfaceData s = (SurfaceData)0;
                s.albedo = lerp(_BaseColor.rgb * sideAlbedo, _SpecColor.rgb * topAlbedo, coverAlbedo);
                s.metallic = _Metallic;
                s.smoothness = _Smoothness;
                s.occlusion = 1;
                s.alpha = 1;
                s.normalTS = half3(0, 0, 1);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = 1;
                if (_Demo3DIdMask > 0.5) return Demo3DIdSet(); // TD15's ID render: black (a structure blue), depth kept
                half3 color = Demo3DLightingPBR(inputData, s);
                color = MixFog(color, inputData.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
        UsePass "Universal Render Pipeline/Lit/MOTIONVECTORS"
        UsePass "Universal Render Pipeline/Lit/META"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
