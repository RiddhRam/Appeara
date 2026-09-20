// Segmented gauge (core integrity, fabrication progress). _Fill 0-1 lights segments left to right; the leading
// segment pulses while _Pulse > 0. Stereo-instancing safe.
Shader "Armory/HudBar"
{
    Properties
    {
        _Fill ("Fill", Range(0, 1)) = 1
        _Segments ("Segments", Float) = 20
        _Gap ("Gap", Range(0, 0.9)) = 0.3
        [HDR] _On ("On", Color) = (0.36, 0.88, 1.0, 1)
        _Off ("Off", Color) = (0.2, 0.3, 0.4, 0.25)
        _Pulse ("Pulse", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-5" "RenderPipeline" = "UniversalPipeline" }
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
                float _Fill;
                float _Segments;
                float _Gap;
                float4 _On;
                float4 _Off;
                float _Pulse;
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
                float x = input.uv.x * _Segments;
                float index = floor(x);
                if (frac(x) > 1.0 - _Gap) discard;
                float lit = step((index + 0.5) / _Segments, _Fill);
                bool leading = lit > 0.5 && (index + 1.5) / _Segments > _Fill;
                float4 on = _On;
                if (leading && _Pulse > 0.0) on.rgb *= 0.7 + 0.5 * sin(_Time.y * 8.0);
                return lerp(_Off, on, lit);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
