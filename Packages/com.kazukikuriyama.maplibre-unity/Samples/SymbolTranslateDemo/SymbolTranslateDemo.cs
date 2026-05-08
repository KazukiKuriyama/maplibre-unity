using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates <c>text-translate</c> / <c>text-translate-anchor</c>.
    /// Two symbol layers reference the same GeoJSON Point source: a baseline
    /// layer renders labels at the feature's coordinates, while a second layer
    /// (suffixed "-translated") applies <c>text-translate: [40, -30]</c> so its
    /// labels float up-and-right by 40 / 30 CSS pixels. The contrast colours
    /// make the displacement obvious without panning.
    ///
    /// Switch <c>text-translate-anchor</c> between "map" and "viewport" by
    /// editing the constant below to compare bearing-locked vs screen-locked
    /// offsets -- rotate the map (right-drag) and the "viewport" anchor stays
    /// fixed on screen while "map" rotates with the world.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class SymbolTranslateDemo : MonoBehaviour
    {
        // Try "map" then rotate the map (right-drag) -- the offset rotates with
        // the world. Switch to "viewport" and the offset stays screen-aligned.
        private const string TranslateAnchor = "map";

        private MapLibreMap _map;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[SymbolTranslateDemo] MapLibreMap component not found");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;
            yield return null;

            _map.AddSource("stations", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        { ""type"": ""Feature"",
                          ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7671, 35.6812] },
                          ""properties"": { ""name"": ""Tokyo"" } },
                        { ""type"": ""Feature"",
                          ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7016, 35.6580] },
                          ""properties"": { ""name"": ""Shibuya"" } },
                        { ""type"": ""Feature"",
                          ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7003, 35.6896] },
                          ""properties"": { ""name"": ""Shinjuku"" } },
                        { ""type"": ""Feature"",
                          ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7771, 35.7141] },
                          ""properties"": { ""name"": ""Ueno"" } }
                    ]
                }")
            });

            yield return null;
            yield return null;

            // Visible anchor -- solid red dot at the actual feature coordinate.
            _map.AddLayer(new LayerDefinition
            {
                Id = "anchor-dot",
                Type = LayerType.Circle,
                Source = "stations",
                SourceLayer = "stations",
                Paint = new Dictionary<string, object>
                {
                    { "circle-radius", 6 },
                    { "circle-color", "#d32f2f" },
                    { "circle-stroke-width", 2 },
                    { "circle-stroke-color", "#ffffff" }
                }
            }, triggerRefresh: false);

            // Baseline label -- black, no translate. Sits exactly on the dot.
            _map.AddLayer(new LayerDefinition
            {
                Id = "label-baseline",
                Type = LayerType.Symbol,
                Source = "stations",
                SourceLayer = "stations",
                Layout = new Dictionary<string, object>
                {
                    { "text-field", "{name}" },
                    { "text-font", new[] { "Noto Sans Regular" } },
                    { "text-size", 16 },
                    { "text-allow-overlap", true }
                },
                Paint = new Dictionary<string, object>
                {
                    { "text-color", "#212121" },
                    { "text-halo-color", "#ffffff" },
                    { "text-halo-width", 1.5 }
                }
            }, triggerRefresh: false);

            // Translated label -- same source, shifted +40px right / -30px up
            // (CSS y points down, so y=-30 = 30 px up on screen).
            _map.AddLayer(new LayerDefinition
            {
                Id = "label-translated",
                Type = LayerType.Symbol,
                Source = "stations",
                SourceLayer = "stations",
                Layout = new Dictionary<string, object>
                {
                    { "text-field", "{name}" },
                    { "text-font", new[] { "Noto Sans Regular" } },
                    { "text-size", 16 },
                    { "text-allow-overlap", true }
                },
                Paint = new Dictionary<string, object>
                {
                    { "text-color", "#1565c0" },
                    { "text-halo-color", "#ffffff" },
                    { "text-halo-width", 1.5 },
                    { "text-translate", new object[] { 40.0, -30.0 } },
                    { "text-translate-anchor", TranslateAnchor }
                }
            });
        }
    }
}
