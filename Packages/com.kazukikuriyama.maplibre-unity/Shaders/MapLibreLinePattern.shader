Shader "MapLibre/LinePattern"
{
    Properties
    {
        _PatternTex ("Pattern Atlas", 2D) = "white" {}
        _PatternUVRect ("Pattern UV Rect (xy=offset, zw=size)", Vector) = (0, 0, 1, 1)
        _PatternPixelSize ("Pattern Pixel Size (CSS px wide, tall)", Vector) = (32, 32, 0, 0)
        _LayerOpacity ("Layer Opacity", Range(0, 1)) = 1.0
        _CSSToLocal ("CSS to Local conversion", Float) = 0.001
        _WidthScale ("Width Scale (zoom-dependent)", Float) = 1.0
        _ClipExtend ("Clip Extend", Float) = 0.015625
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "LinePattern"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 texcoord  : TEXCOORD0;   // perpendicular normal (signed)
                float2 texcoord1 : TEXCOORD1;   // x = cumulative distance, y = side sign (+1/-1)
                float2 texcoord2 : TEXCOORD2;   // x = line width (CSS px), y = line offset (CSS px)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 tileUV     : TEXCOORD0;
                float  lineDist   : TEXCOORD1;
                float  sideSign   : TEXCOORD2;
            };

            TEXTURE2D(_PatternTex);
            SAMPLER(sampler_PatternTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PatternUVRect;
                float4 _PatternPixelSize;
                float _LayerOpacity;
                float _CSSToLocal;
                float _WidthScale;
                float _ClipExtend;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                float halfWidthLocal = input.texcoord2.x * _WidthScale * _CSSToLocal * 0.5;
                float offsetLocal = input.texcoord2.y * _CSSToLocal;

                float3 pos = input.positionOS.xyz;
                pos.xz += input.texcoord.xy * halfWidthLocal;
                pos.xz += input.texcoord.xy * input.texcoord1.y * offsetLocal;
                output.positionCS = TransformObjectToHClip(pos);
                output.tileUV = input.positionOS.xz;
                output.lineDist = input.texcoord1.x;
                output.sideSign = input.texcoord1.y;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float clipBound = 0.5 + _ClipExtend;
                if (abs(input.tileUV.x) > clipBound || abs(input.tileUV.y) > clipBound)
                    discard;

                // Pattern repeats along the line direction every patternRepeatLocal units.
                // U axis = position along the line in pattern-space.
                // V axis = position across the line (-1 → 0, +1 → 1).
                float patternRepeatLocal = _PatternPixelSize.x * _CSSToLocal;
                if (patternRepeatLocal <= 0.0)
                    return half4(0, 0, 0, 0);

                float u = frac(input.lineDist / patternRepeatLocal);
                float v = saturate(input.sideSign * 0.5 + 0.5);

                float2 atlasUV = _PatternUVRect.xy + float2(u, v) * _PatternUVRect.zw;
                half4 color = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, atlasUV);
                color.a *= _LayerOpacity;
                return color;
            }
            ENDHLSL
        }
    }
}
