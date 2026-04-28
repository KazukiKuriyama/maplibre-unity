Shader "MapLibre/Hillshade"
{
    Properties
    {
        _MainTex ("DEM Texture", 2D) = "black" {}
        _TexelSize ("Texel Size", Float) = 0.00390625
        _PixelMeters ("Ground meters per DEM pixel", Float) = 30.0
        _IlluminationDir ("Illumination Direction (xy)", Vector) = (-0.4226, 0.9063, 0, 0)
        _Exaggeration ("Exaggeration", Range(0, 1)) = 0.5
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 1)
        _HighlightColor ("Highlight Color", Color) = (1, 1, 1, 1)
        _AccentColor ("Accent Color", Color) = (0, 0, 0, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1.0
        [KeywordEnum(MAPBOX, TERRARIUM)] _ENCODING ("DEM Encoding", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ENCODING_MAPBOX _ENCODING_TERRARIUM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _TexelSize;
                float _PixelMeters;
                float4 _IlluminationDir;
                float _Exaggeration;
                float4 _ShadowColor;
                float4 _HighlightColor;
                float4 _AccentColor;
                float _Opacity;
            CBUFFER_END

            // Decode elevation from DEM texture based on encoding format
            float DecodeElevation(float3 rgb)
            {
                float3 c = rgb * 255.0;
                #if _ENCODING_TERRARIUM
                    // Terrarium: height = (R * 256 + G + B / 256) - 32768
                    return (c.r * 256.0 + c.g + c.b / 256.0) - 32768.0;
                #else
                    // Mapbox Terrain-RGB: height = -10000 + ((R * 65536 + G * 256 + B) * 0.1)
                    return -10000.0 + (c.r * 65536.0 + c.g * 256.0 + c.b) * 0.1;
                #endif
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 uv = input.uv;
                float ts = _TexelSize;

                // Sample 4 neighboring pixels to compute gradient
                float3 rgbL = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-ts, 0)).rgb;
                float3 rgbR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( ts, 0)).rgb;
                float3 rgbD = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -ts)).rgb;
                float3 rgbU = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  ts)).rgb;

                float hL = DecodeElevation(rgbL);
                float hR = DecodeElevation(rgbR);
                float hD = DecodeElevation(rgbD);
                float hU = DecodeElevation(rgbU);

                // Compute physical slope (rise/run) by dividing the elevation
                // difference (meters) by the horizontal distance spanned by the
                // two samples (2 * _PixelMeters). This makes the result a
                // dimensionless gradient — independent of source DEM zoom level
                // or latitude — so `_Exaggeration` acts as a true slope multiplier.
                float invRun = 1.0 / max(2.0 * _PixelMeters, 1e-6);
                float dzdx = (hR - hL) * invRun * _Exaggeration;
                float dzdy = (hU - hD) * invRun * _Exaggeration;

                // Normal from gradient. Using Y=1 keeps the normal physically
                // meaningful: slope=1 corresponds to a 45° face.
                float3 normal = normalize(float3(-dzdx, 1.0, dzdy));

                // Light direction (illumination dir is 2D azimuth: x=sin(azimuth), y=cos(azimuth))
                float3 lightDir = normalize(float3(_IlluminationDir.x, 0.5, _IlluminationDir.y));

                // Diffuse lighting
                float NdotL = dot(normal, lightDir);

                // Slope magnitude for accent color (steeper slopes get more accent)
                float slope = sqrt(dzdx * dzdx + dzdy * dzdy);
                float slopeIntensity = saturate(slope * 0.5);

                // Blend between shadow and highlight based on lighting
                // NdotL in [-1, 1] -> remap to shadow/highlight blend
                float highlightFactor = saturate(NdotL * 0.5 + 0.5);
                float shadowFactor = 1.0 - highlightFactor;

                // Compose final color
                half3 color = lerp(_ShadowColor.rgb, _HighlightColor.rgb, highlightFactor);

                // Mix in accent color for steep slopes
                color = lerp(color, _AccentColor.rgb, slopeIntensity * 0.3);

                // Alpha: combine shadow/highlight intensity with opacity
                float intensity = max(shadowFactor * 0.6, highlightFactor * 0.4) + slopeIntensity * 0.2;
                half alpha = saturate(intensity) * _Opacity;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
