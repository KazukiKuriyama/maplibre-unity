Shader "MapLibre/LineGradient"
{
    Properties
    {
        // Same uniforms as MapLibre/Line (per-feature width / offset come from
        // vertex attributes); _GradientTex is a 256x1 ramp baked from the
        // line-gradient interpolate expression, sampled with line-progress (0..1)
        // stored in TEXCOORD3.
        _LayerOpacity ("Layer Opacity", Range(0, 1)) = 1.0
        _CSSToLocal ("CSS to Local conversion", Float) = 0.001
        _WidthScale ("Width Scale (zoom-dependent)", Float) = 1.0
        _ClipExtend ("Clip Extend", Float) = 0.015625
        _GradientTex ("Gradient Ramp", 2D) = "white" {}
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
            Name "LineGradient"

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
                float2 texcoord3 : TEXCOORD3;   // x = line-progress (0..1)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 tileUV     : TEXCOORD0;
                float  progress   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float _LayerOpacity;
                float _CSSToLocal;
                float _WidthScale;
                float _ClipExtend;
                float4 _GradientTex_ST;
            CBUFFER_END

            TEXTURE2D(_GradientTex);
            SAMPLER(sampler_GradientTex);

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
                output.progress = saturate(input.texcoord3.x);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float clipBound = 0.5 + _ClipExtend;
                if (abs(input.tileUV.x) > clipBound || abs(input.tileUV.y) > clipBound)
                    discard;

                half4 color = SAMPLE_TEXTURE2D(_GradientTex, sampler_GradientTex,
                    float2(input.progress, 0.5));
                color.a *= _LayerOpacity;
                return color;
            }
            ENDHLSL
        }
    }
}
