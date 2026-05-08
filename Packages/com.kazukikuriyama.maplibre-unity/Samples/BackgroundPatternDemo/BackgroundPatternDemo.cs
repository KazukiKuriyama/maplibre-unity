using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates <c>background-pattern</c>. The demo registers a tiny
    /// procedural sprite at runtime via <see cref="MapLibreMap.AddImage"/>,
    /// then sets the background layer's <c>background-pattern</c> property to
    /// reference that sprite. The pattern tiles seamlessly across the visible
    /// background plane in CSS-pixel-sized units regardless of zoom.
    ///
    /// No basemap is fetched -- the background fully fills the camera view.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class BackgroundPatternDemo : MonoBehaviour
    {
        private const int PatternSize = 32;

        private MapLibreMap _map;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[BackgroundPatternDemo] MapLibreMap component not found");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;
            yield return null;

            // Procedural light/dark grid -- visible without any external sprite endpoint.
            _map.AddImage("grid-pattern", BuildGridPattern(
                new Color32(220, 230, 240, 255),
                new Color32(180, 200, 215, 255)));

            // Swap the background's color for a pattern reference. SetPaintProperty
            // forwards the change to the BackgroundRenderer, which switches to the
            // MapLibre/BackgroundPattern shader and tiles the sprite over the plane.
            _map.SetPaintProperty("background", "background-pattern", "grid-pattern");

            // A small overlay polygon proves the pattern sits behind regular layers.
            _map.AddSource("hex", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""Polygon"",
                        ""coordinates"": [[
                            [139.74, 35.66],[139.79, 35.66],
                            [139.81, 35.69],[139.79, 35.72],
                            [139.74, 35.72],[139.72, 35.69],
                            [139.74, 35.66]
                        ]]
                    },
                    ""properties"": {}
                }")
            });

            yield return null;
            yield return null;

            _map.AddLayer(new LayerDefinition
            {
                Id = "hex-fill",
                Type = LayerType.Fill,
                Source = "hex",
                SourceLayer = "hex",
                Paint = new Dictionary<string, object>
                {
                    { "fill-color", "#1976d2" },
                    { "fill-opacity", 0.7 }
                }
            });
        }

        private static Texture2D BuildGridPattern(Color32 light, Color32 dark)
        {
            var tex = new Texture2D(PatternSize, PatternSize, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[PatternSize * PatternSize];

            // Two-tone checkerboard plus a thin grid line on every quadrant edge.
            int half = PatternSize / 2;
            for (int y = 0; y < PatternSize; y++)
            {
                for (int x = 0; x < PatternSize; x++)
                {
                    bool light1 = (x < half) ^ (y < half);
                    pixels[y * PatternSize + x] = light1 ? light : dark;
                }
            }
            // Thin border on the tile boundary so seams between repeats read clearly.
            for (int i = 0; i < PatternSize; i++)
            {
                pixels[i] = new Color32(120, 130, 140, 255);
                pixels[(PatternSize - 1) * PatternSize + i] = new Color32(120, 130, 140, 255);
                pixels[i * PatternSize] = new Color32(120, 130, 140, 255);
                pixels[i * PatternSize + (PatternSize - 1)] = new Color32(120, 130, 140, 255);
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false);
            return tex;
        }
    }
}
