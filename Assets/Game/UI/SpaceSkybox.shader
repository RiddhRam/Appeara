// Procedural space backdrop for the station windows. Unity's default sky is a white daytime dome, which flattens
// every screenshot and lights the arena like an afternoon. Everything here is analytic (no cubemap, no textures)
// so it costs one cheap fullscreen fill at 90 fps: a navy-to-black gradient, two hashed star layers, a faint
// two-octave value-noise nebula and a single lit planet whose direction/size/colour are material properties.
// Stereo-instancing macros are required: this renders in single-pass instanced VR (Quest Link / OpenXR default).
Shader "Armory/SpaceSkybox"
{
    Properties
    {
        _SkyHorizon ("Horizon (deep navy)", Color) = (0.016, 0.026, 0.062, 1)
        _SkyZenith ("Zenith (near black)", Color) = (0.002, 0.003, 0.012, 1)
        _StarDensity ("Dim Star Density", Float) = 150
        _BrightStarDensity ("Bright Star Density", Float) = 26
        _StarBrightness ("Star Brightness", Float) = 1
        _TwinkleSpeed ("Twinkle Speed", Float) = 2.2
        [HDR] _NebulaA ("Nebula Cyan", Color) = (0.05, 0.32, 0.44, 1)
        [HDR] _NebulaB ("Nebula Magenta", Color) = (0.30, 0.06, 0.38, 1)
        _NebulaStrength ("Nebula Strength", Range(0, 1)) = 0.35
        _NebulaScale ("Nebula Scale", Float) = 2.4
        _PlanetDir ("Planet Direction", Vector) = (0.62, 0.10, -0.78, 0)
        _PlanetSize ("Planet Angular Size", Range(0.005, 0.5)) = 0.13
        _PlanetColor ("Planet Colour", Color) = (0.20, 0.28, 0.44, 1)
        [HDR] _PlanetRim ("Planet Rim", Color) = (0.22, 0.52, 0.9, 1)
        _PlanetLight ("Planet Light Direction", Vector) = (-0.45, 0.35, 0.82, 0)
        [HideInInspector] _PerformanceMode ("Performance Mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off

        Pass
        {
            Name "SpaceSky"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyHorizon;
                float4 _SkyZenith;
                float _StarDensity;
                float _BrightStarDensity;
                float _StarBrightness;
                float _TwinkleSpeed;
                float4 _NebulaA;
                float4 _NebulaB;
                float _NebulaStrength;
                float _NebulaScale;
                float4 _PlanetDir;
                float _PlanetSize;
                float4 _PlanetColor;
                float4 _PlanetRim;
                float4 _PlanetLight;
                float _PerformanceMode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dirOS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // Smoothed value noise. Two calls make a good enough nebula; anything fancier is invisible at this strength.
            float Value3(float3 p)
            {
                float3 i = floor(p);
                float3 f = p - i;
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash13(i), Hash13(i + float3(1, 0, 0)), f.x);
                float b = lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), f.x);
                float c = lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), f.x);
                float d = lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
            }

            // One star layer. `plane` is where the ray hits the unit cube's face and `face` seeds the hash so the six
            // faces never repeat; the cube projection keeps stars evenly spread, where a lat/long grid would clump
            // them into a knot at the zenith. `coverage` is the fraction of cells that hold a star at all.
            float StarLayer(float2 plane, float face, float density, float coverage, float size)
            {
                float2 p = plane * density;
                float2 cell = floor(p);
                float2 seed = cell + face * 71.7;
                float pick = Hash21(seed);
                if (pick > coverage) return 0.0;
                float2 offset = float2(Hash21(seed + 17.13), Hash21(seed + 43.71));
                float d = length(p - cell - offset);
                float core = saturate(1.0 - d / size);
                core = core * core * core;
                return core * (0.35 + 0.65 * (pick / coverage));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                // The skybox mesh is drawn centred on the camera, so its object space *is* the view direction.
                output.dirOS = input.positionOS.xyz;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 dir = normalize(input.dirOS);

                // Deep navy around the horizon fading to black overhead, so the station reads against something.
                float height = saturate(dir.y * 0.5 + 0.5);
                float3 color = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, height * height);

                // Quest/VR path: the full sky below performs ~30 hashes per pixel. Across two high-resolution eyes
                // that can dominate an entire mobile GPU frame. Keep the same palette but use one hash and no 3D
                // noise; the station windows hide the difference far better than dropped/reprojected frames do.
                if (_PerformanceMode > 0.5)
                {
                    float3 axis = abs(dir);
                    float3 cubeDir = dir / max(axis.x, max(axis.y, axis.z));
                    bool xFace = axis.x >= axis.y && axis.x >= axis.z;
                    bool yFace = !xFace && axis.y >= axis.z;
                    float2 starPlane = xFace ? cubeDir.yz : (yFace ? cubeDir.xz : cubeDir.xy);
                    float faceSeed = xFace ? (cubeDir.x > 0.0 ? 1.0 : 2.0) :
                        (yFace ? (cubeDir.y > 0.0 ? 3.0 : 4.0) : (cubeDir.z > 0.0 ? 5.0 : 6.0));
                    float2 grid = starPlane * 72.0;
                    float starHash = Hash21(floor(grid) + faceSeed * 71.7);
                    float2 offset = frac(float2(starHash * 13.17, starHash * 37.91));
                    float star = step(0.975, starHash) * saturate(1.0 - length(frac(grid) - offset) * 3.2);
                    return half4(color + star * _StarBrightness, 1);
                }

                // Faint nebula: two octaves of value noise for the shape, a third (much larger) for the cyan/magenta mix.
                float n = Value3(dir * _NebulaScale) * 0.65 + Value3(dir * _NebulaScale * 2.7) * 0.35;
                float cloud = saturate(n * n * 2.2 - 0.45);
                float3 nebula = lerp(_NebulaA.rgb, _NebulaB.rgb, saturate(Value3(dir * _NebulaScale * 0.7 + 19.3)));
                color += nebula * cloud * _NebulaStrength;

                // Project the ray onto the unit cube; the two minor axes become an even 2D grid for the stars.
                float3 a = abs(dir);
                float3 cube = dir / max(a.x, max(a.y, a.z));
                bool xMajor = a.x >= a.y && a.x >= a.z;
                bool yMajor = !xMajor && a.y >= a.z;
                float2 plane = xMajor ? cube.yz : (yMajor ? cube.xz : cube.xy);
                float face = xMajor ? (cube.x > 0.0 ? 1.0 : 2.0) : (yMajor ? (cube.y > 0.0 ? 3.0 : 4.0) : (cube.z > 0.0 ? 5.0 : 6.0));

                // Dense dim dust, plus a sparse bright layer that twinkles per star (hash of the cell, not the pixel).
                float dim = StarLayer(plane, face, _StarDensity, 0.34, 0.5);
                float bright = StarLayer(plane, face + 13.0, _BrightStarDensity, 0.5, 0.85);
                float2 brightCell = floor(plane * _BrightStarDensity);
                float twinkle = 0.65 + 0.35 * sin(_Time.y * _TwinkleSpeed + Hash21(brightCell + 5.7) * 31.0);
                color += _StarBrightness * (dim * 0.35 + bright * twinkle * 1.6);

                // Distant planet: a lit sphere with a soft terminator, mottled surface and a thin atmosphere rim.
                float3 planetDir = normalize(_PlanetDir.xyz);
                if (dot(dir, planetDir) > 0.0)
                {
                    float3 up = abs(planetDir.y) > 0.95 ? float3(1, 0, 0) : float3(0, 1, 0);
                    float3 tx = normalize(cross(up, planetDir));
                    float3 ty = cross(planetDir, tx);
                    float2 local = float2(dot(dir, tx), dot(dir, ty)) / max(_PlanetSize, 1e-4);
                    float r = length(local);
                    float disc = smoothstep(1.0, 0.985, r);
                    if (disc > 0.0)
                    {
                        float3 normal = tx * local.x + ty * local.y + planetDir * sqrt(saturate(1.0 - r * r));
                        float lambert = saturate(dot(normal, normalize(_PlanetLight.xyz)));
                        float bands = 0.8 + Value3(normal * 5.0) * 0.35;
                        float3 surface = _PlanetColor.rgb * bands * (lambert * lambert * 0.95 + 0.05);
                        color = lerp(color, surface, disc);
                    }
                    color += _PlanetRim.rgb * smoothstep(1.06, 1.0, r) * smoothstep(0.9, 1.0, r) * 0.5;
                }

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
