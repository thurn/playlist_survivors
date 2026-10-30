// Treatment 5, "Spectrum": synthesizer creatures. A black lacquer body with a thin-film
// rainbow that shifts with the viewing angle, height and distance from the hero, so colour
// bands sweep through the pack on every beat. Every triangle edge glows as a neon wire;
// equalizer bars climb the body on each beat, and the bass swells the mesh. Death bursts
// into rainbow shards.
Shader "StyleLab/Synth"
{
    Properties
    {
        _Hue ("Hue offset", Range(0, 1)) = 0
        _WireGlow ("Wire glow", Float) = 2.5
        _Bulge ("Bass swell (metres)", Float) = 0.035
        [HideInInspector] _Hit ("", Float) = 0
        [HideInInspector] _Death ("", Float) = 0
        [HideInInspector] _Seed ("", Float) = 0
        [HideInInspector] _Center ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HeightM ("", Float) = 1.8
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
                half _Hue; half _WireGlow; float _Bulge;
                SL_INSTANCE_PROPS
            CBUFFER_END

            SLVaryings vert(SLAttributes v)
            {
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float h01 = saturate(v.rest.y / max(_HeightM, 0.1));
                p += n * _Bulge * _SL_Pulse * (0.6 + 0.4 * sin(h01 * 12.0 - _SL_Time * 10.0));
                float3 face = TransformObjectToWorldNormal(v.tangentOS.xyz);
                float shatter = SL_Shatter(p, face, v.bary.w, _Death, 1.3);
                SLVaryings o = SL_Finish(v, p, n);
                o.extra.x = shatter;
                return o;
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                float shatter = i.extra.x;
                clip(0.98 - shatter);
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float NdotV = saturate(dot(N, V));
                float h01 = saturate(i.rest.y / max(_HeightM, 0.1));
                float d = distance(i.positionWS.xz, _SL_HeroPos.xz);
                float hue = frac(_Hue + h01 * 0.45 + (1.0 - NdotV) * 0.5 + d * 0.035 - _SL_Beat * 0.125);
                float wave = SL_Wave(i.positionWS, 6.0);

                Light L = SL_MainLight(i.positionWS);
                float3 H = normalize(L.direction + V);
                float spec = pow(saturate(dot(N, H)), 120.0) * L.shadowAttenuation;
                float fres = pow(1.0 - NdotV, 2.0);
                float3 color = float3(0.008, 0.006, 0.02);
                color += SL_Hue(hue) * (0.1 + 1.5 * fres) * (0.8 + 0.6 * wave);
                color += spec * 2.5 * L.color;
                float3 addDiffuse = 0, addSpec = 0;
                SL_AdditionalLights(i.positionWS, i.positionCS, N, V, 0.0, 120.0, addDiffuse, addSpec);
                color += addSpec * 2.0;

                float wire = SL_Wire(i.bary.xyz, 1.6);
                color += SL_Hue(hue + 0.33) * wire * _WireGlow * (0.35 + 2.2 * wave);

                // Equalizer bars: bands up the body, lit up to a level set by the beat.
                float level = 0.08 + _SL_Pulse * 0.75 + _SL_Energy * 0.15;
                float bar = step(frac(h01 * 9.0), 0.3) * step(h01, level);
                color += SL_Hue(h01 * 0.6 + _Hue) * bar * 0.5;

                color += float3(1.0, 1.0, 1.0) * _Hit + SL_Hue(hue + i.bary.w) * 2.5 * shatter;
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
        UsePass "Hidden/StyleLab/Passes/SHADOWCASTER"
        UsePass "Hidden/StyleLab/Passes/DEPTHONLY"
        UsePass "Hidden/StyleLab/Passes/DEPTHNORMALS"
    }
}
