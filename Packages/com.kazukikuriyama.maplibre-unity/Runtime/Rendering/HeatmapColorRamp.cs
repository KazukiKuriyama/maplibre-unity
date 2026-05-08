using MapLibre.Unity.Expressions;
using MapLibre.Unity.Style;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Bakes a heatmap-color expression into a 256×1 Texture2D color ramp.
    /// The shader samples this texture using accumulated density as the U coordinate.
    /// </summary>
    public static class HeatmapColorRamp
    {
        /// <summary>
        /// Default heatmap color ramp matching MapLibre GL JS defaults:
        /// transparent → blue → cyan → lime → yellow → red
        /// </summary>
        private static readonly Color[] DefaultColors =
        {
            new(0f, 0f, 1f, 0f),     // 0.0 -- transparent blue
            new(0f, 0f, 1f, 1f),     // ~0.1 -- blue
            new(0f, 1f, 1f, 1f),     // ~0.3 -- cyan
            new(0f, 1f, 0f, 1f),     // ~0.5 -- lime
            new(1f, 1f, 0f, 1f),     // ~0.7 -- yellow
            new(1f, 0f, 0f, 1f),     // 1.0 -- red
        };

        private static readonly float[] DefaultStops = { 0f, 0.1f, 0.3f, 0.5f, 0.7f, 1f };

        /// <summary>
        /// Generate a 256×1 color ramp texture from a heatmap-color expression.
        /// If the expression is null, uses the MapLibre GL JS default color ramp.
        /// </summary>
        public static Texture2D Generate(Expression heatmapColorExpr, float zoom = 0f)
        {
            var texture = new Texture2D(256, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[256];

            if (heatmapColorExpr != null)
            {
                for (int i = 0; i < 256; i++)
                {
                    float density = i / 255f;
                    var ctx = new EvaluationContext(zoom) { HeatmapDensity = density };
                    var color = heatmapColorExpr.EvaluateColor(ctx, Color.clear);
                    pixels[i] = color;
                }
            }
            else
            {
                for (int i = 0; i < 256; i++)
                {
                    float t = i / 255f;
                    pixels[i] = SampleDefaultRamp(t);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        private static Color32 SampleDefaultRamp(float t)
        {
            if (t <= 0f) return DefaultColors[0];
            if (t >= 1f) return DefaultColors[DefaultColors.Length - 1];

            for (int i = 1; i < DefaultStops.Length; i++)
            {
                if (t <= DefaultStops[i])
                {
                    float localT = (t - DefaultStops[i - 1]) / (DefaultStops[i] - DefaultStops[i - 1]);
                    return Color.Lerp(DefaultColors[i - 1], DefaultColors[i], localT);
                }
            }

            return DefaultColors[DefaultColors.Length - 1];
        }
    }
}
