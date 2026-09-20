// Dark glass HUD card: chamfered top-left / bottom-right corners, hairline border, header strip,
// bright corner ticks and very faint scanlines. Sizes are in metres (_Size) so borders stay crisp on any card.
// Stereo-instancing safe for single-pass VR.
Shader "Armory/HudPanel"
{
    Properties
    {
        _Size ("Size (m)", Vector) = (0.3, 0.15, 0, 0)
        _Chamfer ("Chamfer (m)", Float) = 0.014
        _Border ("Border (m)", Float) = 0.0012
        _HeaderHeight ("Header (m)", Float) = 0.024
        _Fill ("Fill", Color) = (0.02, 0.035, 0.07, 0.82)
        _HeaderFill ("Header Fill", Color) = (0.06, 0.16, 0.24, 0.9)
        [HDR] _Line ("Line", Color) = (0.36, 0.88, 1.0, 0.55)
        [HDR] _Accent ("Accent", Color) = (0.45, 0.95, 1.2, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Size;
                float _Chamfer;
                float _Border;
                float _HeaderHeight;
                float4 _Fill;
                float4 _HeaderFill;
                float4 _Line;
                float4 _Accent;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 size = _Size.xy;
                float2 p = input.uv * size;
                float px = fwidth(p.x) + fwidth(p.y);

                // Distance inside the chamfered rectangle (positive = inside).
                float edge = min(min(p.x, size.x - p.x), min(p.y, size.y - p.y));
                float cutTL = (p.x + (size.y - p.y) - _Chamfer) * 0.7071;
                float cutBR = ((size.x - p.x) + p.y - _Chamfer) * 0.7071;
                float d = min(edge, min(cutTL, cutBR));
                float inside = smoothstep(-px, px, d);
                if (inside <= 0.0) discard;

                bool header = _HeaderHeight > 0.0 && p.y > size.y - _HeaderHeight;
                float4 color = header ? _HeaderFill : _Fill;
                // Top-lit glass gradient + faint scanlines.
                color.rgb *= lerp(0.8, 1.15, input.uv.y);
                color.rgb += 0.012 * step(0.5, frac(p.y * 380.0));

                // Hairline border, and a divider under the header.
                float border = 1.0 - smoothstep(_Border, _Border + px, d);
                float divider = header ? 0.0 : (1.0 - smoothstep(_Border * 0.6, _Border * 0.6 + px, abs(p.y - (size.y - _HeaderHeight))));
                if (_HeaderHeight <= 0.0) divider = 0.0;
                float lineMask = saturate(border + divider * 0.6);

                // Corner ticks: short, brighter strokes on the two square corners.
                float tick = 0.035;
                float bl = (p.x < tick && p.y < _Border * 2.5) || (p.y < tick && p.x < _Border * 2.5) ? 1.0 : 0.0;
                float tr = (p.x > size.x - tick && p.y > size.y - _Border * 2.5) || (p.y > size.y - tick && p.x > size.x - _Border * 2.5) ? 1.0 : 0.0;
                float accent = saturate(bl + tr);

                float3 rgb = lerp(color.rgb, _Line.rgb, lineMask * _Line.a);
                rgb = lerp(rgb, _Accent.rgb, accent);
                float alpha = max(color.a, max(lineMask * _Line.a, accent)) * inside;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
