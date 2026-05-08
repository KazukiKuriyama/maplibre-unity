using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates per-feature `fill-pattern`. Three Tokyo polygons each
    /// declare a different `class` property; a single fill layer uses
    /// <c>["match", ["get", "class"], ...]</c> to pick a different sprite per
    /// feature. The patterns themselves are generated procedurally at runtime
    /// (red dots / blue stripes / green crosshatch) and registered via
    /// <see cref="MapLibreMap.AddImage"/> so the demo is self-contained -- no
    /// external sprite endpoint required.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class PerFeaturePatternDemo : MonoBehaviour
    {
        private const int PatternSize = 32;

        private MapLibreMap _map;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[PerFeaturePatternDemo] MapLibreMap not found");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;
            yield return null;

            RegisterPatterns();
            AddSource();
            AddLayer();
        }

        private void RegisterPatterns()
        {
            // Three distinct procedural patterns. AddImage uses the supplied
            // Texture2D as-is; the pattern shader tiles it across the polygon
            // surface using the texture's CSS-pixel size.
            _map.AddImage("trees", BuildDottedPattern(new Color32(46, 125, 50, 255)));
            _map.AddImage("waves", BuildHorizontalStripePattern(new Color32(25, 118, 210, 255)));
            _map.AddImage("grid",  BuildCrosshatchPattern(new Color32(120, 80, 30, 255)));
        }

        private static Texture2D BuildDottedPattern(Color32 dot)
        {
            var tex = new Texture2D(PatternSize, PatternSize, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[PatternSize * PatternSize];
            // Two dots per tile, offset diagonally so the repeated pattern
            // reads as evenly distributed circles instead of a regular grid.
            DrawCircle(pixels, PatternSize, PatternSize / 4, PatternSize / 4, 4, dot);
            DrawCircle(pixels, PatternSize, 3 * PatternSize / 4, 3 * PatternSize / 4, 4, dot);
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false);
            return tex;
        }

        private static Texture2D BuildHorizontalStripePattern(Color32 stripe)
        {
            var tex = new Texture2D(PatternSize, PatternSize, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[PatternSize * PatternSize];
            for (int y = 0; y < PatternSize; y++)
            {
                bool inStripe = (y % 8) < 4;
                if (!inStripe) continue;
                for (int x = 0; x < PatternSize; x++)
                    pixels[y * PatternSize + x] = stripe;
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false);
            return tex;
        }

        private static Texture2D BuildCrosshatchPattern(Color32 line)
        {
            var tex = new Texture2D(PatternSize, PatternSize, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[PatternSize * PatternSize];
            // Two diagonals at +45° and -45° to form a crosshatch.
            for (int i = 0; i < PatternSize; i++)
            {
                pixels[i * PatternSize + i] = line;
                pixels[i * PatternSize + (PatternSize - 1 - i)] = line;
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false);
            return tex;
        }

        private static void DrawCircle(Color32[] pixels, int size, int cx, int cy, int radius, Color32 color)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radius * radius) continue;
                    int px = ((cx + x) % size + size) % size;
                    int py = ((cy + y) % size + size) % size;
                    pixels[py * size + px] = color;
                }
            }
        }

        private void AddSource()
        {
            // Three Tokyo zones with stable feature ids and a `class` tag the
            // pattern expression matches against.
            _map.AddSource("zones", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        { ""type"": ""Feature"", ""id"": 1,
                          ""geometry"": { ""type"": ""Polygon"",
                            ""coordinates"": [[[139.6950,35.6700],[139.6950,35.6790],[139.7060,35.6790],[139.7060,35.6700],[139.6950,35.6700]]] },
                          ""properties"": { ""class"": ""park"" } },
                        { ""type"": ""Feature"", ""id"": 2,
                          ""geometry"": { ""type"": ""Polygon"",
                            ""coordinates"": [[[139.7700,35.6600],[139.7700,35.6700],[139.7820,35.6700],[139.7820,35.6600],[139.7700,35.6600]]] },
                          ""properties"": { ""class"": ""water"" } },
                        { ""type"": ""Feature"", ""id"": 3,
                          ""geometry"": { ""type"": ""Polygon"",
                            ""coordinates"": [[[139.7500,35.7000],[139.7500,35.7100],[139.7620,35.7100],[139.7620,35.7000],[139.7500,35.7000]]] },
                          ""properties"": { ""class"": ""industrial"" } }
                    ]
                }")
            });
        }

        private void AddLayer()
        {
            // The single layer drives three different patterns from one
            // expression -- proof that grouping is per-feature rather than
            // uniform per layer (the previous behaviour).
            _map.AddLayer(new LayerDefinition
            {
                Id = "zones-fill",
                Type = LayerType.Fill,
                Source = "zones",
                SourceLayer = "zones",
                Paint = new Dictionary<string, object>
                {
                    {
                        "fill-pattern",
                        new object[]
                        {
                            "match", new object[] { "get", "class" },
                            "park",       "trees",
                            "water",      "waves",
                            "industrial", "grid",
                            "trees" // default
                        }
                    },
                    { "fill-opacity", 0.85 }
                }
            });

            _map.AddLayer(new LayerDefinition
            {
                Id = "zones-outline",
                Type = LayerType.Line,
                Source = "zones",
                SourceLayer = "zones",
                Paint = new Dictionary<string, object>
                {
                    { "line-color", "#222222" },
                    { "line-width", 2 }
                }
            });
        }
    }
}
