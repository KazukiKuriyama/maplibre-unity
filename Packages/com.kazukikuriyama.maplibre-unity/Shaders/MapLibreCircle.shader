Shader "MapLibre/Circle"
{
    Properties
    {
        _CSSToLocal ("CSS to Local conversion", Float) = 0.001
        _RadiusScale ("Radius Scale (zoom-dependent)", Float) = 1.0
        _Blur ("Blur", Float) = 0.0
        _StrokeWidth ("Stroke Width (CSS pixels)", Float) = 0.0
        _StrokeColor ("Stroke Color", Color) = (0, 0, 0, 1)
        _StrokeOpacity ("Stroke Opacity", Range(0, 1)) = 1.0
        // circle-pitch-alignment: 0 = "map" (lay flat on ground), 1 = "viewport" (billboard)
        _PitchAlign ("Pitch Alignment (0=map,1=viewport)", Float) = 1.0
        // circle-pitch-scale: 0 = "map" (size shrinks at oblique angles), 1 = "viewport" (constant screen px)
        _PitchScale ("Pitch Scale (0=map,1=viewport)", Float) = 0.0
        // Conversion 1 CSS pixel → clip-space Y (= 2.0 / Screen.height). Used only when _PitchScale == 1.
        _PixelToClipY ("Pixel to Clip Y", Float) = 0.001
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
        // Pixel depth on a viewport-aligned billboard is fixed at the anchor's
        // depth, but the bottom half projects to a screen position where nearby
        // ground tiles already wrote nearer depth. With ZTest LEqual the
        // billboard's lower half loses the depth test and looks "buried" under
        // the map. MapLibre GL JS resolves this in WebGL via painter's
        // algorithm (no depth test); ZTest Always reproduces that behaviour
        // — circles render purely by render-queue order.
        ZTest Always
        Cull Off

        Pass
        {
            Name "Circle"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;   // quad direction [-1, 1]
                float2 uv2        : TEXCOORD1;   // x = radius in CSS pixels
                float4 color      : COLOR;        // per-feature fill color (a = opacity * color.a)
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  radiusLocal : TEXCOORD1;
                half4  fillColor   : COLOR0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _CSSToLocal;
                float _RadiusScale;
                float _Blur;
                float _StrokeWidth;
                float4 _StrokeColor;
                float _StrokeOpacity;
                float _PitchAlign;
                float _PitchScale;
                float _PixelToClipY;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                // Convert per-feature CSS radius to tile-local units
                // _RadiusScale adjusts for zoom-dependent expressions evaluated after dispatch
                float radiusLocal = input.uv2.x * _RadiusScale * _CSSToLocal;
                float strokeLocal = _StrokeWidth * _CSSToLocal;

                // Total outer radius including stroke
                float outerLocal = radiusLocal + strokeLocal;
                // Margin for antialiasing (fixed fraction + blur)
                float margin = outerLocal * 0.15 + _Blur * radiusLocal;
                float expand = outerLocal + margin;

                float4 positionCS;

                if (_PitchAlign > 0.5)
                {
                    // Viewport alignment: billboard the quad to face the camera.
                    // Compute the center in view space, then offset along view xy.
                    float4 centerVS = mul(UNITY_MATRIX_V, mul(UNITY_MATRIX_M, input.positionOS));

                    if (_PitchScale > 0.5)
                    {
                        // Viewport scale: convert directly to clip space and use a
                        // fixed NDC offset so the on-screen size never changes
                        // with depth or pitch. CSS px → NDC = px * _PixelToClipY.
                        float4 centerCS = mul(UNITY_MATRIX_P, centerVS);
                        float radiusCss = input.uv2.x * _RadiusScale + _StrokeWidth;
                        float marginCss = radiusCss * 0.15 + _Blur * input.uv2.x * _RadiusScale;
                        float expandCss = radiusCss + marginCss;
                        // Multiply by w so post-perspective-divide the offset stays constant.
                        float ndcExpand = expandCss * _PixelToClipY * centerCS.w;
                        positionCS = centerCS;
                        positionCS.x += input.uv.x * ndcExpand;
                        positionCS.y += input.uv.y * ndcExpand;
                    }
                    else
                    {
                        // Map scale: world-space radius. After M transforms the unit
                        // tile-local quad to world, the column-X length of M gives
                        // the world size of one tile-local unit.
                        float xScaleWorld = length(UNITY_MATRIX_M._m00_m10_m20);
                        float worldExpand = expand * xScaleWorld;
                        centerVS.x += input.uv.x * worldExpand;
                        centerVS.y += input.uv.y * worldExpand;
                        positionCS = mul(UNITY_MATRIX_P, centerVS);
                    }
                }
                else
                {
                    // Map alignment: lay the quad flat on the ground (XZ plane).
                    float3 pos = input.positionOS.xyz;
                    pos.x += input.uv.x * expand;
                    pos.z += input.uv.y * expand;
                    positionCS = TransformObjectToHClip(pos);
                }

                output.positionCS = positionCS;
                output.uv = input.uv;
                output.radiusLocal = radiusLocal;
                output.fillColor = half4(input.color);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float radiusLocal = input.radiusLocal;
                float strokeLocal = _StrokeWidth * _CSSToLocal;
                float outerLocal = radiusLocal + strokeLocal;
                float margin = outerLocal * 0.15 + _Blur * radiusLocal;
                float expand = outerLocal + margin;

                // Distance from circle center in tile-local units
                float dist = length(input.uv) * expand;

                // Antialiasing: smooth transition over ~1 CSS pixel equivalent
                float aa = max(_Blur * radiusLocal, _CSSToLocal * 0.75);

                // Outer edge (stroke boundary)
                float outerAlpha = 1.0 - smoothstep(outerLocal - aa, outerLocal + aa, dist);

                // Inner edge (fill boundary)
                float innerFactor = smoothstep(radiusLocal - aa, radiusLocal + aa, dist);

                // Fill color from vertex attribute
                half4 fill = input.fillColor;
                fill.a *= (1.0 - innerFactor);

                // Stroke
                float strokeAlpha = outerAlpha * innerFactor;
                half4 stroke = half4(_StrokeColor.rgb, _StrokeColor.a * _StrokeOpacity * strokeAlpha);

                // Composite: stroke over fill
                half4 result;
                result.rgb = stroke.rgb * stroke.a + fill.rgb * fill.a * (1.0 - stroke.a);
                result.a = stroke.a + fill.a * (1.0 - stroke.a);

                if (result.a < 0.003)
                    discard;

                return result;
            }
            ENDHLSL
        }
    }
}
