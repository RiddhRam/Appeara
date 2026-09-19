// Blueprint hologram for AI-generated weapon schematics.
// Technique after Cyanilux's hologram breakdown: unlit, additive, world-space scrolling scanlines, noise wobble,
// stepped glitch bands with RGB split, HDR tint so URP Bloom makes it glow. Black texture areas vanish (additive).
// _Reveal sweeps the image in from the bottom; with no texture (_HasTex = 0) it draws a scanning grid placeholder.
// Stereo-instancing macros keep it correct in single-pass instanced VR (Quest Link / OpenXR default).
Shader "Armory/HologramBlueprint"
{
    Properties
    {
        _MainTex ("Blueprint", 2D) = "black" {}
        [HDR] _Tint ("Tint", Color) = (0.35, 0.9, 1.6, 1)
        _HasTex ("Has Texture", Float) = 0
        _Reveal ("Reveal", Range(0, 1.1)) = 1
        _Alpha ("Alpha", Range(0, 1)) = 1
        _ScanDensity ("Scanline Density", Float) = 90
        _ScanSpeed ("Scanline Speed", Float) = 0.35
        _Glitch ("Glitch Strength", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Hologram"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float _HasTex;
                float _Reveal;
                float _Alpha;
                float _ScanDensity;
                float _ScanSpeed;
                float _Glitch;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float t = _Time.y;
                float2 uv = input.uv;

                // Reveal sweep: nothing above the sweep line, a bright edge on it.
                float revealEdge = uv.y - _Reveal;
                clip(-revealEdge);
                float sweepGlow = smoothstep(0.03, 0.0, abs(revealEdge)) * 2.5;

                // Gentle horizontal wobble + occasional stepped glitch bands (time-quantised, per 1/24 of height).
                float wobble = (Hash(float2(floor(input.positionWS.y * 40.0 + t * 3.0), 1.7)) - 0.5) * 0.004;
                float band = floor(uv.y * 24.0);
                float glitchRoll = Hash(float2(band, floor(t * 12.0)));
                float glitch = step(0.965, glitchRoll) * (glitchRoll - 0.965) * 6.0 * _Glitch;
                float2 guv = float2(uv.x + wobble + glitch, uv.y);

                half3 color;
                if (_HasTex > 0.5)
                {
                    // RGB split grows with the glitch offset.
                    float split = 0.002 + glitch * 0.5;
                    half r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, guv + float2(split, 0)).r;
                    half g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, guv).g;
                    half b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, guv - float2(split, 0)).b;
                    half3 tex = half3(r, g, b);
                    // Keep the image's own colours but push them toward the hologram tint.
                    color = lerp(tex, tex.ggg * _Tint.rgb, 0.35) * 1.4;
                }
                else
                {
                    // Placeholder: blueprint grid + a scanning bar while the image is being generated.
                    float2 grid = abs(frac(guv * 12.0) - 0.5);
                    float lines = smoothstep(0.47, 0.5, max(grid.x, grid.y));
                    float bar = smoothstep(0.06, 0.0, abs(uv.y - frac(t * 0.6)));
                    color = _Tint.rgb * (lines * 0.35 + bar * 0.8 + 0.03);
                }

                // World-space scanlines (sine + sharp frac lines), scrolling upward.
                float y = (input.positionWS.y + t * _ScanSpeed) * _ScanDensity;
                float scan = saturate(sin(y)) * 0.25 + 0.75;
                scan += pow(frac(y * 0.05), 8.0) * 0.4;

                // Frame glow around the card edge.
                float edgeDist = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                float edge = smoothstep(0.015, 0.0, edgeDist);

                float flicker = 0.92 + 0.08 * sin(t * 53.0) * Hash(float2(floor(t * 20.0), 3.1));
                color = color * scan * flicker + _Tint.rgb * (edge * 0.8 + sweepGlow);
                return half4(color * _Alpha, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
