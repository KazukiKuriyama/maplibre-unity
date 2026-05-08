Shader "MapLibre/SdfText"
{
    Properties
    {
        _GlyphAtlas ("Glyph Atlas (Alpha8 SDF)", 2D) = "black" {}
        _TextColor ("Text Color", Color) = (1, 1, 1, 1)
        _HaloColor ("Halo Color", Color) = (0, 0, 0, 1)
        _HaloWidth ("Halo Width (SDF units)", Range(0, 0.4)) = 0.0
        _HaloBlur  ("Halo Blur (SDF units)", Range(0, 0.4)) = 0.05
        _Opacity ("Opacity", Range(0, 1)) = 1.0
        // SDF threshold. Mapbox glyph PBFs encode the actual stroke edge at
        // byte value 192 (= 192/255 ≈ 0.7529). Sampling at 0.5 includes a
        // ~64-byte band outside the real edge, so glyph strokes render so
        // wide they merge into blobs that don't look like text.
        _Cutoff ("SDF Cutoff", Range(0.1, 0.95)) = 0.7529
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "SdfText"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_GlyphAtlas);
            SAMPLER(sampler_GlyphAtlas);

            CBUFFER_START(UnityPerMaterial)
                half4 _TextColor;
                half4 _HaloColor;
                float _HaloWidth;
                float _HaloBlur;
                float _Opacity;
                float _Cutoff;
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
                // Mapbox glyph PBFs encode the SDF in the alpha channel; we
                // upload the same value to RGB during atlas packing so any
                // sample channel returns the distance field. Read .a for
                // forwards compatibility with Alpha8 textures.
                float dist = SAMPLE_TEXTURE2D(_GlyphAtlas, sampler_GlyphAtlas, input.uv).a;

                // Anti-aliased text body: smoothstep across one screen-pixel of
                // the SDF gradient. fwidth(dist) gives the SDF derivative in
                // screen space, which is the right width for crisp edges at
                // any zoom.
                float aa = max(fwidth(dist), 0.0001);
                half textAlpha = smoothstep(_Cutoff - aa, _Cutoff + aa, dist);

                // Halo: a wider edge under the text body, faded out by
                // _HaloBlur. When _HaloWidth = 0 the halo collapses onto the
                // text edge and becomes invisible.
                float haloEdge = _Cutoff - _HaloWidth;
                half haloAlpha = smoothstep(haloEdge - _HaloBlur, haloEdge, dist);

                // Composite: text body wins inside; halo fills the band
                // outside it. Alpha is the union (max) so neither layer
                // bleeds through the other where they overlap.
                half3 rgb = lerp(_HaloColor.rgb, _TextColor.rgb, textAlpha);
                half outA = max(haloAlpha * _HaloColor.a, textAlpha * _TextColor.a);
                outA *= _Opacity;
                return half4(rgb, outA);
            }
            ENDHLSL
        }
    }
}
