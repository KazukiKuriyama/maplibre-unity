Shader "MapLibre/FillExtrusionPattern"
{
    Properties
    {
        _PatternTex ("Pattern Atlas", 2D) = "white" {}
        _PatternUVRect ("Pattern UV Rect (xy=offset, zw=size)", Vector) = (0, 0, 1, 1)
        _PatternPixelSize ("Pattern Pixel Size (CSS px wide, tall)", Vector) = (32, 32, 0, 0)
        _CSSToLocal ("CSS to Local conversion", Float) = 0.001
        _Opacity ("Opacity", Range(0, 1)) = 1.0
        _StyleLightDir ("Light Direction (world-space, toward source)", Vector) = (0.4330127, 0.8660254, 0.25, 0)
        _StyleLightColor ("Light Color", Color) = (1, 1, 1, 1)
        _StyleLightIntensity ("Light Intensity", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+1"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite On
        Cull Back

        Pass
        {
            Name "FillExtrusionPattern"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float2 tileUV     : TEXCOORD2;
            };

            TEXTURE2D(_PatternTex);
            SAMPLER(sampler_PatternTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PatternUVRect;
                float4 _PatternPixelSize;
                float _CSSToLocal;
                float _Opacity;
                float4 _StyleLightDir;
                float4 _StyleLightColor;
                float _StyleLightIntensity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.tileUV = input.positionOS.xz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (abs(input.tileUV.x) > 0.5 || abs(input.tileUV.y) > 0.5)
                    discard;

                float2 patternLocal = _PatternPixelSize.xy * _CSSToLocal;
                if (patternLocal.x <= 0.0 || patternLocal.y <= 0.0)
                    return half4(0, 0, 0, 1);

                float3 normal = normalize(input.normalWS);
                float2 surfaceUV;

                // Roof (or floor): top-down projection.
                // For walls: pick U from the in-plane horizontal axis the wall stretches along
                // (the axis perpendicular to its horizontal normal), and V from Y for vertical.
                if (abs(normal.y) > 0.5)
                {
                    surfaceUV.x = (input.positionOS.x + 0.5) / patternLocal.x;
                    surfaceUV.y = (input.positionOS.z + 0.5) / patternLocal.y;
                }
                else
                {
                    // If normal points more along X, the wall extends in Z, so U follows Z.
                    float horizU = abs(normal.x) > abs(normal.z)
                        ? input.positionOS.z + 0.5
                        : input.positionOS.x + 0.5;
                    surfaceUV.x = horizU / patternLocal.x;
                    surfaceUV.y = input.positionOS.y / patternLocal.y;
                }

                float2 wrapped = frac(surfaceUV);
                float2 atlasUV = _PatternUVRect.xy + wrapped * _PatternUVRect.zw;
                half4 base = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, atlasUV);

                float3 lightDir = normalize(_StyleLightDir.xyz);
                float ndotl = saturate(dot(normal, lightDir));
                float ambientStrength = lerp(0.7, 0.3, _StyleLightIntensity);
                float diffuseStrength = lerp(0.3, 0.7, _StyleLightIntensity);
                float3 ambient = ambientStrength;
                float3 diffuse = _StyleLightColor.rgb * (ndotl * diffuseStrength);
                float3 lighting = ambient + diffuse;

                half4 color = base;
                color.rgb *= lighting;
                color.a = _Opacity;
                return color;
            }
            ENDHLSL
        }
    }
}
