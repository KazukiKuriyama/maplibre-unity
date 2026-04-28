Shader "MapLibre/TerrainTile"
{
    Properties
    {
        _MainTex ("Tile Texture", 2D) = "white" {}
        _DemTex ("DEM Texture", 2D) = "black" {}
        _Opacity ("Opacity", Range(0, 1)) = 1.0
        _MetersToWorldY ("Meters → World Y", Float) = 0.0
        _DemUVOffset ("DEM UV Offset", Vector) = (0, 0, 0, 0)
        _DemUVScale ("DEM UV Scale", Vector) = (1, 1, 0, 0)
        [KeywordEnum(MAPBOX, TERRARIUM)] _ENCODING ("DEM Encoding", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        ZWrite On
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

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
            TEXTURE2D(_DemTex);
            // Use URP's built-in sampler_PointClamp (defined in Core.hlsl) so the
            // DEM sample works in vertex stage — the auto-paired `sampler_DemTex`
            // can be unbound in vertex when the texture comes from a runtime
            // source without import settings.

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _DemTex_ST;
                float _Opacity;
                float _MetersToWorldY;
                float4 _DemUVOffset;
                float4 _DemUVScale;
            CBUFFER_END

            float DecodeElevation(float3 rgb)
            {
                float3 c = rgb * 255.0;
                #if _ENCODING_TERRARIUM
                    return (c.r * 256.0 + c.g + c.b / 256.0) - 32768.0;
                #else
                    return -10000.0 + (c.r * 65536.0 + c.g * 256.0 + c.b) * 0.1;
                #endif
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float2 demUV = _DemUVOffset.xy + input.uv * _DemUVScale.xy;
                float3 demRGB = SAMPLE_TEXTURE2D_LOD(_DemTex, sampler_PointClamp, demUV, 0).rgb;

                // CRITICAL: DEM tiles arrive flagged as sRGB. In Linear color space
                // projects (URP default), the GPU automatically applies sRGB→Linear
                // when sampling, which corrupts the encoded elevation bytes. Undo
                // the conversion so DecodeElevation operates on the raw byte ratios
                // that the encoding spec assumes.
                #ifndef UNITY_COLORSPACE_GAMMA
                    demRGB = LinearToSRGB(demRGB);
                #endif

                float elevationMeters = max(DecodeElevation(demRGB), 0.0);

                input.positionOS.y += elevationMeters * _MetersToWorldY;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                color.a *= _Opacity;
                return color;
            }
            ENDHLSL
        }
    }
}
