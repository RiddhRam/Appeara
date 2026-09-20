// Renders AI-generated weapon art (transparent PNG) as the held weapon: alpha-clipped so the silhouette is the
// gun, double sided so it reads from either hand, with a coloured rim glow so it still looks fabricated rather
// than pasted on. Stereo-instancing safe for single-pass VR.
Shader "Armory/WeaponCutout"
{
    Properties
    {
        _MainTex ("Art", 2D) = "white" {}
        [HDR] _Rim ("Rim Glow", Color) = (0.4, 0.9, 1.2, 1)
        _RimWidth ("Rim Width", Range(0, 0.02)) = 0.006
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.35
        _Brightness ("Brightness", Range(0.5, 3)) = 1.25
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float4 _Rim;
                float _RimWidth;
                float _Cutoff;
                float _Brightness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 art = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Neighbouring alpha decides the outline: opaque here, transparent nearby means we are on the edge.
                float2 step = _RimWidth;
                float neighbours =
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(step.x, 0)).a +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(step.x, 0)).a +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0, step.y)).a +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(0, step.y)).a;
                float edge = art.a > _Cutoff ? saturate(1.0 - neighbours * 0.25) : 0.0;

                clip(art.a - _Cutoff);
                half3 color = art.rgb * _Brightness + _Rim.rgb * edge * 1.5;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
