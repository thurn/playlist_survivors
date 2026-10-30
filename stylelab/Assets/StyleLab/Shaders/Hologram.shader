// Treatment 6, "Hologram": see-through projections. Additive, with a strong fresnel edge,
// fine scanlines, a bright band rolling up the body, a dim wireframe, red and cyan fringes on
// opposite edges, flicker, and horizontal glitch slices on the off-beats. Death scans it out
// from the top down.
Shader "StyleLab/Hologram"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.2, 1.4, 2.0, 1)
        _Glitch ("Glitch", Float) = 0.12
        [HideInInspector] _Hit ("", Float) = 0
        [HideInInspector] _Death ("", Float) = 0
        [HideInInspector] _Seed ("", Float) = 0
        [HideInInspector] _Center ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HeightM ("", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "StyleLabCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Glitch;
                SL_INSTANCE_PROPS
            CBUFFER_END

            SLVaryings vert(SLAttributes v)
            {
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                // Glitch slices: just after each off-beat, a few horizontal slices jump sideways.
                float offbeat = frac(_SL_Beat + 0.5);
                float active = step(offbeat, 0.14) + _Death;
                float slice = floor(v.rest.y * 7.0) + floor(_SL_Time * 18.0) + _Seed * 31.0;
                float r = SL_Hash1(slice);
                float3 side = normalize(cross(float3(0, 1, 0), GetWorldSpaceNormalizeViewDir(p)) + 1e-4);
                p += side * step(0.75, r) * (r - 0.75) * 4.0 * _Glitch * saturate(active);
                return SL_Finish(v, p, n);
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                float h = i.rest.y;
                float h01 = saturate(h / max(_HeightM, 0.1));
                float cut = 1.0 - _Death * 1.1;
                clip(cut - h01);
                float scanEdge = _Death > 0 ? smoothstep(0.06, 0.0, cut - h01) : 0.0;

                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float fres = pow(1.0 - saturate(dot(N, V)), 1.6);
                float scan = 0.55 + 0.45 * sin(i.positionWS.y * 110.0 - _SL_Time * 8.0);
                float roll = exp(-abs(frac(h * 0.45 - _SL_Time * 0.55) - 0.5) * 18.0);
                float flicker = 0.82 + 0.18 * SL_Hash1(floor(_SL_Time * 24.0) + _Seed * 7.0);
                float wave = SL_Wave(i.positionWS, 6.0);
                float wire = SL_Wire(i.bary.xyz, 1.2);

                float3 color = _Color.rgb * ((0.05 + fres * 1.1) * scan + roll * 0.5 + wire * 0.3);
                float3 vn = mul((float3x3)UNITY_MATRIX_V, N);
                color *= float3(1.0 + vn.x * 0.9, 1.0, 1.0 - vn.x * 0.9);
                color *= flicker * (0.75 + 1.0 * wave);
                color += float3(1.5, 1.5, 1.5) * _Hit * fres + _Color.rgb * 4.0 * scanEdge;
                return half4(color, 0);
            }
            ENDHLSL
        }
    }
}
