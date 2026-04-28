Shader "MapLibre/FillPattern"
{
    Properties
    {
        _PatternTex ("Pattern Atlas", 2D) = "white" {}
        _PatternUVRect ("Pattern UV Rect (xy=offset, zw=size)", Vector) = (0, 0, 1, 1)
        _PatternPixelSize ("Pattern Pixel Size (CSS px wide, tall)", Vector) = (32, 32, 0, 0)
        _CSSToLocal ("CSS to Local conversion", Float) = 0.001
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
            Name "FillPattern"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // Object-space XZ for tile clipping AND world-position-derived pattern UV.
                float2 tileUV     : TEXCOORD0;
            };

            TEXTURE2D(_PatternTex);
            SAMPLER(sampler_PatternTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PatternUVRect;
                float4 _PatternPixelSize;
                float _CSSToLocal;
                float _LayerOpacity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.tileUV = input.positionOS.xz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (abs(input.tileUV.x) > 0.5 || abs(input.tileUV.y) > 0.5)
                    discard;

                // Pattern world size (in tile-local units) = pixelSize * cssToLocal.
                // Repeat the pattern every patternWorldSize units across the polygon
                // surface, so the pattern stays at a constant CSS-pixel scale on screen
                // regardless of zoom (matching MapLibre GL JS).
                float2 patternLocal = _PatternPixelSize.xy * _CSSToLocal;
                if (patternLocal.x <= 0.0 || patternLocal.y <= 0.0)
                    return half4(0, 0, 0, 0);

                // tileUV is centered at 0; shift to [0,1] so frac() wraps cleanly.
                float2 surfaceUV = (input.tileUV + 0.5) / patternLocal;
                float2 wrapped = frac(surfaceUV);
                // Map wrapped UV into the atlas sub-rect occupied by this pattern image.
                float2 atlasUV = _PatternUVRect.xy + wrapped * _PatternUVRect.zw;

                half4 color = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, atlasUV);
                color.a *= _LayerOpacity;
                return color;
            }
            ENDHLSL
        }
    }
}
