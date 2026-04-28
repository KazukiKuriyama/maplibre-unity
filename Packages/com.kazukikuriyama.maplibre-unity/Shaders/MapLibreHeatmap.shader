Shader "MapLibre/Heatmap"
{
    Properties
    {
        _CSSToLocal ("CSS to Local conversion", Float) = 0.001
        _RadiusCSS ("Radius in CSS pixels", Float) = 30.0
        _RadiusScale ("Radius Scale (zoom-dependent)", Float) = 1.0
        _Intensity ("Heatmap Intensity", Float) = 1.0
        _Opacity ("Heatmap Opacity", Range(0, 1)) = 1.0
        _ColorRamp ("Color Ramp Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // BlendOp Max: each pixel keeps the maximum value from all overlapping blobs.
        // This ensures dense clusters show the warmest color regardless of draw order,
        // and zoom level doesn't affect the color distribution.
        BlendOp Max
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Heatmap"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;   // quad direction [-1, 1]
                float2 uv2        : TEXCOORD1;   // x = weight
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  weight      : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float _CSSToLocal;
                float _RadiusCSS;
                float _RadiusScale;
                float _Intensity;
                float _Opacity;
            CBUFFER_END

            TEXTURE2D(_ColorRamp);
            SAMPLER(sampler_ColorRamp);

            Varyings vert(Attributes input)
            {
                Varyings output;

                float weight = input.uv2.x;
                // Scale radius by sqrt(weight) for per-feature size variation
                float weightScale = sqrt(max(weight, 0.01));
                float radiusLocal = _RadiusCSS * _RadiusScale * _CSSToLocal * weightScale;

                float3 pos = input.positionOS.xyz;
                pos.x += input.uv.x * radiusLocal;
                pos.z += input.uv.y * radiusLocal;

                output.positionCS = TransformObjectToHClip(pos);
                output.uv = input.uv;
                output.weight = weight;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float dist = length(input.uv);
                if (dist > 1.0)
                    discard;

                // Gaussian falloff
                float gaussian = exp(-2.5 * dist * dist);

                // Density for color ramp lookup
                float density = saturate(gaussian * input.weight * _Intensity);

                // Sample color from ramp
                half4 color = SAMPLE_TEXTURE2D(_ColorRamp, sampler_ColorRamp, float2(density, 0.5));

                // With BlendOp Max, the pixel retains the brightest (highest density) color.
                // Pre-multiply by opacity so Max blending respects it.
                color.rgb *= _Opacity;
                color.a *= _Opacity;

                if (color.a < 0.002)
                    discard;

                return color;
            }
            ENDHLSL
        }
    }
}
