Shader "MapLibre/Icon"
{
    Properties
    {
        _MainTex ("Atlas Texture", 2D) = "white" {}
        _Opacity ("Opacity", Range(0, 1)) = 1.0
        _Color ("Tint Color", Color) = (0, 0, 0, 1)
        _IsSdf ("Is SDF", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+2"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Icon"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Opacity;
                float4 _Color;
                float _IsSdf;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                half4 result;
                if (_IsSdf > 0.5)
                {
                    // SDF icon: alpha channel contains distance field.
                    // Use smoothstep for antialiased edges, tint with _Color.
                    float dist = texColor.a;
                    float edge = smoothstep(0.4, 0.55, dist);
                    result = half4(_Color.rgb, edge * _Color.a);
                }
                else
                {
                    // Regular (raster) icon: use texture color as-is.
                    result = texColor;
                }

                result.a *= _Opacity;
                return result;
            }
            ENDHLSL
        }
    }
}
