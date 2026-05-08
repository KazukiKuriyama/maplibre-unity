using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates the SDF text path. The style declares a <c>glyphs</c> URL
    /// (the MapLibre demotiles font endpoint) and a symbol layer that reads
    /// from a small inline GeoJSON of city points. Because <c>glyphs</c> is set,
    /// SymbolRenderer routes labels through the self-rendered SDF mesh path
    /// instead of TextMeshPro -- no TMP_FontAsset assignment needed.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class GlyphUrlDemo : MonoBehaviour
    {
        // demotiles.maplibre.org's glyph endpoint serves the standard
        // {fontstack}/{range}.pbf protocol used by MapLibre styles.
        private const string DemotilesGlyphsUrl =
            "https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf";

        private const string SourceId = "demo-cities";
        private const string LayerId = "city-labels";

        private MapLibreMap _map;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[GlyphUrlDemo] MapLibreMap not found on this GameObject");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;
            yield return null;

            // Wire the glyph URL. SetGlyphs creates a GlyphSource and pushes
            // it through to every symbol renderer; from this call onwards,
            // any symbol layer with text-field renders SDF text.
            _map.SetGlyphs(DemotilesGlyphsUrl);

            AddDemoSource();
            AddDemoLayer();
        }

        private void AddDemoSource()
        {
            // A handful of major cities with stable feature ids.
            _map.AddSource(SourceId, new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        { ""type"": ""Feature"", ""id"": 1,
                          ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.6917, 35.6895] },
                          ""properties"": { ""name"": ""Tokyo"" } },
                        { ""type"": ""Feature"", ""id"": 2,
                          ""geometry"": { ""type"": ""Point"", ""coordinates"": [-74.0060, 40.7128] },
                          ""properties"": { ""name"": ""New York"" } },
                        { ""type"": ""Feature"", ""id"": 3,
                          ""geometry"": { ""type"": ""Point"", ""coordinates"": [2.3522, 48.8566] },
                          ""properties"": { ""name"": ""Paris"" } },
                        { ""type"": ""Feature"", ""id"": 4,
                          ""geometry"": { ""type"": ""Point"", ""coordinates"": [151.2093, -33.8688] },
                          ""properties"": { ""name"": ""Sydney"" } },
                        { ""type"": ""Feature"", ""id"": 5,
                          ""geometry"": { ""type"": ""Point"", ""coordinates"": [-46.6333, -23.5505] },
                          ""properties"": { ""name"": ""Sao Paulo"" } }
                    ]
                }")
            });
        }

        private void AddDemoLayer()
        {
            _map.AddLayer(new LayerDefinition
            {
                Id = LayerId,
                Type = LayerType.Symbol,
                Source = SourceId,
                SourceLayer = SourceId,
                Layout = new Dictionary<string, object>
                {
                    { "text-field", "{name}" },
                    { "text-font", new[] { "Noto Sans Regular" } },
                    { "text-size", 18 }
                },
                Paint = new Dictionary<string, object>
                {
                    { "text-color", "#1a237e" },
                    { "text-halo-color", "#ffffff" },
                    { "text-halo-width", 1.5 }
                }
            });
        }
    }
}
