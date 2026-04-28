Shader "MapLibre/Sky"
{
    // Full-screen background gradient drawn first (queue Background-1) so the
    // map (Background queue and above) overdraws it where ground is visible.
    // Above the visible ground (when the camera is pitched), the gradient shows
    // through and gives the impression of sky + horizon.
    //
    // Mesh is expected to be a 4-vertex quad with positions in NDC space
    // ((-1,-1,0) .. (1,1,0)). The vertex shader skips MVP transformation.
    Properties
    {
        _SkyColor ("Sky Color (top)", Color) = (0.51, 0.69, 0.86, 1)
        _HorizonColor ("Horizon Color (bottom)", Color) = (0.85, 0.92, 0.97, 1)
        _SkyHorizonBlend ("Sky Horizon Blend", Range(0, 1)) = 0.8
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Background"
            "Queue" = "Background"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Sky"

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
                float2 uv         : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyColor;
                float4 _HorizonColor;
                float _SkyHorizonBlend;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                // Mesh vertices are already in NDC; pass through directly.
                output.positionCS = float4(input.positionOS.xy, 0.0, 1.0);
                output.uv = input.positionOS.xy * 0.5 + 0.5;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // y = 0 at screen bottom (horizon side), y = 1 at top (sky side).
                // _SkyHorizonBlend controls how far up the screen the horizon
                // tint persists before fading into the sky color.
                float t = saturate(input.uv.y);
                float blendStart = saturate(1.0 - _SkyHorizonBlend);
                float blend = saturate((t - blendStart) / max(_SkyHorizonBlend, 1e-4));
                return lerp(_HorizonColor, _SkyColor, blend);
            }
            ENDHLSL
        }
    }
}
