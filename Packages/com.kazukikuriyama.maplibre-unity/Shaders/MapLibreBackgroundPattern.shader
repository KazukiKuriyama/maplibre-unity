Shader "MapLibre/BackgroundPattern"
{
    Properties
    {
        _PatternTex ("Pattern Atlas", 2D) = "white" {}
        // xy = atlas sub-rect offset (0..1), zw = sub-rect size (0..1).
        _PatternUVRect ("Pattern UV Rect (xy=offset, zw=size)", Vector) = (0, 0, 1, 1)
        // x = pattern width in CSS px, y = pattern height in CSS px.
        _PatternPixelSize ("Pattern Pixel Size (CSS px)", Vector) = (32, 32, 0, 0)
        // CSS px → world units (= frustumHeight / screenHeight). Updated per-frame.
        _CSSToWorld ("CSS to World", Float) = 0.001
        _LayerOpacity ("Layer Opacity", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry-1" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "BackgroundPattern"
            Tags { "LightMode" = "UniversalForward" }

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
                // World-space XZ — feed directly into the pattern UV so the pattern
                // tiles seamlessly across the ground plane.
                float2 worldXZ    : TEXCOORD0;
            };

            TEXTURE2D(_PatternTex);
            SAMPLER(sampler_PatternTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PatternUVRect;
                float4 _PatternPixelSize;
                float _CSSToWorld;
                float _LayerOpacity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(worldPos);
                output.worldXZ = worldPos.xz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 patternWorld = _PatternPixelSize.xy * _CSSToWorld;
                if (patternWorld.x <= 0.0 || patternWorld.y <= 0.0)
                    return half4(0, 0, 0, 0);

                float2 surfaceUV = input.worldXZ / patternWorld;
                float2 wrapped = frac(surfaceUV);
                if (wrapped.x < 0.0) wrapped.x += 1.0;
                if (wrapped.y < 0.0) wrapped.y += 1.0;

                float2 atlasUV = _PatternUVRect.xy + wrapped * _PatternUVRect.zw;
                half4 color = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, atlasUV);
                color.a *= _LayerOpacity;
                return color;
            }
            ENDHLSL
        }
    }
}
