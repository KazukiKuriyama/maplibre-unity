Shader "MapLibre/Line"
{
    Properties
    {
        // Per-feature line color, width and offset are supplied via vertex attributes.
        // _CSSToLocal converts CSS pixels to tile-local units; _WidthScale applies
        // a zoom-dependent width adjustment so cameras-only line-width expressions
        // re-scale correctly without rebuilding the mesh.
        _LayerOpacity ("Layer Opacity", Range(0, 1)) = 1.0
        _CSSToLocal ("CSS to Local conversion", Float) = 0.001
        _WidthScale ("Width Scale (zoom-dependent)", Float) = 1.0
        _ClipExtend ("Clip Extend", Float) = 0.015625
        _DashPattern ("Dash Pattern", Vector) = (0, 0, 0, 0)
        _DashTotal ("Dash Total", Float) = 0.0
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
            Name "Line"

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
                float4 color     : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 tileUV     : TEXCOORD0;
                float  lineDist   : TEXCOORD1;
                float  halfWidth  : TEXCOORD2;
                half4  lineColor  : COLOR0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _LayerOpacity;
                float _CSSToLocal;
                float _WidthScale;
                float _ClipExtend;
                float4 _DashPattern;
                float _DashTotal;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                // Per-feature CSS pixel width/offset → tile-local half-width / offset
                float halfWidthLocal = input.texcoord2.x * _WidthScale * _CSSToLocal * 0.5;
                float offsetLocal = input.texcoord2.y * _CSSToLocal;

                float3 pos = input.positionOS.xyz;
                // Expand vertex by perpendicular direction * half width
                pos.xz += input.texcoord.xy * halfWidthLocal;
                // Apply line-offset: shift both sides in the positive normal direction.
                // texcoord1.y stores side sign (+1/-1); texcoord.xy * sign = positive normal.
                pos.xz += input.texcoord.xy * input.texcoord1.y * offsetLocal;
                output.positionCS = TransformObjectToHClip(pos);
                // Use original (pre-offset) position for tile clipping
                output.tileUV = input.positionOS.xz;
                // Cumulative distance along the line in tile-local units
                output.lineDist = input.texcoord1.x;
                output.halfWidth = halfWidthLocal;
                output.lineColor = half4(input.color);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Clip fragments outside tile boundaries + buffer region.
                // _ClipExtend allows rendering into the MVT buffer zone
                // so lines are not cut at tile edges (matching MapLibre GL JS behavior).
                float clipBound = 0.5 + _ClipExtend;
                if (abs(input.tileUV.x) > clipBound || abs(input.tileUV.y) > clipBound)
                    discard;

                // Dash pattern: discard fragments in gap portions.
                // _DashTotal > 0 indicates a dashed line.
                // Distance is in tile-local units; convert to line-width units
                // by dividing by (2 * halfWidth) which equals 1 line-width in tile-local space.
                if (_DashTotal > 0.0 && input.halfWidth > 0.0)
                {
                    float lineWidthLocal = 2.0 * input.halfWidth;
                    float distInLineWidths = input.lineDist / lineWidthLocal;
                    float t = fmod(distInLineWidths, _DashTotal);
                    if (t < 0.0) t += _DashTotal;

                    // Walk through dash/gap pairs in _DashPattern (up to 4 entries)
                    // Odd indices = gap, even indices = dash
                    float cumLen = 0.0;
                    bool inGap = false;

                    // Entry 0 (dash)
                    cumLen += _DashPattern.x;
                    if (t < cumLen) { inGap = false; }
                    else
                    {
                        // Entry 1 (gap)
                        cumLen += _DashPattern.y;
                        if (t < cumLen) { inGap = true; }
                        else
                        {
                            // Entry 2 (dash)
                            cumLen += _DashPattern.z;
                            if (t < cumLen) { inGap = false; }
                            else
                            {
                                // Entry 3 (gap)
                                inGap = true;
                            }
                        }
                    }

                    if (inGap)
                        discard;
                }

                half4 color = input.lineColor;
                color.a *= _LayerOpacity;
                return color;
            }
            ENDHLSL
        }
    }
}
