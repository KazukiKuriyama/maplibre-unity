Shader "MapLibre/FillExtrusion"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 1.0
        // Style "light" properties. World-space direction (toward the light source).
        // Defaults match MapLibre Style Spec: position [1.15, 210, 30] → light from
        // SW high; intensity 0.5; color white.
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
            Name "FillExtrusion"

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
                float4 color      : COLOR;
                float2 tileUV     : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
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
                output.color = input.color;
                output.tileUV = input.positionOS.xz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (abs(input.tileUV.x) > 0.5 || abs(input.tileUV.y) > 0.5)
                    discard;

                // Style-driven directional lighting. _StyleLightDir is the world-space
                // unit vector pointing TOWARD the light source. Intensity rebalances
                // the ambient/diffuse split: at 0 we go pure ambient (flat), at 1
                // we lean diffuse (high contrast).
                float3 normal = normalize(input.normalWS);
                float3 lightDir = normalize(_StyleLightDir.xyz);
                float ndotl = saturate(dot(normal, lightDir));

                float ambientStrength = lerp(0.7, 0.3, _StyleLightIntensity);
                float diffuseStrength = lerp(0.3, 0.7, _StyleLightIntensity);

                float3 ambient = ambientStrength;
                float3 diffuse = _StyleLightColor.rgb * (ndotl * diffuseStrength);
                float3 lighting = ambient + diffuse;

                half4 color = input.color;
                color.rgb *= lighting;
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }
    }
}
