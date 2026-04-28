Shader "MapLibre/Fill"
{
    Properties
    {
        // Per-feature fill color (with opacity baked in) is supplied via vertex colors.
        // _LayerOpacity multiplies on top for runtime SetPaintProperty("fill-opacity") changes
        // that are not data-driven.
        _LayerOpacity ("Layer Opacity", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Fill"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 tileUV     : TEXCOORD0;
                half4  fillColor  : COLOR0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _LayerOpacity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Pass object-space XZ for tile boundary clipping
                // Mesh coordinates are in [-0.5, 0.5] range
                output.tileUV = input.positionOS.xz;
                output.fillColor = half4(input.color);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Clip fragments outside tile boundaries
                // MVT features can extend beyond [0, extent] due to buffer
                if (abs(input.tileUV.x) > 0.5 || abs(input.tileUV.y) > 0.5)
                    discard;

                half4 color = input.fillColor;
                color.a *= _LayerOpacity;
                return color;
            }
            ENDHLSL
        }
    }
}
